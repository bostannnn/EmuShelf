using System;
using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// What the shell adds for the companion's Big Type layers (M47 B15, proposals-screens.html, the owner's picks of 16 November 2026): All apps
/// opened from home's dock (no game running) names the focused app on the Stage, as option 2 draws it: "All apps" (or "Choose an app for
/// slot n", or what a pin did) over the app's name in big type, where it is in the dock under it, "7 of 18 · everything on this device" on the
/// second line and All apps' hints in the Stage's corner, each a touch button. The companion's layers also take the Launcher's colour and
/// the shell's say on holding still.
/// </summary>
public sealed partial class HandheldShellViewModel
{
    private HandheldCompanionViewModel? _watchedScreens;

    /// <summary>All apps is open over the Launcher (no game running) and the Stage names its focused app (Big Type only).</summary>
    public bool IsAllAppsOnStage => HandheldTokens.IsBigType && CompanionScreen is { IsDrawerLayer: true, IsGameRunning: false };

    partial void OnCompanionScreenChanged(HandheldCompanionViewModel? value)
    {
        if (_watchedScreens is { } old)
        {
            old.ShelfChanged -= OnShelfChanged;
            old.PropertyChanged -= OnScreensChanged;
        }
        _watchedScreens = value;
        if (value is null)
            return;
        value.ShelfChanged += OnShelfChanged;
        value.PropertyChanged += OnScreensChanged;
        value.BrowsingGround = LauncherGroundBrush;
        value.HoldsStill = HoldsStill;
    }

    partial void OnLauncherGroundBrushChanged(IBrush value)
    {
        if (CompanionScreen is { } companion)
            companion.BrowsingGround = value;
    }

    partial void OnHoldsStillChanged(bool value)
    {
        if (CompanionScreen is { } companion)
            companion.HoldsStill = value;
    }

    private void OnScreensChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HandheldCompanionViewModel.IsDrawerLayer) || !HandheldTokens.IsBigType || _disposed)
            return;
        // The Stage turns to the focused app as All apps opens (the name arrives from the hinge) and back to the dock as it closes.
        if (sender is HandheldCompanionViewModel { IsGameRunning: false } companion)
            LastListOpened = companion.IsDrawerLayer;
        UpdateStage();
        UpdateHints();
    }

    private void OnShelfChanged()
    {
        if (!IsAllAppsOnStage || _disposed)
            return;
        UpdateStage();
        UpdateHints();
    }

    /// <summary>The Stage while All apps is open from home's dock (Big Type); false when it is not.</summary>
    private bool ShowAllAppsStage()
    {
        if (!IsAllAppsOnStage || CompanionScreen is not { } companion)
            return false;
        ShowStage(HandheldStageKind.AllApps, companion.ShelfName, companion.ShelfPlace);
        BtLabel = companion.ShelfKicker;
        BtName = companion.ShelfName;
        BtLine = companion.DrawerNotice is { Length: > 0 } notice ? notice : companion.ShelfPlace;
        BtFacts = companion.ShelfFacts;
        return true;
    }

    /// <summary>All apps' hints on the Stage while it is open from home's dock; false when it is not.</summary>
    private bool AddAllAppsHints()
    {
        if (!IsAllAppsOnStage || CompanionScreen is not { } companion)
            return false;
        _nextHints.AddRange(companion.DrawerHints);
        return true;
    }

    /// <summary>A tap on one of the Stage's hints: All apps' while it names the focused app there (every hint is also a touch button).</summary>
    public void TapStageHint(HandheldHint hint)
    {
        if (IsAllAppsOnStage && CompanionScreen is { } companion)
            companion.TapHint(hint);
    }

    // ---- 12 Porthole: the idle artwork ----

    /// <summary>
    /// The drift's step under Big Type (proposals-screens.html option 12): the game's console's backdrop (its circle growing from behind the
    /// window), "Recently played" over its name in big type (no logo picture: the owner was told and picked it), its play time and
    /// achievements on the line, its console and when it was last played on the second line, no hints, and its artwork in the round window.
    /// A game with no artwork is shown as when browsing: its name over its console's backdrop, no window.
    /// </summary>
    private void ShowPortholeStage(DriftFrame frame)
    {
        var game = frame.Game;
        _platforms.TryGetValue(game.SystemId, out var platform);
        ShowStage(HandheldStageKind.Hero, game.Title, HeroSubtitle(game), backdrop: BackdropFor(platform));
        _stageGameId = game.Id;
        Stage.IsDrift = true;
        var played = game.Playtime.TotalMinutes >= 1 ? HandheldText.Duration(game.Playtime)
            : game.PlayCount == 0 ? "Not played yet" : "Played";
        BtLabel = "Recently played";
        BtName = game.Title;
        BtLine = string.IsNullOrEmpty(game.AchievementText) ? played : $"{played}  ·  {game.AchievementText} achievements";
        var when = HandheldText.LastPlayed(game.LastPlayedAt, _clock.GetLocalNow());
        if (when.Length > 0 && !char.IsDigit(when[0]))
            when = char.ToLowerInvariant(when[0]) + when[1..];
        BtFacts = game.LastPlayedAt is null ? game.SystemName : $"{game.SystemName}  ·  last played {when}";
        // The logo and the cover today's hero would use are not drawn here: let them go now.
        frame.Logo?.Dispose();
        frame.Cover?.Dispose();
        // A frame shown again after its artwork was let go (the drift resumed on it) shows no window rather than a disposed picture.
        SetPortholeArt(frame.Fanart is { } fanart && !_releasedPortholeArt.TryGetValue(fanart, out _) ? fanart : null);
    }

    // Artwork the drift's window has let go of (disposed): never handed out again.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Bitmap, object> _releasedPortholeArt = [];

    /// <summary>Anything but the drift on the Stage: the window drops back through the hinge.</summary>
    private void SyncPorthole()
    {
        if (!Stage.IsDrift || !HandheldTokens.IsBigType)
            SetPortholeArt(null);
    }

    private void SetPortholeArt(Bitmap? art)
    {
        var old = Stage.PortholeArt;
        if (ReferenceEquals(old, art))
            return;
        Stage.PortholeArt = art;
        // Each Stage took its own copy as the property changed (the Avalonia window its picture, the native Stage its pixels): the
        // artwork goes at once.
        if (old is null)
            return;
        _releasedPortholeArt.AddOrUpdate(old, old);
        old.Dispose();
    }
}
