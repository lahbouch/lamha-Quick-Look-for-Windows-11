using System.Runtime.InteropServices;
using Lamha.Core;
using static Lamha.Explorer.Native;

namespace Lamha.Explorer;

public static class ExplorerSelection
{
    const int TimeoutMs = 2000, MaxItems = 5000;

    [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IServiceProvider { [PreserveSig] int QueryService(in Guid service, in Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object? result); }

    // Only the first method is needed: IOleWindow::GetWindow, inherited by IShellBrowser.
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellBrowser { [PreserveSig] int GetWindow(out nint hwnd); }

    static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");

    /// <summary>Runs on a throwaway STA thread with a timeout, so a hung Explorer cannot freeze the app.</summary>
    public static Task<List<string>> GetAsync(nint explorerWindow)
    {
        var done = new TaskCompletionSource<List<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var t = new Thread(() =>
        {
            try { done.TrySetResult(Read(explorerWindow)); }
            catch (Exception e) { Log.Error("selection read failed", e); done.TrySetResult([]); }
        }) { IsBackground = true, Name = "explorer-selection" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return done.Task.WaitAsync(TimeSpan.FromMilliseconds(TimeoutMs)).ContinueWith(r =>
        {
            if (r.IsCompletedSuccessfully) return r.Result;
            Log.Error("selection read timed out");
            return new List<string>();
        });
    }

    static List<string> Read(nint fg)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        try
        {
            return ExplorerProbe.IsDesktop(ClassOf(fg)) ? ReadDesktop(shell) : ReadWindow(shell, fg);
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    static List<string> ReadDesktop(dynamic shell)
    {
        const int SWC_DESKTOP = 8, SWFO_NEEDDISPATCH = 1;
        object loc = 0, root = 0;
        dynamic windows = shell.Windows();
        dynamic? desktop = windows.FindWindowSW(ref loc, ref root, SWC_DESKTOP, out int _, SWFO_NEEDDISPATCH);
        return desktop is null ? [] : Selected(desktop);
    }

    static List<string> ReadWindow(dynamic shell, nint fg)
    {
        // Every Explorer tab is a separate shell window, and only the top ShellTabWindowClass child is the visible tab.
        nint activeTab = FindWindowExW(fg, 0, "ShellTabWindowClass", null);
        dynamic? fallback = null;
        int matches = 0;

        foreach (dynamic w in shell.Windows())
        {
            nint hwnd = BrowserWindow(w);
            if (hwnd != 0)
            {
                if (GetAncestor(hwnd, GA_ROOT) != fg) continue;
                if (activeTab == 0 || hwnd == activeTab) return Selected(w);
            }
            else if ((nint)(long)w.HWND == fg) // shortcut: no IShellBrowser, so tabs cannot be told apart; used only when unambiguous
            {
                fallback = w;
                matches++;
            }
        }
        return matches == 1 ? Selected(fallback!) : [];
    }

    static nint BrowserWindow(object window)
    {
        if (window is IServiceProvider sp
            && sp.QueryService(SID_STopLevelBrowser, IID_IShellBrowser, out var o) >= 0
            && o is IShellBrowser browser
            && browser.GetWindow(out nint hwnd) >= 0)
            return hwnd;
        return 0;
    }

    static List<string> Selected(dynamic window)
    {
        var paths = new List<string>();
        dynamic items = window.Document.SelectedItems();
        int count = Math.Min((int)items.Count, MaxItems);
        for (int i = 0; i < count; i++)
        {
            string? path = items.Item(i).Path;
            if (!string.IsNullOrEmpty(path)) paths.Add(path);
        }
        return paths;
    }
}
