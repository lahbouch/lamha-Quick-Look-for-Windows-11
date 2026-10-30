using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Lamha.Ui;

/// <summary>Notification-area icon with a WPF menu, so the menu shares the app's RTL layout and font.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly System.Windows.Forms.NotifyIcon _icon;
    readonly ContextMenu _menu;
    readonly MenuItem _integration;
    readonly Window _anchor; // a popup only closes on outside clicks if a window of ours is active

    public TrayIcon(PreviewController controller, Action showSettings, Action restart, Action exit)
    {
        _integration = new MenuItem { Header = "تفعيل التكامل مع المستكشف", IsCheckable = true, IsChecked = controller.Settings.ExplorerIntegration };
        _integration.Click += (_, _) => controller.SetIntegration(_integration.IsChecked);

        _menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        _menu.Items.Add(Item("الإعدادات", showSettings));
        _menu.Items.Add(_integration);
        _menu.Items.Add(new Separator());
        _menu.Items.Add(Item("إعادة تشغيل التطبيق", restart));
        _menu.Items.Add(Item("خروج", exit));
        _menu.Opened += (_, _) => _integration.IsChecked = controller.Settings.ExplorerIntegration;
        _menu.Closed += (_, _) => _anchor!.Hide();

        _anchor = new Window { Width = 1, Height = 1, Left = -100, Top = -100, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize };

        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = "لمحة",
            Visible = true,
        };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Right) ShowMenu();
            else if (e.Button == System.Windows.Forms.MouseButtons.Left) showSettings();
        };
    }

    static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    void ShowMenu()
    {
        _anchor.Show();
        _anchor.Activate();
        _menu.IsOpen = true;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _anchor.Close();
    }
}
