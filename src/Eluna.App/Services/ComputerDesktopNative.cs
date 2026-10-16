using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Eluna.App.Models;

namespace Eluna.App.Services;

// Runs in the disposable worker process: a blocked third-party UIA provider must not block Eluna.
internal static class ComputerDesktopNative
{
    internal static bool IsDesktopLocked()
    {
        _ = GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        if (pid == 0) { return false; }
        try { using Process foreground = Process.GetProcessById((int)pid); return foreground.ProcessName.Equals("LockApp", StringComparison.OrdinalIgnoreCase) || foreground.ProcessName.Equals("LogonUI", StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { return false; }
    }
    internal static void RequireUnlockedDesktop()
    { if (IsDesktopLocked()) { throw new InvalidOperationException("Windowsがロックされています。ロックを解除してからComputer Useを再開してください。"); } }
    internal static string DesktopStatus(ComputerTarget target)
    {
        nint foreground = GetForegroundWindow(); _ = GetWindowThreadProcessId(foreground, out uint pid);
        _ = DwmGetWindowAttribute((nint)target.Handle, 14, out int cloaked, sizeof(int));
        nint input = OpenInputDesktop(0, false, 1);
        string Name(nint desktop) { var text = new StringBuilder(128); return GetUserObjectInformationW(desktop, 2, text, 256, out _) ? text.ToString() : "unavailable"; }
        try { return $"foreground={foreground:X}; foregroundPid={pid}; target={target.Handle:X}; cloaked={cloaked}; visible={IsWindowVisible((nint)target.Handle)}; minimized={IsIconic((nint)target.Handle)}; threadDesktop={Name(GetThreadDesktop(GetCurrentThreadId()))}; inputDesktop={Name(input)}"; }
        finally { if (input != 0) { _ = CloseDesktop(input); } }
    }
    internal static bool TargetInForeground(ComputerTarget target) => GetForegroundWindow() == (nint)target.Handle;
    internal static List<ComputerTarget> Windows(int parent)
    {
        var result = new List<ComputerTarget>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || GetWindowTextLength(handle) == 0) { return true; }
            GetWindowThreadProcessId(handle, out uint pid);
            if (pid == parent || pid == Environment.ProcessId) { return true; }
            try
            {
                using Process process = Process.GetProcessById((int)pid);
                string title = Title(handle);
                if (title.Length > 0 && GetWindowRect(handle, out NativeRect rect) && rect.Right - rect.Left > 30 && rect.Bottom - rect.Top > 30)
                { result.Add(new(handle.ToInt64(), (int)pid, process.StartTime.ToUniversalTime().Ticks, title, process.ProcessName)); }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { }
            return result.Count < 128;
        }, 0);
        return result;
    }

    private static nint Validate(ComputerTarget target, int parent)
    {
        nint handle = (nint)target.Handle;
        GetWindowThreadProcessId(handle, out uint pid);
        if (!IsWindow(handle) || !IsWindowVisible(handle) || pid != target.ProcessId || pid == parent || pid == Environment.ProcessId)
        { throw new InvalidOperationException("選択したアプリが閉じられたか、対象が変わりました。アプリ一覧を更新してください。"); }
        using Process process = Process.GetProcessById((int)pid);
        if (process.StartTime.ToUniversalTime().Ticks != target.Started) { throw new InvalidOperationException("対象アプリが再起動されました。選択し直してください。"); }
        if (IsIconic(handle)) { throw new InvalidOperationException("対象アプリが最小化されています。表示してから実行してください。"); }
        nint popup = GetLastActivePopup(handle);
        if (popup != handle && IsWindowVisible(popup) && GetAncestor(popup, 3) == GetAncestor(handle, 3)) { return popup; }
        return handle;
    }

    internal static ComputerObservation Observe(ComputerTarget target, int parent, bool screenCapture = false)
    {
        RequireUnlockedDesktop();
        nint surface = Validate(target, parent);
        if (!GetWindowRect(surface, out NativeRect rect)) { throw new Win32Exception(); }
        var bounds = new ComputerBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        (List<ComputerElement> elements, _) = Elements(surface);
        (string image, int width, int height, byte[] signature) = Capture(surface, bounds, elements, parent, screenCapture);
        return new(target, surface.ToInt64(), Title(surface), bounds, elements, image, width, height, signature,
            elements.Count == 0 ? "UI要素が得られません。画像対応モデルで座標を判断するか、対象アプリを確認してください。" :
            elements.Count >= 256 ? "要素は先頭256件です。必要な位置までスクロールして再確認します。" : "");
    }

    private static (List<ComputerElement> Items, Dictionary<string, AutomationElement> Live) Elements(nint surface)
    {
        var items = new List<ComputerElement>(); var live = new Dictionary<string, AutomationElement>();
        try
        {
            var pending = new Queue<(AutomationElement Element, int Depth)>();
            pending.Enqueue((AutomationElement.FromHandle(surface), 0));
            TreeWalker walker = TreeWalker.ControlViewWalker; int visited = 0;
            while (pending.TryDequeue(out var next) && visited++ < 640 && items.Count < 256)
            {
                try
                {
                    AutomationElement e = next.Element; AutomationElement.AutomationElementInformation info = e.Current;
                    Rect r = info.BoundingRectangle;
                    if (!info.IsOffscreen && !r.IsEmpty && r.Width > 0 && r.Height > 0)
                    {
                        string name = Limit(info.Name, 240), value = "";
                        var patterns = new List<string>();
                        foreach ((AutomationPattern pattern, string label) in new[] { (InvokePattern.Pattern, "invoke"), (ValuePattern.Pattern, "value"), (SelectionItemPattern.Pattern, "select"), (TogglePattern.Pattern, "toggle"), (ExpandCollapsePattern.Pattern, "expand") })
                        { if (e.TryGetCurrentPattern(pattern, out _)) { patterns.Add(label); } }
                        if (!info.IsPassword)
                        {
                            if (e.TryGetCurrentPattern(ValuePattern.Pattern, out object p)) { value = Limit(((ValuePattern)p).Current.Value, 800); }
                            else if (e.TryGetCurrentPattern(TextPattern.Pattern, out p))
                            { value = Limit(string.Join("\n", ((TextPattern)p).GetVisibleRanges().Take(8).Select(t => t.GetText(800))), 1600); }
                        }
                        string runtime = string.Join('.', e.GetRuntimeId());
                        var item = new ComputerElement(items.Count + 1, runtime, info.IsPassword ? "［パスワード欄］" : name,
                            info.ControlType.ProgrammaticName.Replace("ControlType.", "", StringComparison.Ordinal), info.IsPassword ? "［非表示］" : value,
                            new((int)r.Left, (int)r.Top, (int)Math.Ceiling(r.Width), (int)Math.Ceiling(r.Height)), info.IsPassword, info.IsEnabled, [.. patterns]);
                        items.Add(item); live[runtime] = e;
                    }
                    if (next.Depth < 16)
                    {
                        AutomationElement? child = walker.GetFirstChild(next.Element); int siblings = 0;
                        while (child is not null && siblings++ < 256 && pending.Count < 640)
                        { pending.Enqueue((child, next.Depth + 1)); child = walker.GetNextSibling(child); }
                    }
                }
                catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException) { }
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException) { }
        return (items, live);
    }

    internal static void Execute(ComputerWorkerRequest request)
    {
        ComputerObservation before = request.Observation ?? throw new InvalidDataException("操作前の画面がありません。");
        ComputerAction action = request.Action ?? throw new InvalidDataException("操作がありません。");
        ComputerActionPolicy.Validate(action);
        ComputerObservation now = Observe(before.Target, request.Parent);
        ComputerActionPolicy.RequireFresh(before, now, action);
        nint surface = (nint)now.Surface;
        Activate(surface);
        // Activation can reveal a dialog or change layout; inspect again before any input.
        now = Observe(before.Target, request.Parent);
        ComputerActionPolicy.RequireFresh(before, now, action);
        if (new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0))
        { throw new InvalidOperationException("修飾キーが押されています。キーを離して実行し直してください。"); }
        ComputerElement? expected = action.Element == 0 ? null : before.Elements.Single(e => e.Id == action.Element);
        AutomationElement? element = null;
        if (expected is not null)
        {
            (_, Dictionary<string, AutomationElement> live) = Elements(surface);
            if (!live.TryGetValue(expected.RuntimeId, out element) || element.Current.IsPassword || !element.Current.IsEnabled)
            { throw new ComputerScreenChangedException(); }
        }
        int x = expected is null ? now.Bounds.Left + (int)((now.Bounds.Width - 1L) * action.X / 1000) : expected.Bounds.Left + expected.Bounds.Width / 2;
        int y = expected is null ? now.Bounds.Top + (int)((now.Bounds.Height - 1L) * action.Y / 1000) : expected.Bounds.Top + expected.Bounds.Height / 2;
        if (action.Type is "click" or "double_click" or "right_click" or "drag" or "scroll")
        {
            if (now.Elements.Any(e => e.Password && e.Bounds.Contains(x, y))) { throw new InvalidOperationException("パスワード欄は操作しません。"); }
            PointTarget(surface, now.Bounds, x, y);
        }
        switch (action.Type)
        {
            case "click" when element is not null && element.TryGetCurrentPattern(InvokePattern.Pattern, out object p): ((InvokePattern)p).Invoke(); break;
            case "click" when element is not null && element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object p): ((SelectionItemPattern)p).Select(); break;
            case "click" when element is not null && element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object p):
                var expand = (ExpandCollapsePattern)p; if (expand.Current.ExpandCollapseState == ExpandCollapseState.Collapsed) { expand.Expand(); } else { expand.Collapse(); }
                break;
            case "click" when element is not null && element.TryGetCurrentPattern(TogglePattern.Pattern, out object p): ((TogglePattern)p).Toggle(); break;
            case "click": Move(x, y); Send(Mouse(2), Mouse(4)); break;
            case "double_click": Move(x, y); Send(Mouse(2), Mouse(4), Mouse(2), Mouse(4)); break;
            case "right_click": Move(x, y); Send(Mouse(8), Mouse(16)); break;
            case "set_value":
                if (element is null || !element.TryGetCurrentPattern(ValuePattern.Pattern, out object v) || ((ValuePattern)v).Current.IsReadOnly)
                { throw new InvalidOperationException("この入力欄は置き換えに対応していません。文字入力を使ってください。"); }
                ((ValuePattern)v).SetValue(action.Text); break;
            case "type_text":
                element?.SetFocus(); FocusIsSafe(surface, now.Bounds);
                foreach (char c in action.Text) { Foreground(surface); Send(Key(0, c, 4), Key(0, c, 6)); }
                break;
            case "key":
                element?.SetFocus(); FocusIsSafe(surface, now.Bounds);
                ushort[] keys = ComputerActionPolicy.Keys(action.Keys);
                Send([.. keys.Select(k => Key(k, '\0', 0)), .. keys.Reverse().Select(k => Key(k, '\0', 2))]); break;
            case "scroll": Move(x, y); Send(Mouse(0x800, unchecked((uint)(-action.Amount * 120)))); break;
            case "drag":
                int endX = now.Bounds.Left + (int)((now.Bounds.Width - 1L) * action.X2 / 1000), endY = now.Bounds.Top + (int)((now.Bounds.Height - 1L) * action.Y2 / 1000);
                Move(x, y); Send(Mouse(2));
                try { for (int i = 1; i <= 20; i++) { int dx = x + (endX - x) * i / 20, dy = y + (endY - y) * i / 20; PointTarget(surface, now.Bounds, dx, dy); Move(dx, dy); Thread.Sleep(15); } }
                finally { ReleaseDrag(); }
                break;
            default: throw new InvalidDataException("この操作は直接入力できません。");
        }
    }

    private static void FocusIsSafe(nint surface, ComputerBounds bounds)
    {
        Foreground(surface); AutomationElement focused = AutomationElement.FocusedElement;
        Rect rect = focused.Current.BoundingRectangle;
        string rootId = string.Join('.', AutomationElement.FromHandle(surface).GetRuntimeId());
        AutomationElement? ancestor = focused; bool belongs = false;
        for (int i = 0; ancestor is not null && i < 32; i++)
        { if (string.Join('.', ancestor.GetRuntimeId()) == rootId) { belongs = true; break; } ancestor = TreeWalker.ControlViewWalker.GetParent(ancestor); }
        if (!belongs || focused.Current.IsPassword || rect.IsEmpty || !bounds.Contains((int)(rect.Left + rect.Width / 2), (int)(rect.Top + rect.Height / 2)))
        { throw new InvalidOperationException("入力先を確認できないか、パスワード欄です。利用者が対象アプリで確認してください。"); }
    }
    private static void Foreground(nint surface)
    { if (GetForegroundWindow() != surface) { RequireUnlockedDesktop(); throw new InvalidOperationException("対象アプリを前面にできませんでした。対象をクリックしてから再実行してください。"); } }
    private static void Activate(nint surface)
    {
        _ = SetForegroundWindow(surface);
        for (int i = 0; i < 20 && GetForegroundWindow() != surface; i++) { Thread.Sleep(25); }
        Foreground(surface);
    }
    private static void PointTarget(nint surface, ComputerBounds bounds, int x, int y)
    {
        Foreground(surface);
        if (!bounds.Contains(x, y) || GetAncestor(WindowFromPoint(new(x, y)), 2) != surface)
        { throw new InvalidOperationException("対象の位置が別のウィンドウに隠れています。対象アプリを表示して再実行してください。"); }
    }
    private static string Limit(string? text, int max) => text is null ? "" : text[..Math.Min(max, text.Length)];
    private static string Title(nint handle) { var text = new StringBuilder(Math.Min(4096, GetWindowTextLength(handle) + 1)); GetWindowText(handle, text, text.Capacity); return Limit(text.ToString(), 300); }

    private static (string Image, int Width, int Height, byte[] Signature) Capture(nint surface, ComputerBounds bounds, List<ComputerElement> elements, int parent, bool screenCapture)
    {
        int width = bounds.Width, height = bounds.Height;
        if (width < 1 || height < 1 || width > 8192 || height > 8192 || (long)width * height > 16000000)
        { throw new InvalidOperationException("対象ウィンドウが大きすぎます。サイズを小さくしてください。"); }
        nint screen = GetDC(0), dc = CreateCompatibleDC(screen), bitmap = 0, previous = 0;
        try
        {
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, Bits = 32 };
            bitmap = CreateDIBSection(screen, ref info, 0, out nint pixels, 0, 0);
            if (bitmap == 0 || dc == 0) { throw new Win32Exception(); }
            previous = SelectObject(dc, bitmap);
            bool printed = !screenCapture && PrintWindow(surface, dc, 2);
            var data = new byte[checked(width * height * 4)]; Marshal.Copy(pixels, data, 0, data.Length);
            bool blank = Enumerable.Range(0, Math.Min(1024, data.Length / 4)).All(i => data[i * (data.Length / 4 / Math.Min(1024, data.Length / 4)) * 4] == 0);
            if (!printed || blank)
            {
                Foreground(surface);
                if (!BitBlt(dc, 0, 0, width, height, screen, bounds.Left, bounds.Top, 0x00CC0020 | 0x40000000)) { throw new Win32Exception(); }
                Marshal.Copy(pixels, data, 0, data.Length);
                // A visible-screen fallback must not expose overlapping windows belonging to other apps.
                EnumWindows((h, _) =>
                {
                    if (h == surface) { return false; }
                    // Skip only verified capture-excluded effects owned by this Eluna process.
                    // Other Eluna windows (including approvals) must still be masked.
                    if (ComputerUseOverlay.IsExcludedWindow(h, parent)) { return true; }
                    if (IsWindowVisible(h) && GetWindowRect(h, out NativeRect r)) { Mask(data, width, height, bounds, new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)); }
                    return true;
                }, 0);
            }
            foreach (ComputerElement password in elements.Where(e => e.Password)) { Mask(data, width, height, bounds, password.Bounds); }
            for (int i = 3; i < data.Length; i += 4) { data[i] = 255; }
            BitmapSource source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, data, width * 4);
            double scale = Math.Min(1, 1280d / Math.Max(width, height));
            BitmapSource resized = scale < 1 ? new TransformedBitmap(source, new ScaleTransform(scale, scale)) : source;
            resized.Freeze();
            byte[] signature = new byte[32 * 24];
            for (int y = 0; y < 24; y++) { for (int x = 0; x < 32; x++) { int offset = (y * (height - 1) / 23 * width + x * (width - 1) / 31) * 4; signature[y * 32 + x] = (byte)((data[offset] + data[offset + 1] + data[offset + 2]) / 3); } }
            using var output = new MemoryStream(); var encoder = new JpegBitmapEncoder { QualityLevel = 85 }; encoder.Frames.Add(BitmapFrame.Create(resized)); encoder.Save(output);
            return (Convert.ToBase64String(output.ToArray()), resized.PixelWidth, resized.PixelHeight, signature);
        }
        finally { if (previous != 0) { SelectObject(dc, previous); } if (bitmap != 0) { DeleteObject(bitmap); } if (dc != 0) { DeleteDC(dc); } ReleaseDC(0, screen); }
    }
    private static void Mask(byte[] data, int width, int height, ComputerBounds window, ComputerBounds area)
    {
        int left = Math.Clamp(area.Left - window.Left, 0, width), top = Math.Clamp(area.Top - window.Top, 0, height);
        int right = Math.Clamp(area.Left + area.Width - window.Left, 0, width), bottom = Math.Clamp(area.Top + area.Height - window.Top, 0, height);
        for (int y = top; y < bottom; y++) { for (int x = left; x < right; x++) { int i = (y * width + x) * 4; data[i] = data[i + 1] = data[i + 2] = 24; } }
    }

    internal static void ReleaseDrag() => Send(Mouse(4));
    private static void Move(int x, int y)
    {
        int left = GetSystemMetrics(76), top = GetSystemMetrics(77), width = GetSystemMetrics(78), height = GetSystemMetrics(79);
        Send(new Input { Type = 0, Data = new InputUnion { Mouse = new() { X = (int)((x - (long)left) * 65535 / Math.Max(1, width - 1)), Y = (int)((y - (long)top) * 65535 / Math.Max(1, height - 1)), Flags = 1 | 0x8000 | 0x4000 } } });
    }
    private static Input Mouse(uint flags, uint data = 0) => new() { Type = 0, Data = new() { Mouse = new() { Flags = flags, Data = data } } };
    private static Input Key(ushort key, char scan, uint flags) => new() { Type = 1, Data = new() { Keyboard = new() { Key = key, Scan = scan, Flags = flags } } };
    private static void Send(params Input[] inputs)
    {
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length) { return; }
        Input[] releases = inputs.Where(i => i.Type == 1).Select(i => { i.Data.Keyboard.Flags |= 2; return i; }).ToArray();
        if (releases.Length > 0) { _ = SendInput((uint)releases.Length, releases, Marshal.SizeOf<Input>()); }
        if (inputs.Any(i => i.Type == 0 && (i.Data.Mouse.Flags & 2) != 0)) { Input[] up = [Mouse(4)]; _ = SendInput(1, up, Marshal.SizeOf<Input>()); }
        if (inputs.Any(i => i.Type == 0 && (i.Data.Mouse.Flags & 8) != 0)) { Input[] up = [Mouse(16)]; _ = SendInput(1, up, Marshal.SizeOf<Input>()); }
        throw new InvalidOperationException("Windowsが入力を受け付けませんでした。管理者アプリ・保護画面は操作できません。");
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativePoint(int X, int Y);
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize; public int XPixels, YPixels; public uint Colors, Important; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
    private delegate bool WindowCallback(nint window, nint data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint data);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    [DllImport("user32.dll")] private static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll")] private static extern nint GetThreadDesktop(uint thread);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetUserObjectInformationW(nint obj, int index, StringBuilder info, uint length, out uint needed);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern nint GetLastActivePopup(nint window);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint window, nint dc, uint flags);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint pixels, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sx, int sy, uint operation);
}
