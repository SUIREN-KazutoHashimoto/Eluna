using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Eluna.App.Services;

// A process-owned power request changes neither the power plan nor Windows lock settings.
// SafeHandle releases the request once, including when cancellation and normal cleanup race.
internal sealed class ComputerAwakeRequest : SafeHandleZeroOrMinusOneIsInvalid
{
    private bool _system, _display;
    private ComputerAwakeRequest() : base(true) { }
    internal bool Active => !IsClosed && !IsInvalid && _system && _display;

    internal static ComputerAwakeRequest Acquire(bool enabled)
    {
        if (!enabled) { return new(); }
        nint reason = Marshal.StringToHGlobalUni("ElunaのComputer Useが画面を確認・操作しています。");
        ComputerAwakeRequest request;
        try
        {
            var context = new ReasonContext { Flags = 1, SimpleReason = reason };
            request = PowerCreateRequest(ref context);
        }
        finally { Marshal.FreeHGlobal(reason); }
        try
        {
            if (request.IsInvalid || !(request._system = PowerSetRequest(request, 1)) || !(request._display = PowerSetRequest(request, 0)))
            { throw new Win32Exception(Marshal.GetLastWin32Error(), "Computer Use中の自動スリープ抑止を開始できませんでした。設定でオフにして再実行できます。"); }
            return request;
        }
        catch { request.Dispose(); throw; }
    }

    protected override bool ReleaseHandle()
    {
        if (_display) { _ = PowerClearRequest(handle, 0); }
        if (_system) { _ = PowerClearRequest(handle, 1); }
        return CloseHandle(handle);
    }

    // REASON_CONTEXT's union occupies 24 bytes on x64 (the only distribution target).
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct ReasonContext
    {
        [FieldOffset(0)] internal uint Version;
        [FieldOffset(4)] internal uint Flags;
        [FieldOffset(8)] internal nint SimpleReason;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern ComputerAwakeRequest PowerCreateRequest(ref ReasonContext context);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool PowerSetRequest(ComputerAwakeRequest request, int type);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool PowerClearRequest(nint request, int type);
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
}
