namespace FlipNoteChrome.Core.WindowIdentity;

public sealed record WindowIdentity(
    string ExePath,
    string ClassName,
    string OriginalTitle,
    string NormalizedTitle,
    string TitleHash,
    string StableKey)
{
    public bool IsChrome => ExePath.EndsWith("chrome.exe", StringComparison.OrdinalIgnoreCase)
                         || ClassName.Equals("Chrome_WidgetWin_1", StringComparison.OrdinalIgnoreCase);
}

public static class WindowIdentityService
{
    public static WindowIdentity Resolve(IntPtr hWnd)
    {
        var title = Interop.NativeMethods.GetWindowText(hWnd);
        var cls = Interop.NativeMethods.GetClassName(hWnd);
        Interop.NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
        var exe = Interop.NativeMethods.GetExePath(pid);
        if (string.IsNullOrEmpty(exe)) exe = pid.ToString(); // fallback
        var normalized = TitleNormalizer.Normalize(title, cls, exe);
        var hash = TitleNormalizer.Hash(normalized);
        // StableKey per spec: exePath|className|normalized
        var keyInput = $"{exe.ToLowerInvariant()}|{cls}|{normalized}";
        var stable = TitleNormalizer.Hash(keyInput);
        return new WindowIdentity(exe, cls, title, normalized, hash, stable);
    }

    public static bool IsChromeWindow(IntPtr hWnd)
    {
        IntPtr cur = hWnd;
        for (int i = 0; i < 8 && cur != IntPtr.Zero; i++)
        {
            Interop.NativeMethods.GetWindowThreadProcessId(cur, out var pid);
            var exe = Interop.NativeMethods.GetExePath(pid);
            if (!string.IsNullOrEmpty(exe) && exe.EndsWith("chrome.exe", StringComparison.OrdinalIgnoreCase)) return true;
            // Fallback via ProcessName handles protected path
            try
            {
                var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                if (proc.ProcessName.Equals("chrome", StringComparison.OrdinalIgnoreCase))
                {
                    // Also ensure class is Chrome's to avoid Electron false positive needing both
                    var cls = Interop.NativeMethods.GetClassName(cur);
                    if (cls == "Chrome_WidgetWin_1") return true;
                    return true; // chrome.exe process is definitive even if class differs
                }
            }
            catch { }
            cur = Interop.NativeMethods.GetAncestor(cur, 2);
            if (cur == IntPtr.Zero || cur == hWnd) break;
            hWnd = cur;
        }
        return false;
    }

    public static IntPtr GetChromeTopLevel(IntPtr hWnd)
    {
        IntPtr cur = hWnd;
        for (int i = 0; i < 8 && cur != IntPtr.Zero; i++)
        {
            var cls = Interop.NativeMethods.GetClassName(cur);
            if (cls == "Chrome_WidgetWin_1") return cur;
            var anc = Interop.NativeMethods.GetAncestor(cur, 2);
            if (anc == IntPtr.Zero || anc == cur) break;
            cur = anc;
        }
        return hWnd;
    }

    // Enumerate visible Chrome top-level windows
    public static List<(IntPtr Hwnd, WindowIdentity Identity)> EnumerateChromeWindows()
    {
        var list = new List<(IntPtr, WindowIdentity)>();
        Interop.NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!Interop.NativeMethods.IsWindowVisible(hwnd)) return true;
            if (hwnd == Interop.NativeMethods.GetWindow(hwnd, 0)) { } // no-op
            if (!IsChromeWindow(hwnd)) return true;
            // Skip invisible/minimized Chrome helper windows (small rect)
            if (!Interop.NativeMethods.GetWindowRect(hwnd, out var r)) return true;
            if (r.Width < 200 || r.Height < 100) return true;
            var id = Resolve(hwnd);
            list.Add((hwnd, id));
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
