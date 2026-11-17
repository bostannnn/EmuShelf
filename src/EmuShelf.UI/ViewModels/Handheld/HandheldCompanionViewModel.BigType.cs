using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.App.Services;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.ViewModels;

/// <summary>One of the Photos tab's six (M47 B8): a photo of the game decoded at the tab's size, or a video's place.</summary>
public sealed partial class HandheldCompanionShotViewModel(int index, bool isVideo) : ObservableObject
{
    public int Index { get; } = index;
    public bool IsVideo { get; } = isVideo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    public partial Bitmap? Image { get; set; }

    public bool HasImage => Image is not null;
}

/// <summary>
/// The companion in the Big Type look (M47 B8, the mock's in-game screen): "Now playing", the title, three tabs (Session, Achievements,
/// Photos) moved between with left and right, the session's time counting each second, the totals, the achievements figure and bar, six
/// photos. A on Achievements opens every badge and A on Photos the photo viewer (today's layers, which the mock does not draw); down
/// reaches the dock (All apps and the pinned apps, today's). It rests after <c>InGameRest.afterMs</c> and the first press only wakes it.
/// </summary>
public sealed partial class HandheldCompanionViewModel
{
    private DispatcherTimer? _secondTimer;
    private CancellationTokenSource? _shotsLoad;

    /// <summary>The order of Big Type's tabs (the mock's).</summary>
    private static readonly HandheldCompanionTab[] BigTypeTabs =
        [HandheldCompanionTab.Session, HandheldCompanionTab.Achievements, HandheldCompanionTab.Photos];

    public bool IsSessionTab => Tab == HandheldCompanionTab.Session;

    /// <summary>The in-game colour (the console's mixed toward the dark, <c>InGameMix</c>): the companion's ground under Big Type.</summary>
    [ObservableProperty]
    public partial IBrush? InGameBrush { get; set; }

    /// <summary>The device asks for stillness (battery saver, reduced motion): the companion's parts are simply there (M47 B8).</summary>
    [ObservableProperty]
    public partial bool HoldsStill { get; set; }

    /// <summary>The game ended and the companion's parts are leaving (the shell's play is ending): it stays up until they have.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsBigTypeBody))]
    public partial bool IsLeaving { get; set; }

    /// <summary>When the parts began to leave (a <see cref="Stopwatch"/> timestamp).</summary>
    public long LeavingSince { get; private set; }

    /// <summary>When the companion's parts began to arrive (a <see cref="Stopwatch"/> timestamp; 0: none).</summary>
    [ObservableProperty]
    public partial long ArrivedSince { get; private set; }

    /// <summary>The direction the last tab change moved (1: right) and when (a <see cref="Stopwatch"/> timestamp).</summary>
    public int TabDirection { get; private set; }

    [ObservableProperty]
    public partial long TabChangedSince { get; private set; }

    /// <summary>Big Type's in-game body shows: a game runs, or its parts are leaving.</summary>
    public bool ShowsBigTypeBody => HandheldTokens.IsBigType && (IsGameRunning || IsLeaving);

    /// <summary>The session so far, "m:ss" (the mock's <c>mmss</c>), counting each second.</summary>
    [ObservableProperty]
    public partial string SessionTime { get; private set; } = "0:00";

    /// <summary>All the time played, the session included ("13 h 40 min").</summary>
    [ObservableProperty]
    public partial string TotalTime { get; private set; } = string.Empty;

    /// <summary>The title the mock's <c>.pt</c> shows (the now playing title).</summary>
    public string InGameTitle => NowPlayingTitle;

    /// <summary>The Photos tab's six.</summary>
    public ObservableCollection<HandheldCompanionShotViewModel> Shots { get; } = [];

    /// <summary>"6 photos from this game".</summary>
    public string ShotsText => Model.MediaItems.Count switch
    {
        0 => "No photos of this game yet",
        1 => "1 photo from this game",
        var n => string.Create(CultureInfo.CurrentCulture, $"{n} photos from this game"),
    };

    /// <summary>The Achievements tab's line under its figure.</summary>
    public string UnlockedText => Model.HasAchievements ? "unlocked" : AchievementsMessage;

    partial void OnNowPlayingTitleChanged(string value) => OnPropertyChanged(nameof(InGameTitle));

    private void BeginBigType()
    {
        if (!HandheldTokens.IsBigType)
            return;
        IsLeaving = false;
        ArrivedSince = Stopwatch.GetTimestamp();
        TabDirection = 0;
        TickSecond();
        _ = LoadShotsAsync();
        OnPropertyChanged(nameof(ShowsBigTypeBody));
    }

    private void EndBigType()
    {
        _secondTimer?.Stop();
        _shotsLoad?.Cancel();
        OnPropertyChanged(nameof(ShowsBigTypeBody));
    }

    /// <summary>The shell's play ended: the parts leave (true), or they have gone (false).</summary>
    public void Leave(bool leaving)
    {
        if (leaving)
            LeavingSince = Stopwatch.GetTimestamp();
        IsLeaving = leaving;
        if (!leaving)
        {
            ArrivedSince = 0;
            foreach (var shot in Shots)
                shot.Image?.Dispose();
            Shots.Clear();
        }
    }

    // The session's seconds (the mock's setInterval of 1 s), and the total with it.
    private void TickSecond()
    {
        if (_sessionStartedAt is not { } started || !HandheldTokens.IsBigType)
            return;
        var session = _clock.GetUtcNow() - started;
        if (session < TimeSpan.Zero)
            session = TimeSpan.Zero;
        var seconds = (int)session.TotalSeconds;
        SessionTime = string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
        TotalTime = HandheldText.Duration(_playtimeBefore + session);
        if (_disposed || !IsGameRunning)
            return;
        if (_secondTimer is null)
        {
            _secondTimer = new DispatcherTimer();
            _secondTimer.Tick += (_, _) => TickSecond();
        }
        var into = session.Ticks % TimeSpan.TicksPerSecond;
        _secondTimer.Stop();
        _secondTimer.Interval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond - into) + TimeSpan.FromMilliseconds(5);
        _secondTimer.Start();
    }

    private void DispatchBigTypeBody(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.NavigateUp when Zone == HandheldCompanionZone.Dock:
                Zone = HandheldCompanionZone.Panel;
                break;
            case GamepadAction.NavigateDown when Zone == HandheldCompanionZone.Panel:
                Zone = HandheldCompanionZone.Dock;
                break;
            case GamepadAction.NavigateLeft when Zone == HandheldCompanionZone.Dock:
                DockIndex = Math.Max(0, DockIndex - 1);
                break;
            case GamepadAction.NavigateRight when Zone == HandheldCompanionZone.Dock:
                DockIndex = Math.Min(DockItems.Count - 1, DockIndex + 1);
                break;
            // The mock's playTab: left and right (and the shoulders) move between the tabs.
            case GamepadAction.NavigateLeft or GamepadAction.PreviousPlatform:
                StepTab(-1);
                break;
            case GamepadAction.NavigateRight or GamepadAction.NextPlatform:
                StepTab(1);
                break;
            case GamepadAction.Confirm when Zone == HandheldCompanionZone.Panel:
                OpenPanel();
                break;
            case GamepadAction.Confirm:
                ActivateDockItem(DockItems[DockIndex]);
                break;
            case GamepadAction.Actions when Zone == HandheldCompanionZone.Dock && DockItems[DockIndex].Slot is { } slot:
                Model.EditSlotCommand.Execute(slot.Index);
                break;
            case GamepadAction.Actions when Zone == HandheldCompanionZone.Panel && Tab == HandheldCompanionTab.Photos &&
                                            !HasPhotos && Model.CanFetchMedia && !Model.IsFetchingMedia:
                Model.FetchMediaCommand.Execute(null);
                break;
        }
        UpdateFocus();
    }

    /// <summary>The next tab along (the mock's <c>playTab</c>); past either end nothing changes.</summary>
    public bool StepTab(int direction)
    {
        var at = Array.IndexOf(BigTypeTabs, Tab);
        var next = at + Math.Sign(direction);
        if (at < 0 || next < 0 || next >= BigTypeTabs.Length)
            return false;
        Zone = HandheldCompanionZone.Panel;
        TabDirection = Math.Sign(direction);
        TabChangedSince = Stopwatch.GetTimestamp();
        Tab = BigTypeTabs[next];
        if (Tab == HandheldCompanionTab.Photos)
        {
            _ = LoadPanelPhotoAsync();
            _ = LoadShotsAsync();
        }
        return true;
    }

    /// <summary>The Photos tab's six, decoded at their size off the UI thread (only while a game runs).</summary>
    private async Task LoadShotsAsync()
    {
        if (!HandheldTokens.IsBigType)
            return;
        _shotsLoad?.Cancel();
        _shotsLoad?.Dispose();
        var load = _shotsLoad = new CancellationTokenSource();
        foreach (var old in Shots)
            old.Image?.Dispose();
        Shots.Clear();
        OnPropertyChanged(nameof(ShotsText));
        var items = Model.MediaItems;
        if (!IsGameRunning || items.Count == 0)
            return;
        for (var i = 0; i < Math.Min(items.Count, HandheldTokens.InGameShots); i++)
            Shots.Add(new HandheldCompanionShotViewModel(i, items[i].IsVideo));
        foreach (var shot in Shots.ToArray())
        {
            var item = items[shot.Index];
            if (item.IsVideo)
                continue;
            try
            {
                var path = Model.ResolveMediaPath is { } resolve ? await resolve(item, load.Token) : item.LocalPath;
                if (load.IsCancellationRequested || path is null)
                    return;
                var bitmap = await Task.Run(() => SafeImageDecoder.DecodeToFit(path, (int)HandheldTokens.InGameShotWidth,
                    (int)HandheldTokens.InGameShotHeight), load.Token);
                if (load.IsCancellationRequested || _disposed || !Shots.Contains(shot))
                {
                    bitmap.Dispose();
                    return;
                }
                shot.Image = bitmap;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // An unreadable photo leaves its place empty.
            }
        }
    }
}
