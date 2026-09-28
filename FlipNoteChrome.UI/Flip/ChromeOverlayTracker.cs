using System.Windows;
using System.Windows.Threading;
using FlipNoteChrome.Core.Interop;

namespace FlipNoteChrome.UI.Flip;

public sealed class ChromeOverlayTracker : IDisposable
{
    private readonly Window _overlay;
    private readonly IntPtr _chromeHwnd;
    private readonly DispatcherTimer _timer;
    private readonly Core.Hooks.WinEventTracker? _winEvents;
    private NativeMethods.RECT _lastRect;

    public ChromeOverlayTracker(Window overlay, IntPtr chromeHwnd)
    {
        _overlay = overlay;
        _chromeHwnd = chromeHwnd;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(32) }; // ~30fps tracking
        _timer.Tick += (_, _) => Sync();
        try { _winEvents = new Core.Hooks.WinEventTracker(chromeHwnd); _winEvents.ChromeMoved += () => Sync(); _winEvents.ChromeMinimized += () => _overlay.Dispatcher.Invoke(() => _overlay.Hide()); } catch { }
    }

    public void Start()
    {
        try { _winEvents?.Start(); } catch { }
        Sync();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private void Sync()
    {
        if (!NativeMethods.IsWindowVisible(_chromeHwnd) || NativeMethods.IsIconic(_chromeHwnd))
        {
            // Chrome minimized -> hide overlay to avoid orphan
            _overlay.Dispatcher.Invoke(() => _overlay.Hide());
            return;
        }
        NativeMethods.RECT rect;
        int hr = NativeMethods.DwmGetWindowAttribute(_chromeHwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out rect, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>());
        if (hr != 0 || rect.Width == 0)
        {
            if (!NativeMethods.GetWindowRect(_chromeHwnd, out rect)) return;
        }
        if (rect.Left == _lastRect.Left && rect.Top == _lastRect.Top && rect.Width == _lastRect.Width && rect.Height == _lastRect.Height) return;
        _lastRect = rect;
        _overlay.Dispatcher.Invoke(() =>
        {
            _overlay.Left = rect.Left;
            _overlay.Top = rect.Top;
            _overlay.Width = Math.Max(rect.Width, 100);
            _overlay.Height = Math.Max(rect.Height, 100);
            if (!_overlay.IsVisible) _overlay.Show();
            // Keep overlay just above Chrome in Z-order
            NativeMethods.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(_overlay).Handle,
                _chromeHwnd, 0, 0, 0, 0, 0x0010 | 0x0002 | 0x0001); // SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE
        });
    }

    public void Dispose()
    {
        _timer.Stop();
        try { _winEvents?.Dispose(); } catch { }
    }
}
