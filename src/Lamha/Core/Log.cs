namespace Lamha.Core;

/// <summary>Local log. Never receives document content, and callers pass file extensions rather than paths.</summary>
public static class Log
{
    const long MaxBytes = 2 * 1024 * 1024;
    static readonly object Gate = new();

    public static bool Verbose { get; set; }
    public static string FilePath => Path.Combine(AppPaths.Logs, "lamha.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Debug(string message) { if (Verbose) Write("DEBUG", message); }
    public static void Error(string message, Exception? e = null) =>
        Write("ERROR", e is null ? message : $"{message}: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");

    static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Logs);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes) File.Move(FilePath, FilePath + ".1", overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}\n");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // logging must never take the app down
    }
}
