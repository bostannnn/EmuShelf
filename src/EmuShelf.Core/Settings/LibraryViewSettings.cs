namespace EmuShelf.Core.Settings;

/// <summary>
/// How the library was last being looked at, restored on the next launch. The sort column and
/// scope are stored by name rather than as enums: they are view-layer concepts Core deliberately
/// does not model, and a name in the portable Settings/ JSON stays readable and stays correct if
/// the view's enum is ever reordered.
/// </summary>
public sealed record LibraryViewSettings
{
    /// <summary>Cover grid when true, list view when false.</summary>
    public bool IsGridView { get; init; } = true;

    /// <summary>Name of the sort column, e.g. "Title". Unknown names fall back to the default.</summary>
    public string SortColumn { get; init; } = "Title";

    public bool SortDescending { get; init; }

    public bool IsNavigationCollapsed { get; init; }

    /// <summary>
    /// Empty supported platforms are hidden from the library by default, but remain available in
    /// import and Settings. A game whose file is currently unavailable still counts as a game.
    /// </summary>
    public bool ShowEmptyPlatforms { get; init; }

    /// <summary>"System", "AllGames", or "RecentlyAdded". Unknown names fall back to "System".</summary>
    public string Scope { get; init; } = "System";

    /// <summary>
    /// The system that was shown when <see cref="Scope"/> is "System". Null, or an id no longer
    /// present, restores the first available system.
    /// </summary>
    public string? SelectedSystemId { get; init; }
}
