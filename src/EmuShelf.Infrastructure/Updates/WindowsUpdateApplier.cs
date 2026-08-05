using System.IO.Compression;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Storage;
using EmuShelf.Core.Updates;

namespace EmuShelf.Infrastructure.Updates;

/// <summary>
/// Applies an update to the portable Windows build. A running .exe/.dll cannot be overwritten, so a
/// short-lived <c>.cmd</c> helper waits for this process to exit, overlays the new payload onto the
/// app folder (leaving the portable <c>Data/ Covers/ …</c> directories untouched, since the release
/// zip contains only program files), relaunches EmuShelf, and deletes itself.
/// </summary>
public sealed class WindowsUpdateApplier : IUpdateApplier
{
    private readonly IAppPaths _paths;
    private readonly IAppLogger _logger;

    public WindowsUpdateApplier(IAppPaths paths, IAppLogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public bool CanApply(out string? reason)
    {
        if (string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            reason = "Couldn't locate the running EmuShelf executable to update.";
            return false;
        }

        reason = null;
        return true;
    }

    public void ApplyAndRelaunch(StagedUpdate staged)
    {
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not resolve the running executable path.");
        // BaseDirectory ends in a separator; a quoted path ending in '\' breaks robocopy's own argv
        // parser (it reads \" as an escaped quote), so trim it before embedding it in the script.
        var targetDirectory = _paths.BaseDirectory.TrimEnd('\\', '/');

        var extractDirectory = Path.Combine(Path.GetDirectoryName(staged.PayloadPath)!, "extracted");
        if (Directory.Exists(extractDirectory))
            Directory.Delete(extractDirectory, recursive: true);
        ZipFile.ExtractToDirectory(staged.PayloadPath, extractDirectory);

        // CI zips `publish/EmuShelf`, so the archive root holds a top-level EmuShelf folder; fall back
        // to the extraction root if a future packaging change flattens it.
        var nested = Path.Combine(extractDirectory, "EmuShelf");
        var payloadRoot = Directory.Exists(nested) ? nested : extractDirectory;

        var scriptPath = Path.Combine(Path.GetTempPath(), $"emushelf-update-{Guid.NewGuid():N}.cmd");
        File.WriteAllText(scriptPath, BuildScript(Environment.ProcessId, payloadRoot, targetDirectory, exePath));
        _logger.Information($"Launching Windows update helper {scriptPath}; the app will now exit.");
        UpdateProcess.LaunchDetached("cmd.exe", ["/c", scriptPath]);
    }

    private static string BuildScript(int pid, string source, string destination, string exePath) =>
        $"""
        @echo off
        setlocal
        set "PID={pid}"
        :waitloop
        tasklist /FI "PID eq %PID%" 2>nul | find "%PID%" >nul
        if not errorlevel 1 (
          timeout /t 1 /nobreak >nul
          goto waitloop
        )
        robocopy "{source}" "{destination}" /E /NFL /NDL /NJH /NJS /NC /NS /NP >nul
        start "" "{exePath}"
        del "%~f0"
        """;
}
