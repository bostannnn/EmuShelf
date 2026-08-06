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
    /// <exception cref="RcloneSignInServerBusyException">
    /// A previous, abandoned sign-in is still holding rclone's loopback OAuth port.
    /// </exception>
    public async Task CreateGoogleDriveRemoteAsync(string remoteName, CancellationToken cancellationToken = default)
    {
        ValidateRemoteName(remoteName);

        // A sign-in that was never completed (the app closed while the browser was open) leaves an
        // rclone holding the loopback OAuth port, which makes every later attempt fail to bind it.
        // Clear our own leftovers first, off the calling thread. Connect and sync are serialized by
        // the coordinator's gate, so any of our rclone alive here is an orphan, never a live transfer.
        await Task.Run(KillStaleOAuthProcesses, cancellationToken).ConfigureAwait(false);

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

        await RunAsync(arguments, cancellationToken).ConfigureAwait(false);
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
        try
        {
            var drainOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var readError = process.StandardError.ReadToEndAsync(cancellationToken);
            await Task.WhenAll(drainOutput, readError, process.WaitForExitAsync(cancellationToken));
            if (DescribeFailure(process.ExitCode, await readError) is { } failure)
                throw failure;
        }
        finally
        {
            // The OAuth `config create` blocks on the browser sign-in and binds the loopback port
            // while it waits; a cancelled or abandoned run must not leave it running and holding that
            // port. A run that exited on its own (the common case) is already gone, so this is a no-op.
            TryKill(process);
        }
    }

    /// <summary>
    /// Maps an rclone exit into the exception to surface, or <see langword="null"/> on success. The
    /// port-in-use case gets its own type so the UI can explain it instead of showing rclone's usage
    /// dump. Kept static and pure so the classification is unit-testable without spawning rclone.
    /// </summary>
    internal static Exception? DescribeFailure(int exitCode, string standardError)
    {
        if (exitCode == 0)
            return null;

        var error = (standardError ?? string.Empty).Trim();
        if (error.Contains("address already in use", StringComparison.OrdinalIgnoreCase))
            return new RcloneSignInServerBusyException();

        return new IOException($"rclone exited with code {exitCode}: {error}");
    }

    /// <summary>
    /// Kills any leftover instance of our bundled rclone. Matched by executable path so an unrelated
    /// rclone the user runs for their own purposes is never touched; best-effort throughout, because a
    /// process we cannot inspect or signal must not fail the sign-in.
    /// </summary>
    private void KillStaleOAuthProcesses()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(_rclonePath));
        }
        catch
        {
            return;
        }

        foreach (var process in processes)
        {
            try
            {
                if (PathsEqual(process.MainModule?.FileName, _rclonePath))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2000);
                }
            }
            catch
            {
                // Access denied, already exited, or path unreadable — leave it alone.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone, or we lack permission — nothing more to do.
        }
    }

    private static bool PathsEqual(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;

        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), comparison);
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

/// <summary>
/// rclone could not bind its loopback OAuth callback port (127.0.0.1:53682) because a previous
/// sign-in is still holding it. Distinct from a generic failure so the connect flow can tell the user
/// how to clear it rather than reporting a declined sign-in.
/// </summary>
public sealed class RcloneSignInServerBusyException : IOException
{
    public RcloneSignInServerBusyException()
        : base("A previous Google sign-in is still open and holding the sign-in port (127.0.0.1:53682). " +
               "Close that browser window or restart EmuShelf, then try connecting again.")
    {
    }
}
