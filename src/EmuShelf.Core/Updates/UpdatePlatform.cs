using System.Runtime.InteropServices;

namespace EmuShelf.Core.Updates;

/// <summary>
/// Maps the running OS/architecture to the release-asset names EmuShelf's CI publishes, keeping the
/// updater and its tests in agreement with <c>.github/workflows/build.yml</c>. Platforms without a
/// published artifact (e.g. Intel macOS) return null so the updater simply stays quiet there.
/// </summary>
public static class UpdatePlatform
{
    /// <summary>The portable artifact file name for the current platform, or null when none exists.</summary>
    public static string? CurrentAssetName()
    {
        if (OperatingSystem.IsWindows() && RuntimeInformation.OSArchitecture == Architecture.X64)
            return "EmuShelf-win-x64.zip";
        if (OperatingSystem.IsLinux() && RuntimeInformation.OSArchitecture == Architecture.X64)
            return "EmuShelf-linux-x64.AppImage";
        if (OperatingSystem.IsMacOS() && RuntimeInformation.OSArchitecture == Architecture.Arm64)
            return "EmuShelf-macos-arm64.zip";
        return null;
    }

    /// <summary>
    /// The name of the SHA-256 checksum asset that sits beside a payload. CI names it after the
    /// payload with the extension replaced by <c>.sha256</c> (e.g. <c>EmuShelf-win-x64.zip</c> →
    /// <c>EmuShelf-win-x64.sha256</c>, <c>EmuShelf-linux-x64.AppImage</c> →
    /// <c>EmuShelf-linux-x64.sha256</c>).
    /// </summary>
    public static string ChecksumAssetNameFor(string payloadAssetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadAssetName);
        return Path.GetFileNameWithoutExtension(payloadAssetName) + ".sha256";
    }
}
