using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace FenceForge.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x4646;
    private const int WmHotkey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint VirtualKeyG = 0x47;
    private static readonly IntPtr MessageOnlyWindow = new(-3);

    private HwndSource? _source;
    private bool _registered;

    public event EventHandler? Pressed;

    public void Register()
    {
        if (_registered)
        {
            return;
        }

        var parameters = new HwndSourceParameters("FenceForge.Hotkey")
        {
            ParentWindow = MessageOnlyWindow,
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WindowProcedure);

        if (!RegisterHotKey(
                _source.Handle,
                HotkeyId,
                ModControl | ModShift | ModNoRepeat,
                VirtualKeyG))
        {
            var error = Marshal.GetLastWin32Error();
            _source.RemoveHook(WindowProcedure);
            _source.Dispose();
            _source = null;
            throw new Win32Exception(
                error,
                "Ctrl+Shift+G could not be registered. Another application may already be using it.");
        }

        _registered = true;
    }

    private IntPtr WindowProcedure(
        IntPtr window,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter,
        ref bool handled)
    {
        if (message == WmHotkey && wordParameter.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source is null)
        {
            return;
        }

        if (_registered)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
        }

        _source.RemoveHook(WindowProcedure);
        _source.Dispose();
        _source = null;
        _registered = false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
