using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using Lamha.Core;
using Lamha.Ui;

namespace Lamha.Previews;

public sealed class TextProvider : IPreviewProvider
{
    const int MaxBytes = 64 * 1024 * 1024, MaxHighlightChars = 2 * 1024 * 1024, MaxCsvRows = 100_000;

    public bool Handles(FileKind kind) => kind is FileKind.Text or FileKind.Csv;

    public async Task<IPreview> OpenAsync(PreviewRequest r, CancellationToken ct)
    {
        var (text, encoding, truncated) = await Task.Run(() => Load(r.Path, ct), ct);
        var note = truncated ? " • تم عرض أول 64 ميغابايت" : "";

        if (FileTypes.Detect(r.Path) == FileKind.Csv)
        {
            var rows = await Task.Run(() => CsvParser.Parse(text, MaxCsvRows), ct);
            return new CsvPreview(rows, $"{rows.Count} صف • {encoding}{note}", TextDecoder.IsMostlyRtl(text));
        }
        var ext = Path.GetExtension(r.Path);
        return new TextPreview(text, ext, $"{encoding}{note}");
    }

    static (string, string, bool) Load(string path, CancellationToken ct)
    {
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[Math.Min(s.Length, MaxBytes)];
        int read = s.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        ct.ThrowIfCancellationRequested();
        var (text, encoding) = TextDecoder.Decode(buffer, read);
        return (text, encoding, s.Length > MaxBytes);
    }

    internal static int HighlightLimit => MaxHighlightChars;
}

sealed class TextPreview : IPreview
{
    static readonly string[] Prose = [".txt", ".md", ".log"];

    readonly TextEditor _editor;
    double _size = 14;

    public FrameworkElement View => _editor;
    public IReadOnlyList<UIElement> Tools { get; }
    public string Info { get; }
    public string Status => $"{_size / 14 * 100:0}%";
    public event Action? StatusChanged;

    public TextPreview(string text, string ext, string info)
    {
        Info = info;
        bool prose = Prose.Contains(ext, StringComparer.OrdinalIgnoreCase);
        var plex = (FontFamily)Application.Current.FindResource("PlexArabic");
        _editor = new TextEditor
        {
            IsReadOnly = true,
            ShowLineNumbers = true,
            WordWrap = prose,
            Text = text,
            FontSize = _size,
            // Plex carries the Arabic glyphs that Consolas lacks, so code with Arabic strings still renders.
            FontFamily = prose ? plex : new FontFamily($"Cascadia Mono, Consolas, {plex.Source}"),
            Background = Brushes.White,
            Foreground = (Brush)Application.Current.FindResource("TextPrimary"),
            LineNumbersForeground = (Brush)Application.Current.FindResource("TextSecondary"),
            Padding = new Thickness(10, 8, 10, 8),
            FlowDirection = TextDecoder.IsMostlyRtl(text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _editor.Options.EnableHyperlinks = false; // documents must not offer a click through to the network
        _editor.Options.EnableEmailHyperlinks = false;
        _editor.TextArea.Caret.CaretBrush = Brushes.Transparent;
        if (text.Length <= TextProvider.HighlightLimit)
            _editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext.ToLowerInvariant() is ".htm" ? ".html" : ext.ToLowerInvariant());

        _editor.PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            Resize(_size * (e.Delta > 0 ? 1.1 : 1 / 1.1));
            e.Handled = true;
        };
        _editor.Loaded += (_, _) => _editor.TextArea.Focus();

        Tools =
        [
            Tool.Button("", "تصغير (-)", () => Resize(_size / 1.15)),
            Tool.Button("", "تكبير (+)", () => Resize(_size * 1.15)),
            Tool.Button("", "الحجم الفعلي (1)", () => Resize(14)),
            Tool.Button("", "التفاف الأسطر (W)", () => _editor.WordWrap = !_editor.WordWrap),
        ];
    }

    void Resize(double size)
    {
        _size = Math.Clamp(size, 7, 56);
        _editor.FontSize = _size;
        StatusChanged?.Invoke();
    }

    public bool OnKey(Key key, bool shift)
    {
        switch (key)
        {
            case Key.Add or Key.OemPlus: Resize(_size * 1.15); break;
            case Key.Subtract or Key.OemMinus: Resize(_size / 1.15); break;
            case Key.D1 or Key.NumPad1: Resize(14); break;
            case Key.W: _editor.WordWrap = !_editor.WordWrap; break;
            default: return false;
        }
        return true;
    }

    public bool CopySelection()
    {
        if (_editor.SelectionLength == 0) return false;
        _editor.Copy();
        return true;
    }

    public void Dispose() { }
}

sealed class CsvPreview : IPreview
{
    readonly DataGrid _grid;

    public FrameworkElement View => _grid;
    public IReadOnlyList<UIElement> Tools { get; } = [];
    public string Info { get; }

    public CsvPreview(List<string[]> rows, string info, bool rtl)
    {
        Info = info;
        var table = new DataTable();
        int columns = rows.Count == 0 ? 0 : rows.Max(r => r.Length);
        var header = rows.FirstOrDefault() ?? [];
        for (int c = 0; c < columns; c++)
            table.Columns.Add(new DataColumn($"c{c}") { Caption = c < header.Length ? header[c] : "" });
        foreach (var row in rows.Skip(1))
            table.Rows.Add(row.Cast<object>().ToArray());

        _grid = new DataGrid
        {
            ItemsSource = table.DefaultView,
            IsReadOnly = true,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true,
            HeadersVisibility = DataGridHeadersVisibility.All,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            HorizontalGridLinesBrush = (Brush)Application.Current.FindResource("Hairline"),
            VerticalGridLinesBrush = (Brush)Application.Current.FindResource("Hairline"),
            Background = Brushes.White,
            RowBackground = Brushes.White,
            AlternatingRowBackground = Brushes.White,
            BorderThickness = new Thickness(0),
            FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
        };
        foreach (DataColumn col in table.Columns)
            _grid.Columns.Add(new DataGridTextColumn { Header = col.Caption, Binding = new System.Windows.Data.Binding($"[{col.ColumnName}]"), MaxWidth = 420 });
    }

    public void Dispose() { }
}
