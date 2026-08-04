namespace EmuShelf.Core.Settings;

/// <summary>
/// A complete appearance derived at runtime from one game's artwork, used by the "match colours to
/// artwork" mode. Colours are opaque <c>#RRGGBB</c> hex strings so Core stays UI-framework free, exactly
/// like <see cref="AppTheme"/> and the palette dictionaries; the App layer maps these onto the
/// <c>EmuXxxBrush</c> tokens (deriving the translucent selection/hover tints from <see cref="Accent"/>).
/// </summary>
public sealed record ArtworkPalette(
    bool IsDark,
    double AccentHue,
    string Background,
    string Surface,
    string Elevated,
    string Card,
    string Border,
    string StrongBorder,
    string TextPrimary,
    string TextSecondary,
    string TextTertiary,
    string Accent,
    string AccentText);

/// <summary>
/// Turns a vibrant swatch plus the overall brightness of an image into a coherent, readable
/// <see cref="ArtworkPalette"/>. The hue comes from the art; the lightness of every surface and text
/// colour is <em>forced</em> into a safe band so no cover can make the menu unreadable, and the
/// dark/light decision carries a dead zone so scrolling between similarly-lit covers does not strobe.
/// </summary>
public static class ArtworkPaletteFactory
{
    // Below the low bound the art reads as dark; above the high bound as light. Between them the previous
    // decision is kept (hysteresis), so a run of mid-brightness covers does not flip the whole UI.
    private const double DarkExitLuminance = 0.58;
    private const double LightExitLuminance = 0.42;
    private const double NeutralThreshold = 0.5;

    private const double MinimumTextContrast = 4.5;

    public static ArtworkPalette Create(Rgb vibrant, double averageLuminance, bool? previousIsDark = null)
    {
        var isDark = previousIsDark switch
        {
            true => averageLuminance <= DarkExitLuminance,
            false => averageLuminance < LightExitLuminance,
            null => averageLuminance < NeutralThreshold,
        };

        var (hue, rawSaturation, rawLightness) = ArtworkColor.ToHsl(vibrant);

        // A muddy or washed-out swatch still has a hue; bump saturation and pin lightness so the accent
        // stays vivid and legible whether it sits on a near-black or near-white panel.
        var accentSaturation = Math.Clamp(Math.Max(rawSaturation, 0.5), 0d, 0.95);
        var accentLightness = Math.Clamp(rawLightness, 0.46, 0.60);
        var accent = ArtworkColor.FromHsl(hue, accentSaturation, accentLightness);
        var accentText = OnColor(accent);

        var palette = isDark
            ? Build(
                hue, isDark,
                background: (0.22, 0.075), surface: (0.20, 0.115), elevated: (0.18, 0.155), card: (0.18, 0.135),
                border: (0.15, 0.26), strongBorder: (0.12, 0.36),
                textPrimary: (0.10, 0.95), textSecondary: (0.09, 0.72), textTertiary: (0.08, 0.56),
                accent, accentText)
            : Build(
                hue, isDark,
                background: (0.30, 0.965), surface: (0.32, 0.93), elevated: (0.26, 0.99), card: (0.36, 0.90),
                border: (0.30, 0.82), strongBorder: (0.24, 0.66),
                textPrimary: (0.34, 0.14), textSecondary: (0.24, 0.34), textTertiary: (0.18, 0.47),
                accent, accentText);

        return palette;
    }

    private static ArtworkPalette Build(
        double hue,
        bool isDark,
        (double S, double L) background,
        (double S, double L) surface,
        (double S, double L) elevated,
        (double S, double L) card,
        (double S, double L) border,
        (double S, double L) strongBorder,
        (double S, double L) textPrimary,
        (double S, double L) textSecondary,
        (double S, double L) textTertiary,
        Rgb accent,
        Rgb accentText)
    {
        var backgroundColor = ArtworkColor.FromHsl(hue, background.S, background.L);
        var primaryText = EnsureContrast(ArtworkColor.FromHsl(hue, textPrimary.S, textPrimary.L), backgroundColor, isDark);
        var secondaryText = ArtworkColor.FromHsl(hue, textSecondary.S, textSecondary.L);
        var tertiaryText = ArtworkColor.FromHsl(hue, textTertiary.S, textTertiary.L);

        return new ArtworkPalette(
            IsDark: isDark,
            AccentHue: hue,
            Background: backgroundColor.ToHex(),
            Surface: ArtworkColor.FromHsl(hue, surface.S, surface.L).ToHex(),
            Elevated: ArtworkColor.FromHsl(hue, elevated.S, elevated.L).ToHex(),
            Card: ArtworkColor.FromHsl(hue, card.S, card.L).ToHex(),
            Border: ArtworkColor.FromHsl(hue, border.S, border.L).ToHex(),
            StrongBorder: ArtworkColor.FromHsl(hue, strongBorder.S, strongBorder.L).ToHex(),
            TextPrimary: primaryText.ToHex(),
            TextSecondary: secondaryText.ToHex(),
            TextTertiary: tertiaryText.ToHex(),
            Accent: accent.ToHex(),
            AccentText: accentText.ToHex());
    }

    /// <summary>Black or white — whichever reads better on top of <paramref name="fill"/>.</summary>
    private static Rgb OnColor(Rgb fill)
    {
        var white = new Rgb(255, 255, 255);
        var near = new Rgb(20, 20, 24);
        return ArtworkColor.ContrastRatio(fill, white) >= ArtworkColor.ContrastRatio(fill, near) ? white : near;
    }

    /// <summary>Drive body text toward pure white/near-black until it clears the readability floor.</summary>
    private static Rgb EnsureContrast(Rgb text, Rgb background, bool isDark)
    {
        if (ArtworkColor.ContrastRatio(text, background) >= MinimumTextContrast)
            return text;
        return isDark ? new Rgb(245, 245, 248) : new Rgb(16, 16, 20);
    }
}
