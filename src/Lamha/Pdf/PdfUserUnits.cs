using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lamha.Pdf;

/// <summary>
/// PDF caps pages at 14400 units, so LibreOffice describes a sheet taller than that (about 950 rows) as a small page
/// with a /UserUnit multiplier. PDFium ignores the multiplier and would show a sliver. This reads it back from the
/// PDFs LibreOffice writes for us, whose page objects are plain text and in page order; it is not a general PDF parser.
/// </summary>
public static partial class PdfUserUnits
{
    [GeneratedRegex(@"/Type/Page[^s].*?endobj", RegexOptions.Singleline)]
    private static partial Regex PageObject();

    [GeneratedRegex(@"/UserUnit\s+([0-9.]+)")]
    private static partial Regex UserUnit();

    public static double[] Parse(string pdf, int pageCount)
    {
        var units = Enumerable.Repeat(1.0, pageCount).ToArray();
        var pages = PageObject().Matches(pdf);
        if (pages.Count != pageCount) return units;
        for (int i = 0; i < pageCount; i++)
            if (UserUnit().Match(pages[i].Value) is { Success: true } m
                && double.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, out double u) && u > 0)
                units[i] = u;
        return units;
    }

    public static double[] Read(string path, int pageCount) => Parse(File.ReadAllText(path, Encoding.Latin1), pageCount);
}
