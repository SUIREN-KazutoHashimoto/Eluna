using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Eluna.App.Services;

internal static class WindowNative
{
    internal static IReadOnlyList<WindowDisplay> Displays()
    {
        var displays = new List<WindowDisplay>();
        EnumDisplayMonitors(0, 0, (nint monitor, nint dc, ref NativeRect rect, nint data) =>
        {
            MonitorInfo info = new() { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                string id = "";
                for (uint i = 0; i < 32; i++)
                {
                    DisplayDevice device = new() { Size = Marshal.SizeOf<DisplayDevice>() };
                    if (!EnumDisplayDevices(info.Device, i, ref device, 1)) { break; }
                    if ((device.Flags & 1) != 0) { id = device.Id; break; }
                }
                // A hidden native window obtains the target's actual DPI through GetDpiForWindow.
                // GetDpiForMonitor is not intended for per-monitor-aware calling threads.
                nint probe = CreateWindowEx(0x80, "STATIC", "", 0x80000000, info.Work.Left + (info.Work.Right - info.Work.Left) / 2,
                    info.Work.Top + (info.Work.Bottom - info.Work.Top) / 2, 1, 1, 0, 0, 0, 0);
                uint dpi;
                try { dpi = GetDpiForWindow(probe); } finally { if (probe != 0) { DestroyWindow(probe); } }
                displays.Add(new(id, info.Device, Area(info.Monitor), Area(info.Work), dpi is >= 48 and <= 768 ? dpi : 96, (info.Flags & 1) != 0));
            }
            return true;
        }, 0);
        return displays;
    }

    internal static WindowDisplay? DisplayFor(nint handle)
    {
        MonitorInfo info = new() { Size = Marshal.SizeOf<MonitorInfo>() };
        if (handle == 0 || !GetMonitorInfo(MonitorFromWindow(handle, 2), ref info)) { return null; }
        uint dpi = GetDpiForWindow(handle);
        return new("", info.Device, Area(info.Monitor), Area(info.Work), dpi is >= 48 and <= 768 ? dpi : 96, (info.Flags & 1) != 0);
    }

    internal static WindowArea? ScreenRectangle(Window window) => GetWindowRect(new WindowInteropHelper(window).Handle, out NativeRect rect) ? Area(rect) : null;

    internal static WindowPlacement? Capture(Window window)
    {
        nint handle = new WindowInteropHelper(window).Handle;
        NativePlacement placement = new() { Length = Marshal.SizeOf<NativePlacement>() };
        if (handle == 0 || !GetWindowPlacement(handle, ref placement)) { return null; }
        WindowDisplay? current = DisplayFor(handle);
        WindowDisplay? display = Displays().FirstOrDefault(d => d.Device == current?.Device);
        if (display is null) { return null; }
        WindowArea bounds = ScreenBounds(Area(placement.Normal), display, IsToolWindow(handle));
        // Do not replace a valid save with an intentionally off-screen diagnostic window.
        if (!bounds.IsValid || bounds.Right <= display.WorkArea.Left || bounds.Left >= display.WorkArea.Right || bounds.Bottom <= display.WorkArea.Top || bounds.Top >= display.WorkArea.Bottom) { return null; }
        return new(bounds.Left, bounds.Top, bounds.Width, bounds.Height, placement.Show == 3 || (placement.Show == 2 && (placement.Flags & 2) != 0),
            2, display.Id, display.Device, display.WorkArea, current!.Dpi);
    }

    // WINDOWPLACEMENT uses workspace coordinates, while monitor rectangles use screen pixels.
    // Convert both ways at this boundary; never pass its raw rectangle to SetWindowPos.
    internal static WindowArea ScreenBounds(WindowArea area, WindowDisplay display, bool toolWindow = false) => toolWindow ? area : area with
    { Left = area.Left + display.WorkArea.Left - display.Bounds.Left, Top = area.Top + display.WorkArea.Top - display.Bounds.Top };
    internal static WindowArea WorkspaceBounds(WindowArea area, WindowDisplay display, bool toolWindow = false) => toolWindow ? area : area with
    { Left = area.Left - display.WorkArea.Left + display.Bounds.Left, Top = area.Top - display.WorkArea.Top + display.Bounds.Top };

    internal static bool Apply(Window window, WindowRestorePlan plan)
    {
        nint handle = new WindowInteropHelper(window).Handle;
        WindowArea bounds = WorkspaceBounds(plan.Bounds, plan.Display, IsToolWindow(handle));
        NativePlacement placement = new()
        {
            Length = Marshal.SizeOf<NativePlacement>(),
            Show = window.IsVisible ? 4u : 0u,
            Normal = new() { Left = (int)bounds.Left, Top = (int)bounds.Top, Right = (int)bounds.Right, Bottom = (int)bounds.Bottom }
        };
        if (handle == 0 || !SetWindowPlacement(handle, ref placement)) { return false; }
        // WPF can accept WM_DPICHANGED's suggested size during the initial move.
        // Reapply the physical rectangle after that synchronous transition.
        if (!SetWindowPlacement(handle, ref placement)) { return false; }
        if (plan.Maximized) { window.WindowState = WindowState.Maximized; }
        return true;
    }

    private static bool IsToolWindow(nint handle) => (GetWindowLongPtr(handle, -20).ToInt64() & 0x80) != 0;
    private static WindowArea Area(NativeRect rect) => new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePlacement { internal int Length; internal uint Flags, Show; internal NativePoint Minimum, Maximum; internal NativeRect Normal; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        internal int Size; internal NativeRect Monitor, Work; internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Device;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        internal int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Description;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Key;
    }
    private delegate bool MonitorCallback(nint monitor, nint dc, ref NativeRect rect, nint data);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplayDevicesW")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayDevices(string device, uint number, ref DisplayDevice info, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")] private static extern nint CreateWindowEx(uint extended, string type, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint data);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowPlacement(nint window, ref NativePlacement placement);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPlacement(nint window, ref NativePlacement placement);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
}
