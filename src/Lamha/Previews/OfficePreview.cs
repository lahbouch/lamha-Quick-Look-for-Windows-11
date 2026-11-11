using System.IO.Compression;
using System.Xml.Linq;
using Lamha.Core;
using Lamha.Office;
using Lamha.Pdf;

namespace Lamha.Previews;

public sealed class OfficeProvider(OfficeConverter converter) : IPreviewProvider
{
    public bool Handles(FileKind kind) => kind is FileKind.Word or FileKind.Excel or FileKind.PowerPoint;

    public async Task<IPreview> OpenAsync(PreviewRequest r, CancellationToken ct)
    {
        RejectFakeZip(r.Path);
        var pdf = await converter.ConvertAsync(r.Path, ct);
        var doc = await Task.Run(() =>
        {
            var d = PdfDoc.Open(pdf);
            if (FileTypes.Detect(r.Path) == FileKind.Excel) d.ScalePages(PdfUserUnits.Read(pdf, d.PageCount));
            return d;
        }, ct);
        try
        {
            var options = FileTypes.Detect(r.Path) switch
            {
                FileKind.PowerPoint => new PdfPreview.Options("شريحة", FitMode.Page, Print: true),
                FileKind.Excel => new PdfPreview.Options("ورقة", FitMode.Actual, Print: true, Sheets(doc, r.Path)),
                _ => new PdfPreview.Options("صفحة", PdfProvider.ToFit(r.Settings.Zoom), Print: true, ExtractFrom: r.Path),
            };
            return new PdfPreview(doc, options);
        }
        catch { doc.Dispose(); throw; }
    }

    /// <summary>LibreOffice would happily import a damaged .docx as plain text, hiding the damage from the reader.</summary>
    internal static void RejectFakeZip(string path)
    {
        if (!Path.GetExtension(path).EndsWith('x')) return;
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (s.ReadByte() != 'P' || s.ReadByte() != 'K') throw new OfficeConversionException();
    }

    static List<(string Title, int Page)> Sheets(PdfDoc doc, string path)
    {
        var outline = doc.Outline();
        if (outline.Count == 0) outline = Enumerable.Range(0, doc.PageCount).Select(i => ($"ورقة {i + 1}", i)).ToList();
        var hidden = HiddenSheets(path);
        return outline.Where(s => !hidden.Contains(s.Title)).ToList();
    }

    /// <summary>LibreOffice exports hidden sheets too. Legacy .xls has no zip to read, so its hidden sheets stay visible.</summary>
    internal static HashSet<string> HiddenSheets(string path)
    {
        var names = new HashSet<string>();
        if (!path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) return names;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            if (zip.GetEntry("xl/workbook.xml") is not { } entry) return names;
            using var xml = entry.Open();
            foreach (var sheet in XDocument.Load(xml).Descendants().Where(e => e.Name.LocalName == "sheet"))
                if ((string?)sheet.Attribute("state") is "hidden" or "veryHidden" && (string?)sheet.Attribute("name") is { } name)
                    names.Add(name);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Xml.XmlException) { Log.Error("workbook index unreadable", e); }
        return names;
    }
}
