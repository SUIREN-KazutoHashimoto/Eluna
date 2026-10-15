using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Eluna.App.Models;

namespace Eluna.App.Services;

public sealed record MemoryCapacity(long TotalRam, long AvailableRam, long GpuBytes, string GpuName, long? AvailableGpuBytes = null);
public static class HardwareAdvisor
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, ExtendedVirtual; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    public static MemoryCapacity Capture(IEnumerable<ComputeDevice> devices, string compute = "auto")
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() }; GlobalMemoryStatusEx(ref status);
        ComputeDevice? gpu = compute == "cpu" ? null : compute == "auto" ? devices.FirstOrDefault(d => d.Id.StartsWith("Vulkan", StringComparison.Ordinal)) : devices.FirstOrDefault(d => d.Id == compute);
        Match memory = Regex.Match(gpu?.Name ?? "", @"\((?<total>\d+) MiB");
        long gpuBytes = memory.Success ? long.Parse(memory.Groups["total"].Value, System.Globalization.CultureInfo.InvariantCulture) * 1024 * 1024 : 0;
        string name = gpu?.Name.Split('(')[0].Trim() ?? "CPU";
        GpuMemorySnapshot[] matches = gpu is null ? [] : GpuMemory.Cached.Where(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        GpuMemorySnapshot? snapshot = matches.Length == 1 ? matches[0] : null;
        return new((long)status.TotalPhysical, (long)status.AvailablePhysical, snapshot?.Total ?? gpuBytes, name, snapshot?.Available);
    }
    public static string Describe(long weights, ModelProfile? profile, int context, MemoryCapacity memory)
    {
        long kv = profile?.EstimatedKvBytes(context) ?? 0;
        long lower = weights + kv + 512L * 1024 * 1024, upper = weights + kv + 2L * 1024 * 1024 * 1024;
        string fit = upper > memory.AvailableRam ? "空きRAMを超える見込みです。小さい量子化・モデルを検討してください。" :
            memory.GpuBytes == 0 ? "CPUで使う容量の目安です。速度はモデル規模に左右されます。" : memory.AvailableGpuBytes is long free ? upper <= free ? "現在の空きVRAMに収まる見込みです。" : "現在の空きVRAMを超える見込みです。" : "空きVRAMは取得できません。総容量だけでは収まるか判定できません。";
        return $"RAM {FileSize.Format(memory.AvailableRam)} 空き / {FileSize.Format(memory.TotalRam)} · {memory.GpuName}" + (memory.GpuBytes > 0 ? $" VRAM {(memory.AvailableGpuBytes is long available ? FileSize.Format(available) + " 空き / " : "空き不明 / ")}{FileSize.Format(memory.GpuBytes)}（OS計測）" : "") +
            $"\n重み {FileSize.Format(weights)} · 動作メモリ概算 {FileSize.Format(lower)}〜{FileSize.Format(upper)}（記憶 {context:N0} token）\n{fit}\n" +
            (kv == 0 ? "KVキャッシュの詳細は不明です。実際の必要量はモデルによって増えます。" : "KVは全層attention・F16としての概算です。ハイブリッド型では過大な値になる場合があります。") + " 内蔵GPUはRAMと共有します。動作や速度を保証する値ではありません。";
    }
    public static (long Lower, long Upper) Estimate(long weights, ModelProfile? profile, int context) => (weights + (profile?.EstimatedKvBytes(context) ?? 0) + (512L << 20), weights + (profile?.EstimatedKvBytes(context) ?? 0) + (2L << 30));
}
