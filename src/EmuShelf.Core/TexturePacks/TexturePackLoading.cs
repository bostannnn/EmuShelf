namespace EmuShelf.Core.TexturePacks;

/// <summary>A proven loading state, or a visible reason why it could not be proven.</summary>
public sealed record TexturePackLoadingResolution(
    TexturePackLoadingStatus Status,
    string? Diagnostic = null)
{
    public static TexturePackLoadingResolution Unknown(string diagnostic) =>
        new(TexturePackLoadingStatus.Unknown, diagnostic);
}

/// <summary>
/// Read-only, version-aware discovery of whether one emulator installation would load replacement
/// textures. Implementations must return <see cref="TexturePackLoadingStatus.Unknown"/> rather than
/// a plausible answer whenever the configuration version is unrecognized, the setting is absent, or
/// a per-game override could change the result in a way this adapter cannot order.
/// </summary>
public interface ITexturePackLoadingResolver
{
    string EmulatorId { get; }

    string InstallationId { get; }

    /// <param name="gameKey">
    /// The identifier this emulator names per-game configuration files after (a serial or disc id),
    /// or null to resolve only the global setting.
    /// </param>
    Task<TexturePackLoadingResolution> ResolveAsync(
        string? gameKey = null,
        CancellationToken cancellationToken = default);
}
