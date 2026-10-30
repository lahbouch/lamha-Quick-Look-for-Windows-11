namespace Lamha.Core;

public enum FileKind { Unsupported, Image, Pdf, Word, Excel, PowerPoint, Text, Csv, Video, Audio }

public static class FileTypes
{
    static readonly Dictionary<string, FileKind> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = FileKind.Image, [".jpg"] = FileKind.Image, [".jpeg"] = FileKind.Image, [".webp"] = FileKind.Image,
        [".gif"] = FileKind.Image, [".bmp"] = FileKind.Image, [".tif"] = FileKind.Image, [".tiff"] = FileKind.Image,
        [".svg"] = FileKind.Image,
        [".pdf"] = FileKind.Pdf,
        [".docx"] = FileKind.Word, [".doc"] = FileKind.Word,
        [".xlsx"] = FileKind.Excel, [".xls"] = FileKind.Excel,
        [".pptx"] = FileKind.PowerPoint, [".ppt"] = FileKind.PowerPoint,
        [".txt"] = FileKind.Text, [".md"] = FileKind.Text, [".json"] = FileKind.Text, [".xml"] = FileKind.Text,
        [".html"] = FileKind.Text, [".htm"] = FileKind.Text, [".log"] = FileKind.Text,
        [".csv"] = FileKind.Csv,
        [".mp4"] = FileKind.Video,
        [".mp3"] = FileKind.Audio,
    };

    public static FileKind Detect(string path) =>
        Map.GetValueOrDefault(Path.GetExtension(path), FileKind.Unsupported);

    public static bool IsSupported(string path) => Detect(path) != FileKind.Unsupported;
}
