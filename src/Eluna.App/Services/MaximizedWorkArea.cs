using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Eluna.App.Services;

// A custom title bar must keep its client content inside the monitor work area.
// Windows supplies physical coordinates here, including on secondary/high-DPI displays.
internal static class MaximizedWorkArea
{
    internal static WindowArea RelativeArea(WindowDisplay display) => display.WorkArea with
    { Left = display.WorkArea.Left - display.Bounds.Left, Top = display.WorkArea.Top - display.Bounds.Top };

    internal static void Attach(Window window)
    {
        HwndSource? source = null;
        nint Hook(nint handle, int message, nint wParam, nint lParam, ref bool handled)
        {
            if (message != 0x24 || lParam == 0 || WindowNative.DisplayFor(handle) is not { } display) { return 0; }
            WindowArea area = RelativeArea(display); var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            info.MaxPosition = new((int)area.Left, (int)area.Top); info.MaxSize = new((int)area.Width, (int)area.Height);
            double scale = display.Dpi / 96;
            info.MinTrackSize = new((int)Math.Min(area.Width, Math.Ceiling(window.MinWidth * scale)), (int)Math.Min(area.Height, Math.Ceiling(window.MinHeight * scale)));
            Marshal.StructureToPtr(info, lParam, false); handled = true; return 0;
        }
        window.SourceInitialized += (_, _) => { source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle); source?.AddHook(Hook); };
        window.Closed += (_, _) => { source?.RemoveHook(Hook); source = null; };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point(int x, int y) { internal int X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo { internal Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
}
