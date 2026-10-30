using static Lamha.Explorer.Native;

namespace Lamha.Explorer;

/// <summary>
/// Decides whether Space belongs to us. This is a whitelist on purpose: anything unrecognised (address bar, search
/// box, rename box, a future Explorer control) keeps its normal Space, so failure is harmless.
/// </summary>
public static class ExplorerProbe
{
    public static nint CaptureTarget()
    {
        nint fg = GetForegroundWindow();
        if (fg == 0) return 0;
        var root = ClassOf(fg);
        if (!IsShellRoot(root)) return 0;

        var info = new GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
        if (!GetGUIThreadInfo(GetWindowThreadProcessId(fg, out _), ref info)) return 0;
        if ((info.flags & (GUI_INMOVESIZE | GUI_INMENUMODE | GUI_SYSTEMMENUMODE | GUI_POPUPMENUMODE)) != 0) return 0;
        if (info.hwndFocus == 0) return 0;

        return IsFileView(ClassOf(info.hwndFocus), Ancestors(info.hwndFocus)) ? fg : 0;
    }

    /// <summary>Diagnostic view of what <see cref="CaptureTarget"/> would see, for a window that need not be in front.</summary>
    public static string Describe(nint window)
    {
        var info = new GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
        bool ok = GetGUIThreadInfo(GetWindowThreadProcessId(window, out _), ref info);
        var focus = ClassOf(info.hwndFocus);
        return $"root={ClassOf(window)} ok={ok} flags={info.flags} focus={focus} ancestors=[{string.Join(" > ", Ancestors(info.hwndFocus))}] isFileView={IsFileView(focus, Ancestors(info.hwndFocus))}";
    }

    public static bool IsShellRoot(string cls) => cls is "CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW";

    public static bool IsDesktop(string cls) => cls is "Progman" or "WorkerW";

    public static bool IsFileView(string focusClass, IEnumerable<string> ancestors) =>
        focusClass is "DirectUIHWND" or "SysListView32" && ancestors.Contains("SHELLDLL_DefView");

    static IEnumerable<string> Ancestors(nint hwnd)
    {
        for (int i = 0; i < 8 && hwnd != 0; i++)
        {
            hwnd = GetAncestor(hwnd, GA_PARENT);
            if (hwnd != 0) yield return ClassOf(hwnd);
        }
    }
}
