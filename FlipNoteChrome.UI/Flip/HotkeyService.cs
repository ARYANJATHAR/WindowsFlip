using System.Windows.Interop;
using FlipNoteChrome.Core.Interop;
using FlipNoteChrome.Core.WindowIdentity;

namespace FlipNoteChrome.UI.Flip;

public sealed class HotkeyService : IDisposable
{
    private HwndSource? _source;
    private const int HOTKEY_ID = 0x9001;
    private const uint MOD_CTRL = 0x0002;
    private const uint MOD_ALT = 0x0001;
    private const uint VK_F = 0x46;

    public event Action<IntPtr, WindowIdentity>? HotkeyPressed;

    public void Install(HwndSource source)
    {
        _source = source;
        _source.AddHook(WndProc);
        bool ok = NativeMethods.RegisterHotKey(source.Handle, HOTKEY_ID, MOD_CTRL | MOD_ALT, VK_F);
        if (!ok) throw new InvalidOperationException("Failed to register Ctrl+Alt+F hotkey - already in use.");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            var fg = NativeMethods.GetForegroundWindow();
            if (fg != IntPtr.Zero && WindowIdentityService.IsChromeWindow(fg))
            {
                var id = WindowIdentityService.Resolve(fg);
                HotkeyPressed?.Invoke(fg, id);
            }
            else
            {
                var chromes = WindowIdentityService.EnumerateChromeWindows();
                var top = chromes.FirstOrDefault();
                if (top.Hwnd != IntPtr.Zero) HotkeyPressed?.Invoke(top.Hwnd, top.Identity);
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        try
        {
            if (_source != null)
            {
                NativeMethods.UnregisterHotKey(_source.Handle, HOTKEY_ID);
                _source.RemoveHook(WndProc);
            }
        }
        catch { }
    }
}
