using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Updates;

namespace EmuShelf.Infrastructure.Updates;

/// <summary>
/// Applies an update to the single-file AppImage build. The new AppImage atomically replaces the one
/// on disk, then the process re-execs itself — keeping the <em>same PID</em>. On SteamOS the app runs
/// as a non-Steam shortcut, and because the tracked process never exits, Steam never registers the
/// game as stopped: the update happens without ever leaving gaming mode or dropping to the desktop.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxAppImageUpdateApplier : IUpdateApplier
{
    private const UnixFileMode ExecutableMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private readonly IAppLogger _logger;

    public LinuxAppImageUpdateApplier(IAppLogger logger) => _logger = logger;

    // libc execv replaces the current process image while keeping the process id; it only returns on
    // failure. The argv array must be NULL-terminated, which a trailing null element marshals to.
    [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int execv(string path, string?[] argv);

    public bool CanApply(out string? reason)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APPIMAGE")))
        {
            reason = "Updating in place needs the packaged AppImage (run the .AppImage, not a dev build).";
            return false;
        }

        reason = null;
        return true;
    }

    public void ApplyAndRelaunch(StagedUpdate staged)
    {
        var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
        if (string.IsNullOrWhiteSpace(appImage))
            throw new InvalidOperationException("APPIMAGE is not set; cannot self-update this run.");

        var directory = Path.GetDirectoryName(appImage)
            ?? throw new InvalidOperationException($"Could not resolve the AppImage directory for '{appImage}'.");

        // Write the new file beside the current one, then rename over it. The rename is atomic and
        // gives a new inode, so the still-running process keeps reading the old file until we re-exec.
        var replacement = Path.Combine(directory, "." + Path.GetFileName(appImage) + ".new");
        File.Copy(staged.PayloadPath, replacement, overwrite: true);
        File.SetUnixFileMode(replacement, ExecutableMode);
        File.Move(replacement, appImage, overwrite: true);
        _logger.Information($"Replaced AppImage at {appImage}; re-execing to finish the update.");

        var passthrough = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var argv = new string?[passthrough.Length + 2];
        argv[0] = appImage;
        Array.Copy(passthrough, 0, argv, 1, passthrough.Length);
        argv[^1] = null; // execv requires a NULL-terminated argument vector.

        _ = execv(appImage, argv);

        // execv only returns on failure. Fall back to a detached relaunch so the update still applies;
        // the caller then exits this (now-stale) process.
        var errno = Marshal.GetLastPInvokeError();
        _logger.Error($"execv failed (errno {errno}); relaunching the new AppImage detached instead.");
        Process.Start(new ProcessStartInfo { FileName = appImage, UseShellExecute = false });
    }
}
