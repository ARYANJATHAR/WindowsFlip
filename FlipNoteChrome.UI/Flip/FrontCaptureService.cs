using System.Windows;
using System.Windows.Media.Imaging;
using FlipNoteChrome.Core.Interop;

namespace FlipNoteChrome.UI.Flip;

public static class FrontCaptureService
{
    public static BitmapSource? Capture(IntPtr hwnd)
    {
        IntPtr hdcScreen = IntPtr.Zero, hdcMem = IntPtr.Zero, hBmp = IntPtr.Zero, hOld = IntPtr.Zero;
        try
        {
            if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            {
                if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out rect, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>()) != 0)
                    return null;
            }
            int w = rect.Width, h = rect.Height;
            if (w <= 0 || h <= 0 || w > 5000 || h > 5000) return null;

            hdcScreen = CaptureInterop.GetWindowDC(IntPtr.Zero);
            if (hdcScreen == IntPtr.Zero) return null;
            hdcMem = CaptureInterop.CreateCompatibleDC(hdcScreen);
            if (hdcMem == IntPtr.Zero) return null;
            hBmp = CaptureInterop.CreateCompatibleBitmap(hdcScreen, w, h);
            if (hBmp == IntPtr.Zero) return null;
            hOld = CaptureInterop.SelectObject(hdcMem, hBmp);
            bool printed = CaptureInterop.PrintWindow(hwnd, hdcMem, CaptureInterop.PW_RENDERFULLCONTENT);
            if (!printed)
            {
                IntPtr hdcWin = CaptureInterop.GetWindowDC(hwnd);
                if (hdcWin != IntPtr.Zero)
                {
                    CaptureInterop.BitBlt(hdcMem, 0, 0, w, h, hdcWin, 0, 0, CaptureInterop.SRCCOPY);
                    CaptureInterop.ReleaseDC(hwnd, hdcWin);
                }
            }
            // Directly create BitmapSource from HBITMAP without System.Drawing
            var bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(hBmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bs.Freeze();
            return bs;
        }
        catch (Exception ex)
        {
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlipNoteChrome", "flip.log"), $"{DateTime.Now:HH:mm:ss} Capture failed: {ex}\n"); } catch { }
            return null;
        }
        finally
        {
            if (hOld != IntPtr.Zero && hdcMem != IntPtr.Zero) CaptureInterop.SelectObject(hdcMem, hOld);
            if (hBmp != IntPtr.Zero) CaptureInterop.DeleteObject(hBmp);
            if (hdcMem != IntPtr.Zero) CaptureInterop.DeleteDC(hdcMem);
            if (hdcScreen != IntPtr.Zero) CaptureInterop.ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }
}
