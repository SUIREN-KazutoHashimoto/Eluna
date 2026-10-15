using System.IO;
using Eluna.App.Models;
using Eluna.App.Services;

namespace Eluna.App.ViewModels;

public sealed partial class ChatViewModel
{
    public string ResourceForecast
    {
        get
        {
            MemoryCapacity capacity = HardwareAdvisor.Capture(ComputeChoices, Settings.Compute == "cpu" ? "auto" : Settings.Compute);
            var chat = Inferences.Items.Where(j => j.State is "active" or "queued").Select(j => { var size = HardwareAdvisor.Estimate(j.Request.Model.AllFiles.Sum(f => f.Size), j.Request.Model.Profile, j.Request.Settings.ContextSize); return (size.Lower, size.Upper, Gpu: j.Request.Settings.Compute != "cpu"); }).ToArray();
            int unknownWeights = 0;
            var media = MediaJobs.Where(j => j.IsPending && j.Request is not null).Select(j =>
            {
                if (j.Request!.Context is { } context) { var size = HardwareAdvisor.Estimate(context.Model.AllFiles.Sum(f => f.Size), context.Model.Profile, context.Settings.ContextSize); return (size.Lower, size.Upper, Gpu: context.Settings.Compute != "cpu"); }
                var image = j.Request.Image!; long weights = 0; try { weights = new FileInfo(image.ModelPath).Length; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { unknownWeights++; }
                long pixels = (long)image.Width * image.Height;
                return (Lower: weights * 2 + pixels * 2048 + (512L << 20), Upper: weights * 4 + pixels * 8192 + (1L << 30), Gpu: image.Compute != "cpu");
            }).ToArray();
            long totalLow = chat.Sum(e => e.Lower) + media.Sum(e => e.Lower), totalHigh = chat.Sum(e => e.Upper) + media.Sum(e => e.Upper);
            long peak = chat.OrderByDescending(e => e.Upper).Take(Settings.MaxConcurrentInferences).Sum(e => e.Upper) + media.OrderByDescending(e => e.Upper).Take(Settings.MaxConcurrentMedia).Sum(e => e.Upper);
            long gpuPeak = chat.Where(e => e.Gpu).OrderByDescending(e => e.Upper).Take(Settings.MaxConcurrentInferences).Sum(e => e.Upper) + media.Where(e => e.Gpu).OrderByDescending(e => e.Upper).Take(Settings.MaxConcurrentMedia).Sum(e => e.Upper);
            return $"空きRAM {FileSize.Format(capacity.AvailableRam)} · {capacity.GpuName}\nVRAM " + (capacity.AvailableGpuBytes is long free ? $"空き {FileSize.Format(free)} / {FileSize.Format(capacity.GpuBytes)}" : "空き不明（OSカウンターを取得できないか、更新中）") +
                $"\n実行・予約 {chat.Length + media.Length}件の概算合計 {FileSize.Format(totalLow)}〜{FileSize.Format(totalHigh)}\n設定した同時枠での最大見込み {FileSize.Format(peak)}\nGPUへ全量を載せる場合の上限目安 {FileSize.Format(gpuPeak)}\n" + (unknownWeights > 0 ? "一部の画像モデルの重み容量は確認できていません。\n" : "") +
                "概算は重み・KV・作業領域を合算。画像モデルは解像度を含む幅のある予測です。CPU/GPUの配分や実際の割当量は変わります。予約全件を同時に使う値ではありません。現在の空き容量には実行中の使用分が反映済みです。";
        }
    }
}
