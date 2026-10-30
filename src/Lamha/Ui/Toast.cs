using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Lamha.Explorer;
using static Lamha.Explorer.Native;

namespace Lamha.Ui;

/// <summary>A small note at the bottom of the active monitor that never takes focus.</summary>
public sealed class Toast : Window
{
    readonly TextBlock _text = new() { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxWidth = 460, TextAlignment = TextAlignment.Center };
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2.8) };

    public Toast()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FlowDirection = FlowDirection.RightToLeft;
        FontFamily = (FontFamily)Application.Current.FindResource("PlexArabic");
        FontSize = 13;
        Title = "لمحة";
        Content = new Border
        {
            Background = (Brush)Application.Current.FindResource("TextPrimary"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18, 10, 18, 10),
            Child = _text,
        };
        _timer.Tick += (_, _) => { _timer.Stop(); Hide(); };
    }

    public void Show(string message)
    {
        _text.Text = message;
        Show();
        UpdateLayout();

        nint hwnd = new WindowInteropHelper(this).Handle;
        nint monitor = MonitorFromWindow(GetForegroundWindow(), MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfoW(monitor, ref info);
        GetDpiForMonitor(monitor, 0, out uint dpiX, out _);
        double dpi = dpiX / 96.0;
        int w = (int)(ActualWidth * dpi), h = (int)(ActualHeight * dpi);
        SetWindowPos(hwnd, 0, (info.rcWork.Left + info.rcWork.Right - w) / 2, info.rcWork.Bottom - h - (int)(48 * dpi), w, h, SWP_NOZORDER | SWP_NOACTIVATE);

        _timer.Stop();
        _timer.Start();
    }
}
