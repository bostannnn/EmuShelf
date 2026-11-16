using System;
using EmuShelf.App.Services;
using EmuShelf.Core.Handheld;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// The model view (M47 B12, <c>docs/prototypes/handheld-big-type/proposals.html</c>, approved by the owner 2026-11-15): under Big Type a
/// focused game is either its big name alone (the name view, the default) or its medium alone in the middle of the Stage (the model
/// view), and LB or RB switches between them, on a game in a folder and on a game pinned to home alike (the owner, 2026-11-15: "Switch
/// the view"). The game's name is then only the line; the second line is its play time, achievements and emulator. The choice is kept
/// across home and folders: it shows whenever a game is focused and steps aside for anything that is not one. A fresh start begins in
/// the name view.
/// </summary>
public partial class HandheldShellViewModel
{
    // The view chosen with LB / RB. Not saved: each start of the app begins in the name view (B12's choice, awaiting the owner).
    private bool _modelViewChosen;

    // The model view put its own texts on the Stage's two lines (so the name view's are written back when it goes).
    private bool _modelTexts;
    private HandheldGameEntry? _stageGame;

    // The stick's turn on the model view's medium since it took hold (MediaRotationModel's feel, as decision 17's stick).
    private MediaRotationModel? _modelTurn;

    /// <summary>Whether the idle drift may start now (tests: it never starts over the model view).</summary>
    internal bool CanDriftForTest => CanDrift;

    /// <summary>The view LB and RB have chosen: the model view (true) or the name view.</summary>
    public bool IsModelViewChosen => _modelViewChosen;

    /// <summary>
    /// LB and RB switch the focused game between the name view and the model view here. The one place that says where: under Big Type,
    /// a game is focused, on home (a pinned game) or in a folder, with nothing over it (no sheet, menu, Settings, panel, keyboard, held
    /// tile, launch, companion or idle drift). Anywhere else LB and RB do what they did: on home they turn the page.
    /// </summary>
    public bool CanSwitchGameView =>
        HandheldTokens.IsBigType && IsGameFocused && Sheet is null && !IsMenuOpen && Playing is null &&
        CompanionScreen is not { OwnsPad: true };

    // A game has the focus in the grid, with nothing that takes the Stage over it: a folder's game, or a game pinned to home.
    private bool IsGameFocused =>
        Zone == HandheldZone.Grid && Settings is null && Panel is null && Naming is null && _move is null && !IsDrifting &&
        (OpenFolder is { } folder
            ? folder.SelectedTile is { IsSettings: false, IsActivity: false, IsApp: false }
            : IsLoaded && HomeSlotAt(HomeIndex) is { Kind: HandheldSlotKind.Game } slot && _pins.ContainsKey(slot.GameId));

    // ... and the Stage shows it.
    private bool IsGameOnStage => IsGameFocused && Stage.Kind == HandheldStageKind.Game;

    /// <summary>LB or RB on a game in a folder: the other view.</summary>
    private void SwitchGameView()
    {
        _modelViewChosen = !_modelViewChosen;
        // Not a move: the name comes back rising, as the mock's setWord with no direction.
        LastMoveDirection = null;
        LastFolderOpened = null;
        LastListOpened = null;
        ReleaseModelTurn();
        UpdateStage();
        UpdateHints();
    }

    // The model view is due on what the Stage shows now: a game (on home or in a folder), the model chosen, nothing over it. (A launch
    // from it keeps it: the medium bumps and the colour grows from it.)
    private bool ModelViewDue => HandheldTokens.IsBigType && _modelViewChosen && IsGameOnStage && Sheet is null && !IsMenuOpen;

    /// <summary>
    /// After the Stage was laid for the focus: whether it is in the model view. A game arriving in it got its lines already
    /// (<see cref="ApplyBigTypeGameText"/>); switching on the same game writes them here, once.
    /// </summary>
    private void ApplyModelView()
    {
        var showing = ModelViewDue;
        Stage.GameId = Stage.Kind == HandheldStageKind.Game ? _stageGameId : -1;
        Stage.IsModelView = showing;
        if (!showing)
            ReleaseModelTurn();
        if (_stageGame is not { } game || game.Id != _stageGameId || Stage.Kind != HandheldStageKind.Game)
            return;
        if (showing && !_modelTexts)
            ApplyModelTexts(game);
        else if (!showing && _modelTexts)
            ApplyBigTypeGameText(game);
    }

    /// <summary>The proposal's model view: the line is the game's name, the second line its play time, achievements and emulator.</summary>
    private void ApplyModelTexts(HandheldGameEntry game)
    {
        _modelTexts = true;
        BtLine = game.Title;
        BtFacts = ModelFacts(game);
    }

    /// <summary>The model view's second line: what the name view's line says (or that the file is missing), then the emulator.</summary>
    private string ModelFacts(HandheldGameEntry game)
    {
        if (!game.IsAvailable)
            return "File not found";
        var played = game.Playtime.TotalMinutes >= 1 ? HandheldText.Duration(game.Playtime)
            : game.PlayCount == 0 ? "Not played yet" : "Played";
        var line = string.IsNullOrEmpty(game.AchievementText) ? played : $"{played} · {game.AchievementText} achievements";
        return _emulatorNames.TryGetValue(game.SystemId, out var emulator) && !string.IsNullOrEmpty(emulator) ? $"{line} · {emulator}" : line;
    }

    /// <summary>The second line for the game on the Stage, in whichever view it is.</summary>
    private string StageFactsFor(HandheldGameEntry game) => _modelTexts ? ModelFacts(game) : GameFacts(game);

    /// <summary>
    /// The hints over a focused game (Big Type): the model view's own, or the name view's with RB for the model. On home B does nothing,
    /// so it has no hint there.
    /// </summary>
    private bool AddGameViewHints()
    {
        if (!CanSwitchGameView)
            return false;
        var back = OpenFolder is not null;
        // From the focus, not from the Stage: a held direction lets the Stage catch up after the hints.
        if (_modelViewChosen)
        {
            // The proposal's: no Y here (the sheet still opens on Y, and the Stage goes back to the name under it).
            _nextHints.Add(new HandheldHint("A", "Play"));
            if (back)
                _nextHints.Add(new HandheldHint("B", "Back"));
            if (FocusedGame?.ViewModel?.ShelfMediaShell is not null && !Stage.IsMediumUnavailable)
                _nextHints.Add(new HandheldHint("RS", "Turn"));
            _nextHints.Add(new HandheldHint("RB", "Name"));
            return true;
        }
        _nextHints.Add(new HandheldHint("A", "Play"));
        _nextHints.Add(new HandheldHint("Y", "More"));
        if (back)
            _nextHints.Add(new HandheldHint("B", "Back"));
        _nextHints.Add(new HandheldHint("RB", "Model"));
        return true;
    }

    // The game with the focus: a folder's, or the one pinned to home.
    private HandheldGameEntry? FocusedGame => OpenFolder is { } folder ? folder.SelectedTile?.Entry
        : HomeSlotAt(HomeIndex) is { Kind: HandheldSlotKind.Game } slot && _pins.TryGetValue(slot.GameId, out var pinned) ? pinned : null;

    /// <summary>The right stick on the model view's medium: it takes hold of the pose on screen and turns it; let go, it swings back.</summary>
    private bool ApplyModelStick(float x, float y, double deltaMilliseconds)
    {
        if (!Stage.HasModelMedium || IsMenuOpen || TitlePage is not null || Sheet is not null)
            return false;
        if (MathF.Sqrt((x * x) + (y * y)) <= MediaRotationModel.Deadzone)
        {
            ReleaseModelTurn();
            return true;
        }
        var turn = _modelTurn ??= new MediaRotationModel { IdleSwayEnabled = false };
        if (turn.Update(x, y, deltaMilliseconds))
        {
            Stage.SetModelHold(BigTypeModelMotion.CssPitch(turn.Pitch - MediaRotationModel.RestPitch),
                BigTypeModelMotion.CssYaw(turn.Yaw - MediaRotationModel.RestYaw), held: true);
        }
        return true;
    }

    /// <summary>R3, or the stick let go: the model view's medium swings back to rest.</summary>
    private bool ReleaseModelTurn()
    {
        if (_modelTurn is null)
            return Stage.IsModelView;
        _modelTurn = null;
        Stage.SetModelHold(0f, 0f, held: false);
        return true;
    }
}
