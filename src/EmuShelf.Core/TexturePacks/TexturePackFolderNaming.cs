using EmuShelf.Core.Metadata;

namespace EmuShelf.Core.TexturePacks;

/// <summary>
/// How an emulator names the per-game folder that holds a replacement-texture pack. Each value is
/// the inverse of a <see cref="TexturePackMatchRule"/>: the same identifier the scanner reads back
/// out of a folder name is the one used to build that folder name in the first place.
/// </summary>
public enum TexturePackFolderKind
{
    /// <summary>DuckStation and PCSX2 key folders by the disc serial, verbatim (e.g. SLUS-00594).</summary>
    Serial,

    /// <summary>PPSSPP keys folders by the serial with its separators removed (e.g. ULUS10509).</summary>
    PspGameId,

    /// <summary>Dolphin keys folders by the six-character game id (e.g. GALE01).</summary>
    DolphinDiscId,

    /// <summary>Azahar keys folders by the sixteen-hex 3DS title id (e.g. 0004000000038800).</summary>
    Nintendo3dsTitleId,
}

/// <summary>
/// Builds the folder name an emulator expects for one game's texture pack from the identifiers
/// EmuShelf already stores. Pure and side-effect free — it never touches the filesystem. Returns
/// null when the game carries no identifier of the kind the emulator keys its folders by, so the
/// caller can report that rather than create a wrongly or blankly named folder.
/// </summary>
public static class TexturePackFolderNaming
{
    public static string? Build(TexturePackFolderKind kind, IReadOnlyList<GameIdentifier> identifiers)
    {
        ArgumentNullException.ThrowIfNull(identifiers);
        var name = kind switch
        {
            TexturePackFolderKind.Serial => Simple(identifiers, GameIdentifierKind.Serial),
            TexturePackFolderKind.PspGameId => PspGameId(identifiers),
            TexturePackFolderKind.DolphinDiscId => Simple(identifiers, GameIdentifierKind.DiscId),
            TexturePackFolderKind.Nintendo3dsTitleId => Simple(identifiers, GameIdentifierKind.TitleId),
            _ => null,
        };

        // The result names a single folder joined onto the texture root, so reject anything that is
        // not one path segment. Machine-extracted ids never contain separators, but this keeps a
        // malformed identifier from ever escaping the root or failing directory creation.
        return name is not null && IsSingleSegment(name) ? name : null;
    }

    private static bool IsSingleSegment(string name) =>
        name.IndexOfAny(['/', '\\']) < 0 &&
        name.IndexOf(':') < 0 &&
        name != "." &&
        name != "..";

    private static string? Simple(IReadOnlyList<GameIdentifier> identifiers, GameIdentifierKind kind)
    {
        var value = First(identifiers, kind);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    }

    // Mirrors TexturePackMatcher.NormalizePspGameId so a folder EmuShelf creates is the same folder
    // a later scan would match: the serial's letters and digits only, upper-cased (ULUS-10509 →
    // ULUS10509).
    private static string? PspGameId(IReadOnlyList<GameIdentifier> identifiers)
    {
        var value = First(identifiers, GameIdentifierKind.Serial);
        if (value is null)
            return null;
        var normalized = string.Concat(value.Where(char.IsAsciiLetterOrDigit)).ToUpperInvariant();
        return normalized.Length == 0 ? null : normalized;
    }

    // The primary identifier when one is flagged, otherwise the first of that kind — matching the
    // "any stored value wins" precedence the matcher uses when it tests set membership.
    private static string? First(IReadOnlyList<GameIdentifier> identifiers, GameIdentifierKind kind)
    {
        var ofKind = identifiers
            .Where(id => id.Kind == kind && !string.IsNullOrWhiteSpace(id.Value))
            .ToArray();
        if (ofKind.Length == 0)
            return null;
        return (ofKind.FirstOrDefault(id => id.IsPrimary) ?? ofKind[0]).Value;
    }
}
