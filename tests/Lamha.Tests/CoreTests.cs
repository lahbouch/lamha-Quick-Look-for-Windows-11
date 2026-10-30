using System.Text;
using System.Windows;
using Lamha.Core;
using Lamha.Explorer;
using Lamha.Office;
using Lamha.Pdf;
using Lamha.Previews;
using Lamha.Ui;

namespace Lamha.Tests;

public class FileTypeTests
{
    [Theory]
    [InlineData("a.PNG", FileKind.Image)]
    [InlineData("a.jpeg", FileKind.Image)]
    [InlineData("a.webp", FileKind.Image)]
    [InlineData("a.gif", FileKind.Image)]
    [InlineData("a.bmp", FileKind.Image)]
    [InlineData("a.TIFF", FileKind.Image)]
    [InlineData("a.svg", FileKind.Image)]
    [InlineData("a.pdf", FileKind.Pdf)]
    [InlineData("a.doc", FileKind.Word)]
    [InlineData("a.DOCX", FileKind.Word)]
    [InlineData("a.xls", FileKind.Excel)]
    [InlineData("a.xlsx", FileKind.Excel)]
    [InlineData("a.ppt", FileKind.PowerPoint)]
    [InlineData("a.pptx", FileKind.PowerPoint)]
    [InlineData("a.txt", FileKind.Text)]
    [InlineData("a.md", FileKind.Text)]
    [InlineData("a.json", FileKind.Text)]
    [InlineData("a.xml", FileKind.Text)]
    [InlineData("a.html", FileKind.Text)]
    [InlineData("a.csv", FileKind.Csv)]
    [InlineData("a.mp4", FileKind.Video)]
    [InlineData("a.mp3", FileKind.Audio)]
    [InlineData("a.zip", FileKind.Unsupported)]
    [InlineData("a.exe", FileKind.Unsupported)]
    [InlineData("noextension", FileKind.Unsupported)]
    [InlineData(@"C:\مجلد\تقرير سنوي.final.pdf", FileKind.Pdf)]
    public void Detects_by_extension(string path, FileKind kind) => Assert.Equal(kind, FileTypes.Detect(path));
}

public class ProviderSelectionTests
{
    sealed class Fake(params FileKind[] kinds) : IPreviewProvider
    {
        public bool Handles(FileKind kind) => kinds.Contains(kind);
        public Task<IPreview> OpenAsync(PreviewRequest r, CancellationToken ct) => throw new NotImplementedException();
    }

    [Fact]
    public void Picks_the_provider_for_the_file_kind()
    {
        var pdf = new Fake(FileKind.Pdf);
        var office = new Fake(FileKind.Word, FileKind.Excel);
        var registry = new PreviewRegistry(pdf, office);
        Assert.Same(pdf, registry.Find("x.pdf"));
        Assert.Same(office, registry.Find("x.XLSX"));
        Assert.Null(registry.Find("x.png"));
        Assert.Null(registry.Find("x.zip"));
    }

    [Theory]
    [InlineData(FileKind.Image)] [InlineData(FileKind.Pdf)] [InlineData(FileKind.Word)] [InlineData(FileKind.Excel)]
    [InlineData(FileKind.PowerPoint)] [InlineData(FileKind.Text)] [InlineData(FileKind.Csv)] [InlineData(FileKind.Video)] [InlineData(FileKind.Audio)]
    public void Every_supported_kind_has_a_real_provider(FileKind kind)
    {
        IPreviewProvider[] all = [new PdfProvider(), new OfficeProvider(null!), new ImageProvider(), new TextProvider(), new MediaProvider()];
        Assert.Single(all, p => p.Handles(kind));
    }
}

public class SessionTests
{
    static PreviewSession Make(params string[] files) => new(files, _ => true);

    [Fact]
    public void Skips_unsupported_and_keeps_order()
    {
        var s = Make("b.pdf", "x.zip", "a.png", "folder");
        Assert.Equal(2, s.Count);
        Assert.Equal("b.pdf", s.Current);
        s.MoveNext();
        Assert.Equal("a.png", s.Current);
    }

    [Fact]
    public void Stops_at_both_ends()
    {
        var s = Make("a.pdf", "b.pdf");
        Assert.False(s.MovePrevious());
        Assert.True(s.MoveNext());
        Assert.False(s.MoveNext());
        Assert.Equal("b.pdf", s.Current);
        Assert.Equal("2 / 2", s.Position);
    }

    [Fact]
    public void Empty_selection_has_no_current_file()
    {
        var s = Make("a.zip");
        Assert.Equal(0, s.Count);
        Assert.Null(s.Current);
    }

    [Fact]
    public void Duplicates_collapse_regardless_of_case()
    {
        Assert.Equal(1, Make(@"C:\A.pdf", @"c:\a.PDF").Count);
    }

    [Fact]
    public void Files_deleted_since_selection_are_skipped_forward()
    {
        var gone = new HashSet<string>();
        var s = new PreviewSession(["a.pdf", "b.pdf", "c.pdf"], p => !gone.Contains(p));
        gone.Add("b.pdf");
        Assert.True(s.MoveNext());
        Assert.Equal("c.pdf", s.Current);
        Assert.Equal("2 / 2", s.Position);
    }

    [Fact]
    public void Files_deleted_since_selection_are_skipped_backward()
    {
        var gone = new HashSet<string>();
        var s = new PreviewSession(["a.pdf", "b.pdf", "c.pdf"], p => !gone.Contains(p));
        s.MoveNext();
        s.MoveNext();
        gone.Add("b.pdf");
        Assert.True(s.MovePrevious());
        Assert.Equal("a.pdf", s.Current);
        Assert.Equal("1 / 2", s.Position);
    }

    [Fact]
    public void Move_with_everything_else_gone_stays_put()
    {
        var gone = new HashSet<string>();
        var s = new PreviewSession(["a.pdf", "b.pdf"], p => !gone.Contains(p));
        gone.Add("b.pdf");
        Assert.False(s.MoveNext());
        Assert.Equal("a.pdf", s.Current);
        Assert.Equal("1 / 1", s.Position);
    }
}

public class TextDecoderTests
{
    static (string, string) Decode(byte[] b) => TextDecoder.Decode(b, b.Length);

    [Fact] public void Utf8_with_bom() => Assert.Equal(("é", "UTF-8"), Decode([0xEF, 0xBB, 0xBF, 0xC3, 0xA9]));
    [Fact] public void Utf8_without_bom() => Assert.Equal(("مرحبا", "UTF-8"), Decode(Encoding.UTF8.GetBytes("مرحبا")));
    [Fact] public void Utf16_le_bom() => Assert.Equal(("أ", "UTF-16"), Decode([0xFF, 0xFE, 0x23, 0x06]));
    [Fact] public void Utf16_be_bom() => Assert.Equal(("أ", "UTF-16"), Decode([0xFE, 0xFF, 0x06, 0x23]));
    [Fact] public void Utf16_without_bom() => Assert.Equal("UTF-16", Decode(Encoding.Unicode.GetBytes("Bonjour tout le monde")).Item2);

    [Fact]
    public void Arabic_ansi_is_windows_1256()
    {
        TextDecoder.Decode([], 0);
        var (text, name) = Decode(Encoding.GetEncoding(1256).GetBytes("مرحبا بالعالم، هذا نص عربي"));
        Assert.Equal("Windows-1256", name);
        Assert.Equal("مرحبا بالعالم، هذا نص عربي", text);
    }

    [Fact]
    public void French_ansi_is_windows_1252()
    {
        TextDecoder.Decode([], 0);
        var (text, name) = Decode(Encoding.GetEncoding(1252).GetBytes("Le garçon a déjà mangé à la cantine de l'école."));
        Assert.Equal("Windows-1252", name);
        Assert.Contains("garçon", text);
    }

    [Fact]
    public void A_character_cut_by_the_read_limit_does_not_break_utf8()
    {
        var bytes = Encoding.UTF8.GetBytes("نص");
        var (text, name) = TextDecoder.Decode(bytes, bytes.Length - 1);
        Assert.Equal("UTF-8", name);
        Assert.Equal("ن", text);
    }

    [Fact] public void Empty_input() => Assert.Equal("", Decode([]).Item1);
    [Fact] public void Rtl_detection_arabic() => Assert.True(TextDecoder.IsMostlyRtl("مرحبا Hello"));
    [Fact] public void Rtl_detection_french() => Assert.False(TextDecoder.IsMostlyRtl("Bonjour tout le monde مرحبا"));
}

public class CsvTests
{
    [Fact]
    public void Semicolon_quotes_and_newlines()
    {
        var rows = CsvParser.Parse("a;b\n\"x;1\";\"line\nbreak\"\n\"q\"\"q\";\n", 100);
        Assert.Equal(3, rows.Count);
        Assert.Equal(["x;1", "line\nbreak"], rows[1]);
        Assert.Equal(["q\"q", ""], rows[2]);
    }

    [Fact] public void Tab_delimiter() => Assert.Equal(["a", "b"], CsvParser.Parse("a\tb\n", 10)[0]);
    [Fact] public void Crlf_and_missing_final_newline() => Assert.Equal(2, CsvParser.Parse("a,b\r\nc,d", 10).Count);
    [Fact] public void Row_limit() => Assert.Equal(2, CsvParser.Parse("a\nb\nc\nd\n", 2).Count);
    [Fact] public void Empty_text() => Assert.Empty(CsvParser.Parse("", 10));
}

public class SearchFoldTests
{
    [Fact]
    public void Presentation_forms_match_base_letters()
    {
        var hit = Assert.Single(SearchFold.Find("\uFE97\uFED8\uFEAE\uFEF3\uFEAE", "تقرير"));
        Assert.Equal((0, 5), hit);
    }

    [Fact]
    public void Lam_alef_ligature_expands_and_maps_back()
    {
        var hit = Assert.Single(SearchFold.Find("ب\uFEFBد", "بلاد"));
        Assert.Equal((0, 3), hit);
    }

    [Fact] public void Arabic_stored_in_visual_order_is_found() => Assert.Single(SearchFold.Find("ريرقت 1", "تقرير"));
    [Fact] public void Latin_is_not_searched_backwards() => Assert.Empty(SearchFold.Find("troppar", "rapport"));
    [Fact] public void Tashkeel_is_ignored_both_ways() => Assert.Single(SearchFold.Find("مَدْرَسَة", "مدرسة"));
    [Fact] public void Kashida_is_ignored() => Assert.Single(SearchFold.Find("مـــدرسة", "مدرسة"));
    [Fact] public void Latin_is_case_insensitive() => Assert.Single(SearchFold.Find("Le Rapport", "rapport"));
    [Fact] public void Finds_every_occurrence() => Assert.Equal(2, SearchFold.Find("abc abc", "abc").Count());
    [Fact] public void Empty_query_finds_nothing() => Assert.Empty(SearchFold.Find("abc", "   ".Trim()));
    [Fact] public void Query_of_only_tashkeel_finds_nothing() => Assert.Empty(SearchFold.Find("abc", "\u064E"));
}

public class SpaceGateTests
{
    const int Letter = 0x41;
    const nint Explorer = 42;

    static bool Down(SpaceGate g, long t, out nint fire, bool modifier = false, nint target = Explorer) =>
        g.OnKey(Native.VK_SPACE, true, false, t, () => modifier, () => target, out fire);

    static bool Up(SpaceGate g, long t) => g.OnKey(Native.VK_SPACE, false, true, t, () => false, () => 0, out _);

    [Fact]
    public void Space_in_the_file_view_is_taken_on_both_edges()
    {
        var g = new SpaceGate();
        Assert.True(Down(g, 5000, out var fire));
        Assert.Equal(Explorer, fire);
        Assert.True(Up(g, 5050));
    }

    [Fact]
    public void Space_elsewhere_is_left_alone()
    {
        var g = new SpaceGate();
        Assert.False(Down(g, 5000, out var fire, target: 0));
        Assert.Equal(0, fire);
        Assert.False(Up(g, 5050));
    }

    [Fact]
    public void Modifier_combinations_are_left_alone()
    {
        var g = new SpaceGate();
        Assert.False(Down(g, 5000, out var fire, modifier: true));
        Assert.Equal(0, fire);
    }

    [Fact]
    public void Auto_repeat_fires_once_and_stays_swallowed()
    {
        var g = new SpaceGate();
        Down(g, 5000, out _);
        Assert.True(Down(g, 5030, out var again));
        Assert.Equal(0, again);
        Assert.True(Up(g, 5060));
        Down(g, 5100, out var second);
        Assert.Equal(Explorer, second);
    }

    [Fact]
    public void Repeat_of_a_passed_key_stays_passed()
    {
        var g = new SpaceGate();
        Down(g, 5000, out _, target: 0);
        Assert.False(Down(g, 5030, out _));
    }

    [Fact]
    public void Type_ahead_keeps_space_for_explorer_then_expires()
    {
        var g = new SpaceGate();
        g.OnKey(Letter, true, false, 5000, () => false, () => 0, out _);
        Assert.False(Down(g, 5500, out _));
        Up(g, 5550);
        Assert.True(Down(g, 6100, out _));
    }

    [Fact]
    public void Other_keys_are_never_swallowed() =>
        Assert.False(new SpaceGate().OnKey(Letter, true, false, 5000, () => false, () => Explorer, out _));
}

public class ExplorerProbeTests
{
    [Theory]
    [InlineData("DirectUIHWND", true)]
    [InlineData("SysListView32", true)]
    [InlineData("Edit", false)]                     // rename box
    [InlineData("SysTreeView32", false)]            // navigation pane
    [InlineData("ToolbarWindow32", false)]
    [InlineData("Microsoft.UI.Content.DesktopChildSiteBridge", false)] // address and search boxes
    public void Only_the_file_list_counts(string focus, bool expected) =>
        Assert.Equal(expected, ExplorerProbe.IsFileView(focus, ["SHELLDLL_DefView", "CtrlNotifySink"]));

    [Fact]
    public void A_direct_ui_window_outside_the_shell_view_does_not_count() =>
        Assert.False(ExplorerProbe.IsFileView("DirectUIHWND", ["Chrome_WidgetWin_1"]));

    [Theory]
    [InlineData("CabinetWClass", true)]
    [InlineData("Progman", true)]
    [InlineData("WorkerW", true)]
    [InlineData("Chrome_WidgetWin_1", false)]
    [InlineData("Notepad", false)]
    [InlineData("#32770", false)]
    public void Only_shell_windows_are_roots(string cls, bool expected) => Assert.Equal(expected, ExplorerProbe.IsShellRoot(cls));
}

public class SettingsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("lamha-test").FullName;
    string File1 => Path.Combine(_dir, "settings.json");
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Round_trips()
    {
        new AppSettings { AutoPreview = true, Sizing = WindowSizing.Large, Zoom = DefaultZoom.FitPage, LastWidth = 900 }.Save(File1);
        var s = AppSettings.Load(File1);
        Assert.True(s.AutoPreview);
        Assert.Equal(WindowSizing.Large, s.Sizing);
        Assert.Equal(DefaultZoom.FitPage, s.Zoom);
        Assert.Equal(900, s.LastWidth);
    }

    [Fact] public void Missing_file_gives_defaults() => Assert.True(AppSettings.Load(File1).ExplorerIntegration);

    [Fact]
    public void Corrupt_file_gives_defaults()
    {
        File.WriteAllText(File1, "{ not json");
        Assert.True(AppSettings.Load(File1).ExplorerIntegration);
    }

    [Fact]
    public void Unknown_values_do_not_crash()
    {
        File.WriteAllText(File1, """{"Sizing":"Nonsense"}""");
        Assert.Equal(WindowSizing.Adaptive, AppSettings.Load(File1).Sizing);
    }
}

public class ErrorMessageTests
{
    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_failure_has_an_arabic_message(Exception e)
    {
        var m = PreviewErrors.Message(e);
        Assert.NotEmpty(m);
        Assert.Contains(m, c => c is >= '\u0600' and <= '\u06FF');
    }

    public static TheoryData<Exception> Cases() =>
    [
        new FileNotFoundException(), new DirectoryNotFoundException(), new UnauthorizedAccessException(), new IOException(),
        new PdfCorruptException(), new OfficeConversionException(), new OfficeEngineMissingException(), new InvalidOperationException(),
    ];
}

public class OfficeConverterTests
{
    [Fact]
    public void Excel_exports_one_page_per_sheet() => Assert.Contains("SinglePageSheets", OfficeConverter.ExportFilter(FileKind.Excel));

    [Fact]
    public void Cache_key_changes_with_the_file_and_ignores_path_case()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var key = OfficeConverter.CacheKey(@"C:\a.docx", 10, t);
        Assert.Equal(key, OfficeConverter.CacheKey(@"c:\A.DOCX", 10, t));
        Assert.NotEqual(key, OfficeConverter.CacheKey(@"C:\a.docx", 11, t));
        Assert.NotEqual(key, OfficeConverter.CacheKey(@"C:\a.docx", 10, t.AddSeconds(1)));
        Assert.EndsWith(".pdf", key);
    }

    [Fact]
    public void A_fake_docx_is_rejected_without_reaching_the_converter()
    {
        var f = Path.Combine(Directory.CreateTempSubdirectory("lamha-test").FullName, "x.docx");
        File.WriteAllText(f, "not a zip");
        Assert.Throws<OfficeConversionException>(() => OfficeProvider.RejectFakeZip(f));
    }
}

public class PdfUserUnitTests
{
    const string TwoPages = "1 0 obj\n<</Type/Pages/Count 2>>\nendobj\n" +
        "2 0 obj\n<</Type/Page/Parent 1 0 R/MediaBox[0 0 80 10000]\n/UserUnit 3/Tabs/S\n>>\nendobj\n" +
        "3 0 obj\n<</Type/Page/Parent 1 0 R/MediaBox[0 0 48 15]/Tabs/S\n>>\nendobj\n";

    [Fact] public void Each_page_keeps_its_own_multiplier() => Assert.Equal([3.0, 1.0], PdfUserUnits.Parse(TwoPages, 2));
    [Fact] public void Decimal_values_ignore_the_system_locale() => Assert.Equal([2.5], PdfUserUnits.Parse("<</Type/Page/UserUnit 2.5>>endobj", 1));
    [Fact] public void An_unexpected_structure_changes_nothing() => Assert.Equal([1.0, 1.0, 1.0], PdfUserUnits.Parse(TwoPages, 3));
    [Fact] public void No_page_objects_changes_nothing() => Assert.Equal([1.0], PdfUserUnits.Parse("garbage", 1));
}

public class SvgSanitizerTests : IDisposable
{
    readonly string _file = Path.Combine(Directory.CreateTempSubdirectory("lamha-test").FullName, "x.svg");
    public void Dispose() => File.Delete(_file);

    string Clean(string svg)
    {
        File.WriteAllText(_file, svg);
        return Encoding.UTF8.GetString(ImageProvider.Sanitize(_file));
    }

    const string Head = """<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink">""";

    [Fact] public void External_image_links_are_removed() => Assert.DoesNotContain("evil.test", Clean(Head + """<image xlink:href="http://evil.test/a.png"/></svg>"""));
    [Fact] public void File_links_are_removed() => Assert.DoesNotContain("win.ini", Clean(Head + """<image href="file:///c:/windows/win.ini"/></svg>"""));
    [Fact] public void Scripts_are_removed() => Assert.DoesNotContain("alert", Clean(Head + "<script>alert(1)</script></svg>"));
    [Fact] public void External_css_is_removed() => Assert.DoesNotContain("evil.test", Clean(Head + """<style>@import url(http://evil.test/a.css);</style></svg>"""));
    [Fact] public void Fragment_references_survive() => Assert.Contains("#shape", Clean(Head + """<use href="#shape"/></svg>"""));
    [Fact] public void Local_gradients_survive() => Assert.Contains("url(#g)", Clean(Head + """<rect fill="url(#g)"/></svg>"""));

    [Fact]
    public void Dtd_entities_are_refused() =>
        Assert.ThrowsAny<System.Xml.XmlException>(() => Clean("""<!DOCTYPE svg [<!ENTITY x SYSTEM "file:///c:/windows/win.ini">]><svg xmlns="http://www.w3.org/2000/svg"><text>&x;</text></svg>"""));
}

public class CloseOnBlurTests
{
    [Fact] public void Clicking_another_program_closes() => Assert.True(PreviewWindow.ShouldCloseOnBlur(false, 5000, false));
    [Fact] public void Our_own_dialog_does_not_close() => Assert.False(PreviewWindow.ShouldCloseOnBlur(true, 5000, false));
    [Fact] public void A_focus_bounce_on_opening_does_not_close() => Assert.False(PreviewWindow.ShouldCloseOnBlur(false, 100, false));
    [Fact] public void Automatic_preview_stays_open() => Assert.False(PreviewWindow.ShouldCloseOnBlur(false, 5000, true));
}

public class WindowFitTests
{
    static (double W, double H) Fit(Size? c, bool aspect, AppSettings? s = null) =>
        WindowFit.Size(c, aspect, s ?? new AppSettings(), 1600, 900, 440, 300, 1000, 760, 48);

    [Fact] public void No_content_uses_the_default_size() => Assert.Equal((1000, 760), Fit(null, false));

    [Fact]
    public void A_large_image_shrinks_and_keeps_its_shape()
    {
        var (w, h) = Fit(new Size(4000, 2000), true);
        Assert.Equal(1600, w);
        Assert.Equal(848, h);
    }

    [Fact] public void A_small_image_is_held_at_the_minimum() => Assert.Equal((440, 300), Fit(new Size(100, 100), true));
    [Fact] public void A_tall_document_fills_the_height_without_losing_width() => Assert.Equal((920, 900), Fit(new Size(920, 1200), false));
    [Fact] public void Large_mode_ignores_content() => Assert.Equal((1600, 900), Fit(new Size(100, 100), true, new AppSettings { Sizing = WindowSizing.Large }));

    [Fact]
    public void A_remembered_size_wins_but_stays_on_the_screen() =>
        Assert.Equal((1600, 700), Fit(new Size(100, 100), true, new AppSettings { RememberSize = true, LastWidth = 3000, LastHeight = 700 }));
}
