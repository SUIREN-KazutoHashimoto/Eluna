using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Interop;

namespace Eluna.App.Services;

// Version 1 contains WPF desktop DIPs; version 2 contains physical screen pixels.
internal sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized,
    int Version = 1, string? MonitorId = null, string? MonitorDevice = null, WindowArea? WorkArea = null, double Dpi = 96)
{
    internal WindowArea Bounds => new(Left, Top, Width, Height);
    internal bool IsValid => Bounds.IsValid && Version is 1 or 2 && double.IsFinite(Dpi) && Dpi is >= 48 and <= 768
        && (Version == 1 || WorkArea is { IsValid: true });

    internal static WindowRestorePlan? Plan(WindowPlacement saved, IReadOnlyList<WindowDisplay> displays, double minWidth, double minHeight)
    {
        if (!saved.IsValid || displays.Count == 0) { return null; }
        WindowDisplay primary = displays.FirstOrDefault(d => d.Primary) ?? displays[0];
        double legacyScale = primary.Dpi / 96;
        WindowArea previous = saved.Version == 1
            ? new(saved.Left * legacyScale, saved.Top * legacyScale, saved.Width * legacyScale, saved.Height * legacyScale) : saved.Bounds;
        // Interface IDs survive desktop rearrangement. Do not match a reused DISPLAY number to a different device.
        WindowDisplay? target = saved.Version == 2 ? displays.FirstOrDefault(d => !string.IsNullOrEmpty(saved.MonitorId)
            ? string.Equals(d.Id, saved.MonitorId, StringComparison.OrdinalIgnoreCase)
            : string.Equals(d.Device, saved.MonitorDevice, StringComparison.OrdinalIgnoreCase)) : null;
        target ??= displays.OrderByDescending(d => Intersection(previous, d.WorkArea)).ThenBy(d => Distance(previous, d.WorkArea)).ThenByDescending(d => d.Primary).First();
        double scale = target.Dpi / 96;
        double ratio = saved.Version == 2 ? target.Dpi / saved.Dpi : target.Dpi / primary.Dpi;
        double width = Math.Clamp(previous.Width * ratio, Math.Min(minWidth * scale, target.WorkArea.Width), target.WorkArea.Width);
        double height = Math.Clamp(previous.Height * ratio, Math.Min(minHeight * scale, target.WorkArea.Height), target.WorkArea.Height);
        width = Math.Round(width); height = Math.Round(height);
        double left = saved.Version == 2 ? target.WorkArea.Left + (saved.Left - saved.WorkArea!.Left) * ratio : previous.Left;
        double top = saved.Version == 2 ? target.WorkArea.Top + (saved.Top - saved.WorkArea!.Top) * ratio : previous.Top;
        // Integral physical coordinates avoid accumulating fractional-pixel drift across restarts.
        var bounds = new WindowArea(Math.Round(Math.Clamp(left, target.WorkArea.Left, target.WorkArea.Right - width)),
            Math.Round(Math.Clamp(top, target.WorkArea.Top, target.WorkArea.Bottom - height)), Math.Round(width), Math.Round(height));
        return new(target, bounds, saved.Maximized);
    }

    private static double Intersection(WindowArea a, WindowArea b) => Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left)) * Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top));
    private static double Distance(WindowArea a, WindowArea b)
    {
        double x = Math.Max(0, Math.Max(b.Left - a.Right, a.Left - b.Right)), y = Math.Max(0, Math.Max(b.Top - a.Bottom, a.Top - b.Bottom));
        return x * x + y * y;
    }

    internal static void Restore(Window window, string root)
    {
        // Preview windows explicitly request off-screen manual positioning.
        if (window.WindowStartupLocation == WindowStartupLocation.Manual) { return; }
        try
        {
            string path = Path.Combine(root, "window.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 16_384) { return; }
            WindowPlacement? saved = JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(path));
            WindowRestorePlan? plan = saved is null ? null : Plan(saved, WindowNative.Displays(), window.MinWidth, window.MinHeight);
            if (plan is null) { return; }
            double minimumWidth = window.MinWidth, minimumHeight = window.MinHeight;
            void FitMinimums()
            {
                if (WindowNative.DisplayFor(new WindowInteropHelper(window).Handle) is { } display)
                {
                    window.MinWidth = Math.Min(minimumWidth, display.WorkArea.Width * 96 / display.Dpi);
                    window.MinHeight = Math.Min(minimumHeight, display.WorkArea.Height * 96 / display.Dpi);
                }
            }
            window.MinWidth = Math.Min(minimumWidth, plan.Display.WorkArea.Width * 96 / plan.Display.Dpi);
            window.MinHeight = Math.Min(minimumHeight, plan.Display.WorkArea.Height * 96 / plan.Display.Dpi);
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            if (!WindowNative.Apply(window, plan)) { return; }
            window.LocationChanged += (_, _) => FitMinimums();
            window.DpiChanged += (_, _) => FitMinimums();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }

    internal static async Task SaveAsync(Window window, string root)
    {
        WindowPlacement? saved = WindowNative.Capture(window);
        if (saved is null) { return; }
        string temporary = Path.Combine(root, "window." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(saved));
            File.Move(temporary, Path.Combine(root, "window.json"), true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally
        {
            try { File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

internal sealed record WindowArea(double Left, double Top, double Width, double Height)
{
    [JsonIgnore]
    public double Right => Left + Width;
    [JsonIgnore]
    public double Bottom => Top + Height;
    [JsonIgnore]
    public bool IsValid => new[] { Left, Top, Width, Height }.All(n => double.IsFinite(n) && Math.Abs(n) <= 10_000_000) && Width > 0 && Height > 0;
}
internal sealed record WindowDisplay(string Id, string Device, WindowArea Bounds, WindowArea WorkArea, double Dpi, bool Primary);
internal sealed record WindowRestorePlan(WindowDisplay Display, WindowArea Bounds, bool Maximized);
