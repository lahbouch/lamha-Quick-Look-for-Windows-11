using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Lamha.Core;
using Lamha.Office;
using Lamha.Ui;

namespace Lamha;

public partial class App : Application
{
    const string MutexName = @"Local\Lamha.SingleInstance", ShowSettingsEvent = @"Local\Lamha.ShowSettings";

    Mutex? _mutex;
    EventWaitHandle? _showSettings;
    PreviewController? _controller;
    TrayIcon? _tray;
    OfficeConverter? _converter;
    bool _quiet;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUiException;
        AppDomain.CurrentDomain.UnhandledException += (_, x) => Log.Error("fatal", x.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, x) => { Log.Error("unobserved task", x.Exception); x.SetObserved(); };

        var args = e.Args.ToList();
        bool restarting = args.Remove("--restart");
        _quiet = args.Remove("--tray");

        _mutex = new Mutex(true, MutexName, out bool first);
        if (!first && restarting) first = _mutex.WaitOne(TimeSpan.FromSeconds(5));
        _showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEvent);
        if (!first)
        {
            _showSettings.Set(); // launching a second copy from the Start Menu means "show me the app"
            Shutdown();
            return;
        }

        var settings = AppSettings.Load(AppPaths.Settings);
        Log.Verbose = settings.VerboseLog;
        Log.Info($"startup {typeof(App).Assembly.GetName().Version}");

        _converter = new OfficeConverter();
        _controller = new PreviewController(settings, _converter);
        _tray = new TrayIcon(_controller, ShowSettings, Restart, () => Shutdown());
        Task.Run(OfficeConverter.PruneCache);
        _ = TrimAfterStartup();

        new Thread(WaitForShowSettings) { IsBackground = true, Name = "show-settings" }.Start();

        if (args.Count > 0) _controller.Open(args);
        else if (!_quiet) _controller.Notify("لمحة تعمل في الخلفية. حدد ملفًا في مستكشف الملفات واضغط مسافة.");
    }

    // Startup touches a lot of code that an idle tray app will not need again.
    static async Task TrimAfterStartup()
    {
        await Task.Delay(TimeSpan.FromSeconds(3));
        Explorer.Native.SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
    }

    void WaitForShowSettings()
    {
        while (_showSettings?.WaitOne() == true)
            Dispatcher.BeginInvoke(ShowSettings);
    }

    void ShowSettings() => SettingsWindow.ShowSingle(_controller!);

    void Restart()
    {
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--restart --tray") { UseShellExecute = false });
        Shutdown();
    }

    void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("unhandled", e.Exception);
        e.Handled = true; // a bad document must not take down the tray app
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _controller?.Dispose();
        _converter?.Dispose();
        _mutex?.Dispose();
        _showSettings?.Dispose();
        Log.Info("shutdown");
        base.OnExit(e);
    }
}
