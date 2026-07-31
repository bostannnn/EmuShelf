using System.IO.Compression;
using System.Runtime.InteropServices;
using EmuShelf.Core.Storage;

namespace EmuShelf.Infrastructure.SaveSync;

/// <summary>
/// Downloads the official rclone build for the current OS/architecture and places the executable
/// beside EmuShelf — exactly where <see cref="RcloneExecutable.Resolve"/> looks — so cloud sync
/// works without the user hunting for the right folder.
/// </summary>
public sealed class RcloneInstaller
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>Downloads and installs rclone, returning the path it was written to.</summary>
    public async Task<string> InstallAsync(IAppPaths appPaths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appPaths);

        var url = BuildDownloadUrl();
        var destination = Path.Combine(appPaths.BaseDirectory, RcloneExecutable.FileName);
        Directory.CreateDirectory(appPaths.BaseDirectory);

        var temporaryZip = Path.Combine(
            Path.GetTempPath(),
            "emushelf-rclone-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            await using (var download = await Http.GetStreamAsync(url, cancellationToken))
            await using (var target = new FileStream(temporaryZip, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await download.CopyToAsync(target, cancellationToken);
            }

            using var archive = ZipFile.OpenRead(temporaryZip);
            var entry = archive.Entries.FirstOrDefault(candidate =>
                string.Equals(Path.GetFileName(candidate.FullName), RcloneExecutable.FileName, StringComparison.OrdinalIgnoreCase))
                ?? throw new IOException("The rclone download did not contain the expected executable.");

            var staged = destination + ".download";
            entry.ExtractToFile(staged, overwrite: true);
            File.Move(staged, destination, overwrite: true);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    destination,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            return destination;
        }
        finally
        {
            if (File.Exists(temporaryZip))
                File.Delete(temporaryZip);
        }
    }

    private static string BuildDownloadUrl()
    {
        var os = OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsMacOS() ? "osx"
            : "linux";
        var architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "amd64",
            _ => throw new PlatformNotSupportedException(
                $"No official rclone build is available for {RuntimeInformation.OSArchitecture}."),
        };
        return $"https://downloads.rclone.org/rclone-current-{os}-{architecture}.zip";
    }
}
