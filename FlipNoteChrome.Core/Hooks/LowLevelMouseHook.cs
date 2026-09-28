using System.Diagnostics;
using System.Runtime.InteropServices;
using FlipNoteChrome.Core.Interop;
using FlipNoteChrome.Core.WindowIdentity;

namespace FlipNoteChrome.Core.Hooks;

public sealed class LowLevelMouseHook : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;
    private NativeMethods.LowLevelMouseProc? _proc;
    private bool _disposed;

    // Configurable
    public bool RequireAlt { get; set; } = true;
    public bool MiddleClickAlternative { get; set; } = false;

    public event Action<IntPtr, WindowIdentity.WindowIdentity>? ChromeTitleBarClicked;
    public event Action<string>? DebugLog;
    public bool DebugMode { get; set; } = false;

    public void Install()
    {
        if (_hookId != IntPtr.Zero) return;
        _proc = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        IntPtr hMod = NativeMethods.GetModuleHandle(curModule?.ModuleName);
        // For WH_MOUSE_LL, hMod can be IntPtr.Zero if in same process, but provide handle
        _hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, hMod, 0);
        if (_hookId == IntPtr.Zero)
        {
            // fallback with 0
            _hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, IntPtr.Zero, 0);
        }
        if (_hookId == IntPtr.Zero) throw new InvalidOperationException("Failed to install mouse hook. Try running as administrator.");
    }

    public void Uninstall()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            bool isLeftDown = msg == NativeMethods.WM_LBUTTONDOWN;
            bool isMiddleDown = msg == NativeMethods.WM_MBUTTONDOWN;
            bool shouldCheck = isLeftDown || (MiddleClickAlternative && isMiddleDown);
            if (shouldCheck)
            {
                bool altDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0;
                if (!RequireAlt || altDown || (MiddleClickAlternative && isMiddleDown))
                {
                    var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                    var pt = hookStruct.pt;
                    IntPtr hwnd = NativeMethods.WindowFromPoint(pt);
                    // Walk up to top-level if WindowFromPoint returns child (Chrome has child windows)
                    hwnd = GetTopLevel(hwnd);
                    if (hwnd != IntPtr.Zero)
                    {
                        bool isChrome = WindowIdentityService.IsChromeWindow(hwnd);
                        if (DebugMode) DebugLog?.Invoke($"Hook hit hwnd={hwnd} isChrome={isChrome} pt={pt.X},{pt.Y} alt={altDown}");
                        if (!isChrome) { /* log but ignore */ }
                        else if (TitleBarHitTester.IsTitleBarClick(hwnd, pt))
                        {
                            var id = WindowIdentityService.Resolve(hwnd);
                            if (DebugMode) DebugLog?.Invoke($"TitleBar hit -> flip {id.StableKey[..8]} {id.OriginalTitle}");
                            Task.Run(() => ChromeTitleBarClicked?.Invoke(hwnd, id));
                        }
                        else if (DebugMode) DebugLog?.Invoke("IsChrome but not title bar (check 140px buttons or click tabs)");
                    }
                }
            }
        }
        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static IntPtr GetTopLevel(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return IntPtr.Zero;
        IntPtr cur = hwnd;
        IntPtr parent;
        while (true)
        {
            parent = NativeMethods.GetWindow(cur, 4); // GW_OWNER? Actually use GetAncestor
            // Simpler: use GetWindow with GW_OWNER not correct. Use GetAncestor via PInvoke if needed.
            // For now, try to get root via while GetParent
            IntPtr p = GetParent(cur);
            if (p == IntPtr.Zero) break;
            cur = p;
        }
        return cur;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hWnd);

    public void Dispose()
    {
        if (!_disposed) { Uninstall(); _disposed = true; GC.SuppressFinalize(this); }
    }
}
