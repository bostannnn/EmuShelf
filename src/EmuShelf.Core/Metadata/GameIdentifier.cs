namespace EmuShelf.Core.Metadata;

/// <summary>The kind of stable evidence used to match a local game to a catalog entry.</summary>
public enum GameIdentifierKind
{
    Serial,
    DiscId,
    TitleId,
    Crc32,
    Sha1,
}

/// <summary>
/// One identifier extracted from a game without modifying it. A library entry can
/// have several identifiers, for example one serial for each disc in an M3U.
/// </summary>
public sealed record GameIdentifier(
    GameIdentifierKind Kind,
    string Value,
    string Source,
    bool IsPrimary = false);
