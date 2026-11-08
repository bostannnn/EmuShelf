using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using EmuShelf.App.Services;
using EmuShelf.Core.Handheld;
using EmuShelf.Core.Launching;

namespace EmuShelf.App.ViewModels;

// The Title page (M45 H2): A on a game opens it, Play is the second press. The disc chooser, the
// launch-screen chooser, the actions sheet and the pre-launch save sync are states of the page, so a launch
// never starts before every question is answered. Play runs the app's one launch core through the library.
public sealed partial class HandheldShellViewModel
{
    /// <summary>Opens a game's Title page over the grid it was picked from.</summary>
    public void OpenTitle(HandheldGameEntry entry)
    {
        CloseTitle();
        var page = new HandheldTitlePageViewModel(entry, OpenFolder?.Name ?? "Home", entry.SystemName, _clock.GetLocalNow())
        {
            AccentBrush = _platforms.TryGetValue(entry.SystemId, out var platform) && Color.TryParse(platform.AccentColor, out var accent)
                ? new SolidColorBrush(accent)
                : Brushes.Gray,
        };
        BuildTitleButtons(page);
        TitlePage = page;
        UpdateFocus();

        var cancellation = new CancellationTokenSource();
        _titleLoad = cancellation;
        _ = LoadTitleAsync(page, cancellation.Token);
    }

    /// <summary>Closes the Title page (B, or a launch that started) back onto the grid it was opened from.</summary>
    public void CloseTitle()
    {
        if (TitlePage is not { } page)
            return;
        _titleLoad?.Cancel();
        _titleLoad?.Dispose();
        _titleLoad = null;
        TitlePage = null;
        if (page.Cover is { } cover)
        {
            page.Cover = null;
            cover.Dispose();
        }
        // A pin removed from home while its Title page was open: its entry goes now.
        if (_releaseWithTitle.Count > 0)
        {
            _library.ReleaseGames(_releaseWithTitle.ToList());
            _releaseWithTitle.Clear();
        }
        // The page's achievements live on the companion; leaving the page leaves them too.
        UpdateFocus();
    }

    /// <summary>
    /// The app's pre-launch save sync started or finished. While it runs the Title page shows the blocking
    /// sync panel (Handheld mode has no Gamepad overlay to show it in).
    /// </summary>
    public void SetLaunchSaveSync(bool blocking)
    {
        if (TitlePage is { } page)
            page.IsSyncingSaves = blocking;
    }

    private async Task LoadTitleAsync(HandheldTitlePageViewModel page, CancellationToken token)
    {
        HandheldTitleDetails details;
        try
        {
            details = await _library.GetTitleDetailsAsync(page.Entry, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            // The page still works without them: one disc, the built-in screen.
            details = HandheldTitleDetails.Default;
        }
        if (token.IsCancellationRequested || !ReferenceEquals(TitlePage, page))
            return;
        page.Details = details;
        BuildTitleButtons(page);
        UpdateHints();

        try
        {
            var cover = await _library.LoadCoverAsync(page.Entry, TitleCoverDecodeWidth, fullSize: true, token);
            if (token.IsCancellationRequested || !ReferenceEquals(TitlePage, page))
            {
                cover?.Dispose();
                return;
            }
            page.Cover = cover;
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Play, then Disc for a multi-disc game, then Achievements when the game has a set and a companion can show it.</summary>
    private void BuildTitleButtons(HandheldTitlePageViewModel page)
    {
        var focusedKind = page.FocusedButton?.Kind ?? HandheldTitleButtonKind.Play;
        page.Buttons.Clear();
        page.Buttons.Add(new HandheldTitleButtonViewModel(HandheldTitleButtonKind.Play, "Play", null));
        if (page.Details is { IsMultiDisc: true } details)
        {
            page.Buttons.Add(new HandheldTitleButtonViewModel(
                HandheldTitleButtonKind.Disc, $"Disc {details.SelectedDiscNumber}", $"of {details.DiscNumbers.Count}"));
        }
        if (CanShowAchievements(page))
        {
            page.Buttons.Add(new HandheldTitleButtonViewModel(
                HandheldTitleButtonKind.Achievements, "Achievements", page.Entry.AchievementText));
        }
        page.FocusButton(Math.Max(0, page.IndexOf(focusedKind)));
    }

    // The full badge grid is the companion's (on Screen-2); without one (a desktop head) there is nowhere to show it.
    private bool CanShowAchievements(HandheldTitlePageViewModel page) => page.HasAchievements && _companion is not null;

    private bool DispatchTitleAction(HandheldTitlePageViewModel page, GamepadAction action)
    {
        switch (page.State)
        {
            case HandheldTitleState.Launching:
                // Nothing interrupts a launch in progress; the panel says what it is waiting for.
                return true;
            case HandheldTitleState.Buttons:
                DispatchTitleButtons(page, action);
                return true;
            case HandheldTitleState.ScreenChooser:
                DispatchScreenChooser(page, action);
                return true;
            default:
                DispatchTitleOptions(page, action);
                return true;
        }
    }

    private void DispatchTitleButtons(HandheldTitlePageViewModel page, GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.NavigateLeft when page.ButtonIndex > 0:
                page.FocusButton(page.ButtonIndex - 1);
                UpdateHints();
                break;
            case GamepadAction.NavigateRight when page.ButtonIndex < page.Buttons.Count - 1:
                page.FocusButton(page.ButtonIndex + 1);
                UpdateHints();
                break;
            case GamepadAction.NavigateLeft:
            case GamepadAction.NavigateRight:
            case GamepadAction.NavigateUp:
            case GamepadAction.NavigateDown:
                Bumped?.Invoke(ToDirection(action));
                break;
            case GamepadAction.Confirm:
                ActivateTitleButton(page);
                break;
            case GamepadAction.Cancel:
                CloseTitle();
                break;
            case GamepadAction.Actions:
                OpenTitleActions(page);
                break;
            case GamepadAction.Menu:
                OpenMenu();
                break;
        }
    }

    private void ActivateTitleButton(HandheldTitlePageViewModel page)
    {
        switch (page.FocusedButton?.Kind)
        {
            case HandheldTitleButtonKind.Play:
                _ = PlayAsync(page);
                break;
            case HandheldTitleButtonKind.Disc:
                OpenDiscChooser(page);
                break;
            case HandheldTitleButtonKind.Achievements:
                OpenTitleAchievements();
                break;
        }
    }

    /// <summary>The companion's badge grid on Screen-2, for this game; B there comes back to the page.</summary>
    private void OpenTitleAchievements()
    {
        if (_companion is { } companion && companion.Overlay == SecondScreenOverlayKind.None)
            companion.ToggleAchievementsCommand.Execute(null);
    }

    /// <summary>
    /// Play: asks which screen when the system is set to Ask (and there is a second screen), else launches on
    /// the screen the planner resolved when the page opened.
    /// </summary>
    private Task PlayAsync(HandheldTitlePageViewModel page)
    {
        // Until the details land the page does not know whether to ask or where the platform is pinned;
        // a quick second A must not guess the built-in screen.
        if (page.Details is not { } details)
            return Task.CompletedTask;
        switch (details.Screen)
        {
            case LaunchScreenDecision.Prompt:
                OpenScreenChooser(page);
                return Task.CompletedTask;
            case LaunchScreenDecision.External:
                return LaunchFromTitleAsync(page, GameLaunchScreen.External);
            default:
                return LaunchFromTitleAsync(page, GameLaunchScreen.BuiltIn);
        }
    }

    private async Task LaunchFromTitleAsync(HandheldTitlePageViewModel page, GameLaunchScreen screen)
    {
        page.State = HandheldTitleState.Launching;
        UpdateHints();
        LaunchStarting?.Invoke(page);
        bool started;
        try
        {
            // The cover lifts off the Launcher and lands on the Stage before the emulator takes the screen.
            if (LaunchLead > TimeSpan.Zero)
                await Task.Delay(LaunchLead, _clock);
            started = ReferenceEquals(TitlePage, page) && await _library.LaunchAsync(page.Entry, screen);
        }
        catch (Exception)
        {
            // The launch core reports its own failures on the status line; this only keeps the page usable.
            started = false;
        }
        if (!ReferenceEquals(TitlePage, page))
            return;
        page.IsSyncingSaves = false;
        if (started)
        {
            // Back to the grid, as the 3DS returns to HOME: that is what is on screen when the game closes.
            // The play was recorded at start, so re-read the folder for its last-played facts.
            CloseTitle();
            _ = RefreshAfterPlayAsync();
            return;
        }
        page.State = HandheldTitleState.Buttons;
        UpdateHints();
    }

    // Recent first: an open Recent folder re-reads from the updated list, so the game just played leads it.
    private async Task RefreshAfterPlayAsync()
    {
        await RefreshRecentAsync();
        await RefreshOpenFolderAsync();
    }

    // ---- Screen chooser: two cards (top, bottom) and "Always use this screen" beneath them ----

    private void OpenScreenChooser(HandheldTitlePageViewModel page)
    {
        page.RememberScreen = false;
        page.ScreenIndex = HandheldTitlePageViewModel.ScreenTopIndex;
        page.State = HandheldTitleState.ScreenChooser;
        UpdateHints();
    }

    private int _screenReturnIndex;

    private void DispatchScreenChooser(HandheldTitlePageViewModel page, GamepadAction action)
    {
        const int top = HandheldTitlePageViewModel.ScreenTopIndex;
        const int bottom = HandheldTitlePageViewModel.ScreenBottomIndex;
        const int remember = HandheldTitlePageViewModel.ScreenRememberIndex;
        switch (action)
        {
            case GamepadAction.NavigateLeft when page.ScreenIndex == bottom:
                page.ScreenIndex = top;
                break;
            case GamepadAction.NavigateRight when page.ScreenIndex == top:
                page.ScreenIndex = bottom;
                break;
            case GamepadAction.NavigateDown when page.ScreenIndex != remember:
                _screenReturnIndex = page.ScreenIndex;
                page.ScreenIndex = remember;
                break;
            case GamepadAction.NavigateUp when page.ScreenIndex == remember:
                page.ScreenIndex = _screenReturnIndex;
                break;
            case GamepadAction.NavigateLeft:
            case GamepadAction.NavigateRight:
            case GamepadAction.NavigateUp:
            case GamepadAction.NavigateDown:
                Bumped?.Invoke(ToDirection(action));
                break;
            case GamepadAction.Confirm when page.ScreenIndex == remember:
                page.RememberScreen = !page.RememberScreen;
                break;
            case GamepadAction.Confirm:
                var screen = page.ScreenIndex == bottom ? GameLaunchScreen.External : GameLaunchScreen.BuiltIn;
                if (page.RememberScreen)
                {
                    _library.RememberLaunchScreen(page.Entry.SystemId, screen);
                    // The page now says where it runs, and the next Play goes straight there.
                    if (page.Details is { } details)
                    {
                        page.Details = details with
                        {
                            Screen = screen == GameLaunchScreen.External ? LaunchScreenDecision.External : LaunchScreenDecision.BuiltIn,
                        };
                    }
                }
                _ = LaunchFromTitleAsync(page, screen);
                return;
            case GamepadAction.Cancel:
                ReturnToTitleButtons(page);
                return;
        }
        UpdateHints();
    }

    // ---- List states: the disc chooser and the actions sheet ----

    private void OpenDiscChooser(HandheldTitlePageViewModel page)
    {
        if (page.Details is not { IsMultiDisc: true } details)
            return;
        page.Options.Clear();
        foreach (var number in details.DiscNumbers)
        {
            var disc = number;
            page.Options.Add(new HandheldTitleOptionViewModel(
                $"Disc {disc}",
                disc == details.SelectedDiscNumber ? "Starts here" : null,
                () => _ = ChooseDiscAsync(page, disc)));
        }
        page.State = HandheldTitleState.DiscChooser;
        page.FocusOption(Math.Max(0, details.DiscNumbers.ToList().IndexOf(details.SelectedDiscNumber)));
        UpdateHints();
    }

    private async Task ChooseDiscAsync(HandheldTitlePageViewModel page, int disc)
    {
        ReturnToTitleButtons(page);
        if (page.Details is not { } details || disc == details.SelectedDiscNumber)
            return;
        bool saved;
        try
        {
            saved = await _library.SelectDiscAsync(page.Entry, disc);
        }
        catch (Exception)
        {
            saved = false;
        }
        if (!saved || !ReferenceEquals(TitlePage, page) || page.Details is not { } current)
            return;
        page.Details = current with { SelectedDiscNumber = disc };
        BuildTitleButtons(page);
    }

    /// <summary>
    /// Y: the actions sheet. It holds what the page itself can do; the editing and library actions
    /// (Edit title, Set cover, Scrape, Remove from library) join it with the restyled sheet in H6.
    /// </summary>
    private void OpenTitleActions(HandheldTitlePageViewModel page)
    {
        page.Options.Clear();
        page.Options.Add(new HandheldTitleOptionViewModel("Play", null, () =>
        {
            ReturnToTitleButtons(page);
            _ = PlayAsync(page);
        }));
        if (page.Details is { HasSecondScreen: true, IsDualScreenSystem: false })
        {
            page.Options.Add(new HandheldTitleOptionViewModel("Play on another screen…", "Ask which screen this time", () =>
            {
                ReturnToTitleButtons(page);
                OpenScreenChooser(page);
            }));
        }
        if (page.Details is { IsMultiDisc: true })
            page.Options.Add(new HandheldTitleOptionViewModel("Choose disc…", page.DiscText, () => OpenDiscChooser(page)));
        if (CanShowAchievements(page))
        {
            page.Options.Add(new HandheldTitleOptionViewModel("Achievements", page.Entry.AchievementText, () =>
            {
                ReturnToTitleButtons(page);
                OpenTitleAchievements();
            }));
        }
        // Where the game is (decision 12): these open the Launcher's sheet, or change home and say so.
        foreach (var place in PlaceItems(page.Entry, OpenFolder?.Source))
        {
            var row = place;
            page.Options.Add(new HandheldTitleOptionViewModel(row.Label, row.Note, () =>
            {
                ReturnToTitleButtons(page);
                row.Run();
            }));
        }
        page.Options.Add(new HandheldTitleOptionViewModel($"Back to {page.Crumb}", null, CloseTitle));
        page.State = HandheldTitleState.Actions;
        page.FocusOption(0);
        UpdateHints();
    }

    private void DispatchTitleOptions(HandheldTitlePageViewModel page, GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.NavigateUp when page.OptionIndex > 0:
                page.FocusOption(page.OptionIndex - 1);
                break;
            case GamepadAction.NavigateDown when page.OptionIndex < page.Options.Count - 1:
                page.FocusOption(page.OptionIndex + 1);
                break;
            case GamepadAction.NavigateUp:
            case GamepadAction.NavigateDown:
            case GamepadAction.NavigateLeft:
            case GamepadAction.NavigateRight:
                Bumped?.Invoke(ToDirection(action));
                break;
            case GamepadAction.Confirm when page.OptionIndex >= 0 && page.OptionIndex < page.Options.Count:
                page.Options[page.OptionIndex].Run();
                break;
            case GamepadAction.Cancel:
            case GamepadAction.Actions when page.State == HandheldTitleState.Actions:
                ReturnToTitleButtons(page);
                break;
        }
    }

    private void ReturnToTitleButtons(HandheldTitlePageViewModel page)
    {
        page.Options.Clear();
        page.State = HandheldTitleState.Buttons;
        UpdateHints();
    }

    /// <summary>The system Back button on the Title page: out of a chooser first, then off the page.</summary>
    private void BackOnTitle(HandheldTitlePageViewModel page)
    {
        switch (page.State)
        {
            case HandheldTitleState.Launching:
                return;
            case HandheldTitleState.Buttons:
                CloseTitle();
                return;
            default:
                ReturnToTitleButtons(page);
                return;
        }
    }

    private void AddTitleHints(HandheldTitlePageViewModel page)
    {
        switch (page.State)
        {
            case HandheldTitleState.Launching:
                return;
            case HandheldTitleState.Buttons:
                Hints.Add(new HandheldHint("A", page.FocusedButton?.Kind switch
                {
                    HandheldTitleButtonKind.Disc => "Choose disc",
                    HandheldTitleButtonKind.Achievements => "Open",
                    _ => "Play",
                }));
                Hints.Add(new HandheldHint("Y", "More"));
                Hints.Add(new HandheldHint("B", "Back"));
                return;
            case HandheldTitleState.ScreenChooser:
                Hints.Add(new HandheldHint("A", page.IsRememberFocused ? "Toggle" : "Play here"));
                Hints.Add(new HandheldHint("B", "Back"));
                return;
            default:
                Hints.Add(new HandheldHint("A", "Select"));
                Hints.Add(new HandheldHint("B", "Back"));
                return;
        }
    }

    private static HandheldDirection ToDirection(GamepadAction action) => action switch
    {
        GamepadAction.NavigateUp => HandheldDirection.Up,
        GamepadAction.NavigateDown => HandheldDirection.Down,
        GamepadAction.NavigateLeft => HandheldDirection.Left,
        _ => HandheldDirection.Right,
    };
}
