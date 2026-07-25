using System.Diagnostics;
using EmuShelf.Core.Storage;

namespace EmuShelf.Infrastructure.SaveSync;

/// <summary>
/// Drives rclone's configuration commands — creating a cloud remote through its own OAuth flow and
/// ensuring the save folder exists — writing only to EmuShelf's portable rclone config. Kept
/// separate from <see cref="RcloneCloudSyncTransport"/> so data transfer stays copy-only, and so
/// the OAuth token remains owned by rclone rather than ever passing through EmuShelf.
/// </summary>
public sealed class RcloneConfigurator
{
    private readonly string _rclonePath;
    private readonly string _configurationPath;

    public RcloneConfigurator(IAppPaths appPaths, string? rclonePath = null)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        _rclonePath = RcloneExecutable.Resolve(appPaths, rclonePath);
        _configurationPath = Path.Combine(appPaths.SettingsDirectory, "rclone.conf");
    }

    /// <summary>Whether the portable rclone executable is present beside EmuShelf.</summary>
    public bool IsRcloneAvailable => File.Exists(_rclonePath);

    /// <summary>Runs rclone's Google Drive OAuth and stores the resulting remote in the portable config.</summary>
    public Task CreateGoogleDriveRemoteAsync(string remoteName, CancellationToken cancellationToken = default)
    {
        ValidateRemoteName(remoteName);
        return RunAsync(["config", "create", remoteName, "drive"], cancellationToken);
    }

    /// <summary>Creates the cloud save folder if it does not already exist (idempotent).</summary>
    public Task EnsureFolderAsync(string remoteName, string cloudFolder, CancellationToken cancellationToken = default)
    {
        ValidateRemoteName(remoteName);
        var folder = (cloudFolder ?? string.Empty).Trim().Trim('/').Replace('\\', '/');
        var target = string.IsNullOrEmpty(folder) ? remoteName + ":" : remoteName + ":" + folder;
        return RunAsync(["mkdir", target], cancellationToken);
    }

    private async Task RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        if (!File.Exists(_rclonePath))
            throw new IOException($"rclone was not found at {_rclonePath}. Place rclone beside EmuShelf.");
        Directory.CreateDirectory(Path.GetDirectoryName(_configurationPath)!);

        var startInfo = new ProcessStartInfo
        {
            FileName = _rclonePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(_configurationPath);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("The operating system did not start rclone.");
        var drainOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var readError = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(drainOutput, readError, process.WaitForExitAsync(cancellationToken));
        if (process.ExitCode != 0)
            throw new IOException($"rclone exited with code {process.ExitCode}: {(await readError).Trim()}");
    }

    private static void ValidateRemoteName(string remoteName)
    {
        if (string.IsNullOrWhiteSpace(remoteName) ||
            remoteName.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("The rclone remote name contains unsupported characters.", nameof(remoteName));
        }
    }
}
