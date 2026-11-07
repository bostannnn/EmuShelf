using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using EmuShelf.Core.Settings;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

public class HandheldShellViewModelTests
{
    private sealed class FakeLibrary : IHandheldLibrary
    {
        public List<HandheldPlatform> Platforms { get; } = [];
        public Dictionary<string, List<HandheldGameEntry>> Games { get; } = new();
        public List<long> CoverRequests { get; } = [];
        public List<long> Launched { get; } = [];
        public List<InterfaceMode> ModeSwitches { get; } = [];
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

        public void ReleaseGames(IReadOnlyList<HandheldGameEntry> games) => Released += games.Count;

        public Task<Bitmap?> LoadCoverAsync(HandheldGameEntry game, int decodeWidth, bool fullSize, CancellationToken cancellationToken)
        {
            if (!fullSize)
                CoverRequests.Add(game.Id);
            return Task.FromResult<Bitmap?>(null);
        }

        public Task LaunchAsync(HandheldGameEntry game)
        {
            Launched.Add(game.Id);
            return Task.CompletedTask;
        }

        public Task SwitchModeAsync(InterfaceMode mode)
        {
            ModeSwitches.Add(mode);
            return Task.CompletedTask;
        }

        public void Quit()
        {
        }
    }

    private static async Task<HandheldShellViewModel> CreateAsync(FakeLibrary library)
    {
        var shell = new HandheldShellViewModel(library);
        await shell.InitializeAsync();
        return shell;
    }

    private static void Press(HandheldShellViewModel shell, GamepadAction action, int times = 1)
    {
        for (var i = 0; i < times; i++)
            Assert.True(shell.DispatchGamepadAction(action));
    }

    [Fact]
    public async Task ColdStart_ShowsPlatformFoldersAndEmptySlots_FirstFolderFocused()
    {
        var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 3).WithPlatform("gc", 2));

        var page = Assert.IsType<HandheldHomePageViewModel>(shell.CurrentPage);
        Assert.Equal(20, page.Slots.Count);
        Assert.Equal(["PS2 System", "GC System"], page.Slots.Where(slot => slot.IsFolder).Select(slot => slot.Name));
        Assert.Equal(18, page.Slots.Count(slot => slot.IsEmpty));
        Assert.True(page.Slots[0].IsFocused);
        Assert.Equal(HandheldStageKind.Folder, shell.Stage.Kind);
        Assert.Equal("PS2 System", shell.Stage.Title);
        Assert.Equal("Folder · 3 games", shell.Stage.Subtitle);
        Assert.False(shell.HasMultiplePages);
    }

    [Fact]
    public async Task SelectorLandsOnEmptySlots_AndAThereDoesNothingYet()
    {
        var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 3));

        Press(shell, GamepadAction.NavigateRight);
        Press(shell, GamepadAction.Confirm);

        Assert.Equal(1, shell.HomePosition.Slot);
        Assert.True(shell.CurrentPage!.Slots[1].IsFocused);
        Assert.Equal(HandheldStageKind.EmptySlot, shell.Stage.Kind);
        Assert.False(shell.IsFolderOpen);
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

        // Four columns shown plus the one peeking in: 15 tiles of 30.
        Assert.Equal(15, folder.VisibleTiles.Count);
        Assert.Equal(0, folder.FirstVisibleColumn);

        Press(shell, GamepadAction.NavigateRight, 4);

        Assert.Equal(12, folder.SelectedIndex);
        Assert.Equal(1, folder.FirstVisibleColumn);
        Assert.Equal(3, folder.VisibleTiles[0].Index);
        Assert.True(folder.VisibleTiles.Single(tile => tile.Index == 12).IsFocused);
        // Covers were only asked for the window and its one-column margin, never the whole folder.
        Assert.DoesNotContain(library.Games["ps2"][27].Id, library.CoverRequests);
    }

    [Fact]
    public async Task AOnAGame_LaunchesThroughTheLibrary()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 5);
        var shell = await CreateAsync(library);

        Press(shell, GamepadAction.Confirm);
        Press(shell, GamepadAction.NavigateDown);
        Press(shell, GamepadAction.Confirm);

        Assert.Equal([library.Games["ps2"][1].Id], library.Launched);
    }

    [Fact]
    public async Task BClosesTheFolder_BackOnItsSlot_AndReleasesItsGames()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3).WithPlatform("gc", 4);
        var shell = await CreateAsync(library);
        Press(shell, GamepadAction.NavigateRight);
        Press(shell, GamepadAction.Confirm);
        Assert.Equal("GC System", shell.OpenFolder!.Name);

        Press(shell, GamepadAction.Cancel);

        Assert.False(shell.IsFolderOpen);
        Assert.Equal(1, shell.HomePosition.Slot);
        Assert.Equal("GC System", shell.Stage.Title);
        Assert.Equal(4, library.Released);
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
        Assert.Equal("Switch to Gamepad mode", shell.MenuItems[0].Label);
        Press(shell, GamepadAction.Confirm);

        Assert.False(shell.IsMenuOpen);
        Assert.Equal([InterfaceMode.Gamepad], library.ModeSwitches);
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

        var first = shell.InitializeAsync();
        var second = shell.InitializeAsync();
        // The newer read lands first with two platforms; the older, slower one then lands with none.
        library.Reads[1].SetResult([new HandheldPlatform("a", "A", "A", "#000000", 1), new HandheldPlatform("b", "B", "B", "#000000", 1)]);
        await second;
        library.Reads[0].SetResult([]);
        await first;

        Assert.Equal(2, shell.CurrentPage!.Slots.Count(slot => slot.IsFolder));
    }

    private class NoLibrary : IHandheldLibrary
    {
        public virtual System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<HandheldPlatform>> GetPlatformsAsync(System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult<System.Collections.Generic.IReadOnlyList<HandheldPlatform>>([]);
        public System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<HandheldGameEntry>> GetGamesAsync(string systemId, System.Threading.CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult<System.Collections.Generic.IReadOnlyList<HandheldGameEntry>>([]);
        public void ReleaseGames(System.Collections.Generic.IReadOnlyList<HandheldGameEntry> games) { }
        public System.Threading.Tasks.Task<Avalonia.Media.Imaging.Bitmap?> LoadCoverAsync(HandheldGameEntry game, int decodeWidth, bool fullSize, System.Threading.CancellationToken cancellationToken) =>
            System.Threading.Tasks.Task.FromResult<Avalonia.Media.Imaging.Bitmap?>(null);
        public System.Threading.Tasks.Task LaunchAsync(HandheldGameEntry game) => System.Threading.Tasks.Task.CompletedTask;
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
