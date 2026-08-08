namespace EmuShelf.Core.Emulators;

/// <summary>How an emulator publishes its downloadable builds.</summary>
public enum EmulatorReleaseSourceKind
{
    /// <summary>Builds are GitHub releases on an <c>owner/repo</c>; assets are picked per OS/arch.</summary>
    GitHubReleases,

    /// <summary>
    /// Builds live on a vendor build server whose listing needs a bespoke resolver (Dolphin,
    /// RetroArch). EmuShelf cannot manage the install until that resolver ships, so the UI offers only a
    /// link to <see cref="EmulatorReleaseSource.DownloadPageUrl"/>.
    /// </summary>
    CustomServer,
}

/// <summary>The container a downloaded emulator asset arrives in, which decides how it is unpacked.</summary>
public enum EmulatorArchiveKind
{
    /// <summary>A <c>.zip</c> archive — unpacked with the framework's built-in reader.</summary>
    Zip,

    /// <summary>A bare <c>.AppImage</c> — the download itself is the runnable file (chmod +x only).</summary>
    AppImage,

    /// <summary>A <c>.7z</c> archive — unpacked with SharpCompress.</summary>
    SevenZip,

    /// <summary>A <c>.tar.xz</c> archive — unpacked with SharpCompress.</summary>
    TarXz,

    /// <summary>A macOS <c>.dmg</c> — mounted with <c>hdiutil</c> and the <c>.app</c> copied out.</summary>
    Dmg,
}

/// <summary>
/// One per-OS/architecture download rule: which release asset to pick, how it is packed, and where the
/// runnable executable lives once it is unpacked into the managed install directory.
/// </summary>
/// <param name="Os">Target OS token: <c>windows</c>, <c>linux</c>, or <c>macos</c>.</param>
/// <param name="Arch">Target architecture token: <c>x64</c> or <c>arm64</c>.</param>
/// <param name="AssetNamePattern">
/// A regular expression matched case-insensitively against a release's asset file names. The first
/// asset whose name matches is downloaded.
/// </param>
/// <param name="ArchiveKind">How the matched asset is packed.</param>
/// <param name="ExecutablePattern">
/// A regular expression matched case-insensitively against the relative paths of files unpacked from the
/// archive (with <c>/</c> separators); the first match is recorded as the launchable executable. Ignored
/// for <see cref="EmulatorArchiveKind.AppImage"/>, where the downloaded file itself is the executable.
/// </param>
/// <param name="Repository">
/// An <c>owner/repo</c> override used instead of the source's <see cref="EmulatorReleaseSource.GitHubRepository"/>
/// for this asset. RPCS3 publishes each platform from a separate <c>rpcs3-binaries-*</c> repository, so its
/// per-OS assets carry their own repository here.
/// </param>
public sealed record EmulatorReleaseAsset(
    string Os,
    string Arch,
    string AssetNamePattern,
    EmulatorArchiveKind ArchiveKind,
    string ExecutablePattern,
    string? Repository = null);

/// <summary>
/// Describes where an emulator's builds come from and how to select the right one for this machine.
/// Attached to an <see cref="EmuShelf.Core.Launching.EmulatorDefinition"/> in Integrations. A source with
/// <see cref="EmulatorReleaseSourceKind.CustomServer"/> and no assets is a placeholder EmuShelf cannot
/// install itself; it offers only the <see cref="DownloadPageUrl"/>.
/// </summary>
public sealed record EmulatorReleaseSource(
    EmulatorReleaseSourceKind Kind,
    string? GitHubRepository,
    IReadOnlyList<EmulatorReleaseAsset> Assets,
    bool PublishesChecksums = false,
    string? DownloadPageUrl = null)
{
    /// <summary>True when EmuShelf can download and install this emulator itself on some platform.</summary>
    public bool IsManaged => Kind == EmulatorReleaseSourceKind.GitHubReleases && Assets.Count > 0;

    /// <summary>The per-OS/arch rule for the given platform, or null when no build is offered for it.</summary>
    public EmulatorReleaseAsset? SelectAsset(string os, string arch) =>
        Assets.FirstOrDefault(asset =>
            string.Equals(asset.Os, os, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(asset.Arch, arch, StringComparison.OrdinalIgnoreCase));

    /// <summary>The <c>owner/repo</c> to query for a given asset, honoring its per-asset override.</summary>
    public string? RepositoryFor(EmulatorReleaseAsset asset) =>
        string.IsNullOrWhiteSpace(asset.Repository) ? GitHubRepository : asset.Repository;

    /// <summary>A GitHub-managed source EmuShelf can install and update itself.</summary>
    public static EmulatorReleaseSource GitHub(
        string repository,
        IReadOnlyList<EmulatorReleaseAsset> assets,
        bool publishesChecksums = false,
        string? downloadPageUrl = null) =>
        new(EmulatorReleaseSourceKind.GitHubReleases, repository, assets, publishesChecksums, downloadPageUrl);

    /// <summary>
    /// A vendor-server source EmuShelf cannot manage yet — the UI only points the user at the emulator's
    /// own download page. Used for Dolphin and RetroArch until their build-server resolvers ship.
    /// </summary>
    public static EmulatorReleaseSource CustomServerPlaceholder(string downloadPageUrl) =>
        new(EmulatorReleaseSourceKind.CustomServer, null, [], false, downloadPageUrl);
}
