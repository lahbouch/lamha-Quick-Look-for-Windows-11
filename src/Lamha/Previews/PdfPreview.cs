using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lamha.Core;
using Lamha.Pdf;
using Lamha.Ui;

namespace Lamha.Previews;

public sealed class PdfProvider : IPreviewProvider
{
    public bool Handles(FileKind kind) => kind == FileKind.Pdf;

    public async Task<IPreview> OpenAsync(PreviewRequest r, CancellationToken ct)
    {
        var doc = await Task.Run(() => PdfDoc.Open(r.Path, r.Password), ct);
        return new PdfPreview(doc, new PdfPreview.Options("صفحة", ToFit(r.Settings.Zoom), Print: true));
    }

    internal static FitMode ToFit(DefaultZoom z) => z switch
    {
        DefaultZoom.FitPage => FitMode.Page,
        DefaultZoom.ActualSize => FitMode.Actual,
        _ => FitMode.Width,
    };
}

/// <summary>The reader for PDFs and for Office files converted to PDF.</summary>
public sealed class PdfPreview : IPreview
{
    public sealed record Options(string UnitName, FitMode Fit, bool Print, IReadOnlyList<(string Title, int Page)>? Sheets = null);

    readonly PdfDoc _doc;
    readonly Options _options;
    readonly ScrollViewer _scroll;
    readonly PdfSurface _surface;
    readonly ListBox _thumbs;
    readonly TextBox? _search;
    readonly TextBlock? _searchCount;
    readonly List<SearchHit> _found = [];
    readonly CancellationTokenSource _life = new();
    CancellationTokenSource? _searchCts;
    int _foundIndex = -1;
    ListBox? _tabs;
    string _query = "";

    public FrameworkElement View { get; }
    public IReadOnlyList<UIElement> Tools { get; }
    public string Info => $"{_options.Sheets?.Count ?? _doc.PageCount} {_options.UnitName}";
    public event Action? StatusChanged;
    public string Status => _options.Sheets is { } s
        ? $"{Percent}"
        : $"{_surface.CurrentPage + 1} / {_doc.PageCount}   {Percent}";

    string Percent => $"{_surface.Zoom * 100:0}%";

    public Size? PreferredSize
    {
        get
        {
            var s = _doc.Sizes[0];
            return new Size(Math.Max(s.Width * 4 / 3 + 48, 920), s.Height * 4 / 3 + 40);
        }
    }

    public PdfPreview(PdfDoc doc, Options options)
    {
        _doc = doc;
        _options = options;
        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Focusable = true,
            CanContentScroll = false,
        };
        _surface = new PdfSurface(doc, _scroll);
        _surface.SetFit(options.Fit);
        _surface.Changed += () => StatusChanged?.Invoke();
        _scroll.Content = _surface;

        _thumbs = BuildThumbnails();

        var grid = new Grid { FlowDirection = FlowDirection.LeftToRight };
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_thumbs, 1);
        grid.Children.Add(_thumbs);
        grid.Children.Add(_scroll);
        if (options.Sheets is { Count: > 0 } sheets)
        {
            var tabs = BuildSheetTabs(sheets);
            Grid.SetRow(tabs, 1);
            Grid.SetColumnSpan(tabs, 2);
            grid.Children.Add(tabs);
        }
        View = grid;

        var tools = new List<UIElement>();
        if (options.Sheets is null && doc.PageCount > 1)
        {
            tools.Add(Tool.Button("", "السابق (Page Up)", () => Move(-1)));
            tools.Add(Tool.Button("", "التالي (Page Down)", () => Move(+1)));
            tools.Add(Tool.Button("", "المصغرات (T)", ToggleThumbnails));
            tools.Add(Tool.Separator());
        }
        tools.Add(Tool.Button("", "تصغير (-)", () => _surface.ZoomBy(1 / 1.2)));
        tools.Add(Tool.Button("", "تكبير (+)", () => _surface.ZoomBy(1.2)));
        tools.Add(Tool.Button("", "ملاءمة العرض (2)", () => _surface.SetFit(FitMode.Width)));
        tools.Add(Tool.Button("", "ملاءمة الصفحة (0)", () => _surface.SetFit(FitMode.Page)));
        tools.Add(Tool.Button("", "الحجم الفعلي (1)", () => _surface.SetFit(FitMode.Actual)));
        tools.Add(Tool.Separator());

        _search = new TextBox { Width = 130, Style = (Style)Application.Current.FindResource("SearchBox"), ToolTip = "بحث في المستند (Ctrl+F)" };
        _search.KeyDown += OnSearchKey;
        _searchCount = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 2, 0), Foreground = (Brush)Application.Current.FindResource("TextSecondary"), FontSize = 12 };
        tools.Add(_search);
        tools.Add(_searchCount);
        if (options.Print) tools.Add(Tool.Button("", "طباعة (Ctrl+P)", Print));
        Tools = tools;

        _scroll.Loaded += (_, _) => _scroll.Focus();
    }

    ListBox BuildThumbnails()
    {
        var list = new ListBox
        {
            Width = 150,
            Visibility = Visibility.Collapsed,
            BorderThickness = new Thickness(0),
            Background = (Brush)Application.Current.FindResource("Surface"),
            ItemsSource = Enumerable.Range(0, _doc.PageCount).ToList(),
        };
        VirtualizingPanel.SetIsVirtualizing(list, true);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);

        var factory = new FrameworkElementFactory(typeof(Image));
        factory.SetValue(FrameworkElement.WidthProperty, 118.0);
        factory.SetValue(FrameworkElement.HeightProperty, 150.0);
        factory.SetValue(FrameworkElement.MarginProperty, new Thickness(2));
        factory.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(LoadThumbnail));
        list.ItemTemplate = new DataTemplate { VisualTree = factory };
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is int p) _surface.ScrollToPage(p); };
        return list;
    }

    async void LoadThumbnail(object sender, RoutedEventArgs e)
    {
        if (sender is not Image { DataContext: int page } image) return;
        var ct = _life.Token;
        var size = _doc.Sizes[page];
        double scale = 118 * VisualTreeHelper.GetDpi(image).DpiScaleX / size.Width;
        var px = new Int32Rect(0, 0, (int)Math.Round(size.Width * scale), (int)Math.Round(size.Height * scale));
        try
        {
            var bmp = await Task.Run(() => _doc.RenderTile(page, scale, px, ct), ct);
            if (image.DataContext is int now && now == page) image.Source = bmp;
        }
        catch (Exception x) when (x is OperationCanceledException or ObjectDisposedException or PdfCorruptException) { }
    }

    void ToggleThumbnails() =>
        _thumbs.Visibility = _thumbs.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    FrameworkElement BuildSheetTabs(IReadOnlyList<(string Title, int Page)> sheets)
    {
        var tabs = new ListBox
        {
            Style = (Style)Application.Current.FindResource("SheetTabs"),
            FlowDirection = FlowDirection.RightToLeft,
            ItemsSource = sheets.Select(s => s.Title).ToList(),
            SelectedIndex = 0,
        };
        _tabs = tabs;
        tabs.SelectionChanged += (_, _) =>
        {
            if (tabs.SelectedIndex >= 0) _surface.ShowPages([sheets[tabs.SelectedIndex].Page]);
            ClearSearch();
        };
        _surface.ShowPages([sheets[0].Page]);
        return tabs;
    }

    void OnSearchKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (_search!.Text.Trim() != _query) StartSearch();
        else Jump(Keyboard.Modifiers == ModifierKeys.Shift ? -1 : +1);
    }

    async void StartSearch()
    {
        var query = _search!.Text.Trim();
        ClearSearch();
        _query = query;
        if (query.Length == 0) return;

        _searchCount!.Text = "…";
        var cts = _searchCts = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
        var hits = new List<SearchHit>();
        var rects = new Dictionary<int, List<Rect>>();
        try
        {
            await Task.Run(() =>
            {
                _doc.Search(query, h => hits.Add(h), cts.Token);
                foreach (var h in hits)
                {
                    if (!rects.TryGetValue(h.Page, out var list)) rects[h.Page] = list = [];
                    list.AddRange(_doc.TextRects(h.Page, h.Start, h.Count));
                }
            }, cts.Token);
        }
        catch (OperationCanceledException) { return; }
        if (cts.IsCancellationRequested) return;

        _found.AddRange(hits);
        _surface.SetHits(rects);
        if (_found.Count == 0) _searchCount!.Text = "لا نتائج";
        else Jump(+1);
    }

    void Jump(int step)
    {
        if (_found.Count == 0) return;
        _foundIndex = (_foundIndex + step + _found.Count) % _found.Count;
        var h = _found[_foundIndex];
        _surface.ShowHit(h.Page, _doc.TextRects(h.Page, h.Start, h.Count).FirstOrDefault());
        _searchCount!.Text = $"{_foundIndex + 1} / {_found.Count}";
    }

    void ClearSearch()
    {
        _searchCts?.Cancel();
        _searchCts = null;
        _found.Clear();
        _foundIndex = -1;
        _query = "";
        if (_searchCount is not null) _searchCount.Text = "";
        _surface.SetHits([]);
    }

    void Move(int delta)
    {
        if (_tabs is { } t) t.SelectedIndex = Math.Clamp(t.SelectedIndex + delta, 0, t.Items.Count - 1);
        else _surface.Step(delta);
    }

    public bool OnKey(Key key, bool shift)
    {
        switch (key)
        {
            case Key.Add or Key.OemPlus: _surface.ZoomBy(1.2); break;
            case Key.Subtract or Key.OemMinus: _surface.ZoomBy(1 / 1.2); break;
            case Key.D0 or Key.NumPad0: _surface.SetFit(FitMode.Page); break;
            case Key.D1 or Key.NumPad1: _surface.SetFit(FitMode.Actual); break;
            case Key.D2 or Key.NumPad2: _surface.SetFit(FitMode.Width); break;
            case Key.PageUp: Move(-1); break;
            case Key.PageDown: Move(+1); break;
            case Key.Home when _tabs is null: _surface.ScrollToPage(0); break;
            case Key.End when _tabs is null: _surface.ScrollToPage(_doc.PageCount - 1); break;
            case Key.T when _tabs is null && _doc.PageCount > 1: ToggleThumbnails(); break;
            case Key.F3: Jump(shift ? -1 : +1); break;
            case Key.P when _options.Print: Print(); break;
            default: return false;
        }
        return true;
    }

    public void FocusSearch()
    {
        _search?.Focus();
        _search?.SelectAll();
    }

    public bool CopySelection()
    {
        var text = _surface.SelectedText();
        if (text.Length == 0) return false;
        Clipboard.SetText(text);
        return true;
    }

    void Print()
    {
        var dialog = new PrintDialog { UserPageRangeEnabled = true, MinPage = 1, MaxPage = (uint)_doc.PageCount };
        if (dialog.ShowDialog() != true) return;
        var size = new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
        dialog.PrintDocument(new PdfPaginator(_doc, size), "لمحة");
    }

    public void Dispose()
    {
        _life.Cancel();
        _searchCts?.Cancel();
        _surface.Cancel();
        _doc.Dispose();
    }
}

/// <summary>Prints pages as 300 DPI bitmaps, centred and scaled to the printable area.</summary>
sealed class PdfPaginator(PdfDoc doc, Size paper) : DocumentPaginator
{
    const double PrintDpi = 300;

    public override bool IsPageCountValid => true;
    public override int PageCount => doc.PageCount;
    public override Size PageSize { get => paper; set { } }
    public override IDocumentPaginatorSource? Source => null;

    public override DocumentPage GetPage(int n)
    {
        var size = doc.Sizes[n];
        double px = PrintDpi / 72;
        var bmp = doc.RenderTile(n, px, new Int32Rect(0, 0, (int)(size.Width * px), (int)(size.Height * px)), CancellationToken.None);
        double fit = Math.Min(paper.Width / (size.Width * 4 / 3), paper.Height / (size.Height * 4 / 3));
        double w = size.Width * 4 / 3 * fit, h = size.Height * 4 / 3 * fit;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawImage(bmp, new Rect((paper.Width - w) / 2, (paper.Height - h) / 2, w, h));
        return new DocumentPage(visual, paper, new Rect(paper), new Rect(paper));
    }
}
