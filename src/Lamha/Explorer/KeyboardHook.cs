using System.Runtime.InteropServices;
using Lamha.Core;
using static Lamha.Explorer.Native;

namespace Lamha.Explorer;

/// <summary>
/// Sees Space before Explorer does. The callback runs on its own thread with only cheap Win32 calls, because Windows
/// silently removes low-level hooks that respond slowly and a slow hook would lag every keystroke on the machine.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    const uint WatchMouseOn = 0x8000, WatchMouseOff = 0x8001; // WM_APP range

    readonly HookProc _proc; // must stay referenced: the native side holds only a raw pointer
    readonly HookProc _mouseProc;
    readonly Thread _thread;
    readonly SpaceGate _gate = new();
    uint _threadId;
    nint _hook, _mouseHook;

    /// <summary>Returns the window that owns the file view, or 0 to leave the key alone.</summary>
    public Func<nint>? CaptureTarget { get; set; }
    public event Action<nint>? SpacePressed;
    internal event Action<POINT>? MouseClicked;

    public KeyboardHook()
    {
        _proc = Callback;
        _mouseProc = MouseCallback;
        _thread = new Thread(Run) { IsBackground = true, Name = "keyboard-hook" };
        _thread.Start();
    }

    void Run()
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookExW(WH_KEYBOARD_LL, _proc, GetModuleHandleW(null), 0);
        if (_hook == 0) { Log.Error($"keyboard hook failed: {Marshal.GetLastWin32Error()}"); return; }
        Log.Info("keyboard hook installed");
        while (GetMessageW(out var m, 0, 0, 0) > 0)
        {
            if (m.message == WatchMouseOn && _mouseHook == 0) _mouseHook = SetWindowsHookExW(WH_MOUSE_LL, _mouseProc, GetModuleHandleW(null), 0);
            else if (m.message == WatchMouseOff) StopMouse();
        }
        StopMouse();
        UnhookWindowsHookEx(_hook);
    }

    void StopMouse()
    {
        if (_mouseHook != 0) UnhookWindowsHookEx(_mouseHook);
        _mouseHook = 0;
    }

    /// <summary>Reports clicks while a preview is open. Mouse hooks see every movement, so it is off the rest of the time.</summary>
    public void WatchMouse(bool on) => PostThreadMessageW(_threadId, on ? WatchMouseOn : WatchMouseOff, 0, 0);

    nint MouseCallback(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && (int)wParam is WM_LBUTTONDOWN or WM_RBUTTONDOWN)
            MouseClicked?.Invoke(Marshal.PtrToStructure<POINT>(lParam));
        return CallNextHookEx(0, code, wParam, lParam);
    }

    nint Callback(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            int msg = (int)wParam;
            bool swallow = _gate.OnKey(Marshal.ReadInt32(lParam), msg is WM_KEYDOWN or WM_SYSKEYDOWN, msg is WM_KEYUP or WM_SYSKEYUP,
                Environment.TickCount64, ModifierHeld, () => CaptureTarget?.Invoke() ?? 0, out nint fire);
            if (fire != 0) SpacePressed?.Invoke(fire);
            if (swallow) return 1;
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    static bool ModifierHeld() =>
        GetAsyncKeyState(VK_SHIFT) < 0 || GetAsyncKeyState(VK_CONTROL) < 0 || GetAsyncKeyState(VK_MENU) < 0 ||
        GetAsyncKeyState(VK_LWIN) < 0 || GetAsyncKeyState(VK_RWIN) < 0;

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessageW(_threadId, WM_QUIT, 0, 0);
        _thread.Join(500);
    }
}
