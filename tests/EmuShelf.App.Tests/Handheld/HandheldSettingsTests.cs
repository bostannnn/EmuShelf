using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Settings;
using EmuShelf.Integrations.Emulators;
using EmuShelf.Integrations.Emulators.Android;
using EmuShelf.Integrations.Systems;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

// M45 H6a: Settings is an app on the grid (decision 18), and open it is master-detail across the two screens.
public sealed class HandheldSettingsTileLayoutTests
{
    [Fact]
    public void Reconcile_PutsSettingsOnAHomeSavedBeforeItExisted_AfterTheLastTile()
    {
        var saved = HandheldHomeLayout.FromSlots([HandheldSlot.Recent, HandheldSlot.PlatformFolder("ps2"), HandheldSlot.PlatformFolder("gc")]);

        var layout = HandheldHomeReconciler.Reconcile(saved, ["ps2", "gc"], new HashSet<long>(), out var changed);

        Assert.True(changed);
        Assert.Equal(HandheldSlot.Settings, layout.SlotAt(3));
        Assert.Equal(1, layout.AllSlots().Count(slot => slot.IsSettings));
    }

    [Fact]
    public void Reconcile_NeverDropsSettings_AndKeepsExactlyOne()
    {
        // Twice on home (a hand-edited file): the first stays.
        var twice = HandheldHomeLayout.FromSlots([HandheldSlot.Settings, HandheldSlot.PlatformFolder("ps2"), HandheldSlot.Settings]);
        var layout = HandheldHomeReconciler.Reconcile(twice, ["ps2"], new HashSet<long>(), out _);
        Assert.Equal(0, layout.IndexOf(HandheldSlot.Settings));
        Assert.Equal(1, layout.AllSlots().Count(slot => slot.IsSettings));

        // On home and in a folder: home wins, the folder lets go of it.
        var both = HandheldHomeLayout.FromSlots([HandheldSlot.Settings, HandheldSlot.UserFolder("a")],
            [new HandheldUserFolder("a", "A", "#3BBD79", [], HoldsSettings: true)]);
        layout = HandheldHomeReconciler.Reconcile(both, [], new HashSet<long>(), out _);
        Assert.False(layout.FindFolder("a")!.HoldsSettings);
        Assert.Equal(0, layout.IndexOf(HandheldSlot.Settings));

        // In two folders: the first keeps it, and it is not put on home.
        var two = HandheldHomeLayout.FromSlots([HandheldSlot.UserFolder("a"), HandheldSlot.UserFolder("b")],
        [
            new HandheldUserFolder("a", "A", "#3BBD79", [], HoldsSettings: true),
            new HandheldUserFolder("b", "B", "#3BBD79", [], HoldsSettings: true),
        ]);
        layout = HandheldHomeReconciler.Reconcile(two, [], new HashSet<long>(), out _);
        Assert.True(layout.FindFolder("a")!.HoldsSettings);
        Assert.False(layout.FindFolder("b")!.HoldsSettings);
        Assert.Equal(-1, layout.IndexOf(HandheldSlot.Settings));

        // In a folder whose tile is gone (the folder is dropped): back on home.
        var orphan = HandheldHomeLayout.FromSlots([HandheldSlot.PlatformFolder("ps2")],
            [new HandheldUserFolder("gone", "Gone", "#3BBD79", [], HoldsSettings: true)]);
        layout = HandheldHomeReconciler.Reconcile(orphan, ["ps2"], new HashSet<long>(), out _);
        Assert.Empty(layout.Folders);
        Assert.True(layout.IndexOf(HandheldSlot.Settings) >= 0);

        // Already right: reconciliation leaves it alone and saves nothing.
        HandheldHomeReconciler.Reconcile(layout, ["ps2"], new HashSet<long>(), out var changed);
        Assert.False(changed);
    }

    [Fact]
    public void Folding_SettingsIntoAFolder_AndDeletingThatFolder_PutsItWhereTheFolderWas()
    {
        var home = HandheldHomeLayout.FromSlots([HandheldSlot.Settings, HandheldSlot.PlatformFolder("ps2"), HandheldSlot.UserFolder("a")],
            [new HandheldUserFolder("a", "A", "#3BBD79", [7])]);

        var folded = home.WithSettingsIn("a");
        Assert.Equal(-1, folded.IndexOf(HandheldSlot.Settings));
        Assert.Same(folded.FindFolder("a"), folded.SettingsFolder);

        var deleted = folded.WithoutFolder("a");
        Assert.Equal(2, deleted.IndexOf(HandheldSlot.Settings));
        Assert.Null(deleted.SettingsFolder);

        var back = folded.WithSettingsOnHome(-1, out var placed);
        Assert.Equal(0, placed);
        Assert.Equal(HandheldSlot.Settings, back.SlotAt(0));
        Assert.Null(back.SettingsFolder);
    }

    [Fact]
    public void MoveMode_SettingsDroppedOnYourFolder_GoesIn_AndOnAGame_JustLands()
    {
        Assert.Equal(HandheldDropKind.AddToFolder, HandheldHomeArranging.Classify(HandheldSlot.Settings, HandheldSlot.UserFolder("a")));
        Assert.Equal(HandheldDropKind.Place, HandheldHomeArranging.Classify(HandheldSlot.Settings, HandheldSlot.Game(3)));
        Assert.Equal(HandheldDropKind.Place, HandheldHomeArranging.Classify(HandheldSlot.Settings, HandheldSlot.PlatformFolder("ps2")));
    }
}

public partial class HandheldShellViewModelTests
{
    private sealed class NoConfigurations : IEmulatorConfigurationStore
    {
        public EmulatorConfiguration? Get(string systemId) => null;

        public void Save(EmulatorConfiguration configuration)
        {
        }

        public void SaveAll(IReadOnlyList<EmulatorConfiguration> configurations)
        {
        }

        public void SetActiveEmulator(string systemId, string emulatorId)
        {
        }
    }

    /// <summary>Gamepad mode's Settings projection over the known systems, with an achievements account to type into.</summary>
    private static GamepadSettingsViewModel CreateSettingsProjection()
    {
        var settings = new EmulatorSettingsViewModel(
            KnownSystems.All,
            KnownEmulators.All,
            KnownSystems.All.ToDictionary(system => system.Id, _ => (EmulatorConfiguration?)null, StringComparer.Ordinal),
            new NoConfigurations(),
            new FakeDialogService(),
            retroAchievements: new RetroAchievementsSettingsContext(null, false,
                (_, _, _, _) => Task.FromResult(new RetroAchievementsConnectionSummary(RetroAchievementsConnectionResult.Connected)),
                _ => Task.CompletedTask),
            fixedEmulatorChoices: AndroidEmulatorChoiceCatalog.BySystem);
        return new GamepadSettingsViewModel(settings, androidEmulatorChoices: AndroidEmulatorChoiceCatalog.BySystem);
    }

    private static async Task<HandheldShellViewModel> CreateWithSettingsAsync(FakeLibrary library, InMemoryHandheldHomeLayoutStore? store = null)
    {
        library.SettingsFactory = CreateSettingsProjection;
        var shell = await CreateAsync(library, store);
        return shell;
    }

    [AvaloniaFact]
    public async Task TheDefaultHome_HasTheSettingsTileAfterThePlatforms_AndAOpensSettingsAcrossBothScreens()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 2).WithPlatform("gc", 1);
        library.SettingsFactory = CreateSettingsProjection;
        var shell = new HandheldShellViewModel(library);
        await shell.InitializeAsync(TestContext.Current.CancellationToken);

        // Recent, the two platforms, then Settings: an app on the grid, not a folder.
        var tile = shell.CurrentPage!.Slots[3];
        Assert.True(tile.IsSettings);
        Assert.False(tile.IsFolder);
        Assert.Equal("Settings", tile.Name);
        FocusHome(shell, 3);
        Assert.Equal(HandheldStageKind.SettingsTile, shell.Stage.Kind);
        Assert.Equal("Settings", shell.Stage.Title);
        Assert.True(shell.Stage.HasPlate);
        Assert.Contains(shell.Hints, hint => hint is { Button: "A", Label: "Open" });

        Press(shell, GamepadAction.Confirm);
        await Task.Yield();

        var settings = Assert.IsType<HandheldSettingsViewModel>(shell.Settings);
        Assert.True(shell.IsLauncherOverlayOpen);
        Assert.Equal(1, library.SettingsOpened);
        // The Launcher lists Gamepad mode's sections in its order (the ones this settings model has: an account, no cloud
        // saves, no themes) and the rows…
        Assert.Equal(["Library", "Emulators", "Achievements", "Display", "About"], settings.Sections.Select(section => section.Title));
        Assert.True(settings.Sections[0].IsSelected);
        // …and the Stage shows what the focused row is, its value and its description, with no plate under it.
        Assert.Equal(HandheldStageKind.Setting, shell.Stage.Kind);
        Assert.False(shell.Stage.HasPlate);
        Assert.Equal("Library", shell.Stage.SettingEyebrow);
        Assert.Equal(settings.Projection.FocusedRow!.Label, shell.Stage.Title);
        Assert.Contains(shell.Hints, hint => hint is { Label: "Save", Icon: HandheldGlyph.Menu });
        Assert.Contains(shell.Hints, hint => hint is { Button: "B", Label: "Close" });

        // B closes Settings without saving; home is where it was.
        Press(shell, GamepadAction.Cancel);
        Assert.Null(shell.Settings);
        Assert.False(shell.IsLauncherOverlayOpen);
        Assert.Equal([false], library.SettingsClosed);
        Assert.Equal(HandheldStageKind.SettingsTile, shell.Stage.Kind);
    }

    [AvaloniaFact]
    public async Task TheStartMenu_OpensSettingsToo_AndItOwnsThePadUntilItCloses()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 2);
        var shell = await CreateWithSettingsAsync(library);

        Press(shell, GamepadAction.Menu);
        Assert.Equal(["Search", "Add games", "Settings", "Switch to Gamepad mode", "Quit EmuShelf"], shell.MenuItems.Select(item => item.Label));
        Press(shell, GamepadAction.NavigateDown, 2);
        Press(shell, GamepadAction.Confirm);
        await Task.Yield();
        var settings = Assert.IsType<HandheldSettingsViewModel>(shell.Settings);
        Assert.False(shell.IsMenuOpen);

        // Down moves through the rows (the Stage follows); RB moves to the next section; X is Settings' own.
        var first = shell.Stage.Title;
        Press(shell, GamepadAction.NavigateDown);
        Assert.NotEqual(first, shell.Stage.Title);
        Assert.Equal(settings.Projection.FocusedRow!.Label, shell.Stage.Title);
        Press(shell, GamepadAction.NextPlatform);
        Assert.True(settings.Sections[1].IsSelected);
        Assert.Equal("Emulators", shell.Stage.SettingEyebrow);
        Press(shell, GamepadAction.Search);
        Assert.False(shell.IsSwapped);
        Assert.Equal(0, shell.HomePosition.Slot);

        // Left with a non-choice row focused goes to the list of sections; the Stage then describes the section.
        Press(shell, GamepadAction.PreviousPlatform);
        Press(shell, GamepadAction.NavigateUp);
        Press(shell, GamepadAction.NavigateLeft);
        Assert.True(settings.Projection.IsRailFocused);
        Assert.True(settings.Sections[0].IsFocused);
        Assert.Equal("Settings", shell.Stage.SettingEyebrow);
        Assert.Equal("Library", shell.Stage.Title);
        Assert.Contains(shell.Hints, hint => hint is { Button: "A", Label: "Open" });

        // The system Back button closes Settings like B.
        Assert.True(shell.DispatchBack());
        Assert.Null(shell.Settings);
    }

    [AvaloniaFact]
    public async Task ASwitch_FlipsInPlace_AndTheStageCardIsDrawnAgainWithoutANewSubject()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 2);
        var shell = await CreateWithSettingsAsync(library);
        await shell.OpenSettingsAsync();
        var settings = shell.Settings!;

        // Display's first switch: the CRT tube.
        while (!settings.Sections.Single(section => section.Title == "Display").IsSelected)
            Press(shell, GamepadAction.NextPlatform);
        Press(shell, GamepadAction.NavigateDown);
        var row = settings.Projection.FocusedRow!;
        Assert.Equal(GamepadSettingsRowKind.Toggle, row.Kind);
        var before = shell.Stage.SettingValue;
        var version = shell.Stage.SettingVersion;
        var changes = 0;
        shell.StageChanged += () => changes++;

        Press(shell, GamepadAction.Confirm);

        Assert.NotEqual(before, shell.Stage.SettingValue);
        Assert.Equal(settings.Projection.FocusedRow!.Value, shell.Stage.SettingValue);
        // The row's one-line description opens with the state for Gamepad mode; the Stage shows the state large instead.
        Assert.StartsWith(shell.Stage.SettingValue + " · ", settings.Projection.FocusedRow.Description, StringComparison.Ordinal);
        Assert.False(shell.Stage.SettingDescription.StartsWith(shell.Stage.SettingValue, StringComparison.Ordinal));
        Assert.True(char.IsUpper(shell.Stage.SettingDescription[0]));
        Assert.True(shell.Stage.SettingVersion > version);
        Assert.Equal(0, changes);
        Assert.Contains(shell.Hints, hint => hint is { Button: "A", Label: "Switch" });
    }

    [AvaloniaFact]
    public async Task ATextRow_TypesOnTheDrawnKeyboard_AndDoneSavesItIntoTheRow()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 2);
        var shell = await CreateWithSettingsAsync(library);
        await shell.OpenSettingsAsync();
        var settings = shell.Settings!;

        // Achievements: open the RetroAchievements account, then its first text row.
        Press(shell, GamepadAction.NextPlatform, 2);
        Assert.Equal("Achievements", settings.SectionTitle);
        Press(shell, GamepadAction.NavigateDown);
        Press(shell, GamepadAction.Confirm);
        while (settings.Projection.FocusedRow!.Kind != GamepadSettingsRowKind.Text)
            Press(shell, GamepadAction.NavigateDown);
        var label = settings.Projection.FocusedRow.Label;
        Press(shell, GamepadAction.Confirm);

        var keyboard = Assert.IsType<HandheldTextKeyboardViewModel>(settings.Keyboard);
        Assert.Equal(label, shell.Stage.Title);
        Assert.Equal("Not entered yet", shell.Stage.SettingValue);
        Assert.Contains(shell.Hints, hint => hint is { Button: "X", Label: "Done" });

        // q (row 2), then Shift for a capital W, then a digit from the top row.
        Press(shell, GamepadAction.NavigateDown);
        Press(shell, GamepadAction.Confirm);
        Press(shell, GamepadAction.PreviousPlatform);
        Press(shell, GamepadAction.NavigateRight);
        Press(shell, GamepadAction.Confirm);
        Press(shell, GamepadAction.NavigateUp);
        Press(shell, GamepadAction.Confirm);
        Assert.Equal("qW2", keyboard.Text);
        Assert.Equal("qW2", shell.Stage.SettingValue);
        // B deletes; Y is a space on a plain text row.
        Press(shell, GamepadAction.Cancel);
        Assert.Equal("qW", keyboard.Text);

        Press(shell, GamepadAction.Search);
        Assert.Null(settings.Keyboard);
        Assert.Equal("qW", settings.Projection.FocusedRow!.Value);
        Assert.Equal("qW", shell.Stage.SettingValue);
    }

    [AvaloniaFact]
    public async Task AChoice_OpensItsListOnTheLauncher_WithTheFocusedOneOnTheStage()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 2);
        var shell = await CreateWithSettingsAsync(library);
        await shell.OpenSettingsAsync();
        var settings = shell.Settings!;

        // Emulators: open the first platform, then its emulator choice.
        Press(shell, GamepadAction.NextPlatform);
        Press(shell, GamepadAction.NavigateDown);
        Press(shell, GamepadAction.Confirm);
        Press(shell, GamepadAction.NavigateDown);
        var row = settings.Projection.FocusedRow!;
        Assert.Equal(GamepadSettingsRowKind.Choice, row.Kind);
        Press(shell, GamepadAction.Confirm);

        Assert.True(settings.IsChoicePickerOpen);
        Assert.Equal(row.Label, shell.Stage.Title);
        Assert.Equal(settings.ChoiceOptions[settings.Projection.FocusedChoiceIndex].DisplayName, shell.Stage.SettingValue);
        Press(shell, GamepadAction.NavigateDown);
        Assert.Equal(settings.ChoiceOptions[settings.Projection.FocusedChoiceIndex].DisplayName, shell.Stage.SettingValue);

        // B leaves the choice as it was.
        Press(shell, GamepadAction.Cancel);
        Assert.False(settings.IsChoicePickerOpen);
        Assert.Equal(row.Value, settings.Projection.FocusedRow!.Value);
    }

    [AvaloniaFact]
    public async Task StartSaves_AndClosesSettings()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 2);
        var shell = await CreateWithSettingsAsync(library);
        await shell.OpenSettingsAsync();
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HandheldShellViewModel.Settings) && shell.Settings is null)
                closed.TrySetResult();
        };

        Press(shell, GamepadAction.Menu);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal([true], library.SettingsClosed);
    }

    [Fact]
    public async Task TheSettingsTile_HasNoRemove_AndItsSheetSaysWhereItCanGo()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 2);
        var shell = await CreateAsync(library);
        // ps2, Recent, Settings.
        FocusHome(shell, 2);
        Press(shell, GamepadAction.Actions);

        var sheet = Assert.IsType<HandheldSheetViewModel>(shell.Sheet);
        Assert.Equal("Settings", sheet.Title);
        Assert.Equal(HandheldGlyph.Settings, sheet.SwatchGlyph);
        // No folders of yours yet, so no Add to folder…; and never a Remove of any kind.
        Assert.Equal(["Open", "Move"], sheet.Items.Select(item => item.Label));
        Assert.DoesNotContain(sheet.Items, item => item.Label.Contains("Remove", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheSettingsTile_MovedOnHome_SurvivesAColdStart()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 2);
        var store = new InMemoryHandheldHomeLayoutStore();
        var shell = await CreateAsync(library, store);
        Assert.Equal(2, shell.Layout.IndexOf(HandheldSlot.Settings));

        // Lift it, carry it down a row and drop it on the empty slot there.
        FocusHome(shell, 2);
        Press(shell, GamepadAction.Actions);
        RunSheetRow(shell, "Move");
        Press(shell, GamepadAction.NavigateDown);
        Assert.Equal(HandheldStageKind.SettingsTile, shell.Stage.Kind);
        Press(shell, GamepadAction.Confirm);
        Assert.Equal(7, shell.Layout.IndexOf(HandheldSlot.Settings));

        var again = new HandheldShellViewModel(library, layoutStore: store);
        await again.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.True(again.CurrentPage!.Slots[7].IsSettings);
        Assert.Equal(1, again.Layout.AllSlots().Count(slot => slot.IsSettings));
    }

    [AvaloniaFact]
    public async Task TheSettingsTile_FoldsIntoYourFolder_OpensFromThere_AndGoesBackToHome()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3);
        var store = new InMemoryHandheldHomeLayoutStore();
        var game = library.Games["ps2"][0].Id;
        store.Save(HandheldHomeLayout.FromSlots(
            [HandheldSlot.PlatformFolder("ps2"), HandheldSlot.Recent, HandheldSlot.Settings, HandheldSlot.UserFolder("mine")],
            [new HandheldUserFolder("mine", "Weekend", "#3BBD79", [game])]));
        var shell = await CreateWithSettingsAsync(library, store);

        // Dropped on your folder in move mode, it goes in.
        FocusHome(shell, 2);
        Press(shell, GamepadAction.Actions);
        RunSheetRow(shell, "Move");
        Press(shell, GamepadAction.NavigateRight);
        Assert.True(shell.CurrentPage!.Slots[3].IsDropTarget);
        Press(shell, GamepadAction.Confirm);
        Assert.Equal(-1, shell.Layout.IndexOf(HandheldSlot.Settings));
        Assert.True(shell.Layout.FindFolder("mine")!.HoldsSettings);
        Assert.Equal("Settings is in Weekend", shell.StatusMessage);

        // Inside, it comes first and is not counted as a game; A opens Settings.
        FocusHome(shell, 3);
        Assert.Equal("Your folder · 1 game and Settings", shell.Stage.Subtitle);
        Press(shell, GamepadAction.Confirm);
        var folder = Assert.IsType<HandheldFolderViewModel>(shell.OpenFolder);
        await Task.Yield();
        Assert.True(folder.Items[0].IsSettings);
        Assert.Equal("1 game", folder.CountText);
        Assert.Equal(HandheldStageKind.SettingsTile, shell.Stage.Kind);
        Press(shell, GamepadAction.Confirm);
        await Task.Yield();
        Assert.NotNull(shell.Settings);
        Press(shell, GamepadAction.Cancel);
        Assert.Same(folder, shell.OpenFolder);

        // Y in the folder: Move to home (no Remove); it lands on home's first empty slot.
        Press(shell, GamepadAction.Actions);
        Assert.Equal(["Open", "Move to home"], shell.Sheet!.Items.Select(item => item.Label));
        RunSheetRow(shell, "Move to home");
        Assert.Equal(2, shell.Layout.IndexOf(HandheldSlot.Settings));
        Assert.False(shell.Layout.FindFolder("mine")!.HoldsSettings);
        await Task.Yield();
        Assert.DoesNotContain(shell.OpenFolder!.Items, tile => tile.IsSettings);
    }

    [AvaloniaFact]
    public async Task MovingSettingsOutOfAFolderOfItsOwn_LeavesTheEmptyState_WithBackFocused()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 2);
        var store = new InMemoryHandheldHomeLayoutStore();
        store.Save(HandheldHomeLayout.FromSlots([HandheldSlot.PlatformFolder("ps2"), HandheldSlot.Recent, HandheldSlot.UserFolder("mine")],
            [new HandheldUserFolder("mine", "Weekend", "#3BBD79", [], HoldsSettings: true)]));
        var shell = await CreateAsync(library, store);
        FocusHome(shell, 2);
        Press(shell, GamepadAction.Confirm);
        await Task.Yield();
        var folder = shell.OpenFolder!;
        Assert.False(folder.IsEmpty);
        var changed = new List<string?>();
        folder.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Press(shell, GamepadAction.Actions);
        RunSheetRow(shell, "Move to home");
        await Task.Yield();

        Assert.True(folder.IsEmpty);
        Assert.Contains(nameof(HandheldFolderViewModel.IsEmpty), changed);
        Assert.Equal(HandheldZone.Header, shell.Zone);
        Assert.True(folder.IsBackFocused);
    }

    [Fact]
    public async Task DeletingTheFolderSettingsIsIn_PutsItBackOnHome()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3);
        var store = new InMemoryHandheldHomeLayoutStore();
        store.Save(HandheldHomeLayout.FromSlots([HandheldSlot.PlatformFolder("ps2"), HandheldSlot.Recent, HandheldSlot.UserFolder("mine")],
            [new HandheldUserFolder("mine", "Weekend", "#3BBD79", [], HoldsSettings: true)]));
        var shell = await CreateAsync(library, store);
        Assert.Equal(-1, shell.Layout.IndexOf(HandheldSlot.Settings));

        FocusHome(shell, 2);
        Press(shell, GamepadAction.Actions);
        RunSheetRow(shell, "Delete folder");
        Assert.Equal("Settings goes back on home, where the folder was", shell.Sheet!.Subtitle);
        RunSheetRow(shell, "Delete folder");

        Assert.Equal(2, shell.Layout.IndexOf(HandheldSlot.Settings));
        Assert.Empty(shell.Layout.Folders);
    }
}
