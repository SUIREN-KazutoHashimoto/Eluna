using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Eluna.App.Models;

namespace Eluna.App.Services;

// Two nonactivating windows: the adornment never receives input; only the compact controls do.
// Capture exclusion is required before either window is shown. No desktop-wide input hook is used.
internal sealed class ComputerUseOverlay : IDisposable
{
    internal const string WindowProperty = "Eluna.ComputerUse.CaptureExcluded.v1";
    private readonly Window _frame, _bar;
    private readonly Effects _effects = new();
    private readonly TextBlock _status, _target;
    private readonly DispatcherTimer _timer;
    private ComputerObservation? _observation;
    private ComputerAction? _action;
    private readonly Stopwatch _actionTime = new();
    private bool _active, _disposed;
    private readonly bool _available;
    internal bool Available => _available;
    internal bool Visible => _frame.IsVisible || _bar.IsVisible;

    internal ComputerUseOverlay(Action pause, Action stop)
    {
        _frame = MakeWindow("Eluna Computer Use 操作表示", _effects);
        var row = new Grid { Margin = new(14, 9, 10, 9) };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _status = new() { Text = "Elunaが操作中", FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        _target = new() { Text = "Ctrl + Alt + Esc で停止", FontSize = 11, Margin = new(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        lines.Children.Add(_status); lines.Children.Add(_target); row.Children.Add(lines);
        Button Button(string text, int column, Action command)
        {
            var button = new Button { Content = text, FontSize = 12, Padding = new(12, 7, 12, 7), Margin = new(8, 0, 0, 0), MinHeight = 36, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(button, "Computer Use " + text);
            button.Click += (_, _) => { SetActive(false); command(); };
            Grid.SetColumn(button, column); row.Children.Add(button); return button;
        }
        _ = Button("一時停止", 1, pause); _ = Button("停止", 2, stop);
        var border = new Border { CornerRadius = new(14), BorderThickness = new(1), Child = row };
        _bar = MakeWindow("Eluna Computer Use コントロール", border);
        // Do not inherit chat font scaling: this surface must fit the monitor's work area.
        if (SystemParameters.HighContrast)
        {
            border.Background = SystemColors.WindowBrush; border.BorderBrush = SystemColors.WindowTextBrush;
            _bar.Foreground = SystemColors.WindowTextBrush;
        }
        else
        {
            border.Background = Brush("#F51C2028"); border.BorderBrush = Brush("#887F9CFF");
            _bar.Foreground = Brushes.White; _target.Foreground = Brush("#BFC7D7");
        }
        bool frameReady = Prepare(_frame, true), barReady = Prepare(_bar, false);
        _available = frameReady && barReady;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += Tick;
    }

    private static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    private static Window MakeWindow(string title, UIElement content) => new()
    {
        Title = title,
        Content = content,
        WindowStyle = WindowStyle.None,
        ResizeMode = ResizeMode.NoResize,
        AllowsTransparency = true,
        Background = Brushes.Transparent,
        ShowInTaskbar = false,
        ShowActivated = false,
        Topmost = true,
        Width = 1,
        Height = 1,
        WindowStartupLocation = WindowStartupLocation.Manual,
        FontFamily = new("Segoe UI"),
        UseLayoutRounding = true
    };
    private static bool Prepare(Window window, bool transparent)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) { return false; }
        nint handle = new WindowInteropHelper(window).EnsureHandle();
        nint style = GetWindowLongPtrW(handle, -20);
        _ = SetWindowLongPtrW(handle, -20, style | 0x08000000 | 0x80 | (transparent ? 0x20 : 0)); // NOACTIVATE, TOOLWINDOW, TRANSPARENT
        // Windows 10 2004+ excludes the surface rather than painting a black capture rectangle.
        if (!SetWindowDisplayAffinity(handle, 0x11) || !GetWindowDisplayAffinity(handle, out uint affinity) || affinity != 0x11) { return false; }
        return SetPropW(handle, WindowProperty, (nint)1);
    }
    internal static bool IsExcludedWindow(nint window, int parent)
    {
        _ = GetWindowThreadProcessId(window, out uint pid);
        return pid == parent && GetPropW(window, WindowProperty) == (nint)1 && GetWindowDisplayAffinity(window, out uint affinity) && affinity == 0x11;
    }

    internal void SetActive(bool active)
    {
        if (_disposed) { return; }
        _active = active;
        if (active && _available) { _timer.Start(); Update(); }
        else { _timer.Stop(); _frame.Hide(); _bar.Hide(); _action = null; _actionTime.Reset(); }
    }
    internal void Observe(ComputerObservation observation)
    {
        _observation = observation; _action = null; _actionTime.Reset(); _status.Text = "Elunaが画面を確認中"; Update();
    }
    internal void Status(string text)
    {
        _status.Text = text.Contains("準備", StringComparison.Ordinal) || _observation is null ? "Elunaが準備中" : "Elunaが操作を検討中";
    }
    internal void BeforeAction(ComputerObservation observation, ComputerAction action)
    {
        _observation = observation; _action = action; _actionTime.Restart(); _status.Text = "Eluna · " + action.Label; Update();
    }
    internal void AfterAction() { _actionTime.Restart(); }
    internal async Task RenderPreviewAsync(string path)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        double frameWidth = _frame.IsVisible ? _frame.ActualWidth : _observation?.Bounds.Width ?? 620;
        double frameHeight = _frame.IsVisible ? _frame.ActualHeight : _observation?.Bounds.Height ?? 460;
        if (!_frame.IsVisible)
        {
            _effects.Bounds = _observation!.Bounds; _effects.Observation = _observation; _effects.Action = _action; _effects.Progress = 0;
            _effects.Measure(new(frameWidth, frameHeight)); _effects.Arrange(new(0, 0, frameWidth, frameHeight)); _effects.InvalidateVisual();
        }
        var barContent = (FrameworkElement)_bar.Content;
        if (!_bar.IsVisible) { barContent.Measure(new(490, 68)); barContent.Arrange(new(0, 0, 490, 68)); }
        double barWidth = barContent.ActualWidth, barHeight = barContent.ActualHeight;
        var visual = new DrawingVisual(); double width = Math.Max(frameWidth, barWidth), height = frameHeight + barHeight + 24;
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brush("#15171D"), null, new(0, 0, width, height));
            if (_observation is not null)
            {
                using var stream = new MemoryStream(Convert.FromBase64String(_observation.ImageBase64));
                BitmapFrame bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                dc.DrawImage(bitmap, new(0, 0, frameWidth, frameHeight));
            }
            dc.DrawRectangle(new VisualBrush(_effects), null, new(0, 0, frameWidth, frameHeight));
            dc.DrawRectangle(new VisualBrush(barContent), null, new((width - barWidth) / 2, frameHeight + 12, barWidth, barHeight));
        }
        var image = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32); image.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        await using var output = File.Create(path); encoder.Save(output);
    }
    private void Tick(object? sender, EventArgs e) => Update();
    private void Update()
    {
        if (!_active || !_available || _disposed) { return; }
        _timer.Interval = TimeSpan.FromMilliseconds(_action is not null && _actionTime.Elapsed.TotalSeconds < 1.1 && SystemParameters.ClientAreaAnimation ? 40 : 200);
        nint surface = (nint)(_observation?.Surface ?? 0);
        bool valid = surface != 0 && IsWindow(surface) && !IsIconic(surface);
        ComputerBounds? bounds = null;
        if (valid && GetWindowRect(surface, out NativeRect rect)) { bounds = new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top); }
        bool current = bounds is not null && GetForegroundWindow() == surface;
        if (current)
        {
            Position(_frame, bounds!);
            _effects.Bounds = bounds!;
            _effects.Action = _actionTime.Elapsed.TotalSeconds < 1.1 ? _action : null;
            _effects.Observation = _observation;
            _effects.Progress = Math.Min(1, _actionTime.Elapsed.TotalSeconds / 1.1);
            _effects.InvalidateVisual();
        }
        else { _frame.Hide(); }
        nint monitor = MonitorFromWindow(surface, 2); var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(monitor, ref info)) { _bar.Hide(); return; }
        var work = new ComputerBounds(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
        double scale = (surface == 0 ? 96 : GetDpiForWindow(surface)) / 96d; if (scale <= 0) { scale = 1; }
        ComputerBounds? avoid = current && _action is not null ? ActionArea(_observation!, _action) : null;
        ComputerBounds? placement = BarPlacement(work, (int)Math.Round(490 * scale), (int)Math.Round(68 * scale), avoid);
        if (placement is null) { _bar.Hide(); }
        else { Position(_bar, placement); }
        string title = _observation?.Title ?? "Computer Use";
        _target.Text = title + "  ·  Ctrl + Alt + Esc で停止";
        _bar.ToolTip = title + "\nCtrl + Alt + Esc で停止";
    }

    internal static ComputerBounds? ActionArea(ComputerObservation observation, ComputerAction action)
    {
        if (action.Element != 0)
        {
            ComputerElement? element = observation.Elements.FirstOrDefault(e => e.Id == action.Element);
            return element is { Password: false } ? element.Bounds : null;
        }
        if (action.Type is not ("click" or "double_click" or "right_click" or "drag" or "scroll")) { return null; }
        ComputerBounds b = observation.Bounds;
        int x = b.Left + (int)((b.Width - 1L) * action.X / 1000), y = b.Top + (int)((b.Height - 1L) * action.Y / 1000);
        int x2 = action.Type == "drag" ? b.Left + (int)((b.Width - 1L) * action.X2 / 1000) : x;
        int y2 = action.Type == "drag" ? b.Top + (int)((b.Height - 1L) * action.Y2 / 1000) : y;
        return new(Math.Min(x, x2) - 28, Math.Min(y, y2) - 28, Math.Abs(x - x2) + 56, Math.Abs(y - y2) + 56);
    }
    internal static ComputerBounds? BarPlacement(ComputerBounds work, int width, int height, ComputerBounds? avoid)
    {
        const int margin = 12;
        width = Math.Min(width, work.Width - margin * 2); height = Math.Min(height, work.Height - margin * 2);
        if (width < 300 || height < 48) { return null; }
        int x = work.Left + (work.Width - width) / 2;
        ComputerBounds[] choices = [new(x, work.Top + work.Height - height - margin, width, height), new(x, work.Top + margin, width, height)];
        return choices.FirstOrDefault(p => avoid is null || p.Left >= avoid.Left + avoid.Width || p.Left + p.Width <= avoid.Left || p.Top >= avoid.Top + avoid.Height || p.Top + p.Height <= avoid.Top);
    }
    private static void Position(Window window, ComputerBounds bounds)
    {
        if (!window.IsVisible) { window.Show(); }
        _ = SetWindowPos(new WindowInteropHelper(window).Handle, (nint)(-1), bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x10 | 0x40); // never activate
    }
    public void Dispose()
    {
        if (_disposed) { return; }
        SetActive(false); _disposed = true; _timer.Tick -= Tick;
        foreach (Window window in new[] { _frame, _bar })
        {
            _ = RemovePropW(new WindowInteropHelper(window).Handle, WindowProperty); window.Close();
        }
    }

    private sealed class Effects : FrameworkElement
    {
        internal ComputerBounds Bounds { get; set; } = new(0, 0, 1, 1);
        internal ComputerObservation? Observation { get; set; }
        internal ComputerAction? Action { get; set; }
        internal double Progress { get; set; }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc); if (ActualWidth < 10 || ActualHeight < 10) { return; }
            Brush accent = SystemParameters.HighContrast ? SystemColors.HighlightBrush : Brush("#9DAEFF");
            Brush edge = SystemParameters.HighContrast ? accent : new LinearGradientBrush(Color.FromRgb(124, 157, 255), Color.FromRgb(193, 146, 255), 45);
            var r = new Rect(3, 3, ActualWidth - 6, ActualHeight - 6);
            if (!SystemParameters.HighContrast) { dc.PushOpacity(0.15); dc.DrawRoundedRectangle(null, new(edge, 10), r, 13, 13); dc.Pop(); }
            dc.DrawRoundedRectangle(null, new(edge, 2.5), r, 11, 11);
            if (Observation is null || Action is null || Bounds != Observation.Bounds) { return; }
            ComputerBounds? area = ActionArea(Observation, Action); if (area is null) { return; }
            double sx = ActualWidth / Bounds.Width, sy = ActualHeight / Bounds.Height;
            Point Local(double x, double y) => new((x - Bounds.Left) * sx, (y - Bounds.Top) * sy);
            Point p = Action.Element == 0 ? Local(Bounds.Left + (Bounds.Width - 1L) * Action.X / 1000, Bounds.Top + (Bounds.Height - 1L) * Action.Y / 1000)
                : Local(area.Left + area.Width / 2d, area.Top + area.Height / 2d);
            dc.PushClip(new RectangleGeometry(new(0, 0, ActualWidth, ActualHeight)));
            if (Action.Type == "drag")
            {
                Point end = Local(Bounds.Left + (Bounds.Width - 1L) * Action.X2 / 1000, Bounds.Top + (Bounds.Height - 1L) * Action.Y2 / 1000);
                dc.DrawLine(new(accent, 3) { DashStyle = DashStyles.Dash }, p, end); dc.DrawEllipse(null, new(accent, 2), end, 8, 8);
            }
            if (Action.Type is "type_text" or "set_value" or "key")
            {
                Point top = Local(area.Left, area.Top), bottom = Local(area.Left + area.Width, area.Top + area.Height);
                dc.DrawRoundedRectangle(null, new(accent, 2), new(top, bottom), 5, 5);
            }
            else
            {
                double progress = SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast ? Progress : 0;
                dc.PushOpacity(0.22 * (1 - progress)); dc.DrawEllipse(accent, null, p, 20 + progress * 22, 20 + progress * 22); dc.Pop();
                dc.DrawEllipse(null, new(accent, 2), p, 13 + progress * 10, 13 + progress * 10);
                var cursor = new StreamGeometry(); using (StreamGeometryContext c = cursor.Open())
                { c.BeginFigure(p, true, true); c.LineTo(new(p.X + 2, p.Y + 21), true, false); c.LineTo(new(p.X + 8, p.Y + 15), true, false); c.LineTo(new(p.X + 17, p.Y + 14), true, false); }
                dc.DrawGeometry(accent, new(Brushes.White, 1.5), cursor);
            }
            dc.Pop();
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtrW(nint window, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(nint window, out uint affinity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetPropW(nint window, string name, nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint GetPropW(nint window, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint RemovePropW(nint window, string name);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
}
