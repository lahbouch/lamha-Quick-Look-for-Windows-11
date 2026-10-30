using System.Diagnostics;
using System.Windows;
using Lamha.Core;

namespace Lamha.Ui;

public partial class SettingsWindow : Window
{
    static SettingsWindow? _open;
    readonly PreviewController _controller;

    SettingsWindow(PreviewController controller)
    {
        _controller = controller;
        InitializeComponent();

        var s = controller.Settings;
        StartWithWindows.IsChecked = Startup.Enabled;
        Integration.IsChecked = s.ExplorerIntegration;
        AutoPreview.IsChecked = s.AutoPreview;
        Sizing.SelectedIndex = (int)s.Sizing;
        Zoom.SelectedIndex = (int)s.Zoom;
        RememberSize.IsChecked = s.RememberSize;
        VerboseLog.IsChecked = s.VerboseLog;
        var version = typeof(App).Assembly.GetName().Version;
        About.Text = $"لمحة، الإصدار {version?.ToString(3)}\nمعاينة سريعة للملفات في Windows. يعمل دون اتصال بالإنترنت، ولا يرسل أي بيانات، ولا يعدّل ملفاتك.";
    }

    public static void ShowSingle(PreviewController controller)
    {
        _open ??= new SettingsWindow(controller);
        _open.Closed += (_, _) => _open = null;
        _open.Show();
        _open.Activate();
    }

    void OnSave(object sender, RoutedEventArgs e)
    {
        var s = _controller.Settings;
        Startup.Enabled = StartWithWindows.IsChecked == true;
        s.ExplorerIntegration = Integration.IsChecked == true;
        s.AutoPreview = AutoPreview.IsChecked == true;
        s.Sizing = (WindowSizing)Sizing.SelectedIndex;
        s.Zoom = (DefaultZoom)Zoom.SelectedIndex;
        s.RememberSize = RememberSize.IsChecked == true;
        s.VerboseLog = Log.Verbose = VerboseLog.IsChecked == true;
        s.Save(AppPaths.Settings);
        Close();
    }

    void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.Logs);
        Process.Start(new ProcessStartInfo(AppPaths.Logs) { UseShellExecute = true });
    }
}
