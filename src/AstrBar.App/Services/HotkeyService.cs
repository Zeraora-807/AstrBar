using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AstrBar.Services;

public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0xA57B;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint VkSpace = 0x20;

    private readonly IntPtr _windowHandle;
    private readonly HwndSource _source;
    private readonly Action _onPressed;
    private bool _registered;

    public HotkeyService(IntPtr windowHandle, Action onPressed)
    {
        _windowHandle = windowHandle;
        _onPressed = onPressed;

        _source = HwndSource.FromHwnd(windowHandle)
                  ?? throw new InvalidOperationException("无法创建快捷键窗口钩子。");
        _source.AddHook(WindowProcedure);

        _registered = RegisterHotKey(
            _windowHandle,
            HotkeyId,
            ModControl | ModAlt,
            VkSpace);
    }

    public bool IsRegistered => _registered;

    private IntPtr WindowProcedure(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _onPressed();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered)
        {
            UnregisterHotKey(_windowHandle, HotkeyId);
            _registered = false;
        }

        _source.RemoveHook(WindowProcedure);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(
        IntPtr hWnd,
        int id,
        uint fsModifiers,
        uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
