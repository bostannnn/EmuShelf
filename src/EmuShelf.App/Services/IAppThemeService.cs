using EmuShelf.Core.Settings;

namespace EmuShelf.App.Services;

public interface IAppThemeService
{
    ThemePreference Current { get; }

    /// <summary>True when "match colours to artwork" is on. The chosen <see cref="Current"/> theme then
    /// acts as the fallback for artwork with no usable colour (grayscale covers, extraction failures).</summary>
    bool AmbientFromArtwork { get; }

    /// <summary>Raised after <see cref="AmbientFromArtwork"/> changes so the shell can start or stop
    /// driving per-game palettes.</summary>
    event EventHandler? AmbientFromArtworkChanged;

    Task SetThemeAsync(
        ThemePreference preference,
        CancellationToken cancellationToken = default);

    /// <summary>Persists the ambient toggle. Applying or clearing the live palette is the caller's job
    /// via <see cref="ApplyArtworkPalette"/> / <see cref="ClearArtworkPalette"/>.</summary>
    Task SetAmbientFromArtworkAsync(bool enabled, CancellationToken cancellationToken = default);

    /// <summary>Re-colours the whole UI live from an artwork-derived palette, layered over the chosen
    /// theme. Safe to call repeatedly as focus moves.</summary>
    void ApplyArtworkPalette(ArtworkPalette palette);

    /// <summary>Drops the artwork palette and restores the chosen theme's appearance.</summary>
    void ClearArtworkPalette();
}
