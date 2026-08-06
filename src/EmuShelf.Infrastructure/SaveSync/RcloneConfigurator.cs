using System.Diagnostics;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Build;

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
    /// Runs rclone's Google Drive OAuth and stores the resulting remote in the portable config,
    /// authenticating with the Google OAuth client baked into the build. Without an embedded client
    /// (an unconfigured local build) rclone falls back to its own shared client, which is heavily
    /// rate-limited — the cause of multi-second waits before a launch — and which Google is retiring
    /// during 2026.
    /// </summary>
    public Task CreateGoogleDriveRemoteAsync(string remoteName, CancellationToken cancellationToken = default)
    {
        ValidateRemoteName(remoteName);

        var arguments = new List<string> { "config", "create", remoteName, "drive" };
        var client = ResolveGoogleClient(
            EmbeddedSecrets.GoogleOAuthClientId,
            EmbeddedSecrets.GoogleOAuthClientSecret);
        if (client is { } resolved)
        {
            arguments.Add("client_id");
            arguments.Add(ValidateConfigValue(resolved.ClientId, "client_id"));
            arguments.Add("client_secret");
            arguments.Add(ValidateConfigValue(resolved.ClientSecret, "client_secret"));
        }

        return RunAsync(arguments, cancellationToken);
    }

    /// <summary>
    /// The OAuth client the remote should use: the client embedded in the build, or
    /// <see langword="null"/> when the build has none, so rclone falls back to its shared client.
    /// Both halves must be present — an id without its secret authenticates as nothing.
    /// </summary>
    internal static (string ClientId, string ClientSecret)? ResolveGoogleClient(
        string? embeddedClientId,
        string? embeddedClientSecret)
    {
        if (!string.IsNullOrWhiteSpace(embeddedClientId) && !string.IsNullOrWhiteSpace(embeddedClientSecret))
            return (embeddedClientId.Trim(), embeddedClientSecret.Trim());

        return null;
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
