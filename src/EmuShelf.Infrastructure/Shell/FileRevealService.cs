using System.Diagnostics;
using EmuShelf.Core.Shell;

namespace EmuShelf.Infrastructure.Shell;

/// <summary>
/// Reveals a file in the desktop file manager using each platform's native mechanism:
/// <c>explorer /select,</c> on Windows, <c>open -R</c> on macOS, and the freedesktop
/// <c>org.freedesktop.FileManager1.ShowItems</c> D-Bus method on Linux (falling back to
/// <c>xdg-open</c> on the containing folder when no file manager answers). The reveal is
/// fire-and-forget: the file manager window outlives our process, so we only confirm the OS
/// accepted the launch.
/// </summary>
public sealed class FileRevealService : IFileRevealService
{
    private readonly Func<ProcessStartInfo, Process?> _startProcess;

    public FileRevealService()
        : this(Process.Start)
    {
    }

    internal FileRevealService(Func<ProcessStartInfo, Process?> startProcess)
    {
        _startProcess = startProcess;
    }

    /// <summary>
    /// Message thrown on platforms with no desktop file manager to reveal into (Android). Launching a
    /// helper here would fall through to the Linux <c>xdg-open</c> branch, which does not exist on
    /// Android and would trip the W^X exec restriction — so fail with a clear, catchable reason
    /// instead. Callers surface it as a status message rather than crashing.
    /// </summary>
    private const string NoFileManagerMessage =
        "Revealing files in a file manager is not available on this device.";

    public async Task RevealAsync(string path, CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsAndroid())
            throw new PlatformNotSupportedException(NoFileManagerMessage);

        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A file path is required to reveal it.", nameof(path));

        var fullPath = Path.GetFullPath(path);

        // The item is present: select it inside its folder (the requested "folder open, ROM
        // preselected" behaviour).
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            await SelectInContainerAsync(fullPath, cancellationToken);
            return;
        }

        // The item is gone (a moved or unavailable game). Fall back to opening its folder if that
        // still exists, so the user can still get to where the game used to live.
        var folder = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            throw new DirectoryNotFoundException(
                $"Neither '{fullPath}' nor its containing folder could be found.");

        Start(BuildOpenFolderStartInfo(folder));
    }

    public Task OpenDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsAndroid())
            throw new PlatformNotSupportedException(NoFileManagerMessage);

        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A folder path is required to open it.", nameof(path));

        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"The folder '{fullPath}' could not be found.");

        // Open the folder itself (its contents), not "select it in its parent" — the same
        // fire-and-forget launch RevealAsync uses for its folder fallback.
        Start(BuildOpenFolderStartInfo(fullPath));
        return Task.CompletedTask;
    }

    private async Task SelectInContainerAsync(string fullPath, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            Start(BuildWindowsSelectStartInfo(fullPath));
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            Start(BuildMacSelectStartInfo(fullPath));
            return;
        }

        // Linux has no universal "select this item" call; the freedesktop D-Bus reveal is the one
        // that most file managers honour. If nothing answers it, just open the containing folder.
        if (!await TryLinuxSelectAsync(fullPath, cancellationToken))
            Start(BuildOpenFolderStartInfo(Path.GetDirectoryName(fullPath) ?? fullPath));
    }

    private async Task<bool> TryLinuxSelectAsync(string fullPath, CancellationToken cancellationToken)
    {
        try
        {
            using var process = _startProcess(BuildLinuxSelectStartInfo(fullPath));
            if (process is null)
                return false;
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // dbus-send may be missing, or no FileManager1 provider is registered; the caller then
            // falls back to opening the folder.
            return false;
        }
    }

    private void Start(ProcessStartInfo startInfo)
    {
        // Fire-and-forget: the file manager window outlives us, so dispose our handle to the
        // spawned process right away. That releases the local handle without terminating the child.
        using var process = _startProcess(startInfo);
        if (process is null)
            throw new InvalidOperationException(
                $"The operating system did not start '{startInfo.FileName}'.");
    }

    internal static ProcessStartInfo BuildWindowsSelectStartInfo(string fullPath)
    {
        // explorer.exe uses a non-standard command line where the whole "/select,<path>" has to
        // arrive as one raw, quoted token — ArgumentList's argv-style escaping would break it.
        var normalized = fullPath.Replace('/', '\\');
        return new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{normalized}\"",
            UseShellExecute = false,
        };
    }

    internal static ProcessStartInfo BuildMacSelectStartInfo(string fullPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "open",
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-R");
        startInfo.ArgumentList.Add(fullPath);
        return startInfo;
    }

    internal static ProcessStartInfo BuildLinuxSelectStartInfo(string fullPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dbus-send",
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--session");
        startInfo.ArgumentList.Add("--dest=org.freedesktop.FileManager1");
        startInfo.ArgumentList.Add("--type=method_call");
        startInfo.ArgumentList.Add("/org/freedesktop/FileManager1");
        startInfo.ArgumentList.Add("org.freedesktop.FileManager1.ShowItems");
        // dbus-send splits an array value on commas, so a comma in the file name would arrive as two
        // bogus URIs. Percent-encode commas (the file manager decodes them back) to keep it one item.
        startInfo.ArgumentList.Add($"array:string:{ToFileUri(fullPath).Replace(",", "%2C")}");
        startInfo.ArgumentList.Add("string:");
        return startInfo;
    }

    internal static ProcessStartInfo BuildOpenFolderStartInfo(string folder)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder.Replace('/', '\\')}\"",
                UseShellExecute = false,
            };
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsMacOS() ? "open" : "xdg-open",
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(folder);
        return startInfo;
    }

    /// <summary>Percent-encoded <c>file://</c> URI FileManager1.ShowItems expects.</summary>
    internal static string ToFileUri(string fullPath) => new Uri(fullPath, UriKind.Absolute).AbsoluteUri;
}
