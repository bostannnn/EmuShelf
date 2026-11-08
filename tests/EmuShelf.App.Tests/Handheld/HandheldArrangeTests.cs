using System;
using System.Linq;
using System.Threading.Tasks;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

// M45 H4: arranging home through the shell, as the pad drives it.
public partial class HandheldShellViewModelTests
{
    private static void RunSheetRow(HandheldShellViewModel shell, string label)
    {
        var sheet = Assert.IsType<HandheldSheetViewModel>(shell.Sheet);
        var row = sheet.Items.Single(item => item.Label == label);
        shell.RunSheetItem(row);
    }

    private static void FocusHome(HandheldShellViewModel shell, int slot)
    {
        while (shell.HomePosition.Slot % HandheldHomeLayout.Columns > slot % HandheldHomeLayout.Columns)
            Press(shell, GamepadAction.NavigateLeft);
        while (shell.HomePosition.Slot % HandheldHomeLayout.Columns < slot % HandheldHomeLayout.Columns)
            Press(shell, GamepadAction.NavigateRight);
        while (shell.HomePosition.Slot / HandheldHomeLayout.Columns < slot / HandheldHomeLayout.Columns)
            Press(shell, GamepadAction.NavigateDown);
        while (shell.HomePosition.Slot / HandheldHomeLayout.Columns > slot / HandheldHomeLayout.Columns)
            Press(shell, GamepadAction.NavigateUp);
        Assert.Equal(slot, shell.HomePosition.Slot);
    }

    [Fact]
    public async Task FirstStart_IsTheDefault_RecentFirst_AndIsSavedSoItSurvives()
    {
        var store = new InMemoryHandheldHomeLayoutStore();
        var shell = new HandheldShellViewModel(new FakeLibrary().WithPlatform("ps2", 3).WithPlatform("gc", 2), layoutStore: store);

        await shell.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Recent", "PS2 System", "GC System"], shell.CurrentPage!.Slots.Where(slot => slot.IsFolder).Select(slot => slot.Name));
        Assert.Equal(HandheldStageKind.Folder, shell.Stage.Kind);
        Assert.Equal("Recent", shell.Stage.Title);
        Assert.Equal(HandheldGlyph.Clock, shell.Stage.FolderGlyph);
        Assert.NotNull(store.Saved);
    }

    [Fact]
    public async Task PinToHome_FromInsideAFolder_PutsTheGameOnHome_AndItSurvivesAColdStart()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3);
        var store = new InMemoryHandheldHomeLayoutStore();
        var shell = await CreateAsync(library, store);
        var game = library.Games["ps2"][1];

        Press(shell, GamepadAction.Confirm);
        Press(shell, GamepadAction.NavigateDown);
        Press(shell, GamepadAction.Actions);
        Assert.Equal(["Title page", "Add to folder…", "Pin to home"], shell.Sheet!.Items.Select(item => item.Label));
        RunSheetRow(shell, "Pin to home");

        Assert.Null(shell.Sheet);
        Assert.Equal("Pinned to home, page 1", shell.StatusMessage);
        // ps2, Recent, then the pin; the game is still in its platform folder.
        Assert.Equal(HandheldSlot.Game(game.Id), shell.Layout.SlotAt(2));
        Assert.Equal(3, shell.OpenFolder!.Items.Count);

        // Cold start: a new shell over the same saved file.
        var again = new HandheldShellViewModel(library, layoutStore: store);
        await again.InitializeAsync(TestContext.Current.CancellationToken);
        var pin = again.CurrentPage!.Slots[2];
        Assert.True(pin.IsGame);
        Assert.Equal(game.Title, pin.Name);

        // A on the pin opens its Title page.
        FocusHome(again, 2);
        Assert.Equal(game.Title, again.Stage.Title);
        Press(again, GamepadAction.Confirm);
        Assert.Equal(game.Id, again.TitlePage!.Entry.Id);
    }

    [Fact]
    public async Task AOnAnEmptySlot_NamesANewFolder_OnTheKeyboard_AndDoneMakesIt()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3);
        var store = new InMemoryHandheldHomeLayoutStore();
        store.Save(HandheldHomeLayout.FromSlots([HandheldSlot.PlatformFolder("ps2")]));
        var shell = await CreateAsync(library, store);
        FocusHome(shell, 7);

        Press(shell, GamepadAction.Confirm);

        var naming = Assert.IsType<HandheldNamingViewModel>(shell.Naming);
        Assert.True(naming.IsNew);
        Assert.True(shell.IsLauncherOverlayOpen);
        Assert.Equal(HandheldNamingZone.Keys, naming.Zone);
        Assert.True(naming.KeyRows[0].Keys[0].IsFocused);

        // A types the focused key (a capital first), Y is a space, B deletes; the Stage follows every key.
        Press(shell, GamepadAction.Confirm);
        Press(shell, GamepadAction.NavigateRight);
        Press(shell, GamepadAction.Confirm);
        Assert.Equal("Qw", naming.Text);
        Assert.Equal("Qw", shell.Stage.Title);
        Press(shell, GamepadAction.Actions);
        Press(shell, GamepadAction.NavigateLeft);
        Press(shell, GamepadAction.Confirm);
        Assert.Equal("Qw Q", naming.Text);
        Press(shell, GamepadAction.Cancel);
        Assert.Equal("Qw ", naming.Text);

        // Up from the top row reaches the colour chips; right one, A picks it; X finishes.
        Press(shell, GamepadAction.NavigateUp);
        Assert.Equal(HandheldNamingZone.Colours, naming.Zone);
        Press(shell, GamepadAction.NavigateRight);
        Press(shell, GamepadAction.Confirm);
        Press(shell, GamepadAction.NavigateDown);
        Assert.Equal(HandheldNamingZone.Keys, naming.Zone);
        Press(shell, GamepadAction.Search);

        Assert.Null(shell.Naming);
        var folder = Assert.Single(shell.Layout.Folders);
        Assert.Equal("Qw", folder.Name);
        Assert.Equal(HandheldHomeLayout.FolderColors[1], folder.Color);
        Assert.Empty(folder.GameIds);
        Assert.Equal(HandheldSlot.UserFolder(folder.Id), shell.Layout.SlotAt(7));
        Assert.Equal("Made \"Qw\"", shell.StatusMessage);
        Assert.True(shell.CurrentPage!.Slots[7].IsFocused);
        Assert.Equal(HandheldGlyph.Star, shell.CurrentPage.Slots[7].FolderGlyph);

        // The empty folder says how to fill it, and the selector rests on Back.
        Press(shell, GamepadAction.Confirm);
        Assert.True(shell.OpenFolder!.IsEmpty);
        Assert.Equal("Press Y on a game and choose Add to folder…", shell.OpenFolder.EmptyHint);
        Assert.Equal(HandheldZone.Header, shell.Zone);
    }

    [Fact]
    public async Task BWhileNaming_MakesNothing()
    {
        var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 3));
        FocusHome(shell, 4);
        Press(shell, GamepadAction.Confirm);
        shell.Naming!.Text = "No";

        // B deletes first, and only leaves once there is nothing left to delete.
        Press(shell, GamepadAction.Cancel, 2);
        Assert.NotNull(shell.Naming);
        Press(shell, GamepadAction.Cancel);

        Assert.Null(shell.Naming);
        Assert.Empty(shell.Layout.Folders);
        Assert.True(shell.Layout.SlotAt(4).IsEmpty);
        Assert.Equal(HandheldStageKind.EmptySlot, shell.Stage.Kind);
    }

    [Fact]
    public async Task AddToFolder_NewFolderWithTheGame_ThenAddingAgainSaysAlreadyInIt()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3);
        var shell = await CreateAsync(library);
        var game = library.Games["ps2"][0];
        Press(shell, GamepadAction.Confirm);

        Press(shell, GamepadAction.Actions);
        RunSheetRow(shell, "Add to folder…");
        Assert.Equal("Add to folder", shell.Sheet!.Title);
        RunSheetRow(shell, "New folder");
        shell.Naming!.Text = "Faves";
        Press(shell, GamepadAction.Menu); // Start finishes

        var folder = Assert.Single(shell.Layout.Folders);
        Assert.Equal([game.Id], folder.GameIds);
        // The folder lands in the first free slot on home while you stay in the platform folder.
        Assert.Equal(HandheldSlot.UserFolder(folder.Id), shell.Layout.SlotAt(2));
        Assert.True(shell.IsFolderOpen);

        Press(shell, GamepadAction.Actions);
        RunSheetRow(shell, "Add to folder…");
        Assert.Equal("Already in it", shell.Sheet!.Items.Single(item => item.Label == "Faves").Note);
        RunSheetRow(shell, "Faves");
        Assert.Equal("Already in Faves", shell.StatusMessage);
        Assert.Single(shell.Layout.FindFolder(folder.Id)!.GameIds);
    }

    [Fact]
    public async Task MoveMode_AGameDroppedOnAGame_MakesAFolderOfTheTwo_AndThePinsLeaveHome()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3);
        var store = new InMemoryHandheldHomeLayoutStore();
        var (a, b) = (library.Games["ps2"][0].Id, library.Games["ps2"][1].Id);
        store.Save(HandheldHomeLayout.FromSlots([HandheldSlot.PlatformFolder("ps2"), HandheldSlot.Recent, HandheldSlot.Game(a), HandheldSlot.Game(b)]));
        var shell = await CreateAsync(library, store);
        FocusHome(shell, 3);

        Press(shell, GamepadAction.Actions);
        RunSheetRow(shell, "Move");
        Assert.True(shell.IsMoving);
        Assert.True(shell.IsLauncherOverlayOpen);
        Press(shell, GamepadAction.NavigateLeft);

        // Held over the other game: it is the drop target, and the Stage says what A will do.
        Assert.True(shell.CurrentPage!.Slots[2].IsDropTarget);
        Assert.Contains(shell.Hints, hint => hint is { Button: "A", Label: "Make folder" });
        Assert.StartsWith("Drop on", shell.Stage.Subtitle);

        Press(shell, GamepadAction.Confirm);
        Assert.False(shell.IsMoving);
        Assert.NotNull(shell.Naming);
        // Nothing changes until the folder is named.
        Assert.Equal(HandheldSlot.Game(b), shell.Layout.SlotAt(3));
        shell.Naming!.Text = "Pair";
        Press(shell, GamepadAction.Menu);

        var folder = Assert.Single(shell.Layout.Folders);
        Assert.Equal([a, b], folder.GameIds);
        Assert.Equal(HandheldSlot.UserFolder(folder.Id), shell.Layout.SlotAt(2));
        Assert.True(shell.Layout.SlotAt(3).IsEmpty);
        Assert.False(shell.Layout.IsPinned(a));
        Assert.False(shell.Layout.IsPinned(b));
    }

    [Fact]
    public async Task MoveMode_AGameDroppedOnYourFolder_JoinsIt()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3);
        var store = new InMemoryHandheldHomeLayoutStore();
        var game = library.Games["ps2"][2].Id;
        store.Save(HandheldHomeLayout.FromSlots([HandheldSlot.PlatformFolder("ps2"), HandheldSlot.UserFolder("mine"), HandheldSlot.Game(game)],
            [new HandheldUserFolder("mine", "Mine", "#EF79AC", [library.Games["ps2"][0].Id])]));
        var shell = await CreateAsync(library, store);
        FocusHome(shell, 2);

        Press(shell, GamepadAction.Actions);
        RunSheetRow(shell, "Move");
        Press(shell, GamepadAction.NavigateLeft);
        Press(shell, GamepadAction.Confirm);

        Assert.Equal("Added to Mine", shell.StatusMessage);
        Assert.Equal(2, shell.Layout.FindFolder("mine")!.GameIds.Count);
        Assert.False(shell.Layout.IsPinned(game));
        Assert.True(shell.CurrentPage!.Slots[1].IsFocused);
    }

    [Fact]
    public async Task MoveMode_PlacesAFolder_TilesInTheWaySlideAside_AndBPutsItBack()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3).WithPlatform("gc", 2).WithPlatform("wii", 2);
        var store = new InMemoryHandheldHomeLayoutStore();
        var shell = await CreateAsync(library, store);
        var before = shell.Layout.AllSlots();
        FocusHome(shell, 2); // wii

        Press(shell, GamepadAction.Actions);
        RunSheetRow(shell, "Move");
        Press(shell, GamepadAction.NavigateLeft, 2);

        // The preview: wii at 0, ps2 and gc pushed along into the slot wii left.
        Assert.True(shell.CurrentPage!.Slots[0].IsCarried);
        Assert.Equal("PS2 System", shell.CurrentPage.Slots[1].Name);
        Assert.Equal("GC System", shell.CurrentPage.Slots[2].Name);
        Assert.True(shell.CurrentPage.Slots[5].IsLandingPlace);

        Press(shell, GamepadAction.Cancel);
        Assert.False(shell.IsMoving);
        Assert.Equal(before, shell.Layout.AllSlots());
        Assert.Equal(2, shell.HomePosition.Slot);

        Press(shell, GamepadAction.Actions);
        RunSheetRow(shell, "Move");
        // Across the page edge with the shoulder button, then drop.
        Press(shell, GamepadAction.NextPlatform);
        Press(shell, GamepadAction.Confirm);
        Assert.Equal(HandheldSlot.PlatformFolder("wii"), shell.Layout.SlotAt(22));
        Assert.True(shell.Layout.SlotAt(2).IsEmpty);
        Assert.Equal(1, shell.HomePosition.Page);
        Assert.Equal(1, store.Saved!.AllSlots().Count(slot => slot.SystemId == "wii"));
    }

    [Fact]
    public async Task DeleteFolder_AsksFirst_SayingTheGamesStay_AndKeepsThem()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3);
        var store = new InMemoryHandheldHomeLayoutStore();
        var game = library.Games["ps2"][0].Id;
        store.Save(HandheldHomeLayout.FromSlots([HandheldSlot.PlatformFolder("ps2"), HandheldSlot.UserFolder("mine"), HandheldSlot.Game(game)],
            [new HandheldUserFolder("mine", "Mine", "#EF79AC", [game])]));
        var shell = await CreateAsync(library, store);
        FocusHome(shell, 1);

        Press(shell, GamepadAction.Actions);
        Assert.Equal(["Open", "Move", "Rename or recolour…", "Delete folder"], shell.Sheet!.Items.Select(item => item.Label));
        Assert.Equal("Games stay in the library", shell.Sheet.Items[3].Note);
        RunSheetRow(shell, "Delete folder");
        Assert.Equal("Delete \"Mine\"?", shell.Sheet!.Title);
        Assert.Equal("Its 1 game stay in the library and in their platform folders", shell.Sheet.Subtitle);

        RunSheetRow(shell, "Keep it");
        Assert.NotNull(shell.Layout.FindFolder("mine"));

        Press(shell, GamepadAction.Actions);
        RunSheetRow(shell, "Delete folder");
        RunSheetRow(shell, "Delete folder");
        Assert.Null(shell.Layout.FindFolder("mine"));
        Assert.True(shell.Layout.SlotAt(1).IsEmpty);
        Assert.True(shell.Layout.IsPinned(game));
        Assert.Equal("Deleted \"Mine\". The games are still in the library.", shell.StatusMessage);
    }

    [Fact]
    public async Task RenameAndRecolour_KeepsTheGames()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3);
        var store = new InMemoryHandheldHomeLayoutStore();
        store.Save(HandheldHomeLayout.FromSlots([HandheldSlot.UserFolder("mine")],
            [new HandheldUserFolder("mine", "Mine", HandheldHomeLayout.FolderColors[0], [library.Games["ps2"][0].Id])]));
        var shell = await CreateAsync(library, store);

        Press(shell, GamepadAction.Actions);
        RunSheetRow(shell, "Rename or recolour…");
        var naming = shell.Naming!;
        Assert.False(naming.IsNew);
        Assert.Equal("Mine", naming.Text);
        naming.Text = "Ours";
        Press(shell, GamepadAction.NavigateUp);
        Press(shell, GamepadAction.NavigateRight, 2);
        Press(shell, GamepadAction.Confirm);
        Press(shell, GamepadAction.Menu);

        var folder = shell.Layout.FindFolder("mine")!;
        Assert.Equal("Ours", folder.Name);
        Assert.Equal(HandheldHomeLayout.FolderColors[2], folder.Color);
        Assert.Single(folder.GameIds);
    }

    [Fact]
    public async Task RecentFolder_IsTheLastPlayedNewestFirst_WithNoSortChip()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 5);
        var games = library.Games["ps2"];
        var now = DateTimeOffset.Now;
        games[3] = games[3] with { LastPlayedAt = now.AddDays(-1), PlayCount = 1 };
        games[1] = games[1] with { LastPlayedAt = now.AddHours(-1), PlayCount = 2 };
        var shell = new HandheldShellViewModel(library);
        await shell.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Made for you · 2 games", shell.Stage.Subtitle);
        Press(shell, GamepadAction.Confirm);

        var folder = shell.OpenFolder!;
        Assert.Equal("Recent", folder.Name);
        Assert.False(folder.CanSort);
        Assert.Equal([games[1].Id, games[3].Id], folder.Items.Select(tile => tile.Entry.Id));
        // Up reaches Back; right has no sort chip to go to.
        Press(shell, GamepadAction.NavigateUp);
        var bumps = 0;
        shell.Bumped += _ => bumps++;
        Press(shell, GamepadAction.NavigateRight);
        Assert.Equal(1, bumps);
        Assert.True(folder.IsBackFocused);
    }

    [Fact]
    public async Task YourFolder_RemembersItsSort_AndRemoveFromIt_NamesThePlace()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3);
        var games = library.Games["ps2"];
        var store = new InMemoryHandheldHomeLayoutStore();
        store.Save(HandheldHomeLayout.FromSlots([HandheldSlot.UserFolder("mine")],
            [new HandheldUserFolder("mine", "Mine", "#EF79AC", [games[2].Id, games[0].Id])]));
        var shell = await CreateAsync(library, store);

        Press(shell, GamepadAction.Confirm);
        // Manual is the order the games were added.
        Assert.Equal([games[2].Id, games[0].Id], shell.OpenFolder!.Items.Select(tile => tile.Entry.Id));
        Press(shell, GamepadAction.NavigateUp);
        Press(shell, GamepadAction.NavigateRight);
        Press(shell, GamepadAction.Confirm);
        Assert.Equal(HandheldFolderSort.Title, store.Saved!.FindFolder("mine")!.Sort);

        Press(shell, GamepadAction.NavigateDown);
        Press(shell, GamepadAction.Actions);
        Assert.Contains(shell.Sheet!.Items, item => item is { Label: "Remove from Mine", Note: "Stays in the library" });
        RunSheetRow(shell, "Remove from Mine");
        await Task.Yield();
        Assert.Single(shell.Layout.FindFolder("mine")!.GameIds);
        Assert.Single(shell.OpenFolder!.Items);
        Assert.Equal("Removed from Mine. The game is still in the library.", shell.StatusMessage);

        // Reopened, the folder comes back on the sort it was left on.
        Press(shell, GamepadAction.Cancel);
        Press(shell, GamepadAction.Confirm);
        Assert.Equal(HandheldFolderSort.Title, shell.OpenFolder!.Sort);
    }

    [Fact]
    public async Task ANewPlatform_IsAppendedWithTheNewRibbon_UntilItsFolderIsOpened()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3);
        var store = new InMemoryHandheldHomeLayoutStore();
        var shell = await CreateAsync(library, store);
        library.WithPlatform("gba", 2);

        await shell.InitializeAsync(TestContext.Current.CancellationToken);

        var slot = shell.CurrentPage!.Slots[2];
        Assert.Equal("GBA System", slot.Name);
        Assert.True(slot.IsNew);
        FocusHome(shell, 2);
        Assert.StartsWith("New · ", shell.Stage.Subtitle);

        // Opened, the ribbon comes off as the folder closes (the page it zooms out of stays as it was).
        Press(shell, GamepadAction.Confirm);
        Press(shell, GamepadAction.Cancel);
        Assert.False(store.Saved!.SlotAt(2).IsNew);
        Assert.False(shell.CurrentPage!.Slots[2].IsNew);
    }

    [Fact]
    public async Task ALibraryRefresh_KeepsANameBeingTyped_AndRereadsPinsForTheirDimming()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 2);
        var store = new InMemoryHandheldHomeLayoutStore();
        var game = library.Games["ps2"][0].Id;
        store.Save(HandheldHomeLayout.FromSlots([HandheldSlot.PlatformFolder("ps2"), HandheldSlot.Game(game)]));
        var shell = await CreateAsync(library, store);
        Assert.False(shell.CurrentPage!.Slots[1].IsUnavailable);
        FocusHome(shell, 3);
        Press(shell, GamepadAction.Confirm);
        shell.Naming!.Text = "Keep";

        // The availability pass finds the pinned game's file missing and the library is read again.
        library.Games["ps2"][0] = library.Games["ps2"][0] with { IsAvailable = false };
        await shell.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.True(shell.CurrentPage!.Slots[1].IsUnavailable);
        Assert.Equal("Keep", shell.Naming!.Text);
        Press(shell, GamepadAction.Menu);
        Assert.Equal("Keep", Assert.Single(shell.Layout.Folders).Name);
    }

    [Fact]
    public async Task TheAppsOwnStatus_ShowsAtOnce_OverAnEditsNotice()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 2);
        var shell = await CreateAsync(library);
        shell.PinToHome(library.Games["ps2"][0].Id);
        Assert.StartsWith("Pinned to home", shell.StatusMessage);

        shell.ShowStatus("Could not import");

        Assert.Equal("Could not import", shell.StatusMessage);
    }

    [Fact]
    public async Task RemovedGames_LeaveHome_AndAPlatformWithoutGamesLeavesAnEmptySlot()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3).WithPlatform("gc", 1);
        var store = new InMemoryHandheldHomeLayoutStore();
        var gcGame = library.Games["gc"][0].Id;
        store.Save(HandheldHomeLayout.FromSlots([HandheldSlot.PlatformFolder("ps2"), HandheldSlot.PlatformFolder("gc"), HandheldSlot.Game(gcGame)]));
        var shell = await CreateAsync(library, store);
        Assert.True(shell.Layout.IsPinned(gcGame));

        library.Platforms.RemoveAll(platform => platform.SystemId == "gc");
        library.Games.Remove("gc");
        await shell.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.True(shell.Layout.SlotAt(1).IsEmpty);
        Assert.False(shell.Layout.IsPinned(gcGame));
        Assert.Equal(HandheldSlot.Recent, shell.Layout.SlotAt(3));
    }

    [Fact]
    public async Task MissingFiles_AreDimmed_NotRemoved()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 2);
        library.Games["ps2"][0] = library.Games["ps2"][0] with { IsAvailable = false };
        var store = new InMemoryHandheldHomeLayoutStore();
        store.Save(HandheldHomeLayout.FromSlots([HandheldSlot.PlatformFolder("ps2"), HandheldSlot.Game(library.Games["ps2"][0].Id)]));
        var shell = await CreateAsync(library, store);

        Assert.True(shell.CurrentPage!.Slots[1].IsUnavailable);
        Assert.True(shell.Layout.IsPinned(library.Games["ps2"][0].Id));
    }
}
