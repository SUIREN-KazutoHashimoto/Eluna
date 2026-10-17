using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Eluna.App.Services;

internal static class EmojiRasterizer
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int Render([MarshalAs(UnmanagedType.LPWStr)] string text, uint length, uint size, uint stride, [Out] byte[] pixels, uint capacity);
    private static Render? _render;
    private static readonly ConcurrentDictionary<(string Glyph, int Size), Lazy<Task<BitmapSource?>>> Cache = new();
    private static readonly SemaphoreSlim Gate = new(2, 2);
    internal static event Action? Changed;
    internal static bool Ready => _render is not null;
    internal static async Task InitializeAsync(AppStore store)
    {
        if (Ready) { return; }
        string root = await NativeRuntime.PrepareAsync(store);
        string file = Path.Combine(root, "ui", "Eluna.Text.dll");
        if (!File.Exists(file)) { file = Path.Combine(root, "ui", "Release", "Eluna.Text.dll"); }
        if (!File.Exists(file)) { store.Trace.Write("warning", "ui", "emoji.unavailable", "開発用の描画部品がありません。通常のUnicode表示を使用します。"); return; }
        InitializeFile(file); Changed?.Invoke();
    }
    internal static void InitializeFile(string path)
    {
        if (Ready) { return; }
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) { throw new FileNotFoundException("カラー絵文字の描画部品が見つかりません。", path); }
        nint library = NativeLibrary.Load(path); // App-owned absolute path, never a search-path load.
        try { _render = Marshal.GetDelegateForFunctionPointer<Render>(NativeLibrary.GetExport(library, "RenderEmoji")); }
        catch { NativeLibrary.Free(library); throw; }
    }
    internal static bool IsEmoji(string text) => text.EnumerateRunes().Any(r => r.Value is >= 0x1F000 and <= 0x1FAFF or >= 0x2600 and <= 0x27BF or 0x20E3 or 0x3030 or 0x303D or 0x3297 or 0x3299) || text.Contains('\uFE0F');
    internal static IEnumerable<(string Text, int Index, bool Emoji)> Elements(string text)
    {
        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext()) { string element = elements.GetTextElement(); yield return (element, elements.ElementIndex, IsEmoji(element)); }
    }
    internal static BitmapSource? Get(string glyph, double dipSize, double dpiScale = 1)
    {
        if (!Ready || !IsEmoji(glyph)) { return null; }
        int pixels = Math.Clamp((int)Math.Ceiling(dipSize * dpiScale), 8, 512);
        if (Cache.Count > 1024) { Cache.Clear(); }
        Lazy<Task<BitmapSource?>> entry = Cache.GetOrAdd((glyph, pixels), key => new(() => { Task<BitmapSource?> render = RenderAsync(key.Glyph, key.Size); _ = render.ContinueWith(_ => Changed?.Invoke(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default); return render; }));
        return entry.Value.IsCompletedSuccessfully ? entry.Value.Result : null;
    }
    internal static async Task<BitmapSource?> RenderAsync(string glyph, int size)
    {
        if (size is < 8 or > 512 || glyph.Length is 0 or > 64) { return null; }
        await Gate.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                if (_render is not { } render) { return null; }
                byte[] pixels = new byte[size * size * 4];
                Marshal.ThrowExceptionForHR(render(glyph, (uint)glyph.Length, (uint)size, (uint)(size * 4), pixels, (uint)pixels.Length));
                bool actualColor = false;
                for (int i = 0; i < pixels.Length; i += 4)
                { if (pixels[i + 3] > 20 && (pixels[i] != pixels[i + 3] || pixels[i + 1] != pixels[i + 3] || pixels[i + 2] != pixels[i + 3])) { actualColor = true; break; } }
                if (!actualColor) { return null; } // Keep normal theme-colored glyphs for non-color symbols.
                BitmapSource image = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4); image.Freeze(); return image;
            });
        }
        catch (Exception e) when (e is COMException or ArgumentException) { return null; }
        finally { Gate.Release(); }
    }
}
