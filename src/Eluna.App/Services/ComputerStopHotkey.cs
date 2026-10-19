using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Eluna.App.Services;

internal sealed class ComputerStopHotkey : IDisposable
{
    private const int Id = 0x4E1A;
    private readonly nint _handle;
    private readonly HwndSource _source;
    private readonly Action _stop;
    internal ComputerStopHotkey(Window window, Action stop)
    {
        _handle = new WindowInteropHelper(window).Handle; _source = HwndSource.FromHwnd(_handle); _stop = stop;
        if (!RegisterHotKey(_handle, Id, 0x4003, 0x1B)) { throw new InvalidOperationException("緊急停止キー Ctrl+Alt+Esc を登録できませんでした。他のアプリの同じショートカットを解除してから実行してください。"); }
        _source.AddHook(Hook);
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    { if (message == 0x0312 && wParam == Id) { _stop(); handled = true; } return 0; }
    public void Dispose() { UnregisterHotKey(_handle, Id); _source.RemoveHook(Hook); }
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint window, int id);
}
