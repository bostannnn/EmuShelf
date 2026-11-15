using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EmuShelf.App.Services;

namespace EmuShelf.App.ViewModels;

// The idle drift (M45 H6d, decision 14): after fifteen seconds without a press or a touch, the Stage drifts through the
// recently played games, each as its Title page hero (fan art and logo, or the cover on its tint), on a slow crossfade.
// It goes once round and back to the newest, where it rests (the Stage then draws nothing, as it does after a minute of
// browsing, for the fan's sake). The first press or touch only wakes it: the Stage goes back to what is selected, and
// that press does nothing else, so nothing launches by accident.
public sealed partial class HandheldShellViewModel
{
    private DispatcherTimer? _idleTimer;
    private bool _idleEnabled;
    private IdleDrift? _drift;

    /// <summary>The Stage is showing the idle drift (a recent game's hero), not what is selected.</summary>
    public bool IsDrifting => _drift?.Shown is not null;

    /// <summary>One run of the drift: the recent games read for it, the frame on the Stage and the next one, decoded ahead.</summary>
    private sealed class IdleDrift
    {
        public IReadOnlyList<HandheldGameEntry> Games { get; set; } = [];
        public int ShownCount { get; set; }
        public DriftFrame? Shown { get; set; }
        public CancellationTokenSource Load { get; } = new();
    }

    /// <summary>What one drift step puts on the Stage, decoded before it is shown so the crossfade never waits on a decode.</summary>
    private sealed record DriftFrame(HandheldGameEntry Game, Bitmap? Fanart, Bitmap? Logo, Bitmap? Cover, Color? Tint);

    /// <summary>How many steps a drift over <paramref name="games"/> games takes: once round, then the newest again.</summary>
    internal static int DriftSteps(int games) => games <= 1 ? games : games + 1;

    private void EnableIdle()
    {
        _idleEnabled = true;
        ArmIdle();
    }

    private void DisableIdle()
    {
        _idleEnabled = false;
        _idleTimer?.Stop();
        StopDrift(refreshStage: false);
    }

    /// <summary>Any press or touch: the fifteen seconds start again.</summary>
    private void ArmIdle() => StartIdleTimer(HandheldMotion.IdleDriftDelay);

    private void StartIdleTimer(TimeSpan interval)
    {
        if (!_idleEnabled || _disposed)
            return;
        if (_idleTimer is null)
        {
            _idleTimer = new DispatcherTimer(DispatcherPriority.Background);
            _idleTimer.Tick += (_, _) =>
            {
                _idleTimer?.Stop();
                _ = OnIdleAsync();
            };
        }
        _idleTimer.Stop();
        _idleTimer.Interval = interval;
        _idleTimer.Start();
    }

    /// <summary>The idle timer ran out: the drift starts, or moves on to its next game. Tests call it instead of waiting.</summary>
    internal Task OnIdleAsync() => _drift is { } drift ? AdvanceDriftAsync(drift) : StartDriftAsync();

    /// <summary>
    /// Nothing open over the Launcher, no game running, home loaded, and the device not asking for stillness (battery saver,
    /// reduced motion: M45 H7c): the Stage may drift.
    /// </summary>
    private bool CanDrift => _idleEnabled && !_disposed && !HoldsStill && IsLoaded && !CompanionOwnsPad &&
        (_companion is null || _companion.Overlay == SecondScreenOverlayKind.None) &&
        Settings is null && Panel is null && !IsMenuOpen && Naming is null && Sheet is null && _move is null && TitlePage is null &&
        // M47 B12: the model view is the Stage showing itself (its showcase, then its own rest after a minute), not a Stage to drift.
        !Stage.IsModelView;

    private async Task StartDriftAsync()
    {
        // Not now (a game runs, a sheet is open) or nothing played yet: the next press arms it again, so no timer keeps
        // ticking meanwhile (a long-running DispatcherTimer costs on Android, 2026-11-07).
        if (!CanDrift || _recentIds.Count == 0)
            return;
        var drift = new IdleDrift();
        _drift = drift;
        IReadOnlyList<HandheldGameEntry> games;
        try
        {
            games = await _library.GetGamesByIdAsync(_recentIds, drift.Load.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!ReferenceEquals(_drift, drift))
        {
            _library.ReleaseGames(games);
            return;
        }
        drift.Games = games;
        if (games.Count == 0)
        {
            StopDrift(refreshStage: false);
            return;
        }
        await AdvanceDriftAsync(drift);
    }

    private async Task AdvanceDriftAsync(IdleDrift drift)
    {
        if (drift.ShownCount >= DriftSteps(drift.Games.Count))
            return; // at rest on the newest
        var game = drift.Games[drift.ShownCount % drift.Games.Count];
        DriftFrame frame;
        try
        {
            frame = await LoadDriftFrameAsync(game, drift.Load.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!ReferenceEquals(_drift, drift) || !CanDrift)
        {
            DisposeFrame(frame);
            if (ReferenceEquals(_drift, drift))
                StopDrift(refreshStage: true);
            return;
        }
        // The Stage takes the frame's bitmaps over (and disposes them when it moves on).
        drift.Shown = frame;
        drift.ShownCount++;
        UpdateStage();
        UpdateHints();
        if (drift.ShownCount < DriftSteps(drift.Games.Count))
            StartIdleTimer(HandheldMotion.IdleDriftHold);
    }

    // The hero as the Title page shows it: the fan art and logo, or, without fan art, the cover and the tint picked from it.
    private async Task<DriftFrame> LoadDriftFrameAsync(HandheldGameEntry game, CancellationToken token)
    {
        Bitmap? fanart = null, logo = null, cover = null;
        try
        {
            fanart = await LoadArtSafeAsync(game, HandheldArtKind.Fanart, token);
            if (fanart is not null)
            {
                logo = await LoadArtSafeAsync(game, HandheldArtKind.Logo, token);
                return new DriftFrame(game, fanart, logo, null, null);
            }
            cover = await _library.LoadCoverAsync(game, StageDecodeWidth, fullSize: true, token);
            var tint = cover is null ? null : await PickTintAsync(cover);
            token.ThrowIfCancellationRequested();
            return new DriftFrame(game, null, null, cover, tint);
        }
        catch (OperationCanceledException)
        {
            fanart?.Dispose();
            logo?.Dispose();
            cover?.Dispose();
            throw;
        }
    }

    private static void DisposeFrame(DriftFrame frame)
    {
        frame.Fanart?.Dispose();
        frame.Logo?.Dispose();
        frame.Cover?.Dispose();
    }

    /// <summary>Ends the drift; with <paramref name="refreshStage"/> the Stage goes back to what is selected.</summary>
    private void StopDrift(bool refreshStage)
    {
        if (_drift is not { } drift)
            return;
        _drift = null;
        drift.Load.Cancel();
        drift.Load.Dispose();
        var shown = drift.Shown is not null;
        if (refreshStage && shown)
        {
            UpdateStage();
            UpdateHints();
        }
        // The Stage has let go of the hero by now (or still shows it while the shell is torn down, which disposes it).
        _library.ReleaseGames(drift.Games);
    }

    /// <summary>
    /// A press arrived: the fifteen seconds start again, and a drift on the Stage wakes. True when the press only woke
    /// it and must do nothing else.
    /// </summary>
    private bool WakeOnInput()
    {
        // A new press: what the last one moved is forgotten until this one moves (M47 B6, the letters' entrance).
        LastMoveDirection = null;
        LastFolderOpened = null;
        LastListOpened = null;
        var woke = IsDrifting;
        StopDrift(refreshStage: true);
        ArmIdle();
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        InputRun = _lastInputAt != 0 && EmuShelf.Rendering.Stage.BigType.BigTypeMotion.InRun(
            (float)System.Diagnostics.Stopwatch.GetElapsedTime(_lastInputAt, now).TotalMilliseconds);
        _lastInputAt = now;
        _stageSettledSincePress = false;
        InputArrived?.Invoke();
        return woke;
    }

    private long _lastInputAt;
    private bool _stageSettledSincePress;

    /// <summary>
    /// The last press came within a run of the one before (a held direction, quick taps; M47 B11, tokens.js <c>LetterRun.withinMs</c>).
    /// </summary>
    public bool InputRun { get; private set; }

    /// <summary>
    /// What changes now was brought by a press within a run of the one before (M47 B11): under Big Type the Stage's name follows in its
    /// short form and texts swap rather than roll. Never once the Stage has settled after a held direction (today's once-per-move rule
    /// keeps the Stage waiting while a direction is held): the name the focus rests on lands as the mock's.
    /// </summary>
    public bool QuickChange => InputRun && !_stageSettledSincePress;

    /// <summary>
    /// A press or a touch arrived (M47 B5): the Big Type Launcher's resting motion (the ring's breath, the bob) wakes for another
    /// minute and its idle hop waits again. The drift is not input: it changes the Stage, never the Launcher.
    /// </summary>
    public event Action? InputArrived;

    /// <summary>
    /// A finger touched either screen (the views call this as the touch begins). True when it only woke the drift: the
    /// view then lets the whole touch go, as the first press is swallowed.
    /// </summary>
    public bool WakeOnTouch()
    {
        var woke = WakeOnInput();
        if (woke)
            PlaySound(EmuShelf.Core.Handheld.HandheldSound.Move);
        return woke;
    }

    /// <summary>The drift's frame on the Stage: the Title page's hero for a recent game, marked as the drift.</summary>
    private void ShowDriftStage(DriftFrame frame)
    {
        var game = frame.Game;
        if (Stage.Kind == HandheldStageKind.Hero && Stage.IsDrift && _stageGameId == game.Id)
            return;
        _platforms.TryGetValue(game.SystemId, out var platform);
        var backdrop = BackdropFor(platform);
        if (frame.Tint is { } tint && backdrop is not null)
            backdrop = backdrop with { Color = tint };
        var subtitle = frame.Fanart is not null
            ? HeroSubtitle(game)
            : $"Recently played · {HandheldText.LastPlayed(game.LastPlayedAt, _clock.GetLocalNow())}";
        ShowStage(HandheldStageKind.Hero, game.Title, subtitle, backdrop: backdrop);
        _stageGameId = game.Id;
        Stage.IsDrift = true;
        Stage.AchievementText = game.AchievementText;
        Stage.Fanart = frame.Fanart;
        Stage.Logo = frame.Logo;
        ReplaceStageArt(frame.Cover, borrowed: false);
    }
}
