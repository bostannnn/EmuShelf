using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Settings;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

public partial class HandheldShellViewModelTests
{
    private sealed class FakeLibrary : IHandheldLibrary
    {
        public List<HandheldPlatform> Platforms { get; } = [];
        public Dictionary<string, List<HandheldGameEntry>> Games { get; } = new();
        public List<long> CoverRequests { get; } = [];
        public List<long> PeekRequests { get; } = [];
        public List<long> Launched { get; } = [];
        public List<GameLaunchScreen> LaunchScreens { get; } = [];
        public List<(string SystemId, GameLaunchScreen Screen)> Remembered { get; } = [];
        public List<(long GameId, int Disc)> DiscSelections { get; } = [];
        public Dictionary<long, HandheldTitleDetails> Details { get; } = new();
        public List<InterfaceMode> ModeSwitches { get; } = [];

        /// <summary>What the next launch reports; a pending source holds the launch open (the Launching state).</summary>
        public TaskCompletionSource<bool>? PendingLaunch { get; set; }
        public bool LaunchSucceeds { get; set; } = true;
        public int Released { get; private set; }

        public FakeLibrary WithPlatform(string id, int games)
        {
            Platforms.Add(new HandheldPlatform(id, id.ToUpperInvariant() + " System", id.ToUpperInvariant(), "#3366CC", games));
            Games[id] = Enumerable.Range(0, games)
                .Select(i => new HandheldGameEntry(
                    Platforms.Count * 1000 + i, $"{id} game {i:000}", id, id, null, 0, TimeSpan.Zero, true))
                .ToList();
            return this;
        }

        public Task<IReadOnlyList<HandheldPlatform>> GetPlatformsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HandheldPlatform>>(Platforms.ToList());

        public Task<IReadOnlyList<HandheldGameEntry>> GetGamesAsync(string systemId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HandheldGameEntry>>(Games[systemId]);

        public List<(string SystemId, int Count)> FirstGamesReads { get; } = [];

        public Task<IReadOnlyList<HandheldGameEntry>> GetFirstGamesAsync(string systemId, int count, CancellationToken cancellationToken = default)
        {
            FirstGamesReads.Add((systemId, count));
            return Task.FromResult<IReadOnlyList<HandheldGameEntry>>(Games[systemId].Take(count).ToList());
        }

        public List<IReadOnlyList<long>> ByIdReads { get; } = [];

        public Task<IReadOnlyList<HandheldGameEntry>> GetGamesByIdAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken = default)
        {
            ByIdReads.Add(ids);
            var all = Games.Values.SelectMany(games => games).ToDictionary(game => game.Id);
            return Task.FromResult<IReadOnlyList<HandheldGameEntry>>(ids.Where(all.ContainsKey).Select(id => all[id]).ToList());
        }

        public Task<IReadOnlySet<long>> GetGameIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<long>>(Games.Values.SelectMany(games => games).Select(game => game.Id).ToHashSet());

        public Task<IReadOnlyList<long>> GetRecentGameIdsAsync(int count, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<long>>(Games.Values.SelectMany(games => games)
                .Where(game => game.LastPlayedAt is not null)
                .OrderByDescending(game => game.LastPlayedAt)
                .Take(count)
                .Select(game => game.Id)
                .ToList());

        public void ReleaseGames(IReadOnlyList<HandheldGameEntry> games) => Released += games.Count;

        public Task<Bitmap?> LoadCoverAsync(HandheldGameEntry game, int decodeWidth, bool fullSize, CancellationToken cancellationToken)
        {
            if (!fullSize && decodeWidth == HandheldShellViewModel.TileDecodeWidth)
                CoverRequests.Add(game.Id);
            if (decodeWidth == HandheldShellViewModel.PeekDecodeWidth)
                PeekRequests.Add(game.Id);
            return Task.FromResult<Bitmap?>(null);
        }

        public Task<Bitmap?> LoadArtAsync(HandheldGameEntry game, HandheldArtKind kind, CancellationToken cancellationToken) =>
            Task.FromResult<Bitmap?>(null);

        public Task<HandheldTitleDetails> GetTitleDetailsAsync(HandheldGameEntry game, CancellationToken cancellationToken = default) =>
            Task.FromResult(Details.GetValueOrDefault(game.Id, HandheldTitleDetails.Default));

        public Task<bool> SelectDiscAsync(HandheldGameEntry game, int discNumber)
        {
            DiscSelections.Add((game.Id, discNumber));
            return Task.FromResult(true);
        }

        public void RememberLaunchScreen(string systemId, GameLaunchScreen screen) => Remembered.Add((systemId, screen));

        public Task<bool> LaunchAsync(HandheldGameEntry game, GameLaunchScreen screen)
        {
            Launched.Add(game.Id);
            LaunchScreens.Add(screen);
            return PendingLaunch?.Task ?? Task.FromResult(LaunchSucceeds);
        }

        public Task SwitchModeAsync(InterfaceMode mode)
        {
            ModeSwitches.Add(mode);
            return Task.CompletedTask;
        }

        public int SettingsOpened { get; private set; }

        public Task OpenSettingsAsync()
        {
            SettingsOpened++;
            return Task.CompletedTask;
        }

        public void Quit()
        {
        }
    }

    /// <summary>
    /// A shell whose saved home is the platform folders alone, in order from slot 0 (reconciliation then puts
    /// Recent after them), so these tests can count slots from the first platform. The default layout, with
    /// Recent first, has tests of its own.
    /// </summary>
    private static async Task<HandheldShellViewModel> CreateAsync(FakeLibrary library, InMemoryHandheldHomeLayoutStore? store = null)
    {
        store ??= new InMemoryHandheldHomeLayoutStore();
        if (store.Saved is null)
            store.Save(HandheldHomeLayout.FromSlots(library.Platforms.Select(platform => HandheldSlot.PlatformFolder(platform.SystemId))));
        var shell = new HandheldShellViewModel(library, layoutStore: store);
        await shell.InitializeAsync();
        return shell;
    }

    private static void Press(HandheldShellViewModel shell, GamepadAction action, int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            Assert.True(shell.DispatchGamepadAction(action));
            // Presses here come microseconds apart, a held direction: let the Stage settle as the timer would.
            shell.SettleStage();
        }
    }

    [Fact]
    public async Task ColdStart_ShowsPlatformFoldersAndEmptySlots_FirstFolderFocused()
    {
        var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 3).WithPlatform("gc", 2));

        var page = Assert.IsType<HandheldHomePageViewModel>(shell.CurrentPage);
        Assert.Equal(20, page.Slots.Count);
        // The saved home has the two platforms; reconciliation puts the missing Recent folder after them.
        Assert.Equal(["PS2 System", "GC System", "Recent"], page.Slots.Where(slot => slot.IsFolder).Select(slot => slot.Name));
        Assert.Equal(17, page.Slots.Count(slot => slot.IsEmpty));
        Assert.True(page.Slots[0].IsFocused);
        Assert.Equal(HandheldStageKind.Folder, shell.Stage.Kind);
        Assert.Equal("PS2 System", shell.Stage.Title);
        Assert.Equal("Folder · 3 games", shell.Stage.Subtitle);
        // Home is 40 slots: two pages, the second one empty.
        Assert.True(shell.HasMultiplePages);
    }

    [Fact]
    public async Task SelectorLandsOnEmptySlots_AndAThereStartsANewFolder()
    {
        var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 3));

        // ps2, then Recent, then the first empty slot.
        Press(shell, GamepadAction.NavigateRight, 2);
        Assert.True(shell.CurrentPage!.Slots[2].IsFocused);
        Assert.Equal(HandheldStageKind.EmptySlot, shell.Stage.Kind);

        Press(shell, GamepadAction.Confirm);

        Assert.Equal(2, shell.HomePosition.Slot);
        Assert.False(shell.IsFolderOpen);
        Assert.True(shell.IsNaming);
        Assert.Equal(HandheldStageKind.Folder, shell.Stage.Kind);
        Assert.Equal("New folder", shell.Stage.Title);
    }

    [Fact]
    public async Task OpeningAFolder_SelectsTheFirstGameAndShowsItOnTheStage()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 5);
        var shell = await CreateAsync(library);

        Press(shell, GamepadAction.Confirm);

        var folder = Assert.IsType<HandheldFolderViewModel>(shell.OpenFolder);
        Assert.True(folder.IsLoaded);
        Assert.Equal("5 games", folder.CountText);
        Assert.Equal("Manual", folder.SortLabel);
        Assert.Equal(0, folder.SelectedIndex);
        Assert.True(folder.VisibleTiles[0].IsFocused);
        Assert.Equal(HandheldStageKind.Game, shell.Stage.Kind);
        Assert.Equal("ps2 game 000", shell.Stage.Title);
        Assert.Contains(shell.Hints, hint => hint is { Button: "A", Label: "Play" });
    }

    [Fact]
    public async Task FolderScrollsByColumn_RealizingOnlyTheVisibleWindow()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 30);
        var shell = await CreateAsync(library);
        Press(shell, GamepadAction.Confirm);
        var folder = shell.OpenFolder!;

        // What either screen shows, plus the column peeking in: four columns here, seven on the 16:9 screen
        // (M45 H5 keeps that Launcher ready for a swap), so 24 tiles of 30.
        Assert.Equal(24, folder.VisibleTiles.Count);
        Assert.Equal(0, folder.FirstVisibleColumn);

        Press(shell, GamepadAction.NavigateRight, 4);

        Assert.Equal(12, folder.SelectedIndex);
        Assert.Equal(1, folder.FirstVisibleColumn);
        // The column that scrolled out stays realized just off the left edge, so it can slide out.
        Assert.Equal(0, folder.WindowFirstColumn);
        Assert.Equal(0, folder.VisibleTiles[0].Index);
        Assert.Equal(24, folder.VisibleTiles.Count);
        Assert.True(folder.VisibleTiles.Single(tile => tile.Index == 12).IsFocused);
        // Covers were only asked for the window and its one-column margin, never the whole folder.
        Assert.DoesNotContain(library.Games["ps2"][27].Id, library.CoverRequests);
    }

    [Fact]
    public async Task AOnAGame_OpensItsTitlePage_AndASecondPressPlays()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 5);
        var shell = await CreateAsync(library);

        Press(shell, GamepadAction.Confirm);
        Press(shell, GamepadAction.NavigateDown);
        Press(shell, GamepadAction.Confirm);

        Assert.Empty(library.Launched);
        Assert.Equal(library.Games["ps2"][1].Id, shell.TitlePage!.Entry.Id);
        Assert.Equal(HandheldStageKind.Hero, shell.Stage.Kind);

        Press(shell, GamepadAction.Confirm);

        Assert.Equal([library.Games["ps2"][1].Id], library.Launched);
        Assert.Equal([GameLaunchScreen.BuiltIn], library.LaunchScreens);
        // The game started: back on the folder's grid, on the game that was played.
        Assert.False(shell.IsTitleOpen);
        Assert.True(shell.IsFolderOpen);
        Assert.Equal(1, shell.OpenFolder!.SelectedIndex);
    }

    [Fact]
    public async Task BClosesTheFolder_BackOnItsSlot_AndReleasesItsGames()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3).WithPlatform("gc", 4);
        var shell = await CreateAsync(library);
        Press(shell, GamepadAction.NavigateRight);
        Press(shell, GamepadAction.Confirm);
        Assert.Equal("GC System", shell.OpenFolder!.Name);
        var releasedBefore = library.Released;

        Press(shell, GamepadAction.Cancel);

        Assert.False(shell.IsFolderOpen);
        Assert.Equal(1, shell.HomePosition.Slot);
        Assert.Equal("GC System", shell.Stage.Title);
        Assert.Equal(releasedBefore + 4, library.Released);
    }

    [Fact]
    public async Task HeaderSortChip_ReordersAndKeepsTheSelectedGame()
    {
        var library = new FakeLibrary();
        library.Platforms.Add(new HandheldPlatform("ps2", "PlayStation 2", "PS2", "#3366CC", 3));
        library.Games["ps2"] =
        [
            new(1, "Okami", "ps2", "PS2", null, 0, TimeSpan.Zero, true),
            new(2, "Ape Escape", "ps2", "PS2", null, 0, TimeSpan.Zero, true),
            new(3, "Katamari", "ps2", "PS2", null, 0, TimeSpan.Zero, true),
        ];
        var shell = await CreateAsync(library);
        Press(shell, GamepadAction.Confirm);
        Press(shell, GamepadAction.NavigateDown); // Ape Escape
        Press(shell, GamepadAction.NavigateUp);
        Press(shell, GamepadAction.NavigateUp);   // into the header, on Back

        var folder = shell.OpenFolder!;
        Assert.Equal(HandheldZone.Header, shell.Zone);
        Assert.True(folder.IsBackFocused);

        Press(shell, GamepadAction.NavigateRight);
        Assert.True(folder.IsSortFocused);
        Press(shell, GamepadAction.Confirm);

        Assert.Equal(HandheldFolderSort.Title, folder.Sort);
        Assert.Equal(["Ape Escape", "Katamari", "Okami"], folder.Items.Select(tile => tile.Title));
        Assert.Equal("Okami", folder.SelectedTile!.Title);
        Assert.Equal(HandheldZone.Header, shell.Zone);

        Press(shell, GamepadAction.NavigateDown);
        Assert.Equal(HandheldZone.Grid, shell.Zone);
        Assert.Equal(0, folder.SelectedIndex);
    }

    [Fact]
    public async Task DownFromTheBottomRow_ReachesTheDock_WithTheCompanionSlots()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3);
        var shell = await CreateAsync(library);
        var companion = new SecondScreenViewModel();
        companion.Dock[0].Label = "Browser";
        int? activated = null;
        companion.SlotActivated = slot => activated = slot;
        shell.Companion = companion;

        Press(shell, GamepadAction.NavigateDown, 4);

        Assert.Equal(HandheldZone.Dock, shell.Zone);
        // All apps, the five pinned slots, then the menu button.
        Assert.Equal(7, shell.DockItems.Count);
        Assert.True(shell.DockItems[0].IsAllApps);
        Assert.True(shell.DockItems[^1].IsMenu);
        Assert.Equal(HandheldStageKind.AllApps, shell.Stage.Kind);

        Press(shell, GamepadAction.NavigateRight);
        Assert.Equal("Browser", shell.Stage.Title);
        Press(shell, GamepadAction.Confirm);
        Assert.Equal(0, activated);

        Press(shell, GamepadAction.NavigateUp);
        Assert.Equal(HandheldZone.Grid, shell.Zone);
        Assert.Equal(15, shell.HomePosition.Slot);
    }

    [Fact]
    public async Task ManyPlatforms_PageAcrossTheEdgeAndWithTheShoulders()
    {
        var library = new FakeLibrary();
        for (var i = 0; i < 23; i++)
            library.WithPlatform($"s{i:00}", 1);
        var shell = await CreateAsync(library);

        Assert.True(shell.HasMultiplePages);
        Assert.Equal(2, shell.PageDots.Count);
        Assert.True(shell.PageDots[0].IsCurrent);

        Press(shell, GamepadAction.NavigateRight, 5);
        Assert.Equal(new HandheldHomePosition(1, 0), shell.HomePosition);
        Assert.True(shell.PageDots[1].IsCurrent);
        Assert.Equal("S20 System", shell.Stage.Title);

        Press(shell, GamepadAction.PreviousPlatform);
        Assert.Equal(0, shell.HomePosition.Page);
    }

    [Fact]
    public async Task Menu_SwitchesToGamepadMode()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 1);
        var shell = await CreateAsync(library);

        Press(shell, GamepadAction.Menu);
        Assert.True(shell.IsMenuOpen);
        Assert.Equal("Switch to Gamepad mode", shell.MenuItems[1].Label);
        Press(shell, GamepadAction.NavigateDown);
        Press(shell, GamepadAction.Confirm);

        Assert.False(shell.IsMenuOpen);
        Assert.Equal([InterfaceMode.Gamepad], library.ModeSwitches);
    }

    [Fact]
    public async Task Menu_OpensSettings()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 1);
        var shell = await CreateAsync(library);

        Press(shell, GamepadAction.Menu);
        Assert.Equal("Settings", shell.MenuItems[0].Label);
        Press(shell, GamepadAction.Confirm);

        Assert.False(shell.IsMenuOpen);
        Assert.Equal(1, library.SettingsOpened);
    }

    [Fact]
    public async Task SystemBack_ClosesWhatIsOpen_ButLetsTheAppLeaveFromHome()
    {
        var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 2));

        Assert.False(shell.DispatchBack());
        Press(shell, GamepadAction.Confirm);
        Assert.True(shell.DispatchBack());
        Assert.False(shell.IsFolderOpen);
        // B itself never falls through, even at home.
        Assert.True(shell.DispatchGamepadAction(GamepadAction.Cancel));
    }

    [Fact]
    public async Task EmptyFolder_FocusesTheHeader()
    {
        var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 0));
        // A platform with no games would not be listed by the real library; force the edge here.
        await shell.OpenFolderAsync(new HandheldPlatform("ps2", "PS2 System", "PS2", "#3366CC", 0), 0);

        Assert.True(shell.OpenFolder!.IsEmpty);
        Assert.Equal(HandheldZone.Header, shell.Zone);
        Press(shell, GamepadAction.NavigateDown);
        Assert.Equal(HandheldZone.Dock, shell.Zone);
        Press(shell, GamepadAction.NavigateUp);
        Assert.Equal(HandheldZone.Header, shell.Zone);
    }

    [Fact]
    public async Task WallMovesRaiseTheEdgeBump()
    {
        var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 1));
        var bumps = new List<HandheldDirection>();
        shell.Bumped += bumps.Add;

        Press(shell, GamepadAction.NavigateLeft);
        Press(shell, GamepadAction.NavigateUp);

        Assert.Equal([HandheldDirection.Left, HandheldDirection.Up], bumps);
    }

    // ---- Title page (H2) ----

    private static async Task<(FakeLibrary Library, HandheldShellViewModel Shell, HandheldGameEntry Game)> OpenTitleAsync(
        HandheldTitleDetails? details = null, string? achievements = null)
    {
        var library = new FakeLibrary().WithPlatform("gc", 3);
        if (achievements is not null)
            library.Games["gc"][0] = library.Games["gc"][0] with { AchievementText = achievements, AchievementRatio = 0.5 };
        var game = library.Games["gc"][0];
        if (details is not null)
            library.Details[game.Id] = details;
        var shell = await CreateAsync(library);
        Press(shell, GamepadAction.Confirm); // open the folder
        Press(shell, GamepadAction.Confirm); // open the first game's Title page
        Assert.True(shell.IsTitleOpen);
        return (library, shell, game);
    }

    private static HandheldTitleDetails Details(
        LaunchScreenDecision screen = LaunchScreenDecision.BuiltIn, int discs = 1, int selected = 1) =>
        new("Dolphin", Enumerable.Range(1, discs).ToList(), selected, screen, HasSecondScreen: true, IsDualScreenSystem: false);

    [Fact]
    public async Task TitlePage_ShowsThePlayFacts_AndOnlyPlayForASingleDiscGame()
    {
        var (_, shell, game) = await OpenTitleAsync(Details());
        var page = shell.TitlePage!;

        Assert.Equal(game.Title, page.Title);
        Assert.Equal("gc · Dolphin", page.PlatformLine);
        Assert.Equal("Not yet", page.LastPlayedText);
        Assert.Equal("Top screen", page.ScreenText);
        Assert.False(page.HasDiscs);
        // No companion on this head, so the full badge grid has nowhere to open: no Achievements button.
        Assert.Equal([HandheldTitleButtonKind.Play], page.Buttons.Select(button => button.Kind));
        Assert.Equal(["A", "Y", "B"], shell.Hints.Select(hint => hint.Button));
        Assert.Equal(game.Title, shell.Stage.Title);
        Assert.True(shell.Stage.IsHeroFallback); // no fan art: the cover on a tinted backdrop
    }

    [Fact]
    public async Task AskSystem_PlayOpensTheScreenChooser_AndThePickLaunchesThere()
    {
        var (library, shell, game) = await OpenTitleAsync(Details(LaunchScreenDecision.Prompt));
        Assert.Equal("Asks which screen", shell.TitlePage!.ScreenText);

        Press(shell, GamepadAction.Confirm);
        Assert.True(shell.TitlePage!.IsScreenChooser);
        Assert.Empty(library.Launched);

        Press(shell, GamepadAction.NavigateRight);
        Press(shell, GamepadAction.Confirm);

        Assert.Equal([game.Id], library.Launched);
        Assert.Equal([GameLaunchScreen.External], library.LaunchScreens);
        Assert.Empty(library.Remembered);
        Assert.False(shell.IsTitleOpen);
    }

    [Fact]
    public async Task ScreenChooser_RememberToggle_PinsThePickForThePlatform()
    {
        var (library, shell, _) = await OpenTitleAsync(Details(LaunchScreenDecision.Prompt));
        library.PendingLaunch = new TaskCompletionSource<bool>();
        Press(shell, GamepadAction.Confirm);

        Press(shell, GamepadAction.NavigateDown);   // the remember row
        Assert.True(shell.TitlePage!.IsRememberFocused);
        Press(shell, GamepadAction.Confirm);        // tick it
        Assert.True(shell.TitlePage!.RememberScreen);
        Press(shell, GamepadAction.NavigateUp);     // back to the card it came from (top)
        Press(shell, GamepadAction.Confirm);

        Assert.Equal([("gc", GameLaunchScreen.BuiltIn)], library.Remembered);
        Assert.Equal([GameLaunchScreen.BuiltIn], library.LaunchScreens);
        Assert.Equal("Top screen", shell.TitlePage!.ScreenText);

    }

    [Fact]
    public async Task PinnedSecondScreen_PlaysThereWithoutAsking()
    {
        var (library, shell, _) = await OpenTitleAsync(Details(LaunchScreenDecision.External));
        Assert.Equal("Bottom screen", shell.TitlePage!.ScreenText);

        Press(shell, GamepadAction.Confirm);

        Assert.Equal([GameLaunchScreen.External], library.LaunchScreens);
    }

    [Fact]
    public async Task MultiDisc_HasADiscButton_AndTheChooserRemembersThePick()
    {
        var (library, shell, game) = await OpenTitleAsync(Details(discs: 3, selected: 1));
        var page = shell.TitlePage!;
        Assert.Equal([HandheldTitleButtonKind.Play, HandheldTitleButtonKind.Disc], page.Buttons.Select(button => button.Kind));
        Assert.Equal("3 · starts on disc 1", page.DiscText);

        Press(shell, GamepadAction.NavigateRight);
        Press(shell, GamepadAction.Confirm);
        Assert.True(page.IsDiscChooser);
        Assert.Equal(["Disc 1", "Disc 2", "Disc 3"], page.Options.Select(option => option.Label));
        Assert.Equal(0, page.OptionIndex); // starts on the current disc

        Press(shell, GamepadAction.NavigateDown);
        Press(shell, GamepadAction.Confirm);

        Assert.Equal([(game.Id, 2)], library.DiscSelections);
        Assert.True(page.IsButtons);
        Assert.Equal("Disc 2", page.Buttons[1].Label);
        Assert.True(page.Buttons[1].IsFocused); // focus stays on the Disc button
        Assert.Equal("3 · starts on disc 2", page.DiscText);
        Assert.Empty(library.Launched);
    }

    [Fact]
    public async Task Launching_HoldsThePage_ShowsTheSyncPanel_AndAFailureStaysOnThePage()
    {
        var (library, shell, _) = await OpenTitleAsync(Details());
        var launch = new TaskCompletionSource<bool>();
        library.PendingLaunch = launch;
        var page = shell.TitlePage!;

        Press(shell, GamepadAction.Confirm);
        Assert.True(page.IsLaunching);
        Assert.Equal("Starting…", page.LaunchingHeading);

        shell.SetLaunchSaveSync(true);
        Assert.Equal("Syncing saves…", page.LaunchingHeading);

        // Nothing interrupts a launch: B and the system Back are both swallowed.
        Press(shell, GamepadAction.Cancel);
        Assert.True(shell.DispatchBack());
        Assert.Same(page, shell.TitlePage);

        shell.SetLaunchSaveSync(false);
        launch.SetResult(false);

        Assert.Same(page, shell.TitlePage);
        Assert.True(page.IsButtons);
    }

    [Fact]
    public async Task AfterAPlay_TheFolderIsReRead_SoTheStageShowsThePlayData()
    {
        var (library, shell, game) = await OpenTitleAsync(Details());
        // What the library holds once the launch core has stamped the play.
        library.Games["gc"][0] = game with { PlayCount = 1, Playtime = TimeSpan.FromMinutes(42), LastPlayedAt = DateTimeOffset.Now };

        Press(shell, GamepadAction.Confirm);

        Assert.False(shell.IsTitleOpen);
        Assert.Equal(0, shell.OpenFolder!.SelectedIndex);
        Assert.Equal("gc · 42 min played", shell.Stage.Subtitle);
    }

    [Fact]
    public async Task ClosingTheFolder_ClosesItsTitlePage()
    {
        var (_, shell, _) = await OpenTitleAsync(Details());

        shell.CloseFolder(); // e.g. a library refresh that removed the platform

        Assert.False(shell.IsTitleOpen);
    }

    [Fact]
    public async Task B_LeavesAChooserFirst_ThenThePage()
    {
        var (_, shell, _) = await OpenTitleAsync(Details(LaunchScreenDecision.Prompt));
        Press(shell, GamepadAction.Confirm);
        Assert.True(shell.TitlePage!.IsScreenChooser);

        Press(shell, GamepadAction.Cancel);
        Assert.True(shell.TitlePage!.IsButtons);

        Assert.True(shell.DispatchBack());
        Assert.False(shell.IsTitleOpen);
        Assert.True(shell.IsFolderOpen);
        Assert.Equal(HandheldStageKind.Game, shell.Stage.Kind);
    }

    [Fact]
    public async Task Y_OpensTheActionsSheet_WithWhatThePageCanDo()
    {
        var (library, shell, _) = await OpenTitleAsync(Details(LaunchScreenDecision.External, discs: 2));
        var page = shell.TitlePage!;

        Press(shell, GamepadAction.Actions);

        Assert.True(page.IsActions);
        Assert.Equal(["Play", "Play on another screen…", "Choose disc…", "Add to folder…", "Pin to home", "Back to GC System"],
            page.Options.Select(option => option.Label));

        // "Play on another screen…" asks even though the platform is pinned to the bottom screen.
        Press(shell, GamepadAction.NavigateDown);
        Press(shell, GamepadAction.Confirm);
        Assert.True(page.IsScreenChooser);
        Assert.Empty(library.Launched);

        Press(shell, GamepadAction.Cancel);
        Press(shell, GamepadAction.Actions);
        Press(shell, GamepadAction.NavigateDown, 5);
        Press(shell, GamepadAction.Confirm);
        Assert.False(shell.IsTitleOpen);
    }

    [Fact]
    public async Task TitlePage_StageIsTheHero_AndClosingGoesBackToTheGame()
    {
        var (_, shell, game) = await OpenTitleAsync(Details(), achievements: "12/40");
        Assert.Equal(HandheldStageKind.Hero, shell.Stage.Kind);
        Assert.Equal("12/40", shell.Stage.AchievementText);
        Assert.StartsWith("gc · ", shell.Stage.Subtitle);
        Assert.Equal("12/40 achievements", shell.TitlePage!.AchievementLine);

        Press(shell, GamepadAction.Cancel);

        Assert.Equal(HandheldStageKind.Game, shell.Stage.Kind);
        Assert.Equal(game.Title, shell.Stage.Title);
    }

}

public class HandheldShellStatusTests
{
    [Fact]
    public void StatusMessage_MirrorsTheAppStatusAndClears()
    {
        var shell = new HandheldShellViewModel(new NoLibrary());

        shell.ShowStatus("Launching Okami…");
        Assert.True(shell.HasStatusMessage);
        Assert.Equal("Launching Okami…", shell.StatusMessage);

        shell.ShowStatus(string.Empty);
        Assert.False(shell.HasStatusMessage);
    }

    [Fact]
    public async System.Threading.Tasks.Task OverlappingRefreshes_OnlyTheNewestRebuildsHome()
    {
        var library = new GatedLibrary();
        var shell = new HandheldShellViewModel(library);

        var first = shell.InitializeAsync(TestContext.Current.CancellationToken);
        var second = shell.InitializeAsync(TestContext.Current.CancellationToken);
        // The newer read lands first with two platforms; the older, slower one then lands with none.
        library.Reads[1].SetResult([new HandheldPlatform("a", "A", "A", "#000000", 1), new HandheldPlatform("b", "B", "B", "#000000", 1)]);
        await second;
        library.Reads[0].SetResult([]);
        await first;

        // Recent and the two platforms of the newer read.
        Assert.Equal(3, shell.CurrentPage!.Slots.Count(slot => slot.IsFolder));
    }

    private class NoLibrary : IHandheldLibrary
    {
        public virtual System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<HandheldPlatform>> GetPlatformsAsync(System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult<System.Collections.Generic.IReadOnlyList<HandheldPlatform>>([]);
        public System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<HandheldGameEntry>> GetGamesAsync(string systemId, System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult<System.Collections.Generic.IReadOnlyList<HandheldGameEntry>>([]);
        public System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<HandheldGameEntry>> GetFirstGamesAsync(string systemId, int count, System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult<System.Collections.Generic.IReadOnlyList<HandheldGameEntry>>([]);
        public System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<HandheldGameEntry>> GetGamesByIdAsync(System.Collections.Generic.IReadOnlyList<long> ids, System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult<System.Collections.Generic.IReadOnlyList<HandheldGameEntry>>([]);
        public System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlySet<long>> GetGameIdsAsync(System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult<System.Collections.Generic.IReadOnlySet<long>>(new System.Collections.Generic.HashSet<long>());
        public System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<long>> GetRecentGameIdsAsync(int count, System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult<System.Collections.Generic.IReadOnlyList<long>>([]);
        public void ReleaseGames(System.Collections.Generic.IReadOnlyList<HandheldGameEntry> games) { }
        public System.Threading.Tasks.Task<Avalonia.Media.Imaging.Bitmap?> LoadCoverAsync(HandheldGameEntry game, int decodeWidth, bool fullSize, System.Threading.CancellationToken cancellationToken) =>
            System.Threading.Tasks.Task.FromResult<Avalonia.Media.Imaging.Bitmap?>(null);
        public System.Threading.Tasks.Task<Avalonia.Media.Imaging.Bitmap?> LoadArtAsync(HandheldGameEntry game, HandheldArtKind kind, System.Threading.CancellationToken cancellationToken) =>
            System.Threading.Tasks.Task.FromResult<Avalonia.Media.Imaging.Bitmap?>(null);
        public System.Threading.Tasks.Task<HandheldTitleDetails> GetTitleDetailsAsync(HandheldGameEntry game, System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(HandheldTitleDetails.Default);
        public System.Threading.Tasks.Task<bool> SelectDiscAsync(HandheldGameEntry game, int discNumber) => System.Threading.Tasks.Task.FromResult(false);
        public void RememberLaunchScreen(string systemId, GameLaunchScreen screen) { }
        public System.Threading.Tasks.Task<bool> LaunchAsync(HandheldGameEntry game, GameLaunchScreen screen) => System.Threading.Tasks.Task.FromResult(false);
        public System.Threading.Tasks.Task SwitchModeAsync(EmuShelf.Core.Settings.InterfaceMode mode) => System.Threading.Tasks.Task.CompletedTask;
        public void Quit() { }
    }

    private sealed class GatedLibrary : NoLibrary
    {
        public System.Collections.Generic.List<System.Threading.Tasks.TaskCompletionSource<System.Collections.Generic.IReadOnlyList<HandheldPlatform>>> Reads { get; } = [];

        public override System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<HandheldPlatform>> GetPlatformsAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            var read = new System.Threading.Tasks.TaskCompletionSource<System.Collections.Generic.IReadOnlyList<HandheldPlatform>>();
            Reads.Add(read);
            return read.Task;
        }
    }
}
