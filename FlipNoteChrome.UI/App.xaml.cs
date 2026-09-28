using System.Windows;
using System.Windows.Interop;
using FlipNoteChrome.Core.Hooks;
using FlipNoteChrome.Core.Storage;
using FlipNoteChrome.UI.Flip;
using Hardcodet.Wpf.TaskbarNotification;

namespace FlipNoteChrome.UI;

public partial class App : Application
{
    private LowLevelMouseHook? _mouseHook;
    private HotkeyService? _hotkey;
    private TaskbarIcon? _tray;
    private SqliteNoteStore? _store;
    private FlipWindow? _currentFlip;
    private HwndSource? _hwndSource;
    private Window? _hiddenHost;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _store = new SqliteNoteStore();

        // Hidden host window for hotkey message loop
        _hiddenHost = new Window { Width = 0, Height = 0, ShowInTaskbar = false, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = null, Opacity = 0, ShowActivated = false, Visibility = Visibility.Hidden };
        _hiddenHost.Show();
        _hiddenHost.Hide();
        var helper = new WindowInteropHelper(_hiddenHost);
        helper.EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        _hotkey = new HotkeyService();
        try { _hotkey.Install(_hwndSource); } catch (Exception ex) { MessageBox.Show("Hotkey Ctrl+Alt+F already in use: " + ex.Message); }
        _hotkey.HotkeyPressed += OnFlipRequested;

        _mouseHook = new LowLevelMouseHook { RequireAlt = true, DebugMode = true };
        _mouseHook.ChromeTitleBarClicked += OnFlipRequested;
        _mouseHook.DebugLog += msg => { try { System.IO.File.AppendAllText(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlipNoteChrome", "hook.log"), $"{DateTime.Now:HH:mm:ss} {msg}\n"); } catch { } };
        try { _mouseHook.Install(); } catch (Exception ex) { MessageBox.Show("Mouse hook failed (try Run as Admin): " + ex.Message); }

        SetupTray();

        // Show lightweight settings/main window hidden - tray is primary
        // Keep MainWindow as tray settings launcher
        MainWindow = new MainWindow();
        // Don't show MainWindow on startup - tray only
        // MainWindow.Show() only on double-click
    }

    private void SetupTray()
    {
        _tray = new TaskbarIcon();
        _tray.Icon = System.Drawing.SystemIcons.Application;
        _tray.ToolTipText = "FlipNoteChrome — Alt+Click Chrome title bar to flip";
        _tray.TrayLeftMouseDown += (_, _) => { /* single click does nothing */ };
        _tray.TrayMouseDoubleClick += (_, _) => ShowSettings();

        var menu = new System.Windows.Controls.ContextMenu();
        menu.Items.Add(CreateMenuItem("Flip Focused Chrome (Ctrl+Alt+F)", () =>
        {
            var fg = FlipNoteChrome.Core.Interop.NativeMethods.GetForegroundWindow();
            var cls = fg != IntPtr.Zero ? FlipNoteChrome.Core.Interop.NativeMethods.GetClassName(fg) : "";
            FlipNoteChrome.Core.Interop.NativeMethods.GetWindowThreadProcessId(fg, out var pid);
            var isChrome = fg != IntPtr.Zero && FlipNoteChrome.Core.WindowIdentity.WindowIdentityService.IsChromeWindow(fg);
            if (isChrome)
            {
                var top = FlipNoteChrome.Core.WindowIdentity.WindowIdentityService.GetChromeTopLevel(fg);
                var id = FlipNoteChrome.Core.WindowIdentity.WindowIdentityService.Resolve(top);
                OnFlipRequested(top, id);
            }
            else
            {
                var chromes = FlipNoteChrome.Core.WindowIdentity.WindowIdentityService.EnumerateChromeWindows();
                if (chromes.Count > 0) OnFlipRequested(chromes[0].Hwnd, chromes[0].Identity);
                else _tray?.ShowBalloonTip("FlipNoteChrome", $"No Chrome found (fg hwnd={fg} class={cls} pid={pid}). Open Chrome first.", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
            }
        }));
        menu.Items.Add(CreateMenuItem("Debug: Show Hook Log", () =>
        {
            var p = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlipNoteChrome", "hook.log");
            if (System.IO.File.Exists(p)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = p, UseShellExecute = true });
            else MessageBox.Show("No hook.log yet — try Alt+Click Chrome title bar first.");
        }));
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(CreateMenuItem("Search Notes...", () => new Notes.SearchWindow(_store!).Show()));
        menu.Items.Add(CreateMenuItem("Settings", () => ShowSettings()));
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(CreateMenuItem("Exit", () => Shutdown()));
        _tray.ContextMenu = menu;
    }

    private System.Windows.Controls.MenuItem CreateMenuItem(string header, Action act)
    {
        var mi = new System.Windows.Controls.MenuItem { Header = header };
        mi.Click += (_, _) => act();
        return mi;
    }

    private void ShowSettings()
    {
        if (MainWindow == null) MainWindow = new MainWindow();
        if (!MainWindow.IsVisible) MainWindow.Show();
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    private void OnFlipRequested(IntPtr hwnd, FlipNoteChrome.Core.WindowIdentity.WindowIdentity id)
    {
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlipNoteChrome", "flip.log"), $"{DateTime.Now:HH:mm:ss} OnFlipRequested hwnd={hwnd} key={id.StableKey[..8]} currentFlip={( _currentFlip==null?"null":"exists")}\n"); } catch { }
        var settings = AppSettings.Load();
        if (settings.ExcludeList.Any(ex => id.ExePath.Contains(ex, StringComparison.OrdinalIgnoreCase) || id.ClassName.Contains(ex, StringComparison.OrdinalIgnoreCase)))
            return;
        Dispatcher.InvokeAsync(async () =>
        {
            if (_currentFlip != null)
            {
                // If a flip window is already open, any new trigger should close it (even if hwnd is different)
                try { await _currentFlip.DoFlipBackAsync(); } catch (Exception ex) { try { System.IO.File.AppendAllText(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlipNoteChrome", "flip.log"), $"DoFlipBack ex {ex}\n"); } catch { } try { _currentFlip.Close(); } catch { } }
                _currentFlip = null;
                return;
            }
            if (hwnd == IntPtr.Zero) return;
            // Allow flipping even if IsWindowVisible is false due to prior hide - check if hwnd still valid
            if (!FlipNoteChrome.Core.Interop.NativeMethods.IsWindowVisible(hwnd))
            {
                // If hidden, it is still our Chrome - allow flip back path already handled above; for new flip, need visible
                // So if no currentFlip and hwnd not visible, try enumerate visible Chrome instead
                var chromes = FlipNoteChrome.Core.WindowIdentity.WindowIdentityService.EnumerateChromeWindows();
                if (chromes.Count > 0) { hwnd = chromes[0].Hwnd; id = chromes[0].Identity; }
                else return;
            }
            try
            {
                _currentFlip = new FlipWindow(hwnd, id, _store!);
                if (settings.AlwaysOnTop) _currentFlip.Topmost = true;
                _currentFlip.Closed += (_, _) => { _currentFlip = null; try { System.IO.File.AppendAllText(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlipNoteChrome", "flip.log"), $"{DateTime.Now:HH:mm:ss} FlipWindow Closed\n"); } catch { } };
                _currentFlip.Show();
            }
            catch (Exception ex) { try { System.IO.File.AppendAllText(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlipNoteChrome", "flip.log"), $"Create FlipWindow ex {ex}\n"); MessageBox.Show($"Flip failed: {ex.Message}"); } catch { } }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _currentFlip?.RestoreChrome(); } catch { }
        _tray?.Dispose();
        _hotkey?.Dispose();
        _mouseHook?.Dispose();
        _hwndSource?.Dispose();
        _store?.Dispose();
        base.OnExit(e);
    }
}
