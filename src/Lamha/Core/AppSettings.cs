using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lamha.Core;

public enum WindowSizing { Adaptive, Large }
public enum DefaultZoom { FitWidth, FitPage, ActualSize }

public sealed class AppSettings
{
    public bool ExplorerIntegration { get; set; } = true;
    public bool AutoPreview { get; set; }
    public WindowSizing Sizing { get; set; } = WindowSizing.Adaptive;
    public DefaultZoom Zoom { get; set; } = DefaultZoom.FitWidth;
    public bool RememberSize { get; set; }
    public double LastWidth { get; set; }
    public double LastHeight { get; set; }
    public bool VerboseLog { get; set; }

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Error("settings unreadable, using defaults", e);
        }
        return new();
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("settings not saved", e);
        }
    }
}

public static class AppPaths
{
    public static string Settings => Path.Combine(Roaming, "settings.json");
    public static string Logs => Path.Combine(Local, "logs");
    public static string Cache => Path.Combine(Local, "cache");
    public static string OfficeProfile => Path.Combine(Local, "office-profile");
    static string Roaming => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lamha");
    static string Local => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lamha");
}
