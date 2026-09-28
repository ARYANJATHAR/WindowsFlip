using System.Runtime.InteropServices;
using FlipNoteChrome.Core.Interop;

namespace FlipNoteChrome.Core.Hooks;

public sealed class WinEventTracker : IDisposable
{
    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
    private const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A, EVENT_SYSTEM_MOVESIZEEND = 0x000B, EVENT_SYSTEM_MINIMIZESTART = 0x0016, EVENT_SYSTEM_MINIMIZEEND = 0x0017, EVENT_SYSTEM_LOCATIONCHANGE = 0x800B;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;

    private IntPtr _hook;
    private readonly WinEventDelegate _del;
    private readonly IntPtr _targetHwnd;
    public event Action? ChromeMoved;
    public event Action? ChromeMinimized;

    public WinEventTracker(IntPtr targetHwnd)
    {
        _targetHwnd = targetHwnd;
        _del = Callback;
    }
    public void Start()
    {
        _hook = SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_LOCATIONCHANGE, IntPtr.Zero, _del, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }
    private void Callback(IntPtr h, uint evt, IntPtr hwnd, int o, int c, uint t, uint tm)
    {
        if (hwnd != _targetHwnd) return;
        if (evt == EVENT_SYSTEM_MINIMIZESTART) ChromeMinimized?.Invoke();
        else if (evt == EVENT_SYSTEM_MOVESIZEEND || evt == EVENT_SYSTEM_LOCATIONCHANGE) ChromeMoved?.Invoke();
    }
    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }
}
