using System.IO.Compression;
using EmuShelf.Core.Emulators;
using EmuShelf.Infrastructure.Updates;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace EmuShelf.Infrastructure.Emulators;

/// <summary>
/// Unpacks a downloaded emulator asset into a destination directory according to its archive kind. Reads
/// only the source file and writes only under the destination; it never modifies the download in place.
/// </summary>
public static class EmulatorArchiveExtractor
{
    private const UnixFileMode ExecutableMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    public static void Extract(string archivePath, EmulatorArchiveKind kind, string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        Directory.CreateDirectory(destinationDirectory);

        switch (kind)
        {
            case EmulatorArchiveKind.Zip:
                ZipFile.ExtractToDirectory(archivePath, destinationDirectory, overwriteFiles: true);
                break;
            case EmulatorArchiveKind.SevenZip:
                ExtractSevenZip(archivePath, destinationDirectory);
                break;
            case EmulatorArchiveKind.TarXz:
                ExtractWithReader(archivePath, destinationDirectory);
                break;
            case EmulatorArchiveKind.AppImage:
                // The .AppImage is itself the runnable file; place it in the install folder and mark it +x.
                var placed = Path.Combine(destinationDirectory, Path.GetFileName(archivePath));
                File.Copy(archivePath, placed, overwrite: true);
                MarkExecutable(placed);
                break;
            case EmulatorArchiveKind.Dmg:
                ExtractDmg(archivePath, destinationDirectory);
                break;
            default:
                throw new NotSupportedException($"Unsupported archive kind '{kind}'.");
        }
    }

    /// <summary>Marks a file executable on Unix (0755); a no-op on Windows. Best-effort.</summary>
    public static void MarkExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
            return;
        try
        {
            File.SetUnixFileMode(path, ExecutableMode);
        }
        catch (Exception)
        {
            // Permissions are advisory here; the launcher will still try to run the file.
        }
    }

    private static void ExtractSevenZip(string archivePath, string destinationDirectory)
    {
        // 7z needs random access, so it uses the Archive API rather than the streaming Reader API.
        using var archive = SevenZipArchive.Open(archivePath);
        var options = new ExtractionOptions { ExtractFullPath = true, Overwrite = true };
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory)
                continue;
            entry.WriteToDirectory(destinationDirectory, options);
        }
    }

    private static void ExtractWithReader(string archivePath, string destinationDirectory)
    {
        // ReaderFactory auto-detects the compression (xz) wrapping the tar and streams entries out.
        using var stream = File.OpenRead(archivePath);
        using var reader = ReaderFactory.Open(stream);
        var options = new ExtractionOptions { ExtractFullPath = true, Overwrite = true };
        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.IsDirectory)
                continue;
            reader.WriteEntryToDirectory(destinationDirectory, options);
        }
    }

    private static void ExtractDmg(string dmgPath, string destinationDirectory)
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("DMG images can only be mounted on macOS.");

        // NOTE: verified in code only — this path needs a real macOS run (RPCS3 ships macOS as a .dmg).
        var mountPoint = Path.Combine(Path.GetTempPath(), "emushelf-dmg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mountPoint);
        try
        {
            UpdateProcess.Run(
                "/usr/bin/hdiutil",
                ["attach", dmgPath, "-nobrowse", "-noverify", "-mountpoint", mountPoint],
                throwOnError: true);

            foreach (var app in Directory.EnumerateDirectories(mountPoint, "*.app"))
                CopyDirectory(app, Path.Combine(destinationDirectory, Path.GetFileName(app)));
        }
        finally
        {
            UpdateProcess.Run("/usr/bin/hdiutil", ["detach", mountPoint, "-force"], throwOnError: false);
            TryDeleteDirectory(mountPoint);
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(directory.Replace(sourceDirectory, destinationDirectory));
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(sourceDirectory, destinationDirectory), overwrite: true);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
