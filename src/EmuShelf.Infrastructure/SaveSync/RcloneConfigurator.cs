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

    /// <summary>
    /// Runs rclone's Google Drive OAuth and stores the resulting remote in the portable config.
    /// </summary>
    /// <param name="clientId">
    /// An optional Google OAuth client id. Without one rclone uses its own shared client, which is
    /// heavily rate-limited — the cause of multi-second waits before a launch — and which Google is
    /// retiring during 2026.
    /// </param>
    /// <param name="clientSecret">
    /// The matching client secret. It is handed to rclone and stored only in rclone's own config
    /// beside the OAuth token; EmuShelf never persists it in its settings.
    /// </param>
    public Task CreateGoogleDriveRemoteAsync(
        string remoteName,
        CancellationToken cancellationToken = default,
        string? clientId = null,
        string? clientSecret = null)
    {
        ValidateRemoteName(remoteName);
        var arguments = new List<string> { "config", "create", remoteName, "drive" };
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            // Both halves or neither: a client id without its secret authenticates as nothing and
            // would fail the OAuth flow with a message that does not explain why.
            if (string.IsNullOrWhiteSpace(clientSecret))
                throw new ArgumentException("A Google client id also needs its client secret.", nameof(clientSecret));

            arguments.Add("client_id");
            arguments.Add(ValidateConfigValue(clientId, nameof(clientId)));
            arguments.Add("client_secret");
            arguments.Add(ValidateConfigValue(clientSecret, nameof(clientSecret)));
        }

        return RunAsync(arguments, cancellationToken);
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

    // Passed as its own argv entry, so quoting is not a concern; control characters are, because a
    // newline would end up writing an extra key into rclone's config file.
    private static string ValidateConfigValue(string value, string parameterName)
    {
        var trimmed = value.Trim();
        if (trimmed.Length > 512 || trimmed.Any(char.IsControl))
            throw new ArgumentException("The value contains unsupported characters.", parameterName);
        return trimmed;
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
