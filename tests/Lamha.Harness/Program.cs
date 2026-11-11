using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lamha.Core;
using Lamha.Explorer;
using Lamha.Office;
using Lamha.Previews;
using Lamha.Ui;

namespace Lamha.Harness;

/// <summary>
/// Drives the real preview window without a desktop, so rendering can be checked on a locked or headless session.
///   snapshot OUT_DIR "file|Key|Key" ...   renders each file, presses the keys, saves a PNG of the real window
///   selection HWND                        prints what ExplorerSelection reads from an Explorer window
///   probe HWND                            prints the focus classes ExplorerProbe sees in an Explorer window
/// </summary>
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args[0] == "selection")
        {
            foreach (var p in ExplorerSelection.GetAsync((nint)long.Parse(args[1])).GetAwaiter().GetResult()) Console.WriteLine(p);
            return 0;
        }
        if (args[0] == "probe")
        {
            Console.WriteLine(ExplorerProbe.Describe((nint)long.Parse(args[1])));
            return 0;
        }

        if (args[0] == "hook") return HookTest();
        if (args[0] == "text")
        {
            using var doc = Lamha.Pdf.PdfDoc.Open(args[1]);
            var s = doc.Text(0, 0, 400);
            Console.WriteLine(string.Join(' ', s.Take(60).Select(c => ((int)c).ToString("X4"))));
            Console.WriteLine("folded: " + string.Join(' ', SearchFold.Fold(s).Text.Take(60).Select(c => ((int)c).ToString("X4"))));
            int hits = 0;
            doc.Search(args[2], _ => hits++, default);
            Console.WriteLine($"hits={hits}");
            return 0;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Lamha;component/Ui/Theme.xaml") });
        app.Startup += async (_, _) =>
        {
            try { await Snapshot(args[1], args[2..]); }
            catch (Exception e) { Console.WriteLine(e); }
            app.Shutdown();
        };
        return app.Run();
    }

    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public nint extra; public long pad; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public KEYBDINPUT ki; }
    [DllImport("user32")] static extern uint SendInput(uint count, INPUT[] inputs, int size);

    /// <summary>
    /// Injects a real Space through the OS and checks that the installed hook sees it and that nothing after the hook
    /// does. The observer is installed first, so Windows calls it after KeyboardHook and only if the key survived.
    /// </summary>
    static int HookTest()
    {
        int survived = 0, pressed = 0;
        Native.HookProc observer = (code, w, l) =>
        {
            if (code >= 0 && Marshal.ReadInt32(l) == Native.VK_SPACE) Interlocked.Increment(ref survived);
            return Native.CallNextHookEx(0, code, w, l);
        };
        uint observerThread = 0;
        var t = new Thread(() =>
        {
            observerThread = Native.GetCurrentThreadId();
            nint h = Native.SetWindowsHookExW(Native.WH_KEYBOARD_LL, observer, Native.GetModuleHandleW(null), 0);
            while (Native.GetMessageW(out _, 0, 0, 0) > 0) { }
            Native.UnhookWindowsHookEx(h);
        }) { IsBackground = true };
        t.Start();
        Thread.Sleep(300);

        using (var hook = new KeyboardHook { CaptureTarget = () => 1234 })
        {
            nint target = 0;
            hook.SpacePressed += h => { target = h; Interlocked.Increment(ref pressed); };
            Thread.Sleep(300);
            var input = new[]
            {
                new INPUT { type = 1, ki = new KEYBDINPUT { vk = Native.VK_SPACE } },
                new INPUT { type = 1, ki = new KEYBDINPUT { vk = Native.VK_SPACE, flags = 2 } },
            };
            var sent = SendInput(2, input, Marshal.SizeOf<INPUT>());
            Thread.Sleep(400);
            Console.WriteLine($"sent={sent} fired={pressed} target={target} survivedPastHook={survived}");
            GC.KeepAlive(observer);
            Native.PostThreadMessageW(observerThread, Native.WM_QUIT, 0, 0);
            return sent == 2 && pressed == 1 && target == 1234 && survived == 0 ? 0 : 1;
        }
    }

    static async Task Snapshot(string outDir, string[] specs)
    {
        Directory.CreateDirectory(outDir);
        var settings = new AppSettings();
        using var converter = new OfficeConverter();
        var registry = new PreviewRegistry(new PdfProvider(), new OfficeProvider(converter), new ImageProvider(), new TextProvider(), new MediaProvider());
        var messages = new List<string>();
        var window = new PreviewWindow(registry, settings, messages.Add);

        int n = 0;
        foreach (var spec in specs)
        {
            var parts = spec.Split('|');
            var files = parts[0].Split('>');
            var watch = System.Diagnostics.Stopwatch.StartNew();
            window.ShowSession(new PreviewSession(files), 0);
            await Ready(window);
            long ready = watch.ElapsedMilliseconds;
            foreach (var key in parts.Skip(1))
            {
                if (Enum.TryParse<Key>(key, out var k)) Press(window, k);
                else if (key.StartsWith("find:")) Find(window, key[5..]);
                else if (key.StartsWith("sheet:")) Sheet(window, int.Parse(key[6..]));
                else if (key.StartsWith("tool:")) Click(window, int.Parse(key[5..]));
                else if (key.StartsWith("wait:")) await Task.Delay(int.Parse(key[5..]));
                else if (key.StartsWith("menu:")) ClickMenu(int.Parse(key[5..]));
                await Task.Delay(120);
                await Ready(window);
            }
            await Task.Delay(700); // tiles and thumbnails arrive asynchronously
            var name = $"{n++:00}_{Path.GetExtension(files[0]).TrimStart('.')}.png";
            Save(window, Path.Combine(outDir, name));
            Console.WriteLine($"{name}\t{Path.GetFileName(files[0])}\tready={ready}ms\tsize={window.ActualWidth:0}x{window.ActualHeight:0}\tname=[{window.FileName.Text}]\tmeta=[{window.Meta.Text}]\tstatus=[{window.Status.Text}]\tpos=[{window.Position.Text}]\terror=[{(window.ErrorPanel.IsVisible ? window.ErrorText.Text : "")}]\tpw={window.PasswordPanel.IsVisible}\tvisible={window.IsVisible}\ttools={window.ToolsHost.Children.Count}\tmem={Environment.WorkingSet / 1048576}MB");
            window.HidePreview();
            await Task.Delay(50);
        }
        await Task.Delay(2500); // the window hands memory back shortly after closing
        Console.WriteLine($"mem after close={Environment.WorkingSet / 1048576}MB");
        foreach (var m in messages) Console.WriteLine("notify: " + m);
        window.Destroy();

        var controller = new PreviewController(settings, converter);
        SettingsWindow.ShowSingle(controller);
        await Task.Delay(400);
        foreach (Window w in Application.Current.Windows)
            if (w is SettingsWindow) { Save(w, Path.Combine(outDir, "settings.png")); w.Close(); break; }
        controller.Notify("لا يمكن معاينة هذا النوع من الملفات.");
        await Task.Delay(300);
        foreach (Window w in Application.Current.Windows)
            if (w is Toast) Save(w, Path.Combine(outDir, "toast.png"));
        controller.Dispose();
    }

    static async Task Ready(PreviewWindow w)
    {
        for (int i = 0; i < 800; i++)
        {
            if (!w.IsVisible || w.Host.Content is not null || w.ErrorPanel.IsVisible || w.PasswordPanel.IsVisible) return;
            await Task.Delay(50);
        }
        Console.WriteLine("TIMEOUT waiting for preview");
    }

    static void Press(PreviewWindow w, Key key)
    {
        var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(w), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        w.RaiseEvent(e);
    }

    static void Find(PreviewWindow w, string text)
    {
        var box = w.ToolsHost.Children.OfType<System.Windows.Controls.TextBox>().First();
        box.Text = text;
        box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(w), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
    }

    static void Sheet(PreviewWindow w, int index)
    {
        var tabs = Find<System.Windows.Controls.ListBox>((DependencyObject)w.Host.Content, b => b.Height == 32);
        tabs!.SelectedIndex = index;
    }

    static void Click(PreviewWindow w, int index)
    {
        var b = (System.Windows.Controls.Button)w.ToolsHost.Children.OfType<UIElement>().ElementAt(index);
        b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    }

    // Context menus live in their own popup window, so search every presentation source for the open one.
    static void ClickMenu(int index)
    {
        foreach (PresentationSource source in PresentationSource.CurrentSources)
            if (source.RootVisual is { } root && Find<System.Windows.Controls.ContextMenu>(root, m => m.IsOpen) is { } menu)
            {
                var item = (System.Windows.Controls.MenuItem)menu.Items[index]!;
                Console.WriteLine($"menu click: {item.Header}");
                item.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
                menu.IsOpen = false;
                return;
            }
        Console.WriteLine("menu click: NO OPEN MENU");
    }

    static T? Find<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t && match(t)) return t;
            if (Find(c, match) is { } found) return found;
        }
        return null;
    }

    static void Save(Window w, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(w);
        var bmp = new RenderTargetBitmap((int)(w.ActualWidth * dpi.DpiScaleX), (int)(w.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(w);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(path);
        png.Save(f);
    }
}
