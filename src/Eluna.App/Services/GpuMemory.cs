using System.Runtime.InteropServices;

namespace Eluna.App.Services;

public sealed record GpuMemorySnapshot(string Name, long Total, long? Used, string AdapterId)
{
    public long? Available => Used is long used ? Math.Max(0, Total - used) : null;
}
public static class GpuMemory
{
    private static readonly object Gate = new();
    private static IReadOnlyList<GpuMemorySnapshot> _cached = [];
    private static Task? _refresh;
    private static long _updated;
    public static IReadOnlyList<GpuMemorySnapshot> Cached
    {
        get { lock (Gate) { if (_refresh?.IsCompleted != false && Environment.TickCount64 - _updated > 2000) { _refresh = Task.Run(() => { var result = Capture(); lock (Gate) { _cached = result; _updated = Environment.TickCount64; } }); } return _cached; } }
    }
    public static async Task<IReadOnlyList<GpuMemorySnapshot>> RefreshAsync()
    { _ = Cached; Task? refresh; lock (Gate) { refresh = _refresh; } if (refresh is not null) { await refresh; } lock (Gate) { return _cached; } }
    // Adapter-wide WDDM counters include engine child processes. DXGI process budgets are not global free VRAM.
    private static IReadOnlyList<GpuMemorySnapshot> Capture()
    {
        var result = new List<GpuMemorySnapshot>(); IntPtr factory = IntPtr.Zero, query = IntPtr.Zero;
        try
        {
            var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); if (CreateDXGIFactory1(ref iid, out factory) < 0) { return result; }
            Dictionary<string, long> usage = [];
            if (PdhOpenQueryW(null, UIntPtr.Zero, out query) == 0 && PdhAddEnglishCounterW(query, @"\GPU Adapter Memory(*)\Dedicated Usage", UIntPtr.Zero, out IntPtr counter) == 0 && PdhCollectQueryData(query) == 0)
            {
                uint size = 0; PdhGetFormattedCounterArrayW(counter, 0x1400, ref size, out _, IntPtr.Zero);
                if (size is > 0 and < 16 * 1024 * 1024)
                {
                    IntPtr buffer = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        if (PdhGetFormattedCounterArrayW(counter, 0x1400, ref size, out uint count, buffer) == 0)
                        {
                            for (int i = 0; i < count && (i + 1L) * Marshal.SizeOf<CounterItem>() <= size; i++)
                            {
                                CounterItem item = Marshal.PtrToStructure<CounterItem>(buffer + i * Marshal.SizeOf<CounterItem>());
                                if (item.Value.Status <= 1 && item.Value.Bytes >= 0 && Marshal.PtrToStringUni(item.Name) is string name) { usage[name] = item.Value.Bytes; }
                            }
                        }
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
            }
            var enumerate = Method<EnumAdapter>(factory, 12);
            for (uint index = 0; index < 32 && enumerate(factory, index, out IntPtr adapter) >= 0; index++)
            {
                try
                {
                    if (Method<GetDescription>(adapter, 10)(adapter, out AdapterDescription desc) < 0 || (desc.Flags & 2) != 0) { continue; }
                    string id = $"luid_0x{desc.LuidHigh:x8}_0x{desc.LuidLow:x8}";
                    var matching = usage.Where(p => p.Key.StartsWith(id + "_", StringComparison.OrdinalIgnoreCase)).ToArray();
                    long total = (long)desc.DedicatedVideoMemory.ToUInt64();
                    result.Add(new(desc.Description, total, matching.Length == 0 ? null : Math.Min(total, matching.Sum(p => p.Value)), id));
                }
                finally { Marshal.Release(adapter); }
            }
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException or OverflowException) { }
        finally { if (query != IntPtr.Zero) { PdhCloseQuery(query); } if (factory != IntPtr.Zero) { Marshal.Release(factory); } }
        return result;
    }
    private static T Method<T>(IntPtr obj, int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size));
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapter(IntPtr self, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDescription(IntPtr self, out AdapterDescription description);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Vendor, Device, Subsystem, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow, LuidHigh, Flags;
    }
    [StructLayout(LayoutKind.Explicit, Size = 16)] private struct CounterValue { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public long Bytes; }
    [StructLayout(LayoutKind.Sequential)] private struct CounterItem { public IntPtr Name; public CounterValue Value; }
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, UIntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, UIntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
}
