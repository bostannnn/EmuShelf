using EmuShelf.Core.Emulators;

namespace EmuShelf.Integrations.Emulators;

/// <summary>
/// Where EmuShelf downloads each managed emulator from, and how to pick the right build for a machine.
/// Attached to the emulator definitions via <c>ReleaseSource</c>.
/// </summary>
/// <remarks>
/// The GitHub <c>owner/repo</c> values are stable, but the per-OS/arch <b>asset-name</b> and
/// <b>executable</b> patterns below are matched against live release file names and therefore need a
/// real-hardware/network check (the milestone's "verified per-OS/arch asset patterns" step) — they are
/// deliberately lenient (case-insensitive substrings anchored on OS/arch/extension tokens) so a small
/// upstream rename does not break matching, and every emulator carries a download-page URL so a miss
/// degrades to "open the download page", never to a wrong or partial install. Dolphin and RetroArch are
/// <see cref="EmulatorReleaseSourceKind.CustomServer"/> placeholders: their build servers
/// (dolphin-emu.org, buildbot.libretro.com) need bespoke resolvers that are not built yet.
/// </remarks>
public static class EmulatorReleaseSources
{
    // Executable-match helpers (patterns run case-insensitively against '/'-separated relative paths).
    private const string MacAppBinary = @"\.app/Contents/MacOS/[^/]+$";
    private const string AppImageFile = @"\.appimage$";

    /// <summary>DuckStation — PlayStation. Rolling releases; the tag is treated as an opaque version.</summary>
    public static EmulatorReleaseSource DuckStation { get; } = EmulatorReleaseSource.GitHub(
        "stenzek/duckstation",
        [
            new("windows", "x64", @"duckstation.*win.*x64.*\.zip", EmulatorArchiveKind.Zip, @"duckstation.*\.exe$"),
            new("linux", "x64", @"duckstation.*x64.*\.appimage", EmulatorArchiveKind.AppImage, AppImageFile),
            new("macos", "x64", @"duckstation.*mac.*\.zip", EmulatorArchiveKind.Zip, MacAppBinary),
            new("macos", "arm64", @"duckstation.*mac.*\.zip", EmulatorArchiveKind.Zip, MacAppBinary),
        ],
        downloadPageUrl: "https://www.duckstation.org/");

    /// <summary>PCSX2 — PlayStation 2. Windows/Linux from GitHub; macOS is a universal .tar.xz.</summary>
    public static EmulatorReleaseSource Pcsx2 { get; } = EmulatorReleaseSource.GitHub(
        "PCSX2/pcsx2",
        [
            new("windows", "x64", @"pcsx2.*windows.*x64.*\.7z", EmulatorArchiveKind.SevenZip, @"pcsx2.*\.exe$"),
            new("linux", "x64", @"pcsx2.*linux.*x64.*\.appimage", EmulatorArchiveKind.AppImage, AppImageFile),
            new("macos", "x64", @"pcsx2.*macos.*\.tar\.xz", EmulatorArchiveKind.TarXz, MacAppBinary),
            new("macos", "arm64", @"pcsx2.*macos.*\.tar\.xz", EmulatorArchiveKind.TarXz, MacAppBinary),
        ],
        downloadPageUrl: "https://pcsx2.net/downloads/");

    /// <summary>RPCS3 — PlayStation 3. Each platform ships from its own <c>rpcs3-binaries-*</c> repository.</summary>
    public static EmulatorReleaseSource Rpcs3 { get; } = new(
        EmulatorReleaseSourceKind.GitHubReleases,
        GitHubRepository: null,
        Assets:
        [
            new("windows", "x64", @"rpcs3.*win64.*\.7z", EmulatorArchiveKind.SevenZip, @"rpcs3\.exe$",
                Repository: "rpcs3/rpcs3-binaries-win"),
            new("linux", "x64", @"rpcs3.*linux64.*\.appimage", EmulatorArchiveKind.AppImage, AppImageFile,
                Repository: "rpcs3/rpcs3-binaries-linux"),
            new("macos", "x64", @"rpcs3.*_macos\.dmg$", EmulatorArchiveKind.Dmg, MacAppBinary,
                Repository: "rpcs3/rpcs3-binaries-mac"),
            new("macos", "arm64", @"rpcs3.*_macos_arm64\.dmg$", EmulatorArchiveKind.Dmg, MacAppBinary,
                Repository: "rpcs3/rpcs3-binaries-mac"),
        ],
        DownloadPageUrl: "https://rpcs3.net/download");

    /// <summary>PPSSPP — PSP. GitHub release binaries are Windows-focused; other platforms use the site.</summary>
    public static EmulatorReleaseSource Ppsspp { get; } = EmulatorReleaseSource.GitHub(
        "hrydgard/ppsspp",
        [
            new("windows", "x64", @"ppsspp.*win.*\.zip", EmulatorArchiveKind.Zip, @"ppsspp.*\.exe$"),
        ],
        downloadPageUrl: "https://www.ppsspp.org/download/");

    /// <summary>Azahar — Nintendo 3DS (the maintained Citra successor).</summary>
    public static EmulatorReleaseSource Azahar { get; } = EmulatorReleaseSource.GitHub(
        "azahar-emu/azahar",
        [
            new("windows", "x64", @"azahar.*windows.*\.zip", EmulatorArchiveKind.Zip, @"azahar.*\.exe$"),
            new("linux", "x64", @"azahar.*linux.*\.appimage", EmulatorArchiveKind.AppImage, AppImageFile),
            new("macos", "x64", @"azahar.*macos.*\.zip", EmulatorArchiveKind.Zip, MacAppBinary),
            new("macos", "arm64", @"azahar.*macos.*\.zip", EmulatorArchiveKind.Zip, MacAppBinary),
        ],
        downloadPageUrl: "https://azahar-emu.org/");

    /// <summary>
    /// Dolphin — GameCube/Wii. Placeholder: dolphin-emu.org's build listing needs a bespoke resolver
    /// (Phase 3), so EmuShelf only points the user at the download page for now.
    /// </summary>
    public static EmulatorReleaseSource Dolphin { get; } =
        EmulatorReleaseSource.CustomServerPlaceholder("https://dolphin-emu.org/download/");

    /// <summary>
    /// RetroArch — many systems. Placeholder: buildbot.libretro.com's directory layout needs a bespoke
    /// resolver (Phase 3, prefer Flatpak on Linux), so EmuShelf points the user at the download page for now.
    /// </summary>
    public static EmulatorReleaseSource RetroArch { get; } =
        EmulatorReleaseSource.CustomServerPlaceholder("https://www.retroarch.com/?page=platforms");
}
