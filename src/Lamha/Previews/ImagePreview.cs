using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Linq;
using Lamha.Core;
using Lamha.Ui;
using SkiaSharp;
using Svg.Skia;

namespace Lamha.Previews;

public sealed class ImageProvider : IPreviewProvider
{
    const int MaxDecodeSide = 8192;
    const long MaxAnimationBytes = 512L * 1024 * 1024;

    public bool Handles(FileKind kind) => kind == FileKind.Image;

    public Task<IPreview> OpenAsync(PreviewRequest r, CancellationToken ct) => Task.Run<IPreview>(() =>
    {
        var ext = Path.GetExtension(r.Path).ToLowerInvariant();
        if (ext == ".svg") return CreateOnUi(OpenSvg(r.Path));
        return CreateOnUi(ext is ".gif" or ".webp" ? DecodeSkia(r.Path) : DecodeWic(r.Path));
    }, ct);

    static IPreview CreateOnUi(Loaded image) => Application.Current.Dispatcher.Invoke(() => new ImagePreview(image));

    internal sealed record Loaded(BitmapSource[] Frames, TimeSpan[] Delays, int PixelWidth, int PixelHeight, Func<double, BitmapSource>? Rasterize = null);

    static FileStream Read(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    static Loaded DecodeWic(string path)
    {
        int w, h;
        ushort orientation = 1;
        using (var s = Read(path))
        {
            var frame = BitmapDecoder.Create(s, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            w = frame.PixelWidth;
            h = frame.PixelHeight;
            try { if (frame.Metadata is BitmapMetadata m && m.GetQuery("/app1/ifd/{ushort=274}") is ushort o) orientation = o; }
            catch (Exception e) when (e is NotSupportedException or ArgumentException or InvalidOperationException) { }
        }

        var image = new BitmapImage();
        using (var s = Read(path))
        {
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = s;
            if (Math.Max(w, h) > MaxDecodeSide)
            {
                if (w >= h) image.DecodePixelWidth = MaxDecodeSide;
                else image.DecodePixelHeight = MaxDecodeSide;
            }
            image.EndInit();
        }
        image.Freeze();

        BitmapSource result = Orient(image, orientation);
        bool swapped = orientation is >= 5 and <= 8;
        return new Loaded([result], [TimeSpan.Zero], swapped ? h : w, swapped ? w : h);
    }

    static BitmapSource Orient(BitmapSource src, ushort orientation)
    {
        if (orientation <= 1 || orientation > 8) return src;
        Transform t = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            6 => new RotateTransform(90),
            8 => new RotateTransform(270),
            5 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(270) } },
            _ => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(90) } },
        };
        var result = new TransformedBitmap(src, t);
        result.Freeze();
        return result;
    }

    static Loaded DecodeSkia(string path)
    {
        using var stream = Read(path);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException();
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var meta = codec.FrameInfo;
        int count = Math.Max(1, codec.FrameCount);
        if (info.BytesSize64 * count > MaxAnimationBytes) count = 1;

        var frames = new BitmapSource[count];
        var delays = new TimeSpan[count];
        var previous = new SKBitmap[count];
        for (int i = 0; i < count; i++)
        {
            var bmp = new SKBitmap(info);
            int required = i < meta.Length ? meta[i].RequiredFrame : -1;
            if (required >= 0) previous[required].CopyTo(bmp);
            var result = codec.GetPixels(info, bmp.GetPixels(), new SKCodecOptions(i, required));
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) { if (i == 0) throw new InvalidDataException(); count = i; break; }
            previous[i] = bmp;

            var frame = BitmapSource.Create(info.Width, info.Height, 96, 96, PixelFormats.Pbgra32, null, bmp.GetPixels(), info.BytesSize, info.RowBytes);
            frame.Freeze();
            frames[i] = frame;
            delays[i] = TimeSpan.FromMilliseconds(i < meta.Length ? Math.Max(meta[i].Duration, 20) : 100);
        }
        foreach (var b in previous) b?.Dispose();
        return new Loaded(frames[..count], delays[..count], info.Width, info.Height);
    }

    static Loaded OpenSvg(string path)
    {
        var clean = Sanitize(path);
        var svg = new SKSvg();
        using (var ms = new MemoryStream(clean)) svg.Load(ms);
        var bounds = svg.Picture?.CullRect ?? throw new InvalidDataException();
        if (bounds.Width <= 0 || bounds.Height <= 0) throw new InvalidDataException();
        int w = (int)Math.Ceiling(bounds.Width), h = (int)Math.Ceiling(bounds.Height);

        BitmapSource Rasterize(double scale)
        {
            scale = Math.Min(scale, MaxDecodeSide / (double)Math.Max(w, h));
            var info = new SKImageInfo((int)Math.Ceiling(w * scale), (int)Math.Ceiling(h * scale), SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.Scale((float)scale);
            surface.Canvas.Translate(-bounds.Left, -bounds.Top);
            surface.Canvas.DrawPicture(svg.Picture);
            using var pix = surface.PeekPixels();
            var result = BitmapSource.Create(info.Width, info.Height, 96, 96, PixelFormats.Pbgra32, null, pix.GetPixels(), info.BytesSize, info.RowBytes);
            result.Freeze();
            return result;
        }

        return new Loaded([Rasterize(1)], [TimeSpan.Zero], w, h, Rasterize);
    }

    /// <summary>
    /// SVG is an untrusted format that can reference the network and local files. Parsing without a DTD stops entity
    /// expansion, and dropping every non-fragment, non-data link keeps the preview fully offline.
    /// </summary>
    internal static byte[] Sanitize(string path)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var stream = Read(path);
        using var reader = XmlReader.Create(stream, settings);
        var doc = XDocument.Load(reader);

        foreach (var e in doc.Descendants().Where(e => e.Name.LocalName is "script" or "foreignObject").ToList()) e.Remove();
        foreach (var a in doc.Descendants().Attributes().Where(a => a.Name.LocalName == "href").ToList())
            if (!a.Value.StartsWith('#') && !a.Value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) a.Remove();
        foreach (var a in doc.Descendants().Attributes().Where(a => a.Value.Contains("url(", StringComparison.OrdinalIgnoreCase) && !a.Value.Contains("url(#", StringComparison.OrdinalIgnoreCase)).ToList())
            a.Remove();
        foreach (var e in doc.Descendants().Where(e => e.Name.LocalName == "style").ToList())
            if (e.Value.Contains("@import", StringComparison.OrdinalIgnoreCase) || e.Value.Contains("url(", StringComparison.OrdinalIgnoreCase)) e.Remove();

        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }
}

sealed class ImagePreview : IPreview
{
    const double MinZoom = 0.02, MaxZoom = 32;

    readonly ImageProvider.Loaded _image;
    readonly ScrollViewer _scroll;
    readonly Image _view = new();
    readonly ScaleTransform _scale = new();
    readonly RotateTransform _rotate = new();
    readonly Grid _stage = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly DispatcherTimer? _animation;
    readonly DispatcherTimer _rasterTimer;
    int _frame;
    double _zoom = 1;
    bool _fit = true;

    public FrameworkElement View => _scroll;
    public IReadOnlyList<UIElement> Tools { get; }
    public string Info => $"{_image.PixelWidth} × {_image.PixelHeight}";
    public string Status => $"{_zoom * 100:0}%";
    public event Action? StatusChanged;
    public Size? PreferredSize => new Size(_image.PixelWidth, _image.PixelHeight);

    public ImagePreview(ImageProvider.Loaded image)
    {
        _image = image;
        _view.Source = image.Frames[0];
        _view.Width = image.PixelWidth;
        _view.Height = image.PixelHeight;
        RenderOptions.SetBitmapScalingMode(_view, BitmapScalingMode.HighQuality);

        _stage.Background = (Brush)Application.Current.FindResource("Checker");
        _stage.LayoutTransform = new TransformGroup { Children = { _scale, _rotate } };
        _stage.Children.Add(_view);

        _scroll = new ScrollViewer
        {
            FlowDirection = FlowDirection.LeftToRight,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _stage,
            Focusable = true,
        };
        _scroll.SizeChanged += (_, _) => { if (_fit) ApplyFit(); };
        _scroll.PreviewMouseWheel += OnWheel;
        _scroll.Loaded += (_, _) => _scroll.Focus();

        _rasterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _rasterTimer.Tick += (_, _) => { _rasterTimer.Stop(); Rerender(); };

        if (image.Frames.Length > 1)
        {
            _animation = new DispatcherTimer { Interval = image.Delays[0] };
            _animation.Tick += (_, _) =>
            {
                _frame = (_frame + 1) % image.Frames.Length;
                _view.Source = image.Frames[_frame];
                _animation.Interval = image.Delays[_frame];
            };
            _animation.Start();
        }

        Tools =
        [
            Tool.Button("", "تصغير (-)", () => SetZoom(_zoom / 1.25)),
            Tool.Button("", "تكبير (+)", () => SetZoom(_zoom * 1.25)),
            Tool.Button("", "ملاءمة النافذة (0)", () => { _fit = true; ApplyFit(); }),
            Tool.Button("", "الحجم الفعلي (1)", () => SetZoom(1)),
            Tool.Button("", "تدوير (R)", () => Rotate(90)),
        ];
    }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        SetZoom(_zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15));
        e.Handled = true;
    }

    public bool OnKey(Key key, bool shift)
    {
        switch (key)
        {
            case Key.Add or Key.OemPlus: SetZoom(_zoom * 1.25); break;
            case Key.Subtract or Key.OemMinus: SetZoom(_zoom / 1.25); break;
            case Key.D0 or Key.NumPad0: _fit = true; ApplyFit(); break;
            case Key.D1 or Key.NumPad1: SetZoom(1); break;
            case Key.R: Rotate(shift ? 270 : 90); break;
            default: return false;
        }
        return true;
    }

    void Rotate(int degrees)
    {
        _rotate.Angle = (_rotate.Angle + degrees) % 360;
        if (_fit) ApplyFit();
    }

    void ApplyFit()
    {
        bool sideways = _rotate.Angle % 180 != 0;
        double w = sideways ? _image.PixelHeight : _image.PixelWidth, h = sideways ? _image.PixelWidth : _image.PixelHeight;
        if (_scroll.ActualWidth <= 0) return;
        Scale(Math.Min(1, Math.Min((_scroll.ActualWidth - 4) / w, (_scroll.ActualHeight - 4) / h)));
    }

    void SetZoom(double zoom)
    {
        _fit = false;
        Scale(zoom);
    }

    void Scale(double zoom)
    {
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        _scale.ScaleX = _scale.ScaleY = _zoom;
        // Pixel art and screenshots should show their pixels when magnified, not a blur.
        RenderOptions.SetBitmapScalingMode(_view, _zoom >= 3 && _image.Rasterize is null ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        if (_image.Rasterize is not null) { _rasterTimer.Stop(); _rasterTimer.Start(); }
        StatusChanged?.Invoke();
    }

    // Vector images are re-rasterized for the current zoom, so they never pixelate.
    void Rerender()
    {
        double dpi = VisualTreeHelper.GetDpi(_view).DpiScaleX;
        if (_image.Rasterize is { } r) _view.Source = r(_zoom * dpi);
    }

    public void Dispose()
    {
        _animation?.Stop();
        _rasterTimer.Stop();
    }
}
