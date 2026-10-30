using System.Windows;
using System.Windows.Threading;
using Lamha.Core;
using Lamha.Explorer;
using Lamha.Office;
using Lamha.Previews;

namespace Lamha.Ui;

/// <summary>Connects the Space key, Explorer's selection and the preview window.</summary>
public sealed class PreviewController : IDisposable
{
    readonly KeyboardHook _hook = new();
    readonly PreviewWindow _window;
    readonly Toast _toast = new();
    readonly DispatcherTimer _follow;
    nint _followWindow;
    bool _reading;

    public AppSettings Settings { get; }

    public PreviewController(AppSettings settings, OfficeConverter converter)
    {
        Settings = settings;
        var registry = new PreviewRegistry(new PdfProvider(), new OfficeProvider(converter), new ImageProvider(), new TextProvider(), new MediaProvider());
        _window = new PreviewWindow(registry, settings, Notify);

        _hook.CaptureTarget = () => Settings.ExplorerIntegration ? ExplorerProbe.CaptureTarget() : 0;
        _hook.SpacePressed += target => _window.Dispatcher.BeginInvoke(() => OnSpace(target));

        _hook.MouseClicked += p => _window.Dispatcher.BeginInvoke(() => OnMouseClick(p));
        _window.IsVisibleChanged += (_, e) => _hook.WatchMouse((bool)e.NewValue);

        _follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _follow.Tick += async (_, _) => await FollowSelection();
    }

    void OnMouseClick(Native.POINT p)
    {
        var root = Native.GetAncestor(Native.WindowFromPoint(p), Native.GA_ROOT);
        if (ShouldCloseOnClick(Native.ClassOf(root), Settings.AutoPreview)) _window.HidePreview(restoreFocus: false);
    }

    /// <summary>Only Explorer and the desktop count: the taskbar and other programs must leave the preview open.</summary>
    internal static bool ShouldCloseOnClick(string rootClass, bool autoPreview) =>
        !autoPreview && ExplorerProbe.IsShellRoot(rootClass);

    public void Notify(string message) => _toast.Show(message);

    public void SetIntegration(bool enabled)
    {
        Settings.ExplorerIntegration = enabled;
        Settings.Save(AppPaths.Settings);
        Log.Info($"explorer integration {(enabled ? "on" : "off")}");
    }

    async void OnSpace(nint target)
    {
        if (_window.IsVisible) { _window.HidePreview(); _follow.Stop(); return; }
        if (_reading) return;
        _reading = true;
        try
        {
            var files = await ExplorerSelection.GetAsync(target);
            Log.Info($"explorer selection: {files.Count}");
            if (files.Count == 0) { Notify("لم يتم تحديد أي ملف."); return; }

            var session = new PreviewSession(files);
            if (session.Count == 0) { Notify("لا يمكن معاينة هذا النوع من الملفات."); return; }

            _window.ShowSession(session, target);
            _followWindow = target;
            if (Settings.AutoPreview) _follow.Start();
        }
        finally { _reading = false; }
    }

    /// <summary>"Automatic preview": while the window is open and Explorer has focus, the preview tracks the selection.</summary>
    async Task FollowSelection()
    {
        if (!_window.IsVisible || !Settings.AutoPreview) { _follow.Stop(); return; }
        if (_reading || ExplorerProbe.CaptureTarget() != _followWindow) return;
        _reading = true;
        try
        {
            var files = await ExplorerSelection.GetAsync(_followWindow);
            var session = new PreviewSession(files);
            if (session.Current is { } path && !string.Equals(path, _window.CurrentPath, StringComparison.OrdinalIgnoreCase))
                _window.ShowFile(session);
        }
        finally { _reading = false; }
    }

    /// <summary>Opens files passed on the command line.</summary>
    public void Open(IEnumerable<string> paths)
    {
        var session = new PreviewSession(paths.Select(Path.GetFullPath));
        if (session.Count == 0) Notify("لا يمكن معاينة هذا النوع من الملفات.");
        else _window.ShowSession(session, 0);
    }

    public void Dispose()
    {
        _follow.Stop();
        _hook.Dispose();
        _window.Destroy();
        _toast.Close();
    }
}
