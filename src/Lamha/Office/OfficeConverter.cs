using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Lamha.Core;
using Microsoft.Win32;

namespace Lamha.Office;

public sealed class OfficeEngineMissingException : Exception;
public sealed class OfficeConversionException : Exception;

/// <summary>
/// Converts Office files to PDF with a private LibreOffice. The original is copied first, so nothing can write beside
/// it, and the private profile disables macros and link updates because every previewed file is untrusted.
/// </summary>
public sealed class OfficeConverter : IDisposable
{
    const long CacheLimitBytes = 500L * 1024 * 1024;
    static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    readonly SemaphoreSlim _oneAtATime = new(1, 1); // LibreOffice allows one process per profile
    readonly nint _job = CreateKillOnCloseJob();

    public static string? FindEngine()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "libreoffice", "program", "soffice.exe");
        if (File.Exists(bundled)) return bundled;

        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\LibreOffice\UNO\InstallPath");
        if (key?.GetValue(null) is string dir && File.Exists(Path.Combine(dir, "soffice.exe"))) return Path.Combine(dir, "soffice.exe");

        var standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program", "soffice.exe");
        return File.Exists(standard) ? standard : null;
    }

    /// <summary>Single-sheet pages keep a workbook's layout, where normal printing would split it across sheets of paper.</summary>
    public static string ExportFilter(FileKind kind) => kind switch
    {
        FileKind.Excel => """pdf:calc_pdf_Export:{"SinglePageSheets":{"type":"boolean","value":"true"}}""",
        FileKind.PowerPoint => "pdf:impress_pdf_Export",
        _ => "pdf:writer_pdf_Export",
    };

    public static string CacheKey(string path, long size, DateTime modifiedUtc)
    {
        var raw = Encoding.UTF8.GetBytes($"{path.ToUpperInvariant()}|{size}|{modifiedUtc.Ticks}");
        return Convert.ToHexString(SHA256.HashData(raw))[..32] + ".pdf";
    }

    public async Task<string> ConvertAsync(string path, CancellationToken ct)
    {
        var info = new FileInfo(path);
        var cached = Path.Combine(AppPaths.Cache, CacheKey(path, info.Length, info.LastWriteTimeUtc));
        if (File.Exists(cached))
        {
            File.SetLastAccessTimeUtc(cached, DateTime.UtcNow);
            return cached;
        }

        var engine = FindEngine() ?? throw new OfficeEngineMissingException();
        Log.Info($"office engine: {(engine.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase) ? "bundled" : "system")}");
        await _oneAtATime.WaitAsync(ct);
        var work = Path.Combine(Path.GetTempPath(), "Lamha", Guid.NewGuid().ToString("N"));
        try
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(work);
            WriteProfile();

            // An ASCII name avoids any argument-encoding trouble with Arabic filenames.
            var input = Path.Combine(work, "input" + Path.GetExtension(path));
            await using (var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            await using (var dst = File.Create(input))
                await src.CopyToAsync(dst, ct);

            await RunAsync(engine, input, work, FileTypes.Detect(path), ct);

            var output = Path.Combine(work, "input.pdf");
            if (!File.Exists(output) || new FileInfo(output).Length == 0) throw new OfficeConversionException();
            Directory.CreateDirectory(AppPaths.Cache);
            File.Move(output, cached, overwrite: true);
            return cached;
        }
        finally
        {
            _oneAtATime.Release();
            try { Directory.Delete(work, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    async Task RunAsync(string engine, string input, string outDir, FileKind kind, CancellationToken ct)
    {
        var start = new ProcessStartInfo(engine) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-env:UserInstallation=" + new Uri(AppPaths.OfficeProfile).AbsoluteUri);
        foreach (var a in new[] { "--headless", "--norestore", "--nologo", "--nolockcheck", "--nofirststartwizard", "--convert-to", ExportFilter(kind), "--outdir", outDir, input })
            start.ArgumentList.Add(a);

        using var process = Process.Start(start) ?? throw new OfficeConversionException();
        AssignProcessToJobObject(_job, process.Handle);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            ct.ThrowIfCancellationRequested();
            Log.Error("office conversion timed out");
            throw new OfficeConversionException();
        }
    }

    // Writer and Calc number the link-update setting in opposite directions, and "never" is 0 and 2 respectively.
    const string ProfileXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <oor:items xmlns:oor="http://openoffice.org/2001/registry" xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
        <item oor:path="/org.openoffice.Office.Common/Security/Scripting"><prop oor:name="MacroSecurityLevel" oor:op="fuse"><value>3</value></prop></item>
        <item oor:path="/org.openoffice.Office.Common/Security/Scripting"><prop oor:name="DisableMacrosExecution" oor:op="fuse"><value>true</value></prop></item>
        <item oor:path="/org.openoffice.Office.Common/Security/Scripting"><prop oor:name="BlockUntrustedRefererLinks" oor:op="fuse"><value>true</value></prop></item>
        <item oor:path="/org.openoffice.Office.Writer/Content/Update"><prop oor:name="Link" oor:op="fuse"><value>0</value></prop></item>
        <item oor:path="/org.openoffice.Office.Calc/Content/Update"><prop oor:name="Link" oor:op="fuse"><value>2</value></prop></item>
        </oor:items>
        """;

    static void WriteProfile()
    {
        var dir = Path.Combine(AppPaths.OfficeProfile, "user");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "registrymodifications.xcu");
        if (!File.Exists(file)) File.WriteAllText(file, ProfileXml);
    }

    public static void PruneCache()
    {
        try
        {
            var dir = new DirectoryInfo(AppPaths.Cache);
            if (!dir.Exists) return;
            var files = dir.GetFiles("*.pdf").OrderByDescending(f => f.LastAccessTimeUtc).ToList();
            long total = 0;
            foreach (var f in files)
            {
                total += f.Length;
                if (total > CacheLimitBytes) f.Delete();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Error("cache prune failed", e); }
    }

    public void Dispose() => CloseHandle(_job);

    // The job guarantees no orphaned soffice survives this app, even after a crash.
    static nint CreateKillOnCloseJob()
    {
        const int JobObjectExtendedLimitInformation = 9, JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
        nint job = CreateJobObjectW(0, null);
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>());
        return job;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern nint CreateJobObjectW(nint attributes, string? name);
    [DllImport("kernel32")] static extern bool SetInformationJobObject(nint job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int size);
    [DllImport("kernel32")] static extern bool AssignProcessToJobObject(nint job, nint process);
    [DllImport("kernel32")] static extern bool CloseHandle(nint handle);
}
