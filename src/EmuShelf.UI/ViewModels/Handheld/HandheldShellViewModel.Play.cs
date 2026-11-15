using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.App.Services;
using EmuShelf.Core.Handheld;
using EmuShelf.Core.Launching;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// A game launching or running under the Big Type look (M47 B8, the mock's <c>launch</c> and <c>S.play</c>): what the Stage's
/// takeover shows (the title in the console's colour, the status line) and what the Launcher floods to. It lives from the press that
/// launched until the companion's parts have left after the game ended (or the launch failed).
/// </summary>
public sealed partial class HandheldPlayViewModel : ObservableObject
{
    public HandheldPlayViewModel(HandheldGameEntry entry, Color colour, string? emulator, int? disc)
    {
        Entry = entry;
        Colour = colour;
        ColourBrush = HandheldTokens.GroundBrush(colour);
        InGameColour = HandheldTokens.InGameGround(colour);
        InGameBrush = HandheldTokens.GroundBrush(InGameColour);
        Emulator = emulator;
        Disc = disc;
        Started = Stopwatch.GetTimestamp();
        Status = StartingText;
    }

    public HandheldGameEntry Entry { get; }

    public string Title => Entry.Title;

    /// <summary>The console's colour: the Stage's takeover.</summary>
    public Color Colour { get; }

    public IBrush ColourBrush { get; }

    /// <summary>The Launcher (and the companion) while the game runs: <see cref="Colour"/> mixed toward the dark (<c>InGameMix</c>).</summary>
    public Color InGameColour { get; }

    public IBrush InGameBrush { get; }

    public string? Emulator { get; }

    /// <summary>The disc that plays, when the game has several.</summary>
    public int? Disc { get; }

    /// <summary>When the press that launched landed (a <see cref="Stopwatch"/> timestamp): every launch motion is timed from it.</summary>
    public long Started { get; }

    /// <summary>The Stage's status line under the title: starting, syncing saves, what is running, an unlock.</summary>
    [ObservableProperty]
    public partial string Status { get; set; }

    /// <summary>The takeover has started: the Launcher's ground is the in-game colour and its grid has gone.</summary>
    [ObservableProperty]
    public partial bool IsTakenOver { get; set; }

    /// <summary>The emulator started (the launch went through).</summary>
    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    /// <summary>The game ended (or the launch failed): the companion's parts leave and the Stage's colour fades.</summary>
    [ObservableProperty]
    public partial bool IsEnding { get; set; }

    /// <summary>When it began to end (a <see cref="Stopwatch"/> timestamp).</summary>
    public long EndingSince { get; set; }

    public string StartingText => Emulator is { Length: > 0 } name ? $"Starting {name}…" : "Starting…";

    /// <summary>The mock's <c>run</c>: "Disc 2  ·  Running in PCSX2".</summary>
    public string RunningText => (Disc is { } disc ? $"Disc {disc}  ·  " : string.Empty) +
                                 (Emulator is { Length: > 0 } name ? $"Running in {name}" : "Running");

    /// <summary>What the line says while nothing else is: syncing, starting, running.</summary>
    internal string RestingStatus(bool syncing) => syncing ? SyncingText : IsRunning ? RunningText : StartingText;

    public const string SyncingText = "Syncing saves…";
}

public partial class HandheldShellViewModel
{
    /// <summary>A game launching or running under Big Type (M47 B8); null otherwise, and always null in today's look.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaying))]
    public partial HandheldPlayViewModel? Playing { get; private set; }

    public bool IsPlaying => Playing is not null;

    /// <summary>The Title page shows (today's look); under Big Type the page is the launch's state only and never drawn (M47 B8).</summary>
    public bool ShowsTitlePage => TitlePage is not null && !HandheldTokens.IsBigType;

    /// <summary>
    /// The ground the shelf came back to after a game: its flood grows from the middle of the screen, not the focus (the mock's
    /// <c>endPlay</c>); null once the ground has changed for anything else.
    /// </summary>
    public Color? MiddleFloodGround { get; private set; }

    /// <summary>The play's game ended: the companion's parts leave, then <see cref="Playing"/> goes and home comes back.</summary>
    public event Action<HandheldPlayViewModel>? PlayEnding;

    private bool _launchSyncing;
    private bool _returningFromPlay;
    private SecondScreenViewModel? _playModel;
    private bool _playSawGame;
    private int _playUnlocked;
    private DispatcherTimer? _unlockTimer;

    // ---- A on a game ----

    /// <summary>A on a game: today's look opens its Title page; Big Type plays it (decision "Handheld: A on a game plays").</summary>
    private void OpenOrPlay(HandheldGameEntry entry)
    {
        if (!HandheldTokens.IsBigType)
        {
            OpenTitle(entry);
            return;
        }
        if (!entry.IsAvailable)
        {
            // The mock's startGame: a missing file does not launch; the Stage's line says so, shaking (its toast with error).
            Bumped?.Invoke(HandheldDirection.Up);
            ShowLineError(MissingFileText);
            return;
        }
        OpenTitle(entry, playWhenReady: true);
    }

    /// <summary>The line a missing game's A shows. The app has no Locate yet (the mock's "Y to locate it"); Y holds its other rows.</summary>
    public const string MissingFileText = "Could not find the file  ·  Y for more";

    // ---- The screen chooser as a sheet ----

    /// <summary>
    /// A console set to ask which screen (M47 B8, the mock's <c>startGame</c>): a sheet in the lists' style, "Which screen?", over the game
    /// the press was on. B closes it, and the launch with it.
    /// </summary>
    private void ShowScreenSheet(HandheldTitlePageViewModel page)
    {
        var system = page.PlatformName;
        async void Play(GameLaunchScreen screen, bool always)
        {
            if (!ReferenceEquals(TitlePage, page))
                return;
            if (always)
                RememberScreen(page, screen);
            // The mock's sheetA: the sheet's rows leave (closeSheet), then the launch begins on both screens.
            page.State = HandheldTitleState.Launching;
            if (!HoldsStill && LaunchLead > TimeSpan.Zero)
                await Task.Delay(TimeSpan.FromMilliseconds(BigTypeListMotion.SheetOut.HoldMilliseconds), _clock);
            if (ReferenceEquals(TitlePage, page))
                _ = LaunchFromTitleAsync(page, screen);
        }
        ShowSheet(new HandheldSheetViewModel("Which screen?", page.Entry.Title,
        [
            new HandheldSheetItemViewModel("Top screen", "The game fills the big screen", () => Play(GameLaunchScreen.BuiltIn, false)),
            new HandheldSheetItemViewModel("Bottom screen", "The game runs between the controls", () => Play(GameLaunchScreen.External, false)),
            new HandheldSheetItemViewModel($"Always the top for {system}", "Stops asking for this console", () => Play(GameLaunchScreen.BuiltIn, true)),
            // Not the mock's: the chooser today can remember either screen, so this row keeps that (listed in the decision).
            new HandheldSheetItemViewModel($"Always the bottom for {system}", "Stops asking for this console", () => Play(GameLaunchScreen.External, true)),
        ])
        {
            Kicker = "Play",
            Thumbnail = CoverFor(page.Entry.Id),
            Cancelled = () =>
            {
                if (ReferenceEquals(TitlePage, page))
                    CloseTitle();
            },
        });
    }

    private void RememberScreen(HandheldTitlePageViewModel page, GameLaunchScreen screen)
    {
        _library.RememberLaunchScreen(page.Entry.SystemId, screen);
        if (page.Details is { } details)
        {
            page.Details = details with
            {
                Screen = screen == GameLaunchScreen.External ? LaunchScreenDecision.External : LaunchScreenDecision.BuiltIn,
            };
        }
    }

    // ---- The disc as a row of the Y sheet ----

    /// <summary>
    /// The disc row (the mock's <c>gameRows</c> cycle): "Disc", its value "2 of 3" as its note (the Stage's line while focused); A takes the
    /// next disc, saved as the disc the game starts on, and the sheet stays.
    /// </summary>
    private HandheldSheetItemViewModel DiscRow(HandheldGameEntry game, HandheldDiscs discs)
    {
        var numbers = discs.Numbers;
        var selected = discs.Selected;
        HandheldSheetItemViewModel? row = null;
        row = new HandheldSheetItemViewModel("Disc", DiscValue(selected, numbers.Count), () =>
        {
            var at = numbers.ToList().IndexOf(selected);
            selected = numbers[(at + 1) % numbers.Count];
            row!.Note = DiscValue(selected, numbers.Count);
            _ = SaveDiscAsync(game, selected);
        })
        {
            KeepsOpen = true,
        };
        return row;
    }

    private static string DiscValue(int disc, int count) => $"{disc} of {count}";

    private async Task SaveDiscAsync(HandheldGameEntry game, int disc)
    {
        try
        {
            if (!await _library.SelectDiscAsync(game, disc))
                ShowLineError("Could not change the disc");
        }
        catch (Exception)
        {
            ShowLineError("Could not change the disc");
        }
    }

    // ---- The launch ----

    /// <summary>The emulator starts once the mock has built the companion (<c>Launch.companionAtMs</c>), under Big Type.</summary>
    private TimeSpan BigTypeLaunchLead => LaunchLead > TimeSpan.Zero && !HoldsStill
        ? TimeSpan.FromMilliseconds(BigTypeLaunchMotion.CompanionAt)
        : LaunchLead;

    /// <summary>The press launched (Big Type): the Stage's takeover and the Launcher's flight begin; the colour floods at the takeover.</summary>
    private void BeginPlay(HandheldTitlePageViewModel page)
    {
        EndPlayNow();
        var colour = GameGround(page.Entry) ?? LauncherGround;
        var disc = page.Details is { IsMultiDisc: true } details ? details.SelectedDiscNumber : (int?)null;
        var play = new HandheldPlayViewModel(page.Entry, colour, page.Details?.EmulatorName, disc);
        _launchSyncing = false;
        Playing = play;
        if (CompanionScreen is { } companion)
        {
            companion.Leave(false);
            companion.InGameBrush = play.InGameBrush;
            companion.HoldsStill = HoldsStill;
        }
        WatchPlayModel(Companion);
        UpdateHints();
        _ = TakeOverAsync(play);
    }

    private async Task TakeOverAsync(HandheldPlayViewModel play)
    {
        if (!HoldsStill)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(BigTypeLaunchMotion.TakeoverAt), _clock);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
        if (!ReferenceEquals(Playing, play) || play.IsEnding)
            return;
        play.IsTakenOver = true;
        MiddleFloodGround = null;
        UpdateGround();
    }

    /// <summary>The save sync before a launch started or finished: the status line says so (Big Type).</summary>
    private void PlaySyncing(bool syncing)
    {
        _launchSyncing = syncing;
        if (Playing is { IsEnding: false } play && _unlockTimer?.IsEnabled != true)
            play.Status = play.RestingStatus(syncing);
    }

    /// <summary>The launch went through or did not.</summary>
    private void PlayLaunched(bool started)
    {
        if (Playing is not { IsEnding: false } play)
            return;
        if (!started)
        {
            _ = EndPlayAsync();
            return;
        }
        play.IsRunning = true;
        _launchSyncing = false;
        play.Status = play.RunningText;
    }

    /// <summary>
    /// A press while a game launched or runs, with the companion not owning the pad: the launch in progress swallows it; after it, with
    /// no game reported running (no companion, or it never saw the game), the press brings the shelf back, as the mock's B does.
    /// </summary>
    private bool DispatchPlaying(HandheldPlayViewModel play, GamepadAction action)
    {
        if (play.IsEnding || TitlePage is { State: HandheldTitleState.Launching })
            return true;
        if (!play.IsRunning)
            return false;
        _ = EndPlayAsync();
        return true;
    }

    // The companion's model says when the game runs and when it ends, and when an achievement unlocks.
    private void WatchPlayModel(SecondScreenViewModel? model)
    {
        if (_playModel is { } old)
            old.PropertyChanged -= OnPlayModelChanged;
        _playModel = model;
        _playSawGame = model?.IsGameRunning == true;
        _playUnlocked = model?.UnlockedAchievementCount ?? 0;
        if (model is not null)
            model.PropertyChanged += OnPlayModelChanged;
    }

    private void OnPlayModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SecondScreenViewModel model || Playing is not { IsEnding: false } play)
            return;
        switch (e.PropertyName)
        {
            case nameof(SecondScreenViewModel.IsGameRunning) when model.IsGameRunning:
                _playSawGame = true;
                _playUnlocked = model.UnlockedAchievementCount;
                break;
            case nameof(SecondScreenViewModel.IsGameRunning) when _playSawGame && play.IsRunning:
                _ = EndPlayAsync();
                break;
            case nameof(SecondScreenViewModel.UnlockedAchievementCount) when model.IsGameRunning:
                var unlocked = model.UnlockedAchievementCount;
                if (unlocked > _playUnlocked)
                    ShowUnlock(play, unlocked, model.AchievementCount);
                _playUnlocked = unlocked;
                break;
        }
    }

    /// <summary>An achievement unlocked while playing: the Stage's line says so for <c>UnlockLine.holdMs</c>, then what runs again.</summary>
    private void ShowUnlock(HandheldPlayViewModel play, int unlocked, int total)
    {
        play.Status = $"Achievement unlocked  ·  {unlocked} / {total}";
        if (_disposed)
            return;
        if (_unlockTimer is null)
        {
            _unlockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(BigTypeLaunchMotion.UnlockLineHoldMilliseconds) };
            _unlockTimer.Tick += (_, _) =>
            {
                _unlockTimer?.Stop();
                if (Playing is { IsEnding: false } current)
                    current.Status = current.RestingStatus(_launchSyncing);
            };
        }
        _unlockTimer.Stop();
        _unlockTimer.Start();
    }

    /// <summary>
    /// The game ended (the mock's <c>endPlay</c>): the companion's parts leave and the Stage's colour fades; <c>InGameOut.holdMs</c>
    /// later the ground floods back from the middle, home's tiles pop in and the ring comes back.
    /// </summary>
    public async Task EndPlayAsync()
    {
        if (Playing is not { IsEnding: false } play)
            return;
        play.EndingSince = Stopwatch.GetTimestamp();
        play.IsEnding = true;
        _unlockTimer?.Stop();
        CompanionScreen?.Leave(true);
        PlayEnding?.Invoke(play);
        if (!HoldsStill)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(BigTypeLaunchMotion.ReturnHoldMilliseconds), _clock);
            }
            catch (TaskCanceledException)
            {
            }
        }
        if (ReferenceEquals(Playing, play))
            FinishPlay();
    }

    private void FinishPlay()
    {
        CompanionScreen?.Leave(false);
        WatchPlayModel(null);
        // The ground the shelf comes back to floods from the middle (the mock's endPlay: flood(…, 310, 270)).
        _returningFromPlay = true;
        Playing = null;
        UpdateFocus();
        _returningFromPlay = false;
    }

    /// <summary>Any play left over goes at once (a new launch, Handheld mode closing).</summary>
    private void EndPlayNow()
    {
        if (Playing is null)
            return;
        _unlockTimer?.Stop();
        FinishPlay();
    }

    // ---- A message on the Stage's line (the mock's toast) ----

    private string? _lineMessage;
    private DispatcherTimer? _lineTimer;

    /// <summary>Counts the line's errors: the Stage shakes the line each time it changes (tokens.js <c>Message.shake</c>).</summary>
    [ObservableProperty]
    public partial int LineShakes { get; private set; }

    /// <summary>The message the Stage's line holds in place of its own text, while it lasts.</summary>
    public string? LineMessage => _lineMessage;

    /// <summary>An error on the Stage's line (the mock's <c>toast(text, {error: true})</c>): it shakes, holds <c>errorHoldMs</c>, then the line comes back.</summary>
    public void ShowLineError(string text)
    {
        _lineMessage = text;
        LineShakes++;
        Stage.Line = text;
        if (_disposed)
            return;
        if (_lineTimer is null)
        {
            _lineTimer = new DispatcherTimer();
            _lineTimer.Tick += (_, _) =>
            {
                _lineTimer?.Stop();
                _lineMessage = null;
                UpdateStage();
            };
        }
        _lineTimer.Stop();
        _lineTimer.Interval = TimeSpan.FromMilliseconds(BigTypeLaunchMotion.ErrorHoldMilliseconds);
        _lineTimer.Start();
    }

    /// <summary>A press moves on: a message on the line gives way to what the new focus says.</summary>
    private void DropLineMessage()
    {
        if (_lineMessage is null)
            return;
        _lineTimer?.Stop();
        _lineMessage = null;
    }
}
