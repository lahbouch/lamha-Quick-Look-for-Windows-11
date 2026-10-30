using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lamha.Core;
using Lamha.Ui;

namespace Lamha.Previews;

public sealed class MediaProvider : IPreviewProvider
{
    public bool Handles(FileKind kind) => kind is FileKind.Video or FileKind.Audio;

    public Task<IPreview> OpenAsync(PreviewRequest r, CancellationToken ct) =>
        Task.FromResult<IPreview>(new MediaPreview(r.Path, FileTypes.Detect(r.Path) == FileKind.Video));
}

sealed class MediaPreview : IPreview
{
    readonly MediaElement _player;
    readonly Slider _timeline;
    readonly TextBlock _time;
    readonly Button _play;
    readonly Slider _volume;
    readonly DispatcherTimer _timer;
    readonly Grid _root;
    readonly bool _video;
    bool _playing, _dragging;

    public FrameworkElement View => _root;
    public IReadOnlyList<UIElement> Tools { get; }
    public string Info => _video ? "فيديو" : "صوت";
    public Size? PreferredSize => _video ? new Size(960, 600) : new Size(520, 260);

    public MediaPreview(string path, bool video)
    {
        _video = video;
        _player = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Source = new Uri(path), Volume = 0.8 };
        _player.MediaOpened += (_, _) =>
        {
            if (_player.NaturalDuration.HasTimeSpan) _timeline!.Maximum = _player.NaturalDuration.TimeSpan.TotalSeconds;
            Tick();
        };
        _player.MediaEnded += (_, _) => { _player.Stop(); SetPlaying(false); };
        _player.MediaFailed += (_, e) => { Log.Error("media failed", e.ErrorException); _time!.Text = "تعذر تشغيل هذا الملف"; };

        _play = Tool.Button("", "تشغيل أو إيقاف مؤقت (P)", Toggle);
        _timeline = new Slider { Minimum = 0, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), Style = (Style)Application.Current.FindResource("AccentSlider") };
        _timeline.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => _dragging = true));
        _timeline.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => { _dragging = false; Seek(); }));
        _timeline.PreviewMouseLeftButtonUp += (_, _) => { if (!_dragging) Seek(); };
        _time = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), Foreground = (Brush)Application.Current.FindResource("TextSecondary"), FontSize = 12 };
        var volume = _volume = new Slider { Width = 90, Minimum = 0, Maximum = 1, Value = 0.8, VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.FindResource("AccentSlider"), ToolTip = "مستوى الصوت (السهمان لأعلى ولأسفل)" };
        volume.ValueChanged += (_, e) => _player.Volume = e.NewValue;

        var bar = new Grid { Height = 44, Background = (Brush)Application.Current.FindResource("Surface"), FlowDirection = FlowDirection.LeftToRight };
        foreach (var w in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto })
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        void Add(UIElement e, int col) { Grid.SetColumn(e, col); bar.Children.Add(e); }
        Add(_play, 0);
        Add(_timeline, 1);
        Add(_time, 2);
        Add(new TextBlock { Text = "", FontFamily = (FontFamily)Application.Current.FindResource("Icons"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0), Foreground = (Brush)Application.Current.FindResource("TextSecondary") }, 3);
        Add(volume, 4);

        _root = new Grid { FlowDirection = FlowDirection.LeftToRight, Background = video ? Brushes.Black : (Brush)Application.Current.FindResource("Surface") };
        _root.RowDefinitions.Add(new RowDefinition());
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (!video)
            _root.Children.Add(new TextBlock { Text = "", FontSize = 72, FontFamily = (FontFamily)Application.Current.FindResource("Icons"), Foreground = (Brush)Application.Current.FindResource("AccentTan"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        _root.Children.Add(_player);
        Grid.SetRow(bar, 1);
        _root.Children.Add(bar);

        Tools = video
            ? [Tool.Button("", "ملء الشاشة (.)", () => (Window.GetWindow(_root) as PreviewWindow)?.ToggleFullscreen())]
            : [];

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        _root.Loaded += (_, _) => { _player.Play(); SetPlaying(true); };
    }

    public bool OnKey(Key key, bool shift)
    {
        switch (key)
        {
            case Key.P: Toggle(); break;
            case Key.J: Skip(-5); break;
            case Key.L: Skip(+5); break;
            case Key.Up: _volume.Value += 0.1; break;
            case Key.Down: _volume.Value -= 0.1; break;
            case Key.M: _player.IsMuted = !_player.IsMuted; break;
            default: return false;
        }
        return true;
    }

    void Skip(int seconds)
    {
        _player.Position += TimeSpan.FromSeconds(seconds);
        Tick();
    }

    void Toggle()
    {
        if (_playing) _player.Pause(); else _player.Play();
        SetPlaying(!_playing);
    }

    void SetPlaying(bool playing)
    {
        _playing = playing;
        if (_play.Content is TextBlock glyph) glyph.Text = playing ? "" : "";
    }

    void Seek() => _player.Position = TimeSpan.FromSeconds(_timeline.Value);

    void Tick()
    {
        if (!_dragging) _timeline.Value = _player.Position.TotalSeconds;
        var total = _player.NaturalDuration.HasTimeSpan ? _player.NaturalDuration.TimeSpan : TimeSpan.Zero;
        _time.Text = $"{_player.Position:mm\\:ss} / {total:mm\\:ss}";
    }

    public void Dispose()
    {
        _timer.Stop();
        _player.Stop();
        _player.Close();
    }
}
