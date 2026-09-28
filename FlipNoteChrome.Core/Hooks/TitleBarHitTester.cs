using FlipNoteChrome.Core.Interop;

namespace FlipNoteChrome.Core.Hooks;

public static class TitleBarHitTester
{
    private const int SM_CXSIZE = 34; // close button width
    // Conservative: exclude rightmost 140px (min/max/close) to avoid false positives
    private const int CaptionButtonExclude = 140;

    public static bool IsTitleBarClick(IntPtr hwnd, NativeMethods.POINT pt)
    {
        if (hwnd == IntPtr.Zero) return false;
        // Only Chrome windows
        var cls = NativeMethods.GetClassName(hwnd);
        // Allow Chrome_WidgetWin_1 or chrome.exe, but also accept any if Enum found - filtered outside
        // Use DWM extended frame for accurate rect
        NativeMethods.RECT rect;
        int hr = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<NativeMethods.RECT>());
        if (hr != 0 || rect.Width == 0)
        {
            if (!NativeMethods.GetWindowRect(hwnd, out rect)) return false;
        }
        // Hit test via WM_NCHITTEST - most reliable
        int lParam = (pt.Y << 16) | (pt.X & 0xFFFF);
        IntPtr hit = NativeMethods.SendMessageW(hwnd, NativeMethods.WM_NCHITTEST, IntPtr.Zero, (IntPtr)lParam);
        int hitVal = hit.ToInt32();
        if (hitVal == NativeMethods.HTCAPTION) return true;
        if (hitVal == NativeMethods.HTCLOSE || hitVal == NativeMethods.HTMINBUTTON || hitVal == NativeMethods.HTMAXBUTTON)
            return false;
        // Fallback geometry: top ~32px is title bar on Chrome (with custom frame)
        // Chrome draws its own tabs inside client area, but DWM still reports caption height ~30px
        int captionH = NativeMethods.GetSystemMetrics(4); // SM_CYCAPTION = 4
        if (captionH < 20) captionH = 30;
        // Enlarge a bit for Chrome tab strip (tabs are 32-36px tall)
        captionH = Math.Max(captionH, 36);
        bool inTopStrip = pt.Y >= rect.Top && pt.Y <= rect.Top + captionH;
        bool notInButtons = pt.X <= rect.Right - CaptionButtonExclude;
        bool inHorizontal = pt.X >= rect.Left && pt.X <= rect.Right;
        return inTopStrip && notInButtons && inHorizontal;
    }

    private static class Marshal
    {
        public static int SizeOf<T>() => System.Runtime.InteropServices.Marshal.SizeOf<T>();
    }
}
