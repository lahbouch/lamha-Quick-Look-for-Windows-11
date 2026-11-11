namespace Lamha.Core;

/// <summary>Names for pages saved beside the original. Existing files are never reused, so nothing is overwritten.</summary>
public static class PageFiles
{
    public static string PathFor(string source, int page, int total, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        var dir = Path.GetDirectoryName(source)!;
        var name = Path.GetFileNameWithoutExtension(source);
        // Padding keeps "صفحة 02" before "صفحة 10" when the folder is sorted by name.
        var number = (page + 1).ToString().PadLeft(total.ToString().Length, '0');
        var path = Path.Combine(dir, $"{name} - صفحة {number}.pdf");
        for (int i = 2; exists(path); i++) path = Path.Combine(dir, $"{name} - صفحة {number} ({i}).pdf");
        return path;
    }
}
