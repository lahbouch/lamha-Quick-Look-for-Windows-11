using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lamha.Core;

namespace Lamha.Pdf;

public enum FitMode { None, Width, Page, Actual }

/// <summary>
/// Scrollable page canvas. Only a tile around the viewport is rendered, at device pixels for the current zoom, so memory
/// is bounded by the screen size rather than the document, and text stays sharp at any zoom. Old tiles are stretched
/// until their replacement arrives.
/// </summary>
public sealed class PdfSurface : FrameworkElement
{
    const double PageMargin = 14, Gap = 12, MinZoom = 0.1, MaxZoom = 8, PointsToDip = 96.0 / 72;

    sealed record Tile(double PxPerPt, Int32Rect Px, int FullW, int FullH, System.Windows.Media.Imaging.BitmapSource Bitmap);

    static readonly Brush PageBrush = Frozen(Colors.White);
    static readonly Pen PagePen = Frozen(new Pen(Frozen(Color.FromArgb(40, 64, 64, 64)), 1));
    static readonly Brush SelectBrush = Frozen(Color.FromArgb(90, 0x9A, 0x26, 0x2B));
    static readonly Brush HitBrush = Frozen(Color.FromArgb(90, 0x9B, 0x7F, 0x5D));
    static readonly Brush ActiveHitBrush = Frozen(Color.FromArgb(120, 0x6C, 0x9C, 0xB4));

    readonly PdfDoc _doc;
    readonly ScrollViewer _host;
    readonly DispatcherTimer _timer;
    readonly Dictionary<int, Tile> _tiles = [];
    CancellationTokenSource? _cts;

    int[] _pages;
    double[] _tops = [];
    double _contentW, _contentH;

    int _selPage = -1, _selAnchor, _selEnd;
    List<Rect> _selRects = [];
    Dictionary<int, List<Rect>> _hits = [];
    (int Page, Rect Rect)? _activeHit;

    public double Zoom { get; private set; } = 1;
    public FitMode Fit { get; private set; } = FitMode.Width;
    public int CurrentPage { get; private set; }
    public event Action? Changed;

    static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    static Pen Frozen(Pen p) { p.Freeze(); return p; }

    public PdfSurface(PdfDoc doc, ScrollViewer host)
    {
        _doc = doc;
        _host = host;
        _pages = Enumerable.Range(0, doc.PageCount).ToArray();
        Cursor = Cursors.IBeam;
        Focusable = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(30) };
        _timer.Tick += (_, _) => { _timer.Stop(); _ = RenderPass(); };
        host.ScrollChanged += (_, e) =>
        {
            if (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0) ApplyFit();
            UpdateCurrentPage();
            InvalidateVisual();
            Schedule();
        };
        Layout();
    }

    /// <summary>Restricts the view to some pages; a workbook shows one sheet at a time.</summary>
    public void ShowPages(int[] pages)
    {
        _pages = pages;
        ClearSelection();
        Layout();
        ApplyFit();
        _host.ScrollToHome();
        InvalidateVisual();
        Schedule();
    }

    void Layout()
    {
        double scale = Zoom * PointsToDip, y = PageMargin, w = 0;
        _tops = new double[_pages.Length];
        for (int i = 0; i < _pages.Length; i++)
        {
            var s = _doc.Sizes[_pages[i]];
            _tops[i] = y;
            y += Snap(s.Height * scale) + Gap;
            w = Math.Max(w, s.Width * scale);
        }
        _contentW = w + 2 * PageMargin;
        _contentH = y - Gap + PageMargin;
        InvalidateMeasure();
    }

    double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    double Snap(double dip) { double d = DpiScale; return Math.Round(dip * d) / d; }

    protected override Size MeasureOverride(Size available) => new(_contentW, _contentH);

    Rect PageRect(int slot)
    {
        var s = _doc.Sizes[_pages[slot]];
        double scale = Zoom * PointsToDip, w = Snap(s.Width * scale), h = Snap(s.Height * scale);
        return new Rect(Snap((RenderSize.Width - w) / 2), _tops[slot], w, h);
    }

    Rect ViewportRect => new(_host.HorizontalOffset, _host.VerticalOffset, _host.ViewportWidth, _host.ViewportHeight);

    IEnumerable<int> SlotsIn(Rect r)
    {
        for (int i = 0; i < _pages.Length; i++)
            if (PageRect(i) is var p && p.IntersectsWith(r)) yield return i;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize)); // otherwise gaps between pages ignore the mouse

        foreach (int slot in SlotsIn(ViewportRect))
        {
            int page = _pages[slot];
            var r = PageRect(slot);
            dc.DrawRectangle(PageBrush, PagePen, r);

            if (_tiles.TryGetValue(page, out var t))
            {
                dc.PushClip(new RectangleGeometry(r));
                dc.DrawImage(t.Bitmap, new Rect(
                    r.X + r.Width * t.Px.X / t.FullW, r.Y + r.Height * t.Px.Y / t.FullH,
                    r.Width * t.Px.Width / t.FullW, r.Height * t.Px.Height / t.FullH));
                dc.Pop();
            }

            if (_hits.TryGetValue(page, out var hits))
                foreach (var h in hits) Draw(dc, HitBrush, r, h);
            if (_activeHit is { } a && a.Page == page) Draw(dc, ActiveHitBrush, r, a.Rect);
            if (_selPage == page)
                foreach (var s in _selRects) Draw(dc, SelectBrush, r, s);
        }
    }

    void Draw(DrawingContext dc, Brush brush, Rect page, Rect pts)
    {
        double k = Zoom * PointsToDip;
        dc.DrawRectangle(brush, null, new Rect(page.X + pts.X * k, page.Y + pts.Y * k, pts.Width * k, pts.Height * k));
    }

    void Schedule() { _timer.Stop(); _timer.Start(); }

    async Task RenderPass()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var ct = cts.Token;

        double dpi = DpiScale, pxPerPt = Zoom * PointsToDip * dpi;
        var view = ViewportRect;
        if (view.IsEmpty || view.Width <= 0) return;
        var ahead = view;
        ahead.Inflate(0, view.Height);

        var wanted = SlotsIn(ahead).ToList();
        foreach (int page in _tiles.Keys.Except(wanted.Select(s => _pages[s])).ToList()) _tiles.Remove(page);

        // Render what is on screen before what is merely close to it.
        foreach (int slot in wanted.OrderBy(s => PageRect(s).IntersectsWith(view) ? 0 : 1))
        {
            int page = _pages[slot];
            var r = PageRect(slot);
            var strict = ToPixels(Rect.Intersect(view, r), r, dpi);
            if (_tiles.TryGetValue(page, out var t) && t.PxPerPt == pxPerPt && Contains(t.Px, strict)) continue;

            var area = Rect.Intersect(ahead, r);
            area.Inflate(view.Width / 2, 0);
            area.Intersect(r);
            var px = ToPixels(area, r, dpi);
            if (px.Width <= 0 || px.Height <= 0) continue;

            try
            {
                var bmp = await Task.Run(() => _doc.RenderTile(page, pxPerPt, px, ct), ct);
                if (ct.IsCancellationRequested) return;
                var s = _doc.Sizes[page];
                _tiles[page] = new Tile(pxPerPt, px, (int)Math.Round(s.Width * pxPerPt), (int)Math.Round(s.Height * pxPerPt), bmp);
                InvalidateVisual();
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (PdfCorruptException) { Log.Error($"page {page} failed to render"); }
        }

        // Nearest-neighbour at exact scale keeps text crisp if scrolling lands on a fractional pixel.
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
    }

    static Int32Rect ToPixels(Rect area, Rect page, double dpi)
    {
        if (area.IsEmpty) return Int32Rect.Empty;
        int x = (int)Math.Floor((area.X - page.X) * dpi), y = (int)Math.Floor((area.Y - page.Y) * dpi);
        int r = (int)Math.Ceiling((area.Right - page.X) * dpi), b = (int)Math.Ceiling((area.Bottom - page.Y) * dpi);
        return new Int32Rect(x, y, r - x, b - y);
    }

    static bool Contains(Int32Rect outer, Int32Rect inner) =>
        inner.Width <= 0 || (inner.X >= outer.X && inner.Y >= outer.Y &&
                             inner.X + inner.Width <= outer.X + outer.Width && inner.Y + inner.Height <= outer.Y + outer.Height);

    void UpdateCurrentPage()
    {
        if (_pages.Length == 0) return;
        double middle = _host.VerticalOffset + _host.ViewportHeight / 2;
        int slot = Array.BinarySearch(_tops, middle);
        if (slot < 0) slot = Math.Max(0, ~slot - 1);
        int page = _pages[slot];
        if (page != CurrentPage) { CurrentPage = page; Changed?.Invoke(); }
    }

    public void ScrollToPage(int page)
    {
        int slot = Array.IndexOf(_pages, Math.Clamp(page, 0, _doc.PageCount - 1));
        if (slot >= 0) _host.ScrollToVerticalOffset(_tops[slot] - PageMargin);
    }

    public void Step(int delta)
    {
        int slot = Array.IndexOf(_pages, CurrentPage) + delta;
        if (slot >= 0 && slot < _pages.Length) ScrollToPage(_pages[slot]);
    }

    public void SetFit(FitMode fit)
    {
        Fit = fit;
        ApplyFit();
    }

    void ApplyFit()
    {
        if (Fit == FitMode.None || _pages.Length == 0 || _host.ViewportWidth <= 0) return;
        double maxW = _pages.Max(p => _doc.Sizes[p].Width) * PointsToDip;
        var cur = _doc.Sizes[Array.IndexOf(_pages, CurrentPage) >= 0 ? CurrentPage : _pages[0]];
        double availW = _host.ActualWidth - 2 * PageMargin - SystemParameters.VerticalScrollBarWidth;
        double zoom = Fit switch
        {
            FitMode.Width => availW / maxW,
            FitMode.Page => Math.Min(availW / maxW, (_host.ActualHeight - 2 * PageMargin) / (cur.Height * PointsToDip)),
            _ => 1,
        };
        SetZoomCore(zoom, null);
    }

    public void ZoomBy(double factor, Point? anchor = null)
    {
        Fit = FitMode.None;
        SetZoomCore(Zoom * factor, anchor);
    }

    void SetZoomCore(double zoom, Point? anchor)
    {
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        if (Math.Abs(zoom - Zoom) < 0.0005) { Changed?.Invoke(); return; }

        var inView = anchor ?? new Point(_host.ViewportWidth / 2, _host.ViewportHeight / 2);
        double fx = (_host.HorizontalOffset + inView.X) / Math.Max(1, _contentW);
        double fy = (_host.VerticalOffset + inView.Y) / Math.Max(1, _contentH);

        Zoom = zoom;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
        Layout();
        _host.UpdateLayout();
        _host.ScrollToHorizontalOffset(fx * _contentW - inView.X);
        _host.ScrollToVerticalOffset(fy * _contentH - inView.Y);
        InvalidateVisual();
        Schedule();
        Changed?.Invoke();
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        ZoomBy(e.Delta > 0 ? 1.15 : 1 / 1.15, e.GetPosition(_host));
        e.Handled = true;
    }

    (int Page, Point Pts)? HitPage(Point p)
    {
        foreach (int slot in SlotsIn(ViewportRect))
        {
            var r = PageRect(slot);
            if (!r.Contains(p)) continue;
            double k = Zoom * PointsToDip;
            return (_pages[slot], new Point((p.X - r.X) / k, (p.Y - r.Y) / k));
        }
        return null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        ClearSelection();
        if (e.ClickCount == 1 && HitPage(e.GetPosition(this)) is { } hit)
        {
            int c = _doc.CharAt(hit.Page, hit.Pts);
            if (c >= 0)
            {
                _selPage = hit.Page;
                _selAnchor = _selEnd = c;
                CaptureMouse();
            }
        }
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsMouseCaptured || _selPage < 0) return;
        if (HitPage(e.GetPosition(this)) is { } hit && hit.Page == _selPage)
        {
            int c = _doc.CharAt(_selPage, hit.Pts);
            if (c >= 0 && c != _selEnd)
            {
                _selEnd = c;
                var (start, count) = SelectionRange();
                _selRects = _doc.TextRects(_selPage, start, count);
                InvalidateVisual();
            }
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) => ReleaseMouseCapture();

    (int Start, int Count) SelectionRange() => (Math.Min(_selAnchor, _selEnd), Math.Abs(_selEnd - _selAnchor) + 1);

    public string SelectedText()
    {
        if (_selPage < 0 || _selRects.Count == 0) return "";
        var (start, count) = SelectionRange();
        return _doc.Text(_selPage, start, count);
    }

    void ClearSelection()
    {
        _selPage = -1;
        _selRects = [];
    }

    public void SetHits(Dictionary<int, List<Rect>> hits)
    {
        _hits = hits;
        _activeHit = null;
        InvalidateVisual();
    }

    public void ShowHit(int page, Rect pts)
    {
        _activeHit = (page, pts);
        ScrollToPage(page);
        int slot = Array.IndexOf(_pages, page);
        if (slot >= 0)
        {
            double k = Zoom * PointsToDip;
            _host.ScrollToVerticalOffset(_tops[slot] + pts.Y * k - _host.ViewportHeight / 3);
        }
        InvalidateVisual();
    }

    public void Cancel()
    {
        _timer.Stop();
        _cts?.Cancel();
        _tiles.Clear();
    }
}
