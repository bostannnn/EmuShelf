namespace EmuShelf.Core.TexturePacks;

/// <summary>
/// Whether an emulator would actually load its replacement textures. This is deliberately separate
/// from whether a pack is installed: precedence between global settings, per-game overrides, and a
/// runtime toggle is not always provable from configuration alone, and a guess would turn the
/// library mark into a promise EmuShelf cannot keep.
/// </summary>
public enum TexturePackLoadingStatus
{
    /// <summary>Precedence, configuration version, or a runtime override could not be resolved.</summary>
    Unknown,

    /// <summary>Replacement loading is proven on for this game.</summary>
    Enabled,

    /// <summary>Replacement loading is proven off, so an installed pack will not appear in-game.</summary>
    Disabled,
}

/// <summary>
/// How a game's texture-pack state is presented: whether the grid cover shows the mark, the list
/// column text, the tooltip explaining the state, and the column's sort value. This is pure, so the
/// grid mark, the list text, and the sort order are always derived from one result and cannot
/// disagree. The mark means "a usable pack is installed and matched" — never "the emulator will
/// definitely use it", which is what <see cref="TexturePackLoadingStatus"/> qualifies.
/// </summary>
public sealed record TexturePackDisplay(bool ShowMark, string ColumnText, string Tooltip, int SortKey)
{
    public const string Dash = "—";

    /// <summary>Before any scan has produced a result for this machine.</summary>
    public static TexturePackDisplay NotScanned { get; } =
        new(false, Dash, "Texture packs haven't been scanned yet.", -1);

    /// <summary>No emulator that supports replacement textures is configured for this system.</summary>
    public static TexturePackDisplay Unsupported { get; } =
        new(false, Dash, "No texture-pack-capable emulator is configured for this system.", -1);

    public static TexturePackDisplay For(
        IReadOnlyList<TexturePackMatch> matches,
        TexturePackLoadingStatus loading = TexturePackLoadingStatus.Unknown,
        Func<string, string>? describeEmulator = null)
    {
        ArgumentNullException.ThrowIfNull(matches);
        if (matches.Count == 0)
            return new TexturePackDisplay(false, Dash, "No installed texture pack matches this game.", 0);

        var name = describeEmulator ?? (id => id);
        var lines = new List<string>();
        foreach (var match in matches)
        {
            lines.Add($"{name(match.EmulatorId)} · {match.MatchedIdentifier}");
            lines.Add(match.SourcePath);
        }

        lines.Add(loading switch
        {
            TexturePackLoadingStatus.Enabled => "Replacement loading is enabled.",
            TexturePackLoadingStatus.Disabled =>
                "Replacement loading is turned off in the emulator, so this pack won't be used.",
            _ => "Loading status unknown — EmuShelf can't confirm the emulator's replacement setting.",
        });

        return new TexturePackDisplay(
            ShowMark: true,
            matches.Count == 1 ? "Installed" : $"{matches.Count} packs",
            string.Join(Environment.NewLine, lines),
            matches.Count);
    }
}
