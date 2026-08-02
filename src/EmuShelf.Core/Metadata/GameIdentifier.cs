namespace EmuShelf.Core.Metadata;

/// <summary>The kind of stable evidence used to match a local game to a catalog entry.</summary>
public enum GameIdentifierKind
{
    Serial,
    DiscId,
    TitleId,
    Crc32,
    Sha1,

    // The short romset id of an arcade archive (the zip basename, for example "mslug"), which is
    // how FinalBurn Neo itself keys a set. Appended last so existing persisted identifier kinds
    // keep their stored ordinal.
    ArcadeSetName,
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
