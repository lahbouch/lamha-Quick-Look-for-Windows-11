using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Lamha.Ui;

/// <summary>Toolbar parts shared by all previews.</summary>
public static class Tool
{
    public static Button Button(string glyph, string tip, Action action)
    {
        var b = new Button
        {
            Style = (Style)Application.Current.FindResource("ToolButton"),
            Content = new TextBlock { Text = glyph, FontFamily = (FontFamily)Application.Current.FindResource("Icons") },
            ToolTip = tip,
        };
        AutomationProperties.SetName(b, tip); // the glyph alone means nothing to a screen reader
        b.Click += (_, _) => action();
        return b;
    }

    public static UIElement Separator() => new Rectangle
    {
        Width = 1,
        Height = 18,
        Margin = new Thickness(6, 0, 6, 0),
        Fill = (Brush)Application.Current.FindResource("Hairline"),
    };
}
