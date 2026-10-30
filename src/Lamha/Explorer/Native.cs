using System.Runtime.InteropServices;
using System.Text;

namespace Lamha.Explorer;

static class Native
{
    public const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14, WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204, WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105, WM_QUIT = 0x12;
    public const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_SPACE = 0x20, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    public const uint GA_ROOT = 2, GA_PARENT = 1;
    public const uint GUI_INMOVESIZE = 2, GUI_INMENUMODE = 4, GUI_SYSTEMMENUMODE = 8, GUI_POPUPMENUMODE = 0x10;
    public const uint SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    public delegate nint HookProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)] public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public nint extra; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MSG { public nint hwnd; public uint message; public nint wParam, lParam; public uint time; public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize; public uint flags;
        public nint hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("user32", SetLastError = true)] public static extern nint SetWindowsHookExW(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32")] public static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32")] public static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32")] public static extern int GetMessageW(out MSG msg, nint hwnd, uint min, uint max);
    [DllImport("user32")] public static extern bool PostThreadMessageW(uint thread, uint msg, nint wParam, nint lParam);
    [DllImport("user32")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32")] public static extern nint GetForegroundWindow();
    [DllImport("user32")] public static extern nint WindowFromPoint(POINT point);
    [DllImport("user32")] public static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32")] public static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);
    [DllImport("user32")] public static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32")] public static extern bool IsWindow(nint hwnd);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(nint hwnd, StringBuilder name, int size);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern nint FindWindowExW(nint parent, nint after, string? cls, string? title);
    [DllImport("user32")] public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32")] public static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32")] public static extern bool GetMonitorInfoW(nint monitor, ref MONITORINFO info);
    [DllImport("shcore")] public static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("kernel32")] public static extern uint GetCurrentThreadId();
    [DllImport("kernel32", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandleW(string? name);
    [DllImport("user32")] public static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32")] public static extern bool BringWindowToTop(nint hwnd);
    [DllImport("kernel32")] public static extern bool SetProcessWorkingSetSize(nint process, nint min, nint max);
    [DllImport("dwmapi")] public static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

    public static string ClassOf(nint hwnd)
    {
        var sb = new StringBuilder(64);
        return GetClassNameW(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }
}
