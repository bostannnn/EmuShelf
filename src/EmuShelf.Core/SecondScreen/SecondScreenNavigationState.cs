namespace EmuShelf.Core.SecondScreen;

/// <summary>The surface below any temporary drawer or achievements overlay.</summary>
public enum SecondScreenBaseSurface
{
    BrowseHome,
    GameIdle,
}

/// <summary>A temporary surface shown above the current base surface.</summary>
public enum SecondScreenOverlay
{
    None,
    AppDrawer,
    DockPicker,
    Achievements,
}

/// <summary>
/// Pure navigation state for the companion screen. Every transition advances <see cref="Revision"/>,
/// giving asynchronous artwork and achievement requests a cheap stale-result guard.
/// </summary>
public sealed record SecondScreenNavigationState
{
    private SecondScreenNavigationState(
        SecondScreenBaseSurface baseSurface,
        SecondScreenOverlay overlay,
        int? dockSlot,
        long revision)
    {
        BaseSurface = baseSurface;
        Overlay = overlay;
        DockSlot = dockSlot;
        Revision = revision;
    }

    public SecondScreenBaseSurface BaseSurface { get; }

    public SecondScreenOverlay Overlay { get; }

    public int? DockSlot { get; }

    public long Revision { get; }

    public static SecondScreenNavigationState Initial { get; } = new(
        SecondScreenBaseSurface.BrowseHome,
        SecondScreenOverlay.None,
        dockSlot: null,
        revision: 0);

    public SecondScreenNavigationState StartGame() => Next(
        SecondScreenBaseSurface.GameIdle,
        SecondScreenOverlay.None);

    public SecondScreenNavigationState ReturnToBrowse() => Next(
        SecondScreenBaseSurface.BrowseHome,
        SecondScreenOverlay.None);

    public SecondScreenNavigationState OpenDrawer(int? dockSlot = null)
    {
        if (dockSlot is < 0 or >= SecondScreenDock.SlotCount)
            throw new ArgumentOutOfRangeException(nameof(dockSlot));

        return Next(
            BaseSurface,
            dockSlot is null ? SecondScreenOverlay.AppDrawer : SecondScreenOverlay.DockPicker,
            dockSlot);
    }

    public SecondScreenNavigationState OpenAchievements() => Next(
        BaseSurface,
        SecondScreenOverlay.Achievements);

    public SecondScreenNavigationState CloseOverlay() => Next(
        BaseSurface,
        SecondScreenOverlay.None);

    private SecondScreenNavigationState Next(
        SecondScreenBaseSurface baseSurface,
        SecondScreenOverlay overlay,
        int? dockSlot = null) =>
        new(baseSurface, overlay, dockSlot, checked(Revision + 1));
}
