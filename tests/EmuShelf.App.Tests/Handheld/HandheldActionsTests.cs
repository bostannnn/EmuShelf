using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Handheld;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Settings;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

// M45 H6c: the Y sheet's every action, Search, Add games, and the panels behind them (the scraper, the batch scraper, cover
// search, a game's achievements, Edit title), plus the dock slot's sheet and the toasts. The panels wrap Gamepad mode's view
// models; these tests drive them through the shell the way the pad does.
public partial class HandheldShellViewModelTests
{
    private sealed partial class FakeLibrary
    {
        public List<(long Id, string Title)> Renamed { get; } = [];
        public List<long> Removed { get; } = [];
        public Func<HandheldGameEntry, GamepadScraperViewModel?>? ScraperFactory { get; set; }
        public Func<IReadOnlyList<long>, string, GamepadBatchScraperViewModel?>? BatchFactory { get; set; }
        public Func<HandheldGameEntry, GamepadCoverSearchViewModel?>? CoverSearchFactory { get; set; }
        public Func<HandheldGameEntry, AchievementDetailsViewModel?>? AchievementsFactory { get; set; }
        public List<(long Id, PickedGameCover Cover)> CoversApplied { get; } = [];
        public string? PickedFolder { get; set; }
        public List<(string Folder, string SystemId)> Imports { get; } = [];
        public int ScrapersClosed { get; private set; }

        public Task<IReadOnlyList<long>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<long>>(Games.Values.SelectMany(games => games)
                .Where(game => game.Title.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderBy(game => game.Title, StringComparer.OrdinalIgnoreCase)
                .Select(game => game.Id)
                .ToList());

        public Task<AchievementDetailsViewModel?> CreateAchievementsAsync(HandheldGameEntry game) =>
            Task.FromResult(AchievementsFactory?.Invoke(game));

        public Task<bool> RenameAsync(HandheldGameEntry game, string title)
        {
            Renamed.Add((game.Id, title));
            var games = Games[game.SystemId];
            var at = games.FindIndex(entry => entry.Id == game.Id);
            games[at] = games[at] with { Title = title };
            return Task.FromResult(true);
        }

        public Task<bool> RemoveFromLibraryAsync(HandheldGameEntry game)
        {
            Removed.Add(game.Id);
            Games[game.SystemId].RemoveAll(entry => entry.Id == game.Id);
            return Task.FromResult(true);
        }

        public GamepadScraperViewModel? CreateScraper(HandheldGameEntry game) => ScraperFactory?.Invoke(game);

        public Task CloseScraperAsync(GamepadScraperViewModel scraper)
        {
            ScrapersClosed++;
            scraper.Dispose();
            return Task.CompletedTask;
        }

        public GamepadBatchScraperViewModel? CreateBatchScraper(IReadOnlyList<long> gameIds, string name) => BatchFactory?.Invoke(gameIds, name);

        public GamepadCoverSearchViewModel? CreateCoverSearch(HandheldGameEntry game) => CoverSearchFactory?.Invoke(game);

        public Task ApplyCoverAsync(HandheldGameEntry game, PickedGameCover cover)
        {
            CoversApplied.Add((game.Id, cover));
            return Task.CompletedTask;
        }

        public Task<string?> PickImportFolderAsync() => Task.FromResult(PickedFolder);

        public IReadOnlyList<HandheldImportSystem> ImportSystems =>
            Platforms.Select(platform => new HandheldImportSystem(platform.SystemId, platform.Name, platform.ShortName, platform.AccentColor)).ToList();

        public Task ImportFolderAsync(string folder, string systemId)
        {
            Imports.Add((folder, systemId));
            return Task.CompletedTask;
        }
    }

    private static async Task<HandheldShellViewModel> CreateInFolderAsync(FakeLibrary library, int tile = 0)
    {
        var shell = await CreateAsync(library);
        shell.SearchDebounce = TimeSpan.Zero;
        Press(shell, GamepadAction.Confirm); // the first platform's folder
        Press(shell, GamepadAction.NavigateDown, tile);
        return shell;
    }

    private static void RunMenuRow(HandheldShellViewModel shell, string label)
    {
        Press(shell, GamepadAction.Menu);
        var index = shell.MenuItems.ToList().FindIndex(item => item.Label == label);
        Assert.True(index >= 0, $"No \"{label}\" in the menu");
        Press(shell, GamepadAction.NavigateDown, index);
        Press(shell, GamepadAction.Confirm);
    }

    // ---- The game's sheet ----

    [Fact]
    public async Task TheGamesSheet_HoldsEveryGamepadAction_AndThePlacesItCanBe()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3);
        library.Games["ps2"][0] = library.Games["ps2"][0] with { AchievementText = "4/10" };
        var shell = await CreateInFolderAsync(library);

        Press(shell, GamepadAction.Actions);

        Assert.Equal(["Play", "Achievements", "Add to folder…", "Pin to home", "Edit title", "Set cover", "Scrape with ScreenScraper",
            "Remove from library"], shell.Sheet!.Items.Select(item => item.Label));
        Assert.True(shell.Sheet.Items[^1].IsDestructive);
        Assert.Equal("Its files stay where they are", shell.Sheet.Items[^1].Note);
        Assert.Equal("4/10", shell.Sheet.Items[1].Note);
    }

    [Fact]
    public async Task SheetPlay_GoesThroughTheTitlePage_AndPlaysOnceItKnowsTheScreen()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 2);
        var game = library.Games["ps2"][1];
        library.Details[game.Id] = new HandheldTitleDetails("PCSX2", [1], 1, LaunchScreenDecision.External, true, false);
        var shell = await CreateInFolderAsync(library, tile: 1);

        Press(shell, GamepadAction.Actions);
        RunSheetRow(shell, "Play");
        await Task.Yield();

        Assert.Equal([game.Id], library.Launched);
        Assert.Equal([GameLaunchScreen.External], library.LaunchScreens);
        Assert.False(shell.IsTitleOpen); // a launch that started goes back to the grid, as A, A does
    }

    [Fact]
    public async Task SheetPlay_StillAsksWhichScreen_WhenThePlatformAsks()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 1);
        var game = library.Games["ps2"][0];
        library.Details[game.Id] = new HandheldTitleDetails("PCSX2", [1], 1, LaunchScreenDecision.Prompt, true, false);
        var shell = await CreateInFolderAsync(library);

        Press(shell, GamepadAction.Actions);
        RunSheetRow(shell, "Play");

        Assert.True(shell.TitlePage!.IsScreenChooser);
        Assert.Empty(library.Launched);
    }

    [Fact]
    public async Task RemoveFromLibrary_AsksFirst_AndKeepItLeavesTheGameAlone()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3);
        var game = library.Games["ps2"][1];
        var shell = await CreateInFolderAsync(library, tile: 1);

        Press(shell, GamepadAction.Actions);
        RunSheetRow(shell, "Remove from library");
        Assert.Equal("Remove from library?", shell.Sheet!.Title);
        Assert.StartsWith($"{game.Title}. Its files stay where they are.", shell.Sheet.Subtitle);
        Assert.Equal(["Remove from library", "Keep it"], shell.Sheet.Items.Select(item => item.Label));

        RunSheetRow(shell, "Keep it");
        Assert.Empty(library.Removed);
        Assert.Equal(3, shell.OpenFolder!.Items.Count);
    }

    [Fact]
    public async Task RemoveFromLibrary_TakesItOutOfTheFolder_AndOffHome()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3);
        var game = library.Games["ps2"][1];
        var store = new InMemoryHandheldHomeLayoutStore();
        store.Save(HandheldHomeLayout.FromSlots([HandheldSlot.PlatformFolder("ps2"), HandheldSlot.Game(game.Id)]));
        var shell = await CreateAsync(library, store);
        Press(shell, GamepadAction.Confirm);
        Press(shell, GamepadAction.NavigateDown);

        Press(shell, GamepadAction.Actions);
        RunSheetRow(shell, "Remove from library");
        Assert.Contains("It leaves home and your folders too.", shell.Sheet!.Subtitle);
        RunSheetRow(shell, "Remove from library");
        await Task.Yield();

        Assert.Equal([game.Id], library.Removed);
        Assert.DoesNotContain(shell.OpenFolder!.Items, tile => tile.Entry.Id == game.Id);
        Assert.False(shell.Layout.IsPinned(game.Id));
    }

    [Fact]
    public async Task ChooseDisc_FromTheTile_ListsTheDiscs_AndSavesThePick()
    {
        var library = new FakeLibrary().WithPlatform("ps1", 1);
        var game = library.Games["ps1"][0];
        // The fake's games have no view model; a multi-disc one comes from the library's answer.
        var discs = new DiscsLibrary(library, new HandheldDiscs([1, 2, 3], 2));
        var store = new InMemoryHandheldHomeLayoutStore();
        store.Save(HandheldHomeLayout.FromSlots([HandheldSlot.PlatformFolder("ps1")]));
        var shell = new HandheldShellViewModel(discs, layoutStore: store);
        await shell.InitializeAsync(TestContext.Current.CancellationToken);
        Press(shell, GamepadAction.Confirm); // the folder; its first game is focused

        Press(shell, GamepadAction.Actions);
        var row = shell.Sheet!.Items.Single(item => item.Label == "Choose disc…");
        Assert.Equal("Disc 2 of 3", row.Note);
        shell.RunSheetItem(row);
        Assert.Equal("Which disc?", shell.Sheet!.Title);
        Assert.Equal(1, shell.Sheet.Index); // on the disc it starts on
        Assert.Equal("Starts here", shell.Sheet.Items[1].Note);
        RunSheetRow(shell, "Disc 3");
        await Task.Yield();

        Assert.Equal([(game.Id, 3)], library.DiscSelections);
        Assert.Equal($"{game.Title} starts on disc 3", shell.Notice);
    }

    private sealed class DiscsLibrary(FakeLibrary inner, HandheldDiscs discs) : IHandheldLibrary
    {
        public HandheldDiscs DiscsOf(HandheldGameEntry game) => discs;
        public Task<IReadOnlyList<HandheldPlatform>> GetPlatformsAsync(CancellationToken cancellationToken = default) => inner.GetPlatformsAsync(cancellationToken);
        public Task<IReadOnlyList<HandheldGameEntry>> GetGamesAsync(string systemId, CancellationToken cancellationToken = default) => inner.GetGamesAsync(systemId, cancellationToken);
        public Task<IReadOnlyList<HandheldGameEntry>> GetFirstGamesAsync(string systemId, int count, CancellationToken cancellationToken = default) => inner.GetFirstGamesAsync(systemId, count, cancellationToken);
        public Task<IReadOnlyList<HandheldGameEntry>> GetGamesByIdAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken = default) => inner.GetGamesByIdAsync(ids, cancellationToken);
        public Task<IReadOnlySet<long>> GetGameIdsAsync(CancellationToken cancellationToken = default) => inner.GetGameIdsAsync(cancellationToken);
        public Task<IReadOnlyList<long>> GetRecentGameIdsAsync(int count, CancellationToken cancellationToken = default) => inner.GetRecentGameIdsAsync(count, cancellationToken);
        public void ReleaseGames(IReadOnlyList<HandheldGameEntry> games) => inner.ReleaseGames(games);
        public Task<Avalonia.Media.Imaging.Bitmap?> LoadCoverAsync(HandheldGameEntry game, int decodeWidth, bool fullSize, CancellationToken cancellationToken) => inner.LoadCoverAsync(game, decodeWidth, fullSize, cancellationToken);
        public Task<Avalonia.Media.Imaging.Bitmap?> LoadArtAsync(HandheldGameEntry game, HandheldArtKind kind, CancellationToken cancellationToken) => inner.LoadArtAsync(game, kind, cancellationToken);
        public Task<HandheldTitleDetails> GetTitleDetailsAsync(HandheldGameEntry game, CancellationToken cancellationToken = default) => inner.GetTitleDetailsAsync(game, cancellationToken);
        public Task<bool> SelectDiscAsync(HandheldGameEntry game, int discNumber) => inner.SelectDiscAsync(game, discNumber);
        public void RememberLaunchScreen(string systemId, GameLaunchScreen screen) => inner.RememberLaunchScreen(systemId, screen);
        public Task<bool> LaunchAsync(HandheldGameEntry game, GameLaunchScreen screen) => inner.LaunchAsync(game, screen);
        public Task SwitchModeAsync(InterfaceMode mode) => inner.SwitchModeAsync(mode);
        public void Quit() => inner.Quit();
    }

    // ---- Edit title ----

    [Fact]
    public async Task EditTitle_TypesOnTheKeyboard_TheStageFollows_AndDoneRenames()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 2);
        var game = library.Games["ps2"][0];
        var shell = await CreateInFolderAsync(library);

        Press(shell, GamepadAction.Actions);
        RunSheetRow(shell, "Edit title");
        var panel = Assert.IsType<HandheldTitleEditPanel>(shell.Panel);
        Assert.True(shell.IsLauncherOverlayOpen);
        Assert.Equal(game.Title, panel.Keyboard!.Text);
        Assert.Equal(HandheldStageKind.Panel, shell.Stage.Kind);

        panel.Keyboard.Text = "Okami";
        Press(shell, GamepadAction.Actions); // Y types a space
        Assert.Equal("Okami ", panel.Text);
        Assert.Equal("Okami ", shell.Stage.PanelCard!.Title);
        Assert.Equal(["A", "B", "Y", "X"], shell.Hints.Select(hint => hint.Button));

        Press(shell, GamepadAction.Search); // X is Done
        await Task.Yield();

        Assert.Null(shell.Panel);
        Assert.Equal([(game.Id, "Okami")], library.Renamed);
        Assert.Contains(shell.OpenFolder!.Items, tile => tile.Entry.Title == "Okami");
    }

    [Fact]
    public async Task EditTitle_RefusesAnEmptyName_AndBWithNothingLeftCancels()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 1);
        var shell = await CreateInFolderAsync(library);
        Press(shell, GamepadAction.Actions);
        RunSheetRow(shell, "Edit title");
        var panel = Assert.IsType<HandheldTitleEditPanel>(shell.Panel);

        panel.Keyboard!.Text = "  ";
        Press(shell, GamepadAction.Menu); // Start is Done too
        Assert.Same(panel, shell.Panel);
        Assert.Equal("A game title cannot be empty.", panel.Status);

        panel.Keyboard.Text = string.Empty;
        Press(shell, GamepadAction.Cancel);
        Assert.Null(shell.Panel);
        Assert.Empty(library.Renamed);
        Assert.Equal(HandheldStageKind.Game, shell.Stage.Kind);
    }

    // ---- Search ----

    [Fact]
    public async Task Search_FansTheMatchesOnTheStage_AndDoneOpensThemAsAFolder()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 12).WithPlatform("gc", 3);
        var shell = await CreateAsync(library);
        shell.SearchDebounce = TimeSpan.Zero;

        RunMenuRow(shell, "Search");
        var panel = Assert.IsType<HandheldSearchPanel>(shell.Panel);
        Assert.Equal("Search your library", shell.Stage.PanelCard!.Title);

        panel.Keyboard!.Text = "game 01";
        await panel.Pending;
        Assert.Equal("“game 01”", shell.Stage.PanelCard!.Title);
        Assert.Equal("2 games", shell.Stage.PanelCard.Value); // ps2 game 010 and 011
        Assert.Equal(2, panel.Results.Count);

        Press(shell, GamepadAction.Search);
        await panel.Pending;

        Assert.Null(shell.Panel);
        var folder = shell.OpenFolder!;
        Assert.True(folder.Source.IsSearch);
        Assert.Equal("“game 01”", folder.Name);
        Assert.Equal(["ps2 game 010", "ps2 game 011"], folder.Items.Select(tile => tile.Entry.Title));
        Assert.Equal(0, folder.SelectedIndex);
        Assert.Equal(HandheldStageKind.Game, shell.Stage.Kind);

        // A there opens a game's Title page; B goes back home.
        Press(shell, GamepadAction.Confirm);
        Assert.True(shell.IsTitleOpen);
        Press(shell, GamepadAction.Cancel);
        Press(shell, GamepadAction.Cancel);
        Assert.Null(shell.OpenFolder);

        // Search opens again on the last query.
        RunMenuRow(shell, "Search");
        Assert.Equal("game 01", Assert.IsType<HandheldSearchPanel>(shell.Panel).Query);
    }

    [Fact]
    public async Task Search_WithNoMatches_SaysSo_AndStaysOpen()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 2);
        var shell = await CreateAsync(library);
        shell.SearchDebounce = TimeSpan.Zero;
        RunMenuRow(shell, "Search");
        var panel = Assert.IsType<HandheldSearchPanel>(shell.Panel);

        panel.Keyboard!.Text = "zelda";
        await panel.Pending;
        Assert.Equal("No games match", shell.Stage.PanelCard!.Value);
        Assert.True(shell.Stage.PanelCard.IsWarning);
        Press(shell, GamepadAction.Search);
        await panel.Pending;

        Assert.Same(panel, shell.Panel);
        Assert.Equal("No games match “zelda”.", panel.Status);
        Assert.Null(shell.OpenFolder);
    }

    // ---- Add games ----

    [Fact]
    public async Task AddGames_PicksAFolder_ThenAsksWhichSystem_AndImportsIt()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 1).WithPlatform("gc", 1);
        library.PickedFolder = "/storage/emulated/0/Games/PS2";
        var shell = await CreateAsync(library);

        RunMenuRow(shell, "Add games");
        await Task.Yield();

        Assert.Equal("Add games", shell.Sheet!.Title);
        Assert.Equal("From PS2 · which system are they for?", shell.Sheet.Subtitle);
        Assert.Equal(["PS2 System", "GC System"], shell.Sheet.Items.Select(item => item.Label));
        RunSheetRow(shell, "PS2 System");

        Assert.Equal([("/storage/emulated/0/Games/PS2", "ps2")], library.Imports);
    }

    [Fact]
    public async Task AddGames_CancelledPicker_OpensNothing()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 1);
        var shell = await CreateAsync(library);

        RunMenuRow(shell, "Add games");
        await Task.Yield();

        Assert.Null(shell.Sheet);
        Assert.Empty(library.Imports);
    }

    // ---- The scraper ----

    private static GamepadScraperViewModel Scraper(StubScreenScraperPreviewService preview, StubGameScrapeApplicationService? apply = null) =>
        new(new GameScraperViewModel(1, "Game", preview, apply ?? new StubGameScrapeApplicationService(),
            new StubScreenScraperAccountService(), new ScreenScraperSettings { Enabled = true }));

    [AvaloniaFact]
    public async Task Scraper_DrawsEachTargetAsARow_TheStageDescribesTheFocusedOne_AndApplyClosesWithWhatItWrote()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 1);
        GamepadScraperViewModel? details = null;
        library.ScraperFactory = _ => details = Scraper(new StubScreenScraperPreviewService(ScraperFixtures.ReadyPreview()));
        var shell = await CreateInFolderAsync(library);
        var game = library.Games["ps2"][0];

        Press(shell, GamepadAction.Actions);
        RunSheetRow(shell, "Scrape with ScreenScraper");
        await Task.Yield();
        var panel = Assert.IsType<HandheldScraperPanel>(shell.Panel);
        Assert.Equal(GameScraperState.Ready, details!.Scraper.State);

        // One row per target, the focus on Apply as in Gamepad mode.
        Assert.Equal(details.Targets.Count, panel.Rows.Count);
        Assert.Equal("Apply selected", panel.FocusedRow!.Label);
        Assert.Equal("Apply selected", shell.Stage.PanelCard!.Title);

        // Up to a field: its proposed value large, what it is now beneath.
        while (panel.FocusedRow!.Source is not ScraperFieldRowViewModel)
            Press(shell, GamepadAction.NavigateUp);
        var field = (ScraperFieldRowViewModel)panel.FocusedRow.Source!;
        Assert.Equal(field.Label, shell.Stage.PanelCard!.Title);
        Assert.Equal(field.ProposedValue, shell.Stage.PanelCard.Value);
        Assert.True(panel.FocusedRow.IsOn);
        Press(shell, GamepadAction.Confirm);
        Assert.False(field.IsSelected);
        Assert.False(panel.FocusedRow.IsOn);
        Assert.Equal("Will be left as it is", shell.Stage.PanelCard.Status);

        while (panel.FocusedRow!.Label != "Apply selected")
            Press(shell, GamepadAction.NavigateDown);
        Press(shell, GamepadAction.Confirm);
        await Task.Yield();

        Assert.Null(shell.Panel);
        Assert.Equal(1, library.ScrapersClosed);
        Assert.StartsWith($"{game.Title}: Applied", shell.Notice);
    }

    [AvaloniaFact]
    public async Task Scraper_SignIn_TypesTheUsernameOnTheKeyboard()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 1);
        GamepadScraperViewModel? details = null;
        library.ScraperFactory = _ => details = Scraper(new StubScreenScraperPreviewService(
            ScraperFixtures.Failure(ScreenScraperPreviewStatus.NotConnected)));
        var shell = await CreateInFolderAsync(library);
        Press(shell, GamepadAction.Actions);
        RunSheetRow(shell, "Scrape with ScreenScraper");
        await Task.Yield();
        var panel = Assert.IsType<HandheldScraperPanel>(shell.Panel);

        Assert.Equal(["Username", "Password", "Connect"], panel.Rows.Select(row => row.Label));
        Press(shell, GamepadAction.Confirm);
        Assert.True(panel.IsTyping);
        panel.Keyboard!.Text = "player";
        Press(shell, GamepadAction.Search);
        Assert.False(panel.IsTyping);
        Assert.Equal("player", details!.Scraper.Username);
        Assert.Equal("player", panel.Rows[0].Value);

        // The password is masked on the row and the card until Y shows it.
        Press(shell, GamepadAction.NavigateDown);
        Press(shell, GamepadAction.Confirm);
        Assert.True(panel.Keyboard!.IsSecret);
        panel.Keyboard.Text = "secret";
        Assert.Equal("●●●●●●", shell.Stage.PanelCard!.Value);
        Press(shell, GamepadAction.Actions);
        Assert.Equal("secret", shell.Stage.PanelCard!.Value);
        Press(shell, GamepadAction.Search);
        Assert.Equal("●●●●●●", panel.Rows[1].Value);

        Press(shell, GamepadAction.Cancel);
        Assert.Null(shell.Panel);
        Assert.Equal(1, library.ScrapersClosed);
    }

    // ---- The batch scraper (a folder's sheet) ----

    private sealed class FakeBatch : IScreenScraperBatchService
    {
        public Task<GameScrapeBatchSummary> RunAsync(IReadOnlyList<long> gameIds, ScreenScraperSettings settings, GameMetadataApplyMode mode,
            IReadOnlySet<GameMetadataField>? includeFields, IReadOnlySet<GameMediaKind>? includeMedia, IProgress<GameScrapeBatchProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            progress?.Report(new GameScrapeBatchProgress(gameIds.Count, gameIds.Count, "last"));
            return Task.FromResult(new GameScrapeBatchSummary(gameIds.Count, GameScrapeBatchStopReason.Completed,
                [.. gameIds.Select(id => new GameScrapeBatchItemResult(id, $"g{id}", GameScrapeBatchOutcome.Applied, 3, 1))]));
        }
    }

    [AvaloniaFact]
    public async Task ScrapeAllInThisFolder_RunsGamepadModesBatch_AndSaysWhatItDid()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3);
        IReadOnlyList<long>? scraped = null;
        library.BatchFactory = (ids, name) =>
        {
            scraped = ids;
            return new GamepadBatchScraperViewModel(new GameBatchScraperViewModel(ids, name, new FakeBatch(),
                new ScreenScraperSettings { Enabled = true }));
        };
        var shell = await CreateAsync(library);

        Press(shell, GamepadAction.Actions);
        Assert.Equal("3 games", shell.Sheet!.Items.Single(item => item.Label == "Scrape all in this folder").Note);
        RunSheetRow(shell, "Scrape all in this folder");
        await Task.Yield();

        var panel = Assert.IsType<HandheldBatchScraperPanel>(shell.Panel);
        Assert.Equal(library.Games["ps2"].Select(game => game.Id), scraped);
        Assert.Equal(["Replace values ScreenScraper already set", "Start scraping"], panel.Rows.Select(row => row.Label));
        Assert.Equal("Start scraping", panel.FocusedRow!.Label);
        Assert.Equal("3 games to look up", shell.Stage.PanelCard!.Value);

        Press(shell, GamepadAction.Confirm);
        for (var i = 0; i < 100 && !panel.Details.Batch.IsDone; i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal(["Done"], panel.Rows.Select(row => row.Label));
        Assert.Equal(1, shell.Stage.PanelCard!.Progress);
        Press(shell, GamepadAction.Confirm);
        await Task.Yield();

        Assert.Null(shell.Panel);
        Assert.False(string.IsNullOrEmpty(shell.Notice));
    }

    // ---- Cover search ----

    private sealed class Covers(int count) : IGameArtworkSearchProvider
    {
        public Task<IReadOnlyList<ArtworkSearchResult>> SearchAsync(string title, string systemName, double preferredAspectRatio,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ArtworkSearchResult>>([.. Enumerable.Range(0, count).Select(i => new ArtworkSearchResult("web-search",
                new Uri($"https://images.example/{i}.png"), new Uri($"https://images.example/{i}-preview.png"),
                new Uri($"https://source.example/{i}"), 600, 900, $"cover {i}", ".png"))]);
    }

    private sealed class Downloads(string directory) : IRemoteArtworkDownloader
    {
        private static readonly byte[] Png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

        public async Task<DownloadedArtwork?> DownloadFirstAsync(IReadOnlyList<ArtworkCandidate> candidates, CancellationToken cancellationToken = default)
        {
            var path = Path.Combine(directory, $"{Guid.NewGuid():N}.png");
            await File.WriteAllBytesAsync(path, Png, cancellationToken);
            return new DownloadedArtwork(candidates[0], path);
        }
    }

    [AvaloniaFact]
    public async Task SetCover_ListsTheCoversFound_ShowsTheFocusedOneLarge_AndSetsThePick()
    {
        var directory = Path.Combine(Path.GetTempPath(), "EmuShelfHandheldCovers", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var library = new FakeLibrary().WithPlatform("ps2", 1);
            GamepadCoverSearchViewModel? details = null;
            library.CoverSearchFactory = game => details = new GamepadCoverSearchViewModel(
                new CoverSearchViewModel(new GameCoverPickerContext(game.Title, game.SystemName, 0.7), new Covers(2), new Downloads(directory),
                    () => Task.FromResult<string?>(null)), () => { }, offersLocalFile: false);
            var shell = await CreateInFolderAsync(library);
            var game = library.Games["ps2"][0];

            Press(shell, GamepadAction.Actions);
            RunSheetRow(shell, "Set cover");
            var panel = Assert.IsType<HandheldCoverSearchPanel>(shell.Panel);
            for (var i = 0; i < 100 && details!.Search.Results.Count < 2; i++)
                await Task.Delay(10, TestContext.Current.CancellationToken);

            // The field, Search, then the covers; no "Choose a file" (there is no Desktop to hand off to).
            Assert.Equal(["Search for", "Search", "600 × 900", "600 × 900"], panel.Rows.Select(row => row.Label));
            // The ring stays on the first cover that arrived as the others stream in around it, as in Gamepad mode.
            Assert.Equal(GamepadCoverSearchTargetKind.Candidate, details!.FocusedKind);
            var focused = (CoverSearchResultViewModel)details.FocusedItem!;
            Assert.Equal($"Cover {details.Search.Results.IndexOf(focused) + 1} of 2", shell.Stage.PanelCard!.Title);
            Assert.Same(focused.Preview, shell.Stage.PanelCard.Image);

            Press(shell, GamepadAction.Confirm);
            for (var i = 0; i < 100 && shell.Panel is not null; i++)
                await Task.Delay(10, TestContext.Current.CancellationToken);

            Assert.Null(shell.Panel);
            Assert.Equal(game.Id, Assert.Single(library.CoversApplied).Id);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SetCover_WithoutWebSearch_SaysWhy()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 1);
        var shell = await CreateInFolderAsync(library);

        Press(shell, GamepadAction.Actions);
        RunSheetRow(shell, "Set cover");

        Assert.Null(shell.Panel);
        Assert.Equal(((IHandheldLibrary)library).CoverSearchUnavailableReason, shell.Notice);
        Assert.True(shell.IsStatusError);
    }

    // ---- Achievements ----

    private sealed class FakeAchievements : IAchievementProvider
    {
        public int Refreshes { get; private set; }
        public string Id => "fake";
        public string DisplayName => "Fake";
        public string? AccountId => "me";
        public bool IsConnected => true;
        public bool SupportsPoints => true;
        public bool SupportsHardcore => false;
        public AchievementSnapshot? GetCached(AchievementGameRef game) => null;

        /// <summary>What a refresh returns; null reports offline.</summary>
        public AchievementSnapshot? Fresh { get; set; }

        /// <summary>How long a badge takes to arrive (a cold cache downloading it).</summary>
        public TimeSpan BadgeDelay { get; set; }

        public Task<AchievementResult> RefreshAsync(AchievementGameRef game, CancellationToken cancellationToken = default, bool manual = false)
        {
            Refreshes++;
            return Task.FromResult(Fresh is { } fresh
                ? new AchievementResult(AchievementStatus.Success, fresh)
                : new AchievementResult(AchievementStatus.Offline));
        }

        /// <summary>A badge file every icon resolves to; null leaves the badges as placeholders.</summary>
        public string? BadgePath { get; set; }

        public async Task<string?> GetIconPathAsync(string icon, CancellationToken cancellationToken = default)
        {
            if (BadgeDelay > TimeSpan.Zero)
                await Task.Delay(BadgeDelay, cancellationToken);
            return BadgePath;
        }

        public event Action? Changed { add { } remove { } }
    }

    private static AchievementDetailsViewModel Achievements(FakeAchievements provider, bool cached = true)
    {
        var game = new AchievementGameRef("fake", "1");
        return new AchievementDetailsViewModel("Okami", game, provider, cached ? Snapshot(game) : null, deferBadgeLoading: true);
    }

    private static AchievementSnapshot Snapshot(AchievementGameRef game) =>
        new(game, "me", "Okami",
            [
                new AchievementEntry("a", "First Steps", "Finish the prologue", "1001", 1, true, DateTimeOffset.UtcNow.AddDays(-2), 5),
                new AchievementEntry("b", "Treasure Hunter", "Find every chest", "1002", 2, false, Points: 10),
                new AchievementEntry("c", "True Ending", "See the last scene", "1003", 3, false, Points: 25, IsHidden: true),
            ], DateTimeOffset.UtcNow);

    [AvaloniaFact]
    public async Task Achievements_ListTheBadges_FilterOnLbRb_RevealOnA_AndTheStageShowsTheFocusedOne()
    {
        var provider = new FakeAchievements();
        var (library, shell, _) = await OpenTitleAsync(Details(), achievements: "1/3");
        library.AchievementsFactory = _ => Achievements(provider);

        // The Title page's Achievements button opens them full-screen across both screens (no companion needed).
        Assert.Contains(shell.TitlePage!.Buttons, button => button.Kind == HandheldTitleButtonKind.Achievements);
        Press(shell, GamepadAction.NavigateRight);
        Press(shell, GamepadAction.Confirm);
        await Task.Yield();
        var panel = Assert.IsType<HandheldAchievementsPanel>(shell.Panel);

        Assert.Equal(["First Steps", "Treasure Hunter", "Hidden achievement"], panel.Rows.Select(row => row.Label));
        Assert.Equal(["All  3", "Locked  2", "Unlocked  1"], panel.Chips.Select(chip => chip.Label));
        Assert.Equal("First Steps", shell.Stage.PanelCard!.Title);
        Assert.Equal("Finish the prologue", shell.Stage.PanelCard.Description);
        Assert.False(panel.Rows[0].IsDimmed);
        Assert.True(panel.Rows[1].IsDimmed);

        Press(shell, GamepadAction.NextPlatform);
        Assert.True(panel.Chips[1].IsSelected);
        Assert.Equal(["Treasure Hunter", "Hidden achievement"], panel.Rows.Select(row => row.Label));

        Press(shell, GamepadAction.NavigateDown);
        Assert.Equal("A", shell.Hints[0].Button);
        Press(shell, GamepadAction.Confirm);
        Assert.Equal("True Ending", panel.Rows[1].Label);
        Assert.Equal("See the last scene", shell.Stage.PanelCard!.Description);

        Press(shell, GamepadAction.Search); // X: the next sort
        Assert.StartsWith("Sort: ", shell.Hints.Single(hint => hint.Button == "X").Label);
        var refreshes = provider.Refreshes;
        Press(shell, GamepadAction.Actions); // Y: refresh (the provider is asked off the UI thread)
        for (var i = 0; i < 100 && provider.Refreshes == refreshes; i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(provider.Refreshes > refreshes);

        Press(shell, GamepadAction.Cancel);
        Assert.Null(shell.Panel);
        Assert.True(shell.IsTitleOpen);
        Assert.Equal(HandheldStageKind.Hero, shell.Stage.Kind);
    }

    [AvaloniaFact]
    public async Task Achievements_LoadTheBadges_IntoTheRowsAndTheCard()
    {
        var badge = Path.Combine(Path.GetTempPath(), $"emushelf-badge-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(badge, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="),
            TestContext.Current.CancellationToken);
        try
        {
            var provider = new FakeAchievements { BadgePath = badge };
            var (library, shell, _) = await OpenTitleAsync(Details(), achievements: "1/3");
            library.AchievementsFactory = _ => Achievements(provider);
            Press(shell, GamepadAction.NavigateRight);
            Press(shell, GamepadAction.Confirm);
            var panel = Assert.IsType<HandheldAchievementsPanel>(shell.Panel);
            for (var i = 0; i < 200 && (panel.Rows[0].Image is null || shell.Stage.PanelCard?.Image is null); i++)
                await Task.Delay(10, TestContext.Current.CancellationToken);

            Assert.NotNull(panel.Rows[0].Image);
            Assert.NotNull(panel.Rows[1].Image);
            Assert.Same(panel.Rows[0].Image, shell.Stage.PanelCard!.Image);
        }
        finally
        {
            File.Delete(badge);
        }
    }

    [AvaloniaFact]
    public async Task Achievements_NotCachedYet_ShowTheBadgesOnceTheRefreshAndTheDownloadsLand()
    {
        var badge = Path.Combine(Path.GetTempPath(), $"emushelf-badge-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(badge, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="),
            TestContext.Current.CancellationToken);
        try
        {
            var provider = new FakeAchievements { BadgePath = badge, BadgeDelay = TimeSpan.FromMilliseconds(150) };
            provider.Fresh = Snapshot(new AchievementGameRef("fake", "1"));
            var (library, shell, _) = await OpenTitleAsync(Details(), achievements: "1/3");
            library.AchievementsFactory = _ => Achievements(provider, cached: false);
            Press(shell, GamepadAction.NavigateRight);
            Press(shell, GamepadAction.Confirm);
            var panel = Assert.IsType<HandheldAchievementsPanel>(shell.Panel);
            for (var i = 0; i < 300 && (panel.Rows.Count == 0 || panel.Rows[0].Image is null); i++)
                await Task.Delay(10, TestContext.Current.CancellationToken);

            Assert.Equal(3, panel.Rows.Count);
            Assert.NotNull(panel.Rows[0].Image);
            Assert.Same(panel.Rows[0].Image, shell.Stage.PanelCard!.Image);
        }
        finally
        {
            File.Delete(badge);
        }
    }

    // ---- The dock's slots ----

    [Fact]
    public async Task YOnADockApp_OffersOpenChangeAndClear()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 1);
        var shell = await CreateAsync(library);
        var companion = new SecondScreenViewModel();
        var cleared = new List<int>();
        var edited = new List<int>();
        companion.SlotClearRequested = cleared.Add;
        companion.SlotEditRequested = edited.Add;
        companion.Dock[0].Label = "Browser";
        shell.Companion = companion;

        Press(shell, GamepadAction.NavigateDown, 4); // home's bottom row, then the dock
        Assert.Equal(HandheldZone.Dock, shell.Zone);
        Press(shell, GamepadAction.Actions); // All apps has no sheet
        Assert.Null(shell.Sheet);
        Press(shell, GamepadAction.NavigateRight);
        Assert.Equal("Y", shell.Hints[1].Button);
        Press(shell, GamepadAction.Actions);

        Assert.Equal("Browser", shell.Sheet!.Title);
        Assert.Equal(["Open", "Change app…", "Clear slot"], shell.Sheet.Items.Select(item => item.Label));
        RunSheetRow(shell, "Clear slot");
        Assert.Equal([0], cleared);
        Assert.Equal("Slot 1 cleared", shell.Notice);

        // An empty slot offers to choose one.
        Press(shell, GamepadAction.NavigateRight);
        Press(shell, GamepadAction.Actions);
        Assert.Equal(["Choose an app…"], shell.Sheet!.Items.Select(item => item.Label));
        RunSheetRow(shell, "Choose an app…");
        Assert.Equal([1], edited);
    }

    // ---- Toasts ----

    [Fact]
    public void TheAppsMessages_AreMarkedByWhatTheyAre()
    {
        var shell = new HandheldShellViewModel(new FakeLibrary());

        shell.ShowStatus("Could not start PCSX2", StatusSeverity.Error);
        Assert.Equal(HandheldGlyph.Alert, shell.StatusGlyph);
        Assert.True(shell.IsStatusError);

        shell.ShowStatus("Scanning PlayStation 2… 12 found", StatusSeverity.Progress);
        Assert.Equal(HandheldGlyph.Working, shell.StatusGlyph);
        Assert.False(shell.IsStatusError);

        shell.ShowStatus("Added 12 games from folder");
        Assert.Equal(HandheldGlyph.Check, shell.StatusGlyph);

        shell.Announce("EmuShelf 2.0 is available. Install it from Settings → About.", HandheldGlyph.Update);
        Assert.Equal(HandheldGlyph.Update, shell.StatusGlyph);
        Assert.Equal("EmuShelf 2.0 is available. Install it from Settings → About.", shell.StatusMessage);
    }
}
