using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Systems;
using EmuShelf.Integrations.Emulators.Android;
using EmuShelf.Rendering.Stage.BigType;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

/// <summary>M47 B14: setup's pacing is tokens.js <c>SetupCount</c>, a pure function of time, the same held still.</summary>
public class HandheldBigTypeSetupMotionTests
{
    [Fact]
    public void TheNumbers_AreTokensJs()
    {
        var tokens = File.ReadAllText(Path.Combine(HandheldGroundColourTests.RepositoryRoot(), "docs", "prototypes", "handheld-big-type", "tokens.js"));
        double Field(string field) => double.Parse(Regex.Match(tokens, $@"\bSetupCount:\s*\{{[^}}]*\b{field}:\s*(?<v>[0-9.]+)").Groups["v"].Value,
            CultureInfo.InvariantCulture);
        Assert.Equal(Field("stepMs"), BigTypeSetupMotion.CountStepMilliseconds);
        Assert.Equal(Field("doneHoldMs"), BigTypeSetupMotion.DoneHoldMilliseconds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACount_IsShownAtOnce_ThenAtMostOnceEveryStep(bool still)
    {
        // The first count shows when it arrives.
        Assert.Equal(1000f, BigTypeSetupMotion.NextCountAt(null, 1000f, still));
        Assert.Equal(0f, BigTypeSetupMotion.WaitBeforeCount(null, 1000f, still));
        // One arriving 100 ms after the last shown waits for the rest of the step; one arriving later shows at once.
        Assert.Equal(1260f, BigTypeSetupMotion.NextCountAt(1000f, 1100f, still));
        Assert.Equal(160f, BigTypeSetupMotion.WaitBeforeCount(1000f, 1100f, still));
        Assert.Equal(1260f, BigTypeSetupMotion.NextCountAt(1000f, 1260f, still));
        Assert.Equal(1500f, BigTypeSetupMotion.NextCountAt(1000f, 1500f, still));
        // Never before now.
        for (var now = 1000f; now < 2000f; now += 37f)
            Assert.True(BigTypeSetupMotion.NextCountAt(1000f, now, still) >= now);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Home_ComesBack_DoneHoldAfterAllSet(bool still)
    {
        Assert.False(BigTypeSetupMotion.HomeDue(0f, still));
        Assert.False(BigTypeSetupMotion.HomeDue(1099f, still));
        Assert.True(BigTypeSetupMotion.HomeDue(1100f, still));
    }
}

public partial class HandheldShellViewModelTests
{
    private sealed partial class FakeLibrary
    {
        /// <summary>Setup's import of one folder (null: none added, nothing counted).</summary>
        public Func<string, string, Action<int>, Task<HandheldSetupImport>>? SetupImport { get; set; }

        public List<(string Folder, string SystemId)> SetupImports { get; } = [];

        public Dictionary<string, HandheldSetupEmulator> SetupEmulators { get; } = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<HandheldSetupEmulator>> DescribeEmulatorsAsync(IReadOnlyList<string> systemIds) =>
            Task.FromResult<IReadOnlyList<HandheldSetupEmulator>>(systemIds
                .Select(id => SetupEmulators.TryGetValue(id, out var emulator) ? emulator : new HandheldSetupEmulator(id, null, null)).ToList());

        public Task<HandheldSetupImport> ImportFolderCountingAsync(string folder, string systemId, Action<int> found)
        {
            SetupImports.Add((folder, systemId));
            return SetupImport?.Invoke(folder, systemId, found) ?? Task.FromResult(new HandheldSetupImport(0, null));
        }
    }

    /// <summary>
    /// M47 B14 through the shell, under Big Type: the empty library says so and A starts setup (as the menu's Add games does), the mock's
    /// three questions with the app's "which system" between, B back through them, the count climbing as the folders are read, "All
    /// set", then home on the first console's folder; nothing found, a failure, the picker cancelled; Classic untouched.
    /// </summary>
    [Collection(nameof(HandheldTokenLookCollection))]
    public class BigTypeSetup
    {
        private static async Task BigType(Func<Task> run)
        {
            HandheldTokens.Use(HandheldLook.BigType);
            try
            {
                await run();
            }
            finally
            {
                HandheldTokens.Use(HandheldLook.Classic);
            }
        }

        private static async Task WaitFor(Func<bool> done, int tries = 600)
        {
            for (var i = 0; i < tries && !done(); i++)
                await Task.Delay(5);
        }

        private static FakeLibrary EmptyLibrary()
        {
            var library = new FakeLibrary { PickedFolder = "/storage/emulated/0/ROMs/PS2" };
            library.MoreImportSystems.Add(new HandheldImportSystem("ps2", "PlayStation 2", "PS2", "#2B5BC7"));
            library.MoreImportSystems.Add(new HandheldImportSystem("gc", "GameCube", "GC", "#5B3FA0"));
            library.SetupEmulators["ps2"] = new HandheldSetupEmulator("ps2", "PCSX2", true);
            library.SetupEmulators["gc"] = new HandheldSetupEmulator("gc", "Dolphin", false);
            return library;
        }

        private static async Task<HandheldShellViewModel> EmptyShellAsync(FakeLibrary library)
        {
            var shell = new HandheldShellViewModel(library);
            await shell.InitializeAsync();
            return shell;
        }

        private static void AssertStage(HandheldShellViewModel shell, string label, string name, string line, string facts)
        {
            Assert.Equal(label, shell.Stage.Label);
            Assert.Equal(name, shell.Stage.Name);
            Assert.Equal(line, shell.Stage.Line);
            Assert.Equal(facts, shell.Stage.Facts);
        }

        private static string Hints(HandheldShellViewModel shell) => string.Join(" ", shell.Hints.Select(hint => $"{hint.Button}:{hint.Label}"));

        [Fact]
        public Task TheEmptyLibrary_SaysSo_AndAStartsSetupOnItsFirstQuestion() => BigType(async () =>
        {
            var library = EmptyLibrary();
            var shell = await EmptyShellAsync(library);
            Assert.True(shell.IsLibraryEmpty);
            // The mock's describe with no library: "Welcome", "No games yet", A starts setup, or the menu.
            AssertStage(shell, "Welcome", "No games yet", HandheldShellViewModel.EmptyLibraryLine, HandheldShellViewModel.EmptyLibraryFacts);
            Assert.Contains(shell.Hints, hint => hint is { Button: "A", Label: "Set up" });

            Press(shell, GamepadAction.Confirm);
            var setup = Assert.IsType<HandheldSetupPanel>(shell.Panel);
            // No picker yet: A opened setup, not Add games' picker.
            Assert.Equal(0, library.FolderPicks);
            Assert.Equal("Setup", setup.Heading);
            Assert.Equal("1 / 4", setup.Count);
            AssertStage(shell, "Setup · 1 of 4", HandheldSetupPanel.WhereQuestion, HandheldSetupPanel.ChooseFolder, string.Empty);
            Assert.Equal("A:Choose B:Back", Hints(shell));

            // B on the first question closes setup, with nothing imported.
            Press(shell, GamepadAction.Cancel);
            Assert.Null(shell.Panel);
            Assert.Empty(library.SetupImports);
            AssertStage(shell, "Welcome", "No games yet", HandheldShellViewModel.EmptyLibraryLine, HandheldShellViewModel.EmptyLibraryFacts);
            shell.Dispose();
        });

        [Fact]
        public Task EveryQuestion_IsAskedInBigType_AnswersAreGivenWithA_AndChangedWithBAndY() => BigType(async () =>
        {
            var library = EmptyLibrary();
            var shell = await EmptyShellAsync(library);
            Press(shell, GamepadAction.Confirm);
            var setup = Assert.IsType<HandheldSetupPanel>(shell.Panel);

            // Choose a folder…: the picker, then which system its games are for (the app's own question, still 1 of 4).
            Press(shell, GamepadAction.Confirm);
            Assert.Equal(1, library.FolderPicks);
            Assert.Equal(HandheldSetupStep.System, setup.Step);
            Assert.Equal(HandheldDirection.Right, shell.LastMoveDirection);
            AssertStage(shell, "Setup · 1 of 4", HandheldSetupPanel.SystemQuestion, "PlayStation 2", "For the games in PS2");
            // B there drops the folder.
            Press(shell, GamepadAction.Cancel);
            Assert.Equal(HandheldSetupStep.Where, setup.Step);
            Assert.Empty(setup.Folders);
            Assert.Equal(HandheldDirection.Left, shell.LastMoveDirection);

            // Again, this time GameCube: the folder is a row, focused, its system as its value.
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.NavigateDown);
            Press(shell, GamepadAction.Confirm);
            Assert.Equal(HandheldSetupStep.Where, setup.Step);
            AssertStage(shell, "Setup · 1 of 4", HandheldSetupPanel.WhereQuestion, "PS2", "GameCube");
            Assert.Equal("A:Next Y:Remove B:Back", Hints(shell));
            Assert.Equal(["PS2", HandheldSetupPanel.ChooseFolder], setup.Rows.Select(row => row.Label));

            // The same folder picked again is for the system chosen last; Y removes a folder; the answer changes.
            Press(shell, GamepadAction.NavigateDown);
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.Confirm);
            Assert.Single(setup.Folders);
            Assert.Equal("ps2", setup.Folders[0].SystemId);
            Press(shell, GamepadAction.Actions);
            Assert.Empty(setup.Folders);
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.Confirm);
            Assert.Equal("ps2", Assert.Single(setup.Folders).SystemId);

            // A on a folder goes on: the emulators that will run its games, the mock's Found.
            Press(shell, GamepadAction.Confirm);
            await WaitFor(() => setup.Step == HandheldSetupStep.Emulators);
            Assert.Equal("2 / 4", setup.Count);
            AssertStage(shell, "Setup · 2 of 4", HandheldSetupPanel.EmulatorsQuestion, "PCSX2", "Found");
            Assert.Equal("A:Next B:Back", Hints(shell));
            // B goes back to where the games are, A comes back.
            Press(shell, GamepadAction.Cancel);
            Assert.Equal(HandheldSetupStep.Where, setup.Step);
            Press(shell, GamepadAction.Confirm);
            await WaitFor(() => setup.Step == HandheldSetupStep.Emulators);

            Press(shell, GamepadAction.Confirm);
            Assert.Equal("3 / 4", setup.Count);
            AssertStage(shell, "Setup · 3 of 4", HandheldSetupPanel.AchievementsQuestion, HandheldSetupPanel.ConnectAchievements, "Sign in");
            Press(shell, GamepadAction.NavigateDown);
            AssertStage(shell, "Setup · 3 of 4", HandheldSetupPanel.AchievementsQuestion, HandheldSetupPanel.SkipAchievements, "You can connect later");
            // Past the last row it bumps.
            Assert.True(shell.DispatchGamepadAction(GamepadAction.NavigateDown));
            Press(shell, GamepadAction.Cancel);
            Assert.Equal(HandheldSetupStep.Emulators, setup.Step);
            Assert.Empty(library.SetupImports);
            shell.Dispose();
        });

        [Fact]
        public Task TheLastStep_CountsTheGamesAsTheyAreFound_SaysAllSet_AndLandsOnTheFirstFolder() => BigType(async () =>
        {
            var library = EmptyLibrary();
            var shell = await EmptyShellAsync(library);
            var go = new TaskCompletionSource();
            var counts = new List<string>();
            library.SetupImport = async (folder, systemId, found) =>
            {
                found(3);
                found(9);
                await go.Task;
                found(17);
                library.WithPlatform("ps2", 17);
                await shell.InitializeAsync();
                return new HandheldSetupImport(17, null);
            };
            Press(shell, GamepadAction.Confirm);
            var setup = Assert.IsType<HandheldSetupPanel>(shell.Panel);
            setup.Changed += () => counts.Add(shell.Stage.Name);
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.Confirm);
            await WaitFor(() => setup.Step == HandheldSetupStep.Emulators);
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.Confirm); // Connect RetroAchievements

            Assert.Equal(HandheldSetupStep.Counting, setup.Step);
            Assert.Equal("4 / 4", setup.Count);
            Assert.True(setup.ConnectsAchievements);
            Assert.Equal(("/storage/emulated/0/ROMs/PS2", "ps2"), Assert.Single(library.SetupImports));
            // The count is typed as the keyboard's word is; the rows are gone; nothing can be pressed while folders are read.
            Assert.True(shell.Stage.IsTyped);
            Assert.Empty(setup.Rows);
            Assert.Empty(shell.Hints);
            Assert.Equal("Setup · 4 of 4", shell.Stage.Label);
            Assert.Equal("Looking through PS2", shell.Stage.Line);
            // 3 at once; 9 waits its turn (the mock's 260 ms), so 3 then 9.
            Assert.Equal("3 games", shell.Stage.Name);
            await WaitFor(() => shell.Stage.Name == "9 games");
            Assert.Equal("9 games", shell.Stage.Name);
            Assert.Equal(HandheldSetupStep.Counting, setup.Step);

            go.SetResult();
            await WaitFor(() => shell.Stage.Line == HandheldSetupPanel.AllSet);
            Assert.Equal("17 games", shell.Stage.Name);
            Assert.True(setup.IsDone);
            // The counts climbed, never back.
            var shown = counts.Where(name => name.EndsWith(" games", StringComparison.Ordinal))
                .Select(name => int.Parse(name.Split(' ')[0], CultureInfo.InvariantCulture)).ToList();
            Assert.Equal(shown.OrderBy(n => n), shown);

            // "All set" holds, then home: on the first console's folder.
            await WaitFor(() => shell.Panel is null);
            Assert.Null(shell.Panel);
            Assert.False(shell.IsLibraryEmpty);
            Assert.Equal(HandheldStageKind.Folder, shell.Stage.Kind);
            Assert.Equal("PS2 System", shell.Stage.Title);
            // Connect RetroAchievements: Settings is asked to open once setup is done.
            await WaitFor(() => library.SettingsOpened == 1);
            Assert.Equal(1, library.SettingsOpened);
            shell.Dispose();
        });

        [Fact]
        public Task AScanThatFindsNothing_SaysSo_AndBGoesBackToChooseAnother() => BigType(async () =>
        {
            var library = EmptyLibrary();
            var shell = await EmptyShellAsync(library);
            Press(shell, GamepadAction.Confirm);
            var setup = Assert.IsType<HandheldSetupPanel>(shell.Panel);
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.Confirm);
            await WaitFor(() => setup.Step == HandheldSetupStep.Emulators);
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.NavigateDown);
            Press(shell, GamepadAction.Confirm); // Skip for now
            Assert.False(setup.ConnectsAchievements);
            await WaitFor(() => shell.Stage.Line.StartsWith("No games found", StringComparison.Ordinal));
            AssertStage(shell, "Setup · 4 of 4", "0 games", "No games found in PS2", string.Empty);
            Assert.Equal("B:Back", Hints(shell));
            Assert.False(setup.IsDone);
            Assert.Same(setup, shell.Panel);

            Press(shell, GamepadAction.Cancel);
            Assert.Equal(HandheldSetupStep.Where, setup.Step);
            // The folder chosen is still there to change.
            Assert.Equal("PS2", setup.Rows[0].Label);
            Assert.True(shell.IsLibraryEmpty);
            shell.Dispose();
        });

        [Fact]
        public Task AFolderWhoseGamesAreAlreadyHere_SaysSo_NotThatNoneWereFound() => BigType(async () =>
        {
            var library = EmptyLibrary();
            library.SetupImport = (_, _, found) =>
            {
                found(9);
                return Task.FromResult(new HandheldSetupImport(0, null));
            };
            var shell = await EmptyShellAsync(library);
            Press(shell, GamepadAction.Confirm);
            var setup = Assert.IsType<HandheldSetupPanel>(shell.Panel);
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.Confirm);
            await WaitFor(() => setup.Step == HandheldSetupStep.Emulators);
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.Confirm);
            await WaitFor(() => shell.Stage.Line.StartsWith("Nothing new", StringComparison.Ordinal));
            Assert.Equal("Nothing new in PS2: its games are already here", shell.Stage.Line);
            Assert.False(setup.IsDone);
            Assert.Equal("B:Back", Hints(shell));
            shell.Dispose();
        });

        [Fact]
        public Task AFolderThatCannotBeRead_SaysWhy_AndBGoesBack() => BigType(async () =>
        {
            var library = EmptyLibrary();
            library.SetupImport = (_, _, _) => Task.FromResult(new HandheldSetupImport(0, "Access denied"));
            var shell = await EmptyShellAsync(library);
            Press(shell, GamepadAction.Confirm);
            var setup = Assert.IsType<HandheldSetupPanel>(shell.Panel);
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.Confirm);
            await WaitFor(() => setup.Step == HandheldSetupStep.Emulators);
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.Confirm);
            await WaitFor(() => shell.Stage.Line.StartsWith("Could not read", StringComparison.Ordinal));
            Assert.Equal("Could not read PS2: Access denied", shell.Stage.Line);
            Assert.True(setup.Card.IsWarning);
            Assert.Equal("B:Back", Hints(shell));
            Press(shell, GamepadAction.Cancel);
            Assert.Equal(HandheldSetupStep.Where, setup.Step);
            shell.Dispose();
        });

        [Fact]
        public Task ThePickerCancelled_StaysOnTheQuestion() => BigType(async () =>
        {
            var library = EmptyLibrary();
            library.PickedFolder = null;
            var shell = await EmptyShellAsync(library);
            Press(shell, GamepadAction.Confirm);
            var setup = Assert.IsType<HandheldSetupPanel>(shell.Panel);
            Press(shell, GamepadAction.Confirm);
            Assert.Equal(1, library.FolderPicks);
            Assert.Equal(HandheldSetupStep.Where, setup.Step);
            AssertStage(shell, "Setup · 1 of 4", HandheldSetupPanel.WhereQuestion, HandheldSetupPanel.ChooseFolder, string.Empty);
            shell.Dispose();
        });

        [Fact]
        public Task TheMenusAddGames_StartsSetupToo_AndOnALibraryWithGames_EndsWhereItWasWithWhatItFound() => BigType(async () =>
        {
            var library = new FakeLibrary { PickedFolder = "/storage/emulated/0/ROMs/GC" }.WithPlatform("ps2", 2).WithPlatform("gc", 1);
            library.SetupImport = (_, _, found) =>
            {
                found(2);
                return Task.FromResult(new HandheldSetupImport(2, null));
            };
            var shell = await CreateAsync(library);
            var before = shell.HomePosition;
            var addGames = shell.MenuItems.Single(item => item.Label == "Add games");
            await addGames.Invoke();
            var setup = Assert.IsType<HandheldSetupPanel>(shell.Panel);
            Assert.Equal(0, library.FolderPicks);
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.NavigateDown);
            Press(shell, GamepadAction.Confirm); // GC System
            Press(shell, GamepadAction.Confirm);
            await WaitFor(() => setup.Step == HandheldSetupStep.Emulators);
            // No emulator could be named: the system, and where to choose one.
            AssertStage(shell, "Setup · 2 of 4", HandheldSetupPanel.EmulatorsQuestion, "GC System", "Choose one in Settings");
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.NavigateDown);
            Press(shell, GamepadAction.Confirm);
            await WaitFor(() => shell.Panel is null);
            Assert.Equal(("/storage/emulated/0/ROMs/GC", "gc"), Assert.Single(library.SetupImports));
            // The mock's closePanel('Found 2 new games'): back where it was, the line says what was found.
            Assert.Equal("Found 2 new games", shell.Notice);
            Assert.Equal(before, shell.HomePosition);
            Assert.Equal(0, library.SettingsOpened);
            shell.Dispose();
        });

        [Fact]
        public async Task Classic_KeepsAddGamesPickerAndSheet_AndTheEmptyLibrarysWords()
        {
            var library = EmptyLibrary();
            var shell = await EmptyShellAsync(library);
            Assert.Equal("Add games", shell.Stage.Title);
            Assert.Equal("Pick the folder your games are in", shell.Stage.Subtitle);
            Assert.Contains(shell.Hints, hint => hint is { Button: "A", Label: "Add games" });
            Press(shell, GamepadAction.Confirm);
            await Task.Yield();
            Assert.Null(shell.Panel);
            Assert.Equal(1, library.FolderPicks);
            Assert.Equal("Add games", Assert.IsType<HandheldSheetViewModel>(shell.Sheet).Title);
            shell.Dispose();
        }
    }
}
