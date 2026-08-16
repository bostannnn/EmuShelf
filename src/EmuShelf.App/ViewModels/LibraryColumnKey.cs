namespace EmuShelf.App.ViewModels;

/// <summary>
/// Stable identity for a Desktop list-view column (M40). Persisted by name in the library view
/// state, so existing values must not be renamed or reordered casually — an unknown name is
/// tolerated on load (the column simply keeps its default), which lets new columns be added freely.
/// </summary>
public enum LibraryColumnKey
{
    Cover,
    Title,
    Console,
    Format,
    // The softcore column keeps the historical `Achievements` name so persisted layouts still map to
    // it; it is shown to the user as "Softcore". HardcoreAchievements is the paired gold column.
    Achievements,
    HardcoreAchievements,
    Textures,
    Status,
    LastPlayed,
    Playtime,
    PlayCount,
    DateAdded,
    Completeness,
    ArtworkCover,
    Screenshot,
    Fanart,
    Logo,
    Description,
    TitleScreen,
    BoxBack,
    BoxSpine,
    PhysicalMedia,
    PhysicalMediaTexture,
    Rating,
    Genre,
    Year,
    Players,
    Developer,
    Publisher,
}
