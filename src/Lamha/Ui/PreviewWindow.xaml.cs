using System.ComponentModel;
using System.Diagnostics;
using System.Runtime;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Lamha.Core;
using Lamha.Explorer;
using Lamha.Pdf;
using Lamha.Previews;
using static Lamha.Explorer.Native;

namespace Lamha.Ui;

public partial class PreviewWindow : Window
{
    const double DefaultWidth = 1000, DefaultHeight = 760, HeaderHeight = 48, MaxScreenShare = 0.86;

    readonly PreviewRegistry _registry;
    readonly AppSettings _settings;
    readonly Action<string> _notify;
    PreviewSession? _session;
    IPreview? _preview;
    CancellationTokenSource? _load;
    nint _owner;
    bool _reallyClosing, _fullscreen;

    public PreviewWindow(PreviewRegistry registry, AppSettings settings, Action<string> notify)
    {
        _registry = registry;
        _settings = settings;
        _notify = notify;
        InitializeComponent();
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        int round = 2; // DWMWCP_ROUND: the standard Windows 11 corners and shadow, without a transparent window
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
    }

    public string? CurrentPath => _session?.Current;

    public void ShowSession(PreviewSession session, nint owner)
    {
        _session = session;
        _owner = owner;
        Nav.Visibility = session.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        PlaceWindow(null, keepAspect: false);
        Show();
        Activate();
        TakeForeground();
        Load();
    }

    /// <summary>A background process may not take the foreground, except by joining the input queue of the current foreground thread.</summary>
    void TakeForeground()
    {
        nint hwnd = new WindowInteropHelper(this).Handle, fg = GetForegroundWindow();
        if (fg == hwnd) return;
        uint me = GetCurrentThreadId(), other = GetWindowThreadProcessId(fg, out _);
        AttachThreadInput(me, other, true);
        BringWindowToTop(hwnd);
        SetForegroundWindow(hwnd);
        AttachThreadInput(me, other, false);
        Log.Debug($"foreground taken: {GetForegroundWindow() == hwnd}");
    }

    async void Load(string? password = null)
    {
        _load?.Cancel();
        var cts = _load = new CancellationTokenSource();
        var ct = cts.Token;

        ReleasePreview();
        ErrorPanel.Visibility = PasswordPanel.Visibility = Loading.Visibility = Visibility.Collapsed;

        var path = _session?.Current;
        if (path is null) { HidePreview(); return; }

        FileName.Text = Path.GetFileName(path);
        Meta.Text = FileMeta(path);
        Position.Text = _session!.Position;
        PrevButton.IsEnabled = _session.Index > 0;
        NextButton.IsEnabled = _session.Index < _session.Count - 1;
        Log.Info($"preview open {Path.GetExtension(path).ToLowerInvariant()}");

        _ = ShowSpinnerSoon(ct);
        try
        {
            var provider = _registry.Find(path) ?? throw new NotSupportedException();
            var preview = await provider.OpenAsync(new PreviewRequest(path, password, _settings), ct);
            if (ct.IsCancellationRequested) { preview.Dispose(); return; }
            Attach(preview, path);
        }
        catch (OperationCanceledException) { return; }
        catch (PdfPasswordException e)
        {
            if (ct.IsCancellationRequested) return;
            PasswordText.Text = e.WrongPassword ? "كلمة المرور غير صحيحة. حاول مرة أخرى." : "هذا الملف محمي بكلمة مرور.";
            PasswordInput.Clear();
            PasswordPanel.Visibility = Visibility.Visible;
            PasswordInput.Focus();
        }
        catch (Exception e)
        {
            if (ct.IsCancellationRequested) return;
            Log.Error($"preview failed {Path.GetExtension(path).ToLowerInvariant()}", e);
            ErrorText.Text = PreviewErrors.Message(e);
            ErrorPanel.Visibility = Visibility.Visible;
        }
        finally
        {
            if (!ct.IsCancellationRequested) Loading.Visibility = Visibility.Collapsed;
        }
    }

    // Fast previews should appear directly, without a spinner flashing first.
    async Task ShowSpinnerSoon(CancellationToken ct)
    {
        await Task.Delay(140, CancellationToken.None);
        if (!ct.IsCancellationRequested && _preview is null && ErrorPanel.Visibility != Visibility.Visible && PasswordPanel.Visibility != Visibility.Visible)
            Loading.Visibility = Visibility.Visible;
    }

    void Attach(IPreview preview, string path)
    {
        _preview = preview;
        Host.Content = preview.View;
        foreach (var tool in preview.Tools) ToolsHost.Children.Add(tool);
        preview.StatusChanged += OnStatus;
        OnStatus();
        Meta.Text = $"{preview.Info} • {FileMeta(path)}";
        PlaceWindow(preview.PreferredSize, preview is ImagePreview);
    }

    void OnStatus() => Status.Text = _preview?.Status ?? "";

    static string FileMeta(string path)
    {
        try
        {
            var f = new FileInfo(path);
            return $"{FormatSize(f.Length)} • {f.LastWriteTime:yyyy/MM/dd HH:mm}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    internal static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} بايت",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} ك.ب",
        < 1024L * 1024 * 1024 => $"{bytes / 1048576.0:0.#} م.ب",
        _ => $"{bytes / 1073741824.0:0.##} غ.ب",
    };

    void ReleasePreview()
    {
        if (_preview is { } p) p.StatusChanged -= OnStatus;
        Host.Content = null;
        ToolsHost.Children.Clear();
        Status.Text = "";
        _preview?.Dispose();
        _preview = null;
    }

    void PlaceWindow(Size? content, bool keepAspect)
    {
        nint hwnd = new WindowInteropHelper(this).EnsureHandle();
        nint monitor = MonitorFromWindow(_owner != 0 ? _owner : GetForegroundWindow(), MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfoW(monitor, ref info);
        GetDpiForMonitor(monitor, 0, out uint dpiX, out _);
        double dpi = dpiX / 96.0;

        double workW = (info.rcWork.Right - info.rcWork.Left) / dpi, workH = (info.rcWork.Bottom - info.rcWork.Top) / dpi;
        var (w, h) = WindowFit.Size(content, keepAspect, _settings, workW * MaxScreenShare, workH * MaxScreenShare, MinWidth, MinHeight, DefaultWidth, DefaultHeight, HeaderHeight);
        int pw = (int)(w * dpi), ph = (int)(h * dpi);
        SetWindowPos(hwnd, 0,
            info.rcWork.Left + (info.rcWork.Right - info.rcWork.Left - pw) / 2,
            info.rcWork.Top + (info.rcWork.Bottom - info.rcWork.Top - ph) / 2,
            pw, ph, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    void Navigate(Func<PreviewSession, bool> move)
    {
        if (_session is null) return;
        if (move(_session)) Load();
        else if (_session.Current is { } p && !File.Exists(p)) _notify("لم يعد هذا الملف موجودًا.");
    }

    public void ShowFile(PreviewSession session)
    {
        _session = session;
        Nav.Visibility = session.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        Load();
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        // Typing in the search or password box keeps its normal keys.
        bool typing = Keyboard.FocusedElement is TextBoxBase or PasswordBox;
        if (typing)
        {
            if (e.Key == Key.Escape) { Body.Focus(); (Host.Content as UIElement)?.Focus(); e.Handled = true; }
            return;
        }

        if (e.Key == Key.Escape && HelpPanel.IsVisible)
        {
            ToggleHelp();
            e.Handled = true;
            return;
        }

        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        switch (e.Key)
        {
            case Key.Space or Key.Escape when !ctrl:
                if (_fullscreen && e.Key == Key.Escape) ToggleFullscreen();
                else HidePreview();
                break;
            case Key.Left: Navigate(s => s.MovePrevious()); break;
            case Key.Right: Navigate(s => s.MoveNext()); break;
            case Key.Enter: OpenOriginal(); break;
            case Key.C when ctrl: CopyCurrent(); break;
            case Key.F when ctrl: _preview?.FocusSearch(); break;
            case Key.F: OpenFolder(); break;
            case Key.F1: ToggleHelp(); break;
            case Key.F11: ToggleFullscreen(); break;
            default:
                if (_preview?.OnKey(e.Key, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) != true) return;
                break;
        }
        e.Handled = true;
    }

    void OpenOriginal()
    {
        if (CurrentPath is not { } path) return;
        if (!File.Exists(path)) { _notify("لم يعد هذا الملف موجودًا."); return; }
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            HidePreview(restoreFocus: false);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            Log.Error("open original failed", e);
            _notify("لا يوجد برنامج لفتح هذا الملف.");
        }
    }

    void ToggleHelp() =>
        HelpPanel.Visibility = HelpPanel.IsVisible ? Visibility.Collapsed : Visibility.Visible;

    void OpenFolder()
    {
        if (CurrentPath is not { } path) return;
        if (!File.Exists(path)) { _notify("لم يعد هذا الملف موجودًا."); return; }
        Process.Start("explorer.exe", $"/select,\"{path}\"");
        HidePreview(restoreFocus: false);
    }

    void CopyCurrent()
    {
        if (_preview?.CopySelection() == true) return;
        if (CurrentPath is not { } path) return;
        if (!File.Exists(path)) { _notify("لم يعد هذا الملف موجودًا."); return; }
        var list = new System.Collections.Specialized.StringCollection { path };
        Clipboard.SetFileDropList(list);
        _notify("تم نسخ الملف.");
    }

    public void ToggleFullscreen()
    {
        _fullscreen = !_fullscreen;
        Header.Visibility = _fullscreen ? Visibility.Collapsed : Visibility.Visible;
        WindowState = _fullscreen ? WindowState.Maximized : WindowState.Normal;
    }

    public void HidePreview(bool restoreFocus = true)
    {
        if (!IsVisible) return;
        if (_fullscreen) ToggleFullscreen();
        _load?.Cancel();
        if (_settings.RememberSize)
        {
            _settings.LastWidth = ActualWidth;
            _settings.LastHeight = ActualHeight;
            _settings.Save(AppPaths.Settings);
        }
        ReleasePreview();
        Loading.Visibility = HelpPanel.Visibility = Visibility.Collapsed;
        Hide();
        Log.Info("preview closed");
        if (restoreFocus && IsWindow(_owner)) SetForegroundWindow(_owner);
        _ = ReleaseMemorySoon();
    }

    // The app idles in the tray all day, so what a large document used is handed back once the user has moved on.
    async Task ReleaseMemorySoon()
    {
        await Task.Delay(1500);
        if (IsVisible) return;
        await Task.Run(() =>
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
            GC.WaitForPendingFinalizers();
            SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
        });
    }

    void OnPrevious(object sender, RoutedEventArgs e) => Navigate(s => s.MovePrevious());
    void OnNext(object sender, RoutedEventArgs e) => Navigate(s => s.MoveNext());
    void OnClose(object sender, RoutedEventArgs e) => HidePreview();
    void OnOpenOriginal(object sender, RoutedEventArgs e) => OpenOriginal();
    void OnOpenFolder(object sender, RoutedEventArgs e) => OpenFolder();
    void OnHelp(object sender, RoutedEventArgs e) => ToggleHelp();

    void OnBodyDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2 && _preview is not null) OpenOriginal();
    }

    void OnPasswordKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; Load(PasswordInput.Password); }
    }

    void OnPasswordSubmit(object sender, RoutedEventArgs e) => Load(PasswordInput.Password);

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_reallyClosing) { e.Cancel = true; HidePreview(); }
        base.OnClosing(e);
    }

    public void Destroy()
    {
        _reallyClosing = true;
        Close();
    }
}

/// <summary>Window size arithmetic, kept free of WPF so it can be unit tested.</summary>
public static class WindowFit
{
    public static (double W, double H) Size(Size? content, bool keepAspect, AppSettings s, double maxW, double maxH, double minW, double minH, double defaultW, double defaultH, double header)
    {
        if (s.RememberSize && s.LastWidth > 0 && s.LastHeight > 0) return (Math.Min(s.LastWidth, maxW), Math.Min(s.LastHeight, maxH));
        if (s.Sizing == WindowSizing.Large) return (maxW, maxH);
        if (content is not { } c) return (Math.Min(defaultW, maxW), Math.Min(defaultH, maxH));

        double w = c.Width, h = c.Height + header;
        if (keepAspect)
        {
            double k = Math.Min(1, Math.Min(maxW / w, (maxH - header) / c.Height));
            w = c.Width * k;
            h = c.Height * k + header;
        }
        return (Math.Clamp(w, minW, maxW), Math.Clamp(h, minH, maxH));
    }
}
