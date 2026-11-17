using Avalonia.Media;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// The Launcher's ground in the Big Type look (M47 B3): the whole bottom screen is the focused item's flat colour. A console's
/// folder or game takes its console's ground, Recent its slate, Settings and Activity their indigo, your folders their own
/// colour. Focus on something with no colour of its own (an empty slot, the dock) keeps the colour it had. Both Launchers
/// (the Avalonia one and the native one) draw this one value, so a sheet opening over either shows the same ground.
/// </summary>
public partial class HandheldShellViewModel
{
    /// <summary>The colour the Launcher's ground is in the Big Type look (today's look never draws it).</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    public partial Color LauncherGround { get; private set; } = HandheldTokens.DefaultGround;

    /// <summary><see cref="LauncherGround"/> as a flat brush.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    public partial IBrush LauncherGroundBrush { get; private set; } = HandheldTokens.GroundBrush(HandheldTokens.DefaultGround);

    private static IBrush AppGroundBrush => HandheldTokens.GroundBrush(HandheldTokens.AppGround);

    private IBrush GameGroundBrush(HandheldGameEntry game) => HandheldTokens.GroundBrush(GameGround(game));

    private Color? GameGround(HandheldGameEntry game) =>
        HandheldTokens.ConsoleColour(_platforms.GetValueOrDefault(game.SystemId));

    /// <summary>The colour a home slot stands for: its folder's, its pinned game's console's, the apps'; none for an empty slot.</summary>
    private Color? GroundOf(HandheldSlot content, HandheldFolderSource? source, HandheldGameEntry? game) =>
        content.IsSettings || content.IsActivity ? HandheldTokens.AppGround
        : source is not null ? HandheldTokens.LauncherColour(source)
        : game is not null ? GameGround(game)
        : null;

    /// <summary>Follows the focus: the ground takes the focused item's colour, when it has one.</summary>
    private void UpdateGround()
    {
        // Settings has its own colour while it is open (M47 B7, the mock's focusSet: SETC); closing it, the focus's comes back.
        // While a game runs (M47 B8, the mock's launch) the Launcher is the in-game colour from the takeover until the shelf comes back.
        Color? colour = Playing is { IsTakenOver: true } play ? play.InGameColour
            : HandheldTokens.IsBigType && Settings is not null ? HandheldTokens.AppGround
            // A Search hit focused (M47 B9, the mock's focusRes lands with the game's colour): the ground floods to its console's.
            : HandheldTokens.IsBigType && Panel is HandheldSearchPanel { FocusedHit: { } hit } ? GameGround(hit.Entry)
            : Zone switch
        {
            HandheldZone.Grid when OpenFolder is { } folder => folder.SelectedTile switch
            {
                { IsApp: true } => HandheldTokens.AppGround,
                { } tile => GameGround(tile.Entry),
                _ => null,
            },
            // Big Type (M47 B9): a held tile keeps its own colour as it trades places.
            HandheldZone.Grid when _move is { Trades: true } held => HeldGround(held.Carried),
            HandheldZone.Grid => HomeGroundAt(HomeIndex),
            HandheldZone.Header when OpenFolder is { } folder => HandheldTokens.LauncherColour(folder.Source),
            _ => null,
        };
        if (colour is { } ground && ground != LauncherGround)
        {
            MiddleFloodGround = _returningFromPlay ? ground : null;
            LauncherGround = ground;
            LauncherGroundBrush = HandheldTokens.GroundBrush(ground);
            // The default background is drawn in the Launcher's colour (M47 B13): it follows, as one backdrop changing colour.
            if (HandheldTokens.IsBigType && ShowsDefaultBackdrop && Stage.Backdrop!.Color != ground)
                Stage.Backdrop = DefaultBackdrop(ground);
        }
        foreach (var item in DockItems)
            item.Ground = LauncherGroundBrush;
    }

    private Color? HeldGround(EmuShelf.Core.Handheld.HandheldSlot content)
    {
        HandheldGameEntry? game = null;
        if (content.Kind == EmuShelf.Core.Handheld.HandheldSlotKind.Game)
            _pins.TryGetValue(content.GameId, out game);
        return GroundOf(content, FolderSourceFor(content), game);
    }

    private Color? HomeGroundAt(int index)
    {
        var content = HomeSlotAt(index);
        HandheldGameEntry? game = null;
        if (content.Kind == HandheldSlotKind.Game)
            _pins.TryGetValue(content.GameId, out game);
        return GroundOf(content, FolderSourceFor(content), game);
    }
}
