using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Updates;

namespace EmuShelf.Infrastructure.Updates;

/// <summary>
/// Applies an update to the macOS <c>.app</c> bundle. The user's data lives under
/// <c>~/Library/Application Support</c>, so the whole bundle can be swapped safely. A short-lived
/// shell helper waits for this process to exit, replaces the bundle, and reopens it. The freshly
/// downloaded bundle has its <c>com.apple.quarantine</c> flag cleared first — EmuShelf isn't
/// notarized, and without that Gatekeeper would refuse to launch the replacement.
///
/// When Steam started this run (the Gamepad-mode setup adds EmuShelf as a non-Steam game), the helper
/// reopens through <c>steam://rungameid</c> rather than <c>open</c>-ing the bundle directly so Steam
/// Input reattaches and the controller keeps working — see <see cref="UpdateRelaunch"/>.
/// </summary>
public sealed class MacUpdateApplier : IUpdateApplier
{
    private readonly IAppLogger _logger;

    public MacUpdateApplier(IAppLogger logger) => _logger = logger;

    public bool CanApply(out string? reason)
    {
        if (ResolveBundlePath() is null)
        {
            reason = "Updating in place needs the packaged EmuShelf.app bundle.";
            return false;
        }

        reason = null;
        return true;
    }

    public void ApplyAndRelaunch(StagedUpdate staged)
    {
        var currentBundle = ResolveBundlePath()
            ?? throw new InvalidOperationException("Could not locate the running EmuShelf.app bundle.");

        var extractDirectory = Path.Combine(Path.GetDirectoryName(staged.PayloadPath)!, "extracted");
        if (Directory.Exists(extractDirectory))
            Directory.Delete(extractDirectory, recursive: true);
        Directory.CreateDirectory(extractDirectory);

        // ditto preserves the apphost's executable bit and any bundle symlinks that a plain unzip drops.
        UpdateProcess.Run("/usr/bin/ditto", ["-x", "-k", staged.PayloadPath, extractDirectory]);

        var newBundle = Path.Combine(extractDirectory, "EmuShelf.app");
        if (!Directory.Exists(newBundle))
            throw new InvalidDataException("The macOS update archive did not contain EmuShelf.app.");

        // We downloaded this ourselves, so we may clear the quarantine flag; best-effort.
        UpdateProcess.Run("/usr/bin/xattr", ["-dr", "com.apple.quarantine", newBundle], throwOnError: false);

        // Reopen through Steam when Steam launched us, so Steam Input reattaches and the controller
        // keeps working; a plain run reopens the freshly swapped bundle directly, as before.
        var relaunchTarget = UpdateRelaunch.ResolveTarget(currentBundle);

        var scriptPath = Path.Combine(Path.GetTempPath(), $"emushelf-update-{Guid.NewGuid():N}.sh");
        File.WriteAllText(scriptPath, BuildScript(Environment.ProcessId, newBundle, currentBundle, relaunchTarget));
        _logger.Information(
            $"Launching macOS update helper {scriptPath}; will relaunch via '{relaunchTarget}'. The app will now exit.");
        UpdateProcess.LaunchDetached("/bin/bash", [scriptPath]);
    }

    /// <summary>Walks up from the running executable (…/EmuShelf.app/Contents/MacOS/EmuShelf) to the
    /// enclosing <c>.app</c> bundle, or null when the app isn't running from a bundle.</summary>
    private static string? ResolveBundlePath()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            return null;

        var macOsDirectory = Path.GetDirectoryName(executable);
        var contentsDirectory = macOsDirectory is null ? null : Path.GetDirectoryName(macOsDirectory);
        var bundle = contentsDirectory is null ? null : Path.GetDirectoryName(contentsDirectory);
        return bundle is not null && bundle.EndsWith(".app", StringComparison.Ordinal) ? bundle : null;
    }

    private static string BuildScript(int pid, string newBundle, string oldBundle, string relaunchTarget) =>
        $"""
        #!/bin/bash
        PID="{pid}"
        while kill -0 "$PID" 2>/dev/null; do sleep 0.5; done
        rm -rf "{oldBundle}"
        mv "{newBundle}" "{oldBundle}"
        open "{relaunchTarget}"
        rm -- "$0"
        """;
}
