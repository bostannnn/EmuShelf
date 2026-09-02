using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Input;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Settings;
using EmuShelf.Core.TexturePacks;
using EmuShelf.Core.Updates;
using EmuShelf.Integrations.Emulators;
using EmuShelf.Integrations.Emulators.Android;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.Tests;

public sealed class GamepadSettingsViewModelTests
{
    private readonly FakeDialogService _dialogs = new();
    private readonly RecordingConfigurationStore _configurations = new();

    [AvaloniaFact]
    public void ShoulderSections_MirrorDesktopStructureAndRestoreEachRowsFocus()
    {
        using var viewModel = CreateGamepadSettings(
            retroAchievements: CreateRetroAchievementsContext(),
            cloudSaves: CreateCloudContext(),
            texturePacks: CreateTextureContext());

        // Both modes present the same sections in the same order (only Themes is a separate gallery
        // page); Emulators is now a couch section rather than a Desktop-only slice.
        Assert.Equal(
            [
                SettingsSection.General, SettingsSection.Emulators, SettingsSection.RetroAchievements,
                SettingsSection.Saves, SettingsSection.TexturePacks, SettingsSection.About,
            ],
            viewModel.Sections);

        viewModel.Dispatch(GamepadAction.NavigateDown);
        var rememberedGeneralRow = viewModel.FocusedRow!.Key;

        viewModel.SelectedSection = SettingsSection.RetroAchievements;
        viewModel.Dispatch(GamepadAction.NavigateDown);
        var rememberedRetroRow = viewModel.FocusedRow!.Key;

        viewModel.SelectedSection = SettingsSection.General;
        Assert.Equal(rememberedGeneralRow, viewModel.FocusedRow!.Key);
        viewModel.SelectedSection = SettingsSection.RetroAchievements;
        Assert.Equal(rememberedRetroRow, viewModel.FocusedRow!.Key);

        // Shoulder buttons still page through the sections, now including Emulators between them.
        Assert.True(viewModel.Dispatch(GamepadAction.PreviousPlatform));
        Assert.Equal(SettingsSection.Emulators, viewModel.SelectedSection);
    }

    [AvaloniaFact]
    public void SectionPaging_PlacesThemesBeforeAbout_MirroringDesktopOrder()
    {
        var choices = ThemeCatalog.All.Select(theme => new ThemeChoiceViewModel(theme)).ToArray();
        using var viewModel = CreateGamepadSettings(
            retroAchievements: CreateRetroAchievementsContext(),
            screenScraper: CreateScreenScraperContext(),
            cloudSaves: CreateCloudContext(),
            texturePacks: CreateTextureContext(),
            themeChoices: choices);

        SettingsSection CurrentPage() =>
            viewModel.IsThemesSection ? SettingsSection.Themes : viewModel.SelectedSection;

        // Page right (RB) from the first section to the last, recording each page. The Themes gallery is
        // its own page, but it must occupy Desktop's slot — right before About, which stays last — so
        // both surfaces read top-to-bottom identically. RB clamps at the final page.
        var order = new List<SettingsSection> { CurrentPage() };
        for (var step = 0; step < 16; step++)
        {
            viewModel.Dispatch(GamepadAction.NextPlatform);
            var page = CurrentPage();
            if (page == order[^1])
                break;
            order.Add(page);
        }

        Assert.Equal(
            [
                SettingsSection.General, SettingsSection.Emulators, SettingsSection.RetroAchievements,
                SettingsSection.ArtworkMetadata, SettingsSection.Saves, SettingsSection.TexturePacks,
                SettingsSection.Themes, SettingsSection.About,
            ],
            order);
    }

    [AvaloniaFact]
    public async Task TextAndSecretEntry_CommitToExistingModelWithoutExposingTheSecret()
    {
        var keyboard = new RecordingOnScreenKeyboardService();
        using var viewModel = CreateGamepadSettings(
            retroAchievements: CreateRetroAchievementsContext(),
            onScreenKeyboard: keyboard);
        viewModel.SelectedSection = SettingsSection.RetroAchievements;

        var username = viewModel.Rows.Single(row => row.Key == "retro.username");
        await username.SelectCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsTextEntryOpen);
        Assert.False(viewModel.IsSecretEntry);
        viewModel.DraftText = "couch-player";
        viewModel.RequestOnScreenKeyboard();
        viewModel.Dispatch(GamepadAction.Confirm);

        Assert.Equal("couch-player", viewModel.Settings.RetroAchievementsUsername);
        Assert.Equal("retro.username", viewModel.FocusedRow!.Key);
        Assert.Equal(1, keyboard.Requests);
        Assert.False(keyboard.LastRequest!.IsSecret);

        var secret = viewModel.Rows.Single(row => row.Key == "retro.api-key");
        await secret.SelectCommand.ExecuteAsync(null);
        viewModel.DraftText = "do-not-display-this-key";
        viewModel.RequestOnScreenKeyboard();
        viewModel.Dispatch(GamepadAction.Confirm);

        Assert.Equal("do-not-display-this-key", viewModel.Settings.RetroAchievementsApiKey);
        Assert.DoesNotContain(viewModel.Rows, row => row.Value.Contains("do-not-display", StringComparison.Ordinal));
        Assert.Equal(string.Empty, viewModel.DraftText);
        Assert.True(keyboard.LastRequest!.IsSecret);
    }

    [AvaloniaFact]
    public async Task ScreenScraperSection_ConnectsThroughExistingModelAndSwitchesToConnectedRows()
    {
        string? connectedUser = null;
        string? connectedPassword = null;
        using var viewModel = CreateGamepadSettings(
            screenScraper: CreateScreenScraperContext(
                onConnect: (username, password) =>
                {
                    connectedUser = username;
                    connectedPassword = password;
                }));

        Assert.Contains(SettingsSection.ArtworkMetadata, viewModel.Sections);

        viewModel.SelectedSection = SettingsSection.ArtworkMetadata;
        Assert.True(viewModel.IsArtworkMetadataSection);
        Assert.Equal("Artwork & Metadata", viewModel.SectionTitle);

        // Disconnected: the section offers username, a masked password, and connect.
        var username = viewModel.Rows.Single(row => row.Key == "scraper.username");
        await username.SelectCommand.ExecuteAsync(null);
        viewModel.DraftText = "collector";
        viewModel.Dispatch(GamepadAction.Confirm);

        var password = viewModel.Rows.Single(row => row.Key == "scraper.password");
        await password.SelectCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsSecretEntry);
        viewModel.DraftText = "s3cret-pass";
        viewModel.Dispatch(GamepadAction.Confirm);

        Assert.Equal("collector", viewModel.Settings.ScreenScraperUsername);
        Assert.DoesNotContain(viewModel.Rows, row => row.Value.Contains("s3cret", StringComparison.Ordinal));

        var connect = viewModel.Rows.Single(row => row.Key == "scraper.connect");
        await connect.SelectCommand.ExecuteAsync(null);

        Assert.Equal("collector", connectedUser);
        Assert.Equal("s3cret-pass", connectedPassword);
        Assert.Equal("collector", viewModel.Settings.ScreenScraperConnectedName);

        // Connected: the entry rows collapse into the account summary and a disconnect action.
        Assert.Contains(viewModel.Rows, row => row.Key == "scraper.disconnect");
        Assert.DoesNotContain(viewModel.Rows, row => row.Key == "scraper.connect");
    }

    [AvaloniaFact]
    public void ScreenScraperSection_IsOmittedWhenNoAccountContextIsProvided()
    {
        using var viewModel = CreateGamepadSettings(retroAchievements: CreateRetroAchievementsContext());

        Assert.DoesNotContain(SettingsSection.ArtworkMetadata, viewModel.Sections);
    }

    [AvaloniaFact]
    public async Task ArtworkMetadataPill_ShowsFetchSummary_AndGeneralRescanIsNotMasked()
    {
        // Metadata fetch now lives in the Artwork & Metadata section; rescan stays in General. A
        // completed fetch must show its summary in the Artwork & Metadata pill (not the frozen
        // "Fetching N of N" progress line), and must not leak into the separate General rescan pill.
        var maintenance = new LibraryMaintenanceActions(
            RescanSystem: (_, _) => Task.FromResult(string.Empty),
            RescanAll: _ => Task.FromResult("Rescan complete — no new games"),
            FetchAllMetadata: progress =>
            {
                progress.Report(new MetadataEnrichmentProgress(40, 40, "Final Fantasy"));
                return Task.FromResult("Added 40 covers");
            });
        using var viewModel = CreateGamepadSettings(maintenance, screenScraper: CreateScreenScraperContext());
        viewModel.SelectedSection = SettingsSection.ArtworkMetadata;

        await viewModel.Rows.Single(row => row.Key == "general.fetch-metadata").SelectCommand.ExecuteAsync(null);
        // The completion summary shows — not the frozen "Fetching 40 of 40" progress line.
        Assert.Equal("Added 40 covers", viewModel.StatusText);
        Assert.Equal(string.Empty, viewModel.Settings.MetadataProgressText);
        Assert.False(viewModel.Settings.HasMetadataProgress);

        viewModel.SelectedSection = SettingsSection.General;
        await viewModel.Rows.Single(row => row.Key == "general.rescan").SelectCommand.ExecuteAsync(null);
        // The General rescan pill shows its own status; the earlier metadata summary does not mask it.
        Assert.Equal("Rescan complete — no new games", viewModel.StatusText);
    }

    [AvaloniaFact]
    public async Task EmulatorsSection_ExposesRpcs3LibrarySync_TheOnlyControllerPathToImportPs3()
    {
        var synced = 0;
        var maintenance = new LibraryMaintenanceActions(
            (_, _) => Task.FromResult(string.Empty),
            _ => Task.FromResult(string.Empty),
            SyncRpcs3Library: () =>
            {
                synced++;
                return Task.FromResult("RPCS3 library sync complete — 2 added");
            });
        using var viewModel = CreateGamepadSettings(maintenance);
        viewModel.SelectedSection = SettingsSection.Emulators;
        await ExpandPlatformAsync(viewModel, "playstation3");

        // Same stable id and command as Desktop's PS3-row "Sync RPCS3 library" button — now reachable
        // on a controller because Emulators is a couch section instead of Desktop-only.
        var row = viewModel.Rows.Single(candidate => candidate.Key == "emulators.playstation3.sync");
        await row.SelectCommand.ExecuteAsync(null);

        Assert.Equal(1, synced);
        Assert.Equal("RPCS3 library sync complete — 2 added", viewModel.StatusText);
    }

    [AvaloniaFact]
    public async Task EmulatorsSection_OmitsRpcs3Sync_WhenTheMaintenanceActionIsUnavailable()
    {
        var maintenance = new LibraryMaintenanceActions(
            (_, _) => Task.FromResult(string.Empty),
            _ => Task.FromResult(string.Empty));
        using var viewModel = CreateGamepadSettings(maintenance);
        viewModel.SelectedSection = SettingsSection.Emulators;
        await ExpandPlatformAsync(viewModel, "playstation3");

        Assert.DoesNotContain(viewModel.Rows, row => row.Key == "emulators.playstation3.sync");
    }

    [AvaloniaFact]
    public async Task EmulatorsSection_CloseOnReturnToggle_AppearsOnlyWhenWired_AndPersistsOnSave()
    {
        bool? saved = null;
        var maintenance = new LibraryMaintenanceActions(
            (_, _) => Task.FromResult(string.Empty),
            _ => Task.FromResult(string.Empty),
            GetCloseEmulatorOnReturn: () => true,
            SetCloseEmulatorOnReturn: value =>
            {
                saved = value;
                return Task.CompletedTask;
            });
        using var viewModel = CreateGamepadSettings(maintenance);
        viewModel.SelectedSection = SettingsSection.Emulators;

        var row = viewModel.Rows.Single(candidate => candidate.Key == "emulators.close-on-return");
        Assert.Equal("CLOSE", row.Value);

        // Activating the toggle flips it (the row re-renders to KEEP) and Save persists the new value.
        await row.SelectCommand.ExecuteAsync(null);
        Assert.False(viewModel.CloseEmulatorOnReturn);
        Assert.Equal("KEEP", viewModel.Rows.Single(candidate => candidate.Key == "emulators.close-on-return").Value);

        await viewModel.Settings.SaveCommand.ExecuteAsync(null);
        Assert.False(saved);
    }

    [AvaloniaFact]
    public void EmulatorsSection_OmitsCloseOnReturnToggle_WhenNotWired()
    {
        // Desktop wires no close-on-return delegate, so the row is absent.
        var maintenance = new LibraryMaintenanceActions(
            (_, _) => Task.FromResult(string.Empty),
            _ => Task.FromResult(string.Empty));
        using var viewModel = CreateGamepadSettings(maintenance);
        viewModel.SelectedSection = SettingsSection.Emulators;

        Assert.DoesNotContain(viewModel.Rows, row => row.Key == "emulators.close-on-return");
    }

    [AvaloniaFact]
    public async Task GeneralSection_OffersChangeDataFolder_WhenTheHostWiresIt()
    {
        // Android has no file manager to "open" the folder in, so instead the General section offers a way
        // to move the user-chosen data folder. Activating it runs the host pick and surfaces any rejection.
        DataLocationPickResult result = DataLocationPickResult.Failed("EmuShelf can't write to that folder.");
        var maintenance = new LibraryMaintenanceActions(
            (_, _) => Task.FromResult(string.Empty),
            _ => Task.FromResult(string.Empty),
            DataDirectory: "/storage/emulated/0/EmuShelf",
            ChangeDataFolder: () => Task.FromResult(result));
        using var viewModel = CreateGamepadSettings(maintenance);
        viewModel.SelectedSection = SettingsSection.General;

        var row = viewModel.Rows.Single(candidate => candidate.Key == "general.change-data-folder");
        Assert.True(row.IsAction);
        // The current location is shown in place of a file-manager button, and the row is surface-specific
        // (no Desktop counterpart), so it stays out of the cross-surface parity comparison.
        Assert.Contains("/storage/emulated/0/EmuShelf", row.Description);
        Assert.Equal(string.Empty, row.ParityId);

        await row.SelectCommand.ExecuteAsync(null);
        Assert.Equal("EmuShelf can't write to that folder.", viewModel.Settings.DataFolderStatusText);
        // The refusal is also visible on the couch status pill, not only Desktop's inline label.
        Assert.Equal("EmuShelf can't write to that folder.", viewModel.StatusText);
    }

    [AvaloniaFact]
    public void GeneralSection_OmitsChangeDataFolder_WhenNotWired()
    {
        // Desktop reveals the folder in a file manager instead of moving it, so it wires no delegate.
        var maintenance = new LibraryMaintenanceActions(
            (_, _) => Task.FromResult(string.Empty),
            _ => Task.FromResult(string.Empty),
            DataDirectory: "/portable/EmuShelf");
        using var viewModel = CreateGamepadSettings(maintenance);
        viewModel.SelectedSection = SettingsSection.General;

        Assert.DoesNotContain(viewModel.Rows, row => row.Key == "general.change-data-folder");
    }

    [AvaloniaFact]
    public async Task AndroidEmulatorChoice_ListsStandaloneAndCoreEntriesAndPersistsThePair()
    {
        using var viewModel = CreateGamepadSettings(
            androidEmulatorChoices: AndroidEmulatorChoiceCatalog.BySystem);
        viewModel.SelectedSection = SettingsSection.Emulators;
        await ExpandPlatformAsync(viewModel, "nds");

        string[] expectedChoices =
        [
            "WatermelonDS", "melonDS", "melonDS (nightly)",
            "RetroArch · melonDS DS", "RetroArch · melonDS", "RetroArch · DeSmuME",
        ];
        var settingsRow = viewModel.Settings.Rows.Single(row => row.SystemId == "nds");
        Assert.Equal(expectedChoices, settingsRow.AvailableChoices.Select(choice => choice.DisplayName));
        Assert.Equal("watermelonds", settingsRow.EmulatorId);
        Assert.Null(settingsRow.SelectedChoice?.CorePath);
        var emulatorRow = viewModel.Rows.Single(row => row.Key == "emulators.nds.emulator");
        Assert.Equal("WatermelonDS", emulatorRow.Value);

        // A opens an explicit list without changing the value. Walking down to the first
        // RetroArch-core-as-emulator item and confirming persists the (emulator, core) pair; the
        // standalone builds above it carry no core.
        await emulatorRow.SelectCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsChoicePickerOpen);
        Assert.Equal(expectedChoices, viewModel.ChoiceOptions.Select(option => option.DisplayName));
        Assert.Equal("watermelonds", settingsRow.EmulatorId);
        viewModel.Dispatch(GamepadAction.NavigateDown);
        viewModel.Dispatch(GamepadAction.NavigateDown);
        viewModel.Dispatch(GamepadAction.NavigateDown);
        viewModel.Dispatch(GamepadAction.Confirm);
        Assert.False(viewModel.IsChoicePickerOpen);
        var expectedPath = AndroidRetroArchCoreCatalog.BySystem["nds"][0].Path;
        Assert.Equal("retroarch", settingsRow.EmulatorId);
        Assert.Equal(expectedPath, settingsRow.CorePath);

        // Direct Left/Right adjustment is symmetric and wraps, so a quick change never requires
        // blindly pressing A through the whole list. Left lands on the standalone nightly channel,
        // which is its own emulator and carries no core.
        viewModel.Dispatch(GamepadAction.NavigateLeft);
        Assert.Equal("melonds-nightly", settingsRow.EmulatorId);
        Assert.Null(settingsRow.SelectedChoice?.CorePath);
        viewModel.Dispatch(GamepadAction.NavigateRight);
        Assert.Equal("retroarch", settingsRow.EmulatorId);
        Assert.Equal(expectedPath, settingsRow.CorePath);

        await viewModel.Settings.SaveCommand.ExecuteAsync(null);
        var saved = Assert.Single(
            _configurations.SavedConfigurations,
            configuration => configuration.SystemId == "nds" && configuration.EmulatorId == "retroarch");
        Assert.Equal(expectedPath, saved.CorePath);
        Assert.Equal("retroarch", _configurations.ActiveEmulators["nds"]);

        var resolution = AndroidLaunchResolver.Resolve(
            "nds",
            "/storage/emulated/0/ROMs/DS/game.nds",
            AndroidEmulatorLaunchProfiles.RetroArch.Id,
            saved.CorePath);
        Assert.True(resolution.Success, resolution.FailureReason);
        Assert.Equal(expectedPath, resolution.Intent!.StringExtras["LIBRETRO"]);
    }

    [AvaloniaFact]
    public async Task AndroidOnlyStandaloneChoices_PersistTheirShortIdsWithoutCorePaths()
    {
        using var viewModel = CreateGamepadSettings(
            androidEmulatorChoices: AndroidEmulatorChoiceCatalog.BySystem);

        var ds = viewModel.Settings.Rows.Single(row => row.SystemId == "nds");
        var ps2 = viewModel.Settings.Rows.Single(row => row.SystemId == "playstation2");
        Assert.Equal("watermelonds", ds.EmulatorId);
        Assert.Equal("armsx2", ps2.EmulatorId);

        await viewModel.Settings.SaveCommand.ExecuteAsync(null);

        var savedDs = Assert.Single(_configurations.SavedConfigurations, configuration =>
            configuration.SystemId == "nds" && configuration.EmulatorId == "watermelonds");
        var savedPs2 = Assert.Single(_configurations.SavedConfigurations, configuration =>
            configuration.SystemId == "playstation2" && configuration.EmulatorId == "armsx2");
        Assert.Null(savedDs.CorePath);
        Assert.Null(savedPs2.CorePath);
        Assert.Equal("watermelonds", _configurations.ActiveEmulators["nds"]);
        Assert.Equal("armsx2", _configurations.ActiveEmulators["playstation2"]);
    }

    [AvaloniaFact]
    public async Task EmulatorChoice_IsOnlyProjectedWhenAndroidSuppliesTheCatalog()
    {
        using var viewModel = CreateGamepadSettings();
        viewModel.SelectedSection = SettingsSection.Emulators;
        await ExpandPlatformAsync(viewModel, "nds");

        Assert.DoesNotContain(viewModel.Rows, row => row.Key.EndsWith(".emulator", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task EmulatorsSection_ListsOnePlatformSummaryEach_AndExpandsOnlyOneInPlace()
    {
        var maintenance = new LibraryMaintenanceActions(
            (_, _) => Task.FromResult(string.Empty),
            _ => Task.FromResult(string.Empty));
        using var viewModel = CreateGamepadSettings(
            maintenance,
            androidEmulatorChoices: AndroidEmulatorChoiceCatalog.BySystem,
            gameCountBySystem: systemId => systemId == "nds" ? 131 : 0);
        viewModel.SelectedSection = SettingsSection.Emulators;

        // Collapsed: one focusable summary per platform, no per-platform rows, no non-focusable headers.
        var summaries = viewModel.Rows.Where(row => row.IsSummary).ToList();
        Assert.Equal(viewModel.Settings.Rows.Count, summaries.Count);
        Assert.DoesNotContain(viewModel.Rows, row => row.IsHeader);
        Assert.DoesNotContain(viewModel.Rows, row => row.Key.EndsWith(".emulator", StringComparison.Ordinal));
        var ds = summaries.Single(row => row.SystemId == "nds");
        Assert.Equal("Nintendo DS", ds.Label);
        Assert.Equal("WatermelonDS · 131 games", ds.Value);
        Assert.False(ds.IsExpanded);
        Assert.True(ds.IsCompact);

        // A expands that platform beneath its summary; expanding another collapses it.
        await ds.SelectCommand.ExecuteAsync(null);
        ds = viewModel.Rows.Single(row => row.Key == "emulators.nds.summary");
        Assert.True(ds.IsExpanded);
        Assert.Contains(viewModel.Rows, row => row.Key == "emulators.nds.emulator");
        Assert.Same(ds, viewModel.FocusedRow);
        await ExpandPlatformAsync(viewModel, "gba");
        Assert.False(viewModel.Rows.Single(row => row.Key == "emulators.nds.summary").IsExpanded);
        Assert.DoesNotContain(viewModel.Rows, row => row.Key == "emulators.nds.emulator");
        Assert.Contains(viewModel.Rows, row => row.Key == "emulators.gba.emulator");
    }

    [AvaloniaFact]
    public async Task EmulatorsSection_YOnASummaryRescansThatPlatform()
    {
        var rescanned = new List<string>();
        var maintenance = new LibraryMaintenanceActions(
            (systemId, _) =>
            {
                rescanned.Add(systemId);
                return Task.FromResult("Rescan complete — no new games");
            },
            _ => Task.FromResult(string.Empty));
        using var viewModel = CreateGamepadSettings(maintenance);
        viewModel.SelectedSection = SettingsSection.Emulators;

        var index = viewModel.Rows.ToList().FindIndex(row => row.Key == "emulators.snes.summary");
        viewModel.FocusedRowIndex = index;
        Assert.Equal("Rescan", viewModel.ActionsHint);
        Assert.True(viewModel.Dispatch(GamepadAction.Actions));
        await Task.Delay(50);

        Assert.Equal(["snes"], rescanned);
        // The platform stays collapsed: Y never changes the list shape.
        Assert.False(viewModel.Rows.Single(row => row.Key == "emulators.snes.summary").IsExpanded);
    }

    [AvaloniaFact]
    public async Task EmulatorsSection_SaysWhenTheChosenAndroidEmulatorIsNotInstalled()
    {
        using var viewModel = CreateGamepadSettings(
            androidEmulatorChoices: AndroidEmulatorChoiceCatalog.BySystem,
            gameCountBySystem: _ => 234,
            isEmulatorChoiceInstalled: choice => choice.EmulatorId != "armsx2");
        viewModel.SelectedSection = SettingsSection.Emulators;

        var ps2 = viewModel.Rows.Single(row => row.Key == "emulators.playstation2.summary");
        Assert.True(ps2.IsWarning);
        Assert.Equal("ARMSX2 not installed · 234 games", ps2.Value);
        Assert.False(viewModel.Rows.Single(row => row.Key == "emulators.nds.summary").IsWarning);
        Assert.Equal("PlayStation 2 needs attention", viewModel.EmulatorsRailStatus);
        Assert.True(viewModel.IsEmulatorsRailWarning);

        await ps2.SelectCommand.ExecuteAsync(null);
        var choice = viewModel.Rows.Single(row => row.Key == "emulators.playstation2.emulator");
        Assert.True(choice.IsWarning);
        Assert.StartsWith("ARMSX2 is not installed on this device", choice.Description);
    }

    [AvaloniaFact]
    public async Task EmulatorsSection_CloseOnReturnRow_ReportsTheShizukuGapAndYRequestsIt()
    {
        var granted = 0;
        var maintenance = new LibraryMaintenanceActions(
            (_, _) => Task.FromResult(string.Empty),
            _ => Task.FromResult(string.Empty),
            GetCloseEmulatorOnReturn: () => true,
            SetCloseEmulatorOnReturn: _ => Task.CompletedTask);
        using var viewModel = CreateGamepadSettings(
            maintenance,
            closeOnReturnWarning: () => "Shizuku permission not granted · press Y to grant it",
            grantCloseOnReturnPrivilege: () =>
            {
                granted++;
                return Task.CompletedTask;
            });
        viewModel.SelectedSection = SettingsSection.Emulators;

        var row = viewModel.Rows.Single(candidate => candidate.Key == "emulators.close-on-return");
        Assert.True(row.IsWarning);
        Assert.Equal("Shizuku permission not granted · press Y to grant it", row.Description);
        Assert.Equal("Shizuku needs attention", viewModel.EmulatorsRailStatus);
        viewModel.FocusedRowIndex = viewModel.Rows.IndexOf(row);
        Assert.Equal("Grant Shizuku", viewModel.ActionsHint);
        Assert.True(viewModel.Dispatch(GamepadAction.Actions));
        await Task.Delay(50);
        Assert.Equal(1, granted);

        // Off, the setting has nothing to warn about and Y does nothing.
        await row.SelectCommand.ExecuteAsync(null);
        row = viewModel.Rows.Single(candidate => candidate.Key == "emulators.close-on-return");
        Assert.False(row.IsWarning);
        Assert.Equal(string.Empty, viewModel.ActionsHint);
        Assert.False(viewModel.Dispatch(GamepadAction.Actions));
    }

    [AvaloniaFact]
    public async Task EmulatorsSection_FolderRow_RescansOnA_AndForgetsOnYAfterConfirmation()
    {
        var rescanned = new List<string>();
        var forgotten = new List<long>();
        var folders = new List<LibraryFolder> { new() { Id = 7, SystemId = "snes", Path = "/roms/snes" } };
        var maintenance = new LibraryMaintenanceActions(
            (systemId, _) =>
            {
                rescanned.Add(systemId);
                return Task.FromResult(string.Empty);
            },
            _ => Task.FromResult(string.Empty),
            Folders: new LibraryFolderManagementActions(
                systemId => folders.Where(folder => folder.SystemId == systemId).ToArray(),
                (_, _) => Task.FromResult("Folder remembered."),
                (_, _, _) => Task.FromResult("Folder changed."),
                (_, id) =>
                {
                    forgotten.Add(id);
                    folders.RemoveAll(folder => folder.Id == id);
                    return Task.FromResult("Folder forgotten.");
                }));
        using var viewModel = CreateGamepadSettings(maintenance);
        viewModel.SelectedSection = SettingsSection.Emulators;
        await ExpandPlatformAsync(viewModel, "snes");

        var folder = viewModel.Rows.Single(row => row.Key == "emulators.snes.folder.7");
        Assert.Equal("/roms/snes", folder.Label);
        Assert.DoesNotContain(viewModel.Rows, row => row.Key == "emulators.snes.rescan");
        await folder.SelectCommand.ExecuteAsync(null);
        Assert.Equal(["snes"], rescanned);

        viewModel.FocusedRowIndex = viewModel.Rows.IndexOf(
            viewModel.Rows.Single(row => row.Key == "emulators.snes.folder.7"));
        Assert.Equal("Forget folder", viewModel.ActionsHint);
        Assert.True(viewModel.Dispatch(GamepadAction.Actions));
        Assert.True(viewModel.IsConfirmationOpen);
        Assert.Equal("Forget this folder?", viewModel.ConfirmationTitle);
        viewModel.Dispatch(GamepadAction.NavigateRight);
        viewModel.Dispatch(GamepadAction.Confirm);
        await Task.Delay(50);
        Assert.Equal([7L], forgotten);
    }

    [AvaloniaFact]
    public async Task EmulatorsSection_ProbesTheDeviceOncePerScreen_NotOncePerRebuild()
    {
        // Each probe is a PackageManager binder round trip on the device, and the rail status is evaluated
        // in every section, so the answers are held until the user has been away.
        var installedProbes = 0;
        var shizukuProbes = 0;
        var maintenance = new LibraryMaintenanceActions(
            (_, _) => Task.FromResult(string.Empty),
            _ => Task.FromResult(string.Empty),
            GetCloseEmulatorOnReturn: () => true,
            SetCloseEmulatorOnReturn: _ => Task.CompletedTask);
        using var viewModel = CreateGamepadSettings(
            maintenance,
            androidEmulatorChoices: AndroidEmulatorChoiceCatalog.BySystem,
            isEmulatorChoiceInstalled: _ =>
            {
                installedProbes++;
                return true;
            },
            closeOnReturnWarning: () =>
            {
                shizukuProbes++;
                return "Shizuku permission not granted · press Y to grant it";
            });
        viewModel.SelectedSection = SettingsSection.Emulators;
        var afterOpen = installedProbes;
        Assert.InRange(afterOpen, 1, viewModel.Settings.Rows.Count);

        // Rebuilds trigger a full rail recompute; none of them may re-probe the device.
        await ExpandPlatformAsync(viewModel, "nds");
        await ExpandPlatformAsync(viewModel, "snes");
        viewModel.SelectedSection = SettingsSection.About;
        viewModel.SelectedSection = SettingsSection.Emulators;
        Assert.Equal(afterOpen, installedProbes);
        var afterRebuilds = shizukuProbes;

        // Returning to the foreground is the one thing that can have changed either answer.
        viewModel.RefreshDeviceState();
        Assert.True(installedProbes > afterOpen);
        Assert.True(shizukuProbes > afterRebuilds);
    }

    [AvaloniaFact]
    public void EmulatorsSection_ShizukuWarningClears_WhenTheGrantLandsWhileAway()
    {
        // Y only raises Shizuku's own dialog; the grant lands after EmuShelf has lost the foreground, so
        // without the re-read on return the row keeps telling the user to grant a permission they granted.
        var granted = false;
        var maintenance = new LibraryMaintenanceActions(
            (_, _) => Task.FromResult(string.Empty),
            _ => Task.FromResult(string.Empty),
            GetCloseEmulatorOnReturn: () => true,
            SetCloseEmulatorOnReturn: _ => Task.CompletedTask);
        using var viewModel = CreateGamepadSettings(
            maintenance,
            closeOnReturnWarning: () => granted ? null : "Shizuku permission not granted · press Y to grant it",
            grantCloseOnReturnPrivilege: () => Task.CompletedTask);
        viewModel.SelectedSection = SettingsSection.Emulators;
        Assert.True(viewModel.Rows.Single(row => row.Key == "emulators.close-on-return").IsWarning);
        Assert.Equal("Shizuku needs attention", viewModel.EmulatorsRailStatus);

        granted = true;
        viewModel.RefreshDeviceState();

        var row = viewModel.Rows.Single(candidate => candidate.Key == "emulators.close-on-return");
        Assert.False(row.IsWarning);
        Assert.Equal("Force-stop the game's emulator when you come back, so it stops draining the battery.", row.Description);
        Assert.Equal(string.Empty, viewModel.EmulatorsRailStatus);
        Assert.False(viewModel.IsEmulatorsRailWarning);
        Assert.Equal(string.Empty, viewModel.ActionsHint);
    }

    [AvaloniaFact]
    public async Task EmulatorsSection_GameCountsAreRereadWhenAScanFinishes()
    {
        // Mirrors the host: the rows read a snapshot taken when Settings opened, and only refreshGameCounts
        // re-reads the library. A rescan started from here has to make that happen or the line stays wrong.
        var library = new Dictionary<string, int>(StringComparer.Ordinal) { ["snes"] = 0 };
        var snapshot = new Dictionary<string, int>(library, StringComparer.Ordinal);
        var maintenance = new LibraryMaintenanceActions(
            (_, _) =>
            {
                library["snes"] = 52;
                return Task.FromResult("Rescan complete — 52 games");
            },
            _ => Task.FromResult(string.Empty));
        using var viewModel = CreateGamepadSettings(
            maintenance,
            gameCountBySystem: systemId => snapshot.GetValueOrDefault(systemId),
            refreshGameCounts: () =>
            {
                snapshot = new Dictionary<string, int>(library, StringComparer.Ordinal);
                return Task.CompletedTask;
            });
        viewModel.SelectedSection = SettingsSection.Emulators;
        Assert.Contains("0 games", viewModel.Rows.Single(row => row.Key == "emulators.snes.summary").Value);

        viewModel.FocusedRowIndex = viewModel.Rows.ToList().FindIndex(row => row.Key == "emulators.snes.summary");
        Assert.True(viewModel.Dispatch(GamepadAction.Actions));
        await Task.Delay(50);

        Assert.Contains("52 games", viewModel.Rows.Single(row => row.Key == "emulators.snes.summary").Value);
        Assert.Equal("52 games", viewModel.LibraryRailStatus);
    }

    [AvaloniaFact]
    public async Task HotkeysSection_IsItsOwnRailEntryAndOpensTheControllerNativeEditor()
    {
        var snapshot = new HotkeyEmulatorSnapshot(
            "duckstation", "DuckStation", [], "Recommended hotkeys aren't applied yet.",
            HotkeyRowTone.Info, CanOperate: true);
        var hotkeys = new HotkeySettingsContext(
            [snapshot],
            (_, _) => Task.FromResult(snapshot),
            (_, _) => Task.FromResult(snapshot),
            "R rewinds, L fast-forwards, F2 saves, F4 loads, F8 closes the game.");
        var settings = new EmulatorSettingsViewModel(
            KnownSystems.All,
            KnownEmulators.All,
            KnownSystems.All.ToDictionary(system => system.Id, _ => (EmulatorConfiguration?)null, StringComparer.Ordinal),
            _configurations,
            _dialogs,
            hotkeys: hotkeys);
        var opened = 0;
        using var viewModel = new GamepadSettingsViewModel(
            settings,
            onScreenKeyboard: null,
            themeChoices: null,
            applyTheme: null,
            openHotkeys: () => { opened++; return Task.CompletedTask; });

        // Hotkeys is a peer rail section (matching Desktop), not a Library row.
        Assert.Contains(SettingsSection.Hotkeys, viewModel.Sections);
        viewModel.SelectedSection = SettingsSection.Hotkeys;
        Assert.True(viewModel.IsHotkeysSection);
        Assert.Equal("Hotkeys", viewModel.SectionTitle);
        Assert.Contains(viewModel.Rows, row => row.Key == "hotkeys.scheme");

        // A controller can't navigate the per-emulator matrix as a flat list, so the section's row
        // opens the controller-native overlay through the same callback the shell wires.
        var open = viewModel.Rows.Single(row => row.Key == "hotkeys.open");
        await open.SelectCommand.ExecuteAsync(null);
        Assert.Equal(1, opened);
    }

    [AvaloniaFact]
    public async Task SaveRow_UsesExistingPersistenceAndReportsSavedClose()
    {
        bool? showEmpty = null;
        var metadata = new RecordingMetadataPreferences();
        var maintenance = CreateMaintenance(value => showEmpty = value);
        var settings = CreateSettings(
            maintenance: maintenance,
            metadataPreferences: metadata,
            screenScraper: CreateScreenScraperContext());
        using var viewModel = new GamepadSettingsViewModel(settings);
        bool? closedAsSaved = null;
        viewModel.CloseRequested += saved => closedAsSaved = saved;

        await viewModel.Rows.Single(row => row.Key == "general.empty-platforms").SelectCommand.ExecuteAsync(null);
        // The metadata auto-fetch toggle moved into the Artwork & Metadata section.
        viewModel.SelectedSection = SettingsSection.ArtworkMetadata;
        await viewModel.Rows.Single(row => row.Key == "general.metadata-auto").SelectCommand.ExecuteAsync(null);
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.CloseRequested += value =>
        {
            if (value)
                saved.TrySetResult();
        };
        Assert.True(viewModel.Dispatch(GamepadAction.Menu));
        await saved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(closedAsSaved);
        Assert.True(showEmpty);
        Assert.True(metadata.AutomaticallyFetchAfterImport);
        Assert.Equal(1, _configurations.BatchSaveCalls);
    }

    [AvaloniaFact]
    public async Task SaveRow_IsControllerReachableWithUpThenA()
    {
        var settings = CreateSettings();
        using var viewModel = new GamepadSettingsViewModel(settings);
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.CloseRequested += value =>
        {
            if (value)
                saved.TrySetResult();
        };

        Assert.Equal("general.empty-platforms", viewModel.FocusedRow!.Key);
        Assert.True(viewModel.Dispatch(GamepadAction.NavigateUp));
        Assert.Same(viewModel.SaveRow, viewModel.FocusedRow);
        Assert.True(viewModel.Dispatch(GamepadAction.Confirm));
        await saved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, _configurations.BatchSaveCalls);
    }

    [AvaloniaFact]
    public async Task DestructiveSaveAction_DefaultsToCancelAndRestoresItsRowAfterConfirmation()
    {
        var forceCalls = 0;
        using var viewModel = CreateGamepadSettings(cloudSaves: CreateCloudContext(
            connected: true,
            force: (_, _, _, _) =>
            {
                forceCalls++;
                return Task.FromResult(CloudSaveSyncOutcome.Completed(new SaveSyncReport([])));
            }));
        viewModel.SelectedSection = SettingsSection.Saves;
        var replace = viewModel.Rows.Single(row => row.Key.EndsWith("replace-local", StringComparison.Ordinal));

        await replace.SelectCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsConfirmationOpen);
        Assert.False(viewModel.IsConfirmChoiceSelected);
        viewModel.Dispatch(GamepadAction.Confirm);
        Assert.False(viewModel.IsConfirmationOpen);
        Assert.Equal(0, forceCalls);
        Assert.Equal(replace.Key, viewModel.FocusedRow!.Key);

        await viewModel.FocusedRow.SelectCommand.ExecuteAsync(null);
        viewModel.Dispatch(GamepadAction.NavigateRight);
        await viewModel.ChooseConfirmationConfirmCommand.ExecuteAsync(null);

        Assert.Equal(1, forceCalls);
        Assert.False(viewModel.IsConfirmationOpen);
        Assert.Equal(replace.Key, viewModel.FocusedRow!.Key);
    }

    [AvaloniaFact]
    public async Task FolderPicker_ReturnsToTheSameControllerRowAndPersistsThroughExistingCallback()
    {
        _dialogs.FolderToReturn = "D:/Portable/Saves";
        var persisted = new Dictionary<string, string?>(StringComparer.Ordinal);
        using var viewModel = CreateGamepadSettings(cloudSaves: CreateCloudContext(
            updateOverride: (systemId, value) => persisted[systemId] = value));
        viewModel.SelectedSection = SettingsSection.Saves;
        var folder = viewModel.Rows.Single(row => row.Key == "saves.playstation2.folder");

        await folder.SelectCommand.ExecuteAsync(null);

        Assert.Equal("D:/Portable/Saves", persisted["playstation2"]);
        Assert.Equal(folder.Key, viewModel.FocusedRow!.Key);
        Assert.Equal("D:/Portable/Saves", viewModel.FocusedRow.Value);
    }

    [AvaloniaFact]
    public async Task TextureChoices_UseExistingFiltersAndKeepLogicalFocus()
    {
        using var viewModel = CreateGamepadSettings(texturePacks: CreateTextureContext());
        viewModel.SelectedSection = SettingsSection.TexturePacks;
        var status = viewModel.Rows.Single(row => row.Key == "textures.status-filter");
        await status.SelectCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsChoicePickerOpen);
        Assert.Equal("All", viewModel.Settings.TextureStatusFilter);
        viewModel.Dispatch(GamepadAction.NavigateDown);
        viewModel.Dispatch(GamepadAction.Confirm);
        Assert.False(viewModel.IsChoicePickerOpen);
        Assert.Equal("Matched", viewModel.Settings.TextureStatusFilter);
        Assert.Equal(status.Key, viewModel.FocusedRow!.Key);
        // Left directly reverses a choice. It no longer unexpectedly leaves for the section rail.
        viewModel.Dispatch(GamepadAction.NavigateLeft);
        Assert.False(viewModel.IsRailFocused);
        Assert.Equal("All", viewModel.Settings.TextureStatusFilter);
    }

    [AvaloniaFact]
    public void NormalBack_CancelsWithoutInvokingTheExistingSaveCommand()
    {
        using var viewModel = CreateGamepadSettings();
        bool? closeResult = null;
        viewModel.CloseRequested += saved => closeResult = saved;

        viewModel.Dispatch(GamepadAction.Cancel);

        Assert.False(closeResult);
        Assert.Equal(0, _configurations.BatchSaveCalls);
    }

    [AvaloniaFact]
    public void RowsExposeDistinctControllerControlSemantics()
    {
        using var viewModel = CreateGamepadSettings(
            retroAchievements: CreateRetroAchievementsContext(),
            cloudSaves: CreateCloudContext(),
            texturePacks: CreateTextureContext());

        var toggle = viewModel.Rows.Single(row => row.Key == "general.empty-platforms");
        var action = viewModel.Rows.Single(row => row.Key == "general.rescan");
        Assert.True(toggle.IsToggle);
        Assert.False(toggle.IsAction);
        Assert.True(action.IsAction);
        Assert.Equal("RESCAN", action.ActionButtonText);
        Assert.Same(viewModel.Rows.Single(row => row.IsSaveRow), viewModel.SaveRow);

        viewModel.SelectedSection = SettingsSection.RetroAchievements;
        var secret = viewModel.Rows.Single(row => row.Key == "retro.api-key");
        Assert.True(secret.IsEditableValue);
        Assert.True(secret.ShowsActionButton);
        Assert.Equal("EDIT", secret.ActionButtonText);

        viewModel.SelectedSection = SettingsSection.Saves;
        var folder = viewModel.Rows.Single(row => row.Key == "saves.playstation2.folder");
        Assert.True(folder.IsEditableValue);
        Assert.Equal("CHOOSE", folder.ActionButtonText);

        viewModel.SelectedSection = SettingsSection.TexturePacks;
        Assert.True(viewModel.Rows.Single(row => row.Key == "textures.status-filter").IsChoice);
        Assert.True(viewModel.Rows.Single(row => row.Key == "textures.empty").IsInformation);
    }

    [AvaloniaFact]
    public void AboutUpdateRow_WhileDownloading_ShowsCoordinatorLiveProgressText()
    {
        var coordinator = CreateUpdateCoordinator();
        using var viewModel = CreateGamepadSettings(updates: coordinator);
        viewModel.SelectedSection = SettingsSection.About;
        string CheckRowHint() => viewModel.Rows.Single(row => row.Key == "about.check-updates").Description;

        // Idle: the check row falls back to its static prompt.
        Assert.Equal("Look on GitHub for a newer EmuShelf. Only the public releases page is contacted.", CheckRowHint());

        // A download begins: the coordinator drives the live percentage on its own object, which must
        // rebuild the row so its hint reflects the moving progress rather than a static line.
        coordinator.IsBusy = true;
        coordinator.StatusText = "Downloading update… 42%";
        Assert.Equal("Downloading update… 42%", CheckRowHint());

        coordinator.StatusText = "Downloading update… 87%";
        Assert.Equal("Downloading update… 87%", CheckRowHint());

        // Once the download settles the row returns to the static status the Desktop view model owns.
        coordinator.IsBusy = false;
        Assert.Equal("Look on GitHub for a newer EmuShelf. Only the public releases page is contacted.", CheckRowHint());
    }

    private static AppUpdateCoordinator CreateUpdateCoordinator() => new(
        new StubUpdateService(),
        new StubUpdateApplier(),
        new StubSettingsService(),
        new AppSettings(),
        NullAppLogger.Instance,
        requestExit: () => { });

    private GamepadSettingsViewModel CreateGamepadSettings(
        LibraryMaintenanceActions? maintenance = null,
        IMetadataPreferencesService? metadataPreferences = null,
        RetroAchievementsSettingsContext? retroAchievements = null,
        CloudSaveSyncSettingsContext? cloudSaves = null,
        TexturePackSettingsContext? texturePacks = null,
        ScreenScraperSettingsContext? screenScraper = null,
        IOnScreenKeyboardService? onScreenKeyboard = null,
        AppUpdateCoordinator? updates = null,
        IReadOnlyList<ThemeChoiceViewModel>? themeChoices = null,
        IReadOnlyDictionary<string, IReadOnlyList<EmulatorChoice>>? androidEmulatorChoices = null,
        Func<string, int>? gameCountBySystem = null,
        Func<EmulatorChoice, bool>? isEmulatorChoiceInstalled = null,
        Func<string?>? closeOnReturnWarning = null,
        Func<Task>? grantCloseOnReturnPrivilege = null,
        Func<Task>? refreshGameCounts = null) => new(
            CreateSettings(
                maintenance,
                metadataPreferences,
                retroAchievements,
                cloudSaves,
                texturePacks,
                screenScraper,
                updates,
                androidEmulatorChoices),
            onScreenKeyboard,
            themeChoices,
            androidEmulatorChoices: androidEmulatorChoices,
            gameCountBySystem: gameCountBySystem,
            isEmulatorChoiceInstalled: isEmulatorChoiceInstalled,
            closeOnReturnWarning: closeOnReturnWarning,
            grantCloseOnReturnPrivilege: grantCloseOnReturnPrivilege,
            refreshGameCounts: refreshGameCounts);

    /// <summary>Emulators lists one summary row per platform; A on it reveals that platform's rows.</summary>
    private static async Task ExpandPlatformAsync(GamepadSettingsViewModel viewModel, string systemId)
    {
        var summary = viewModel.Rows.Single(row => row.Key == $"emulators.{systemId}.summary");
        await summary.SelectCommand.ExecuteAsync(null);
    }

    private EmulatorSettingsViewModel CreateSettings(
        LibraryMaintenanceActions? maintenance = null,
        IMetadataPreferencesService? metadataPreferences = null,
        RetroAchievementsSettingsContext? retroAchievements = null,
        CloudSaveSyncSettingsContext? cloudSaves = null,
        TexturePackSettingsContext? texturePacks = null,
        ScreenScraperSettingsContext? screenScraper = null,
        AppUpdateCoordinator? updates = null,
        IReadOnlyDictionary<string, IReadOnlyList<EmulatorChoice>>? fixedEmulatorChoices = null) => new(
            KnownSystems.All,
            KnownEmulators.All,
            KnownSystems.All.ToDictionary(
                system => system.Id,
                _ => (EmulatorConfiguration?)null,
                StringComparer.Ordinal),
            _configurations,
            _dialogs,
            maintenance,
            metadataPreferences,
            retroAchievements: retroAchievements,
            cloudSaves: cloudSaves,
            texturePacks: texturePacks,
            screenScraper: screenScraper,
            updates: updates,
            fixedEmulatorChoices: fixedEmulatorChoices);

    private static LibraryMaintenanceActions CreateMaintenance(Action<bool> setShowEmpty) => new(
        (_, _) => Task.FromResult(string.Empty),
        _ => Task.FromResult(string.Empty),
        _ => Task.FromResult(string.Empty),
        _ => Task.FromResult(string.Empty),
        () => Task.FromResult(string.Empty),
        () => false,
        value =>
        {
            setShowEmpty(value);
            return Task.CompletedTask;
        });

    private static RetroAchievementsSettingsContext CreateRetroAchievementsContext() => new(
        null,
        false,
        (_, _, _, _) => Task.FromResult(new RetroAchievementsConnectionSummary(
            RetroAchievementsConnectionResult.Connected)),
        _ => Task.CompletedTask,
        (_, _) => Task.FromResult<RetroAchievementsLibrarySyncSummary?>(null));

    private static ScreenScraperSettingsContext CreateScreenScraperContext(
        bool connected = false,
        Action<string, string>? onConnect = null,
        Action? onDisconnect = null) => new(
        connected,
        null,
        (username, password, _) =>
        {
            onConnect?.Invoke(username, password);
            return Task.FromResult(new ScreenScraperConnectionSummary(
                ScreenScraperConnectionResult.Connected));
        },
        _ =>
        {
            onDisconnect?.Invoke();
            return Task.CompletedTask;
        });

    private static CloudSaveSyncSettingsContext CreateCloudContext(
        bool connected = false,
        Func<string, SaveSyncDirection, IProgress<SaveSyncProgress>?, CancellationToken, Task<CloudSaveSyncOutcome>>? force = null,
        Action<string, string?>? updateOverride = null)
    {
        var current = new CloudSaveSyncSettings
        {
            Enabled = connected,
            TransportKind = connected ? CloudTransportKind.GoogleDrive : CloudTransportKind.Rclone,
            CloudFolder = connected ? "EmuShelf/Saves" : null,
        };
        var platform = new CloudSaveSyncPlatformContext(
            "playstation2",
            "PlayStation 2",
            "PCSX2 memory cards and opted-in manual states.",
            "Use the PCSX2-detected folder",
            null,
            null,
            null,
            SupportsSaveStates: true);
        return new CloudSaveSyncSettingsContext(
            current,
            "/portable/Logs/save-sync.log",
            () => [platform],
            (_, _) => Task.FromResult<string?>("/detected/pcsx2"),
            _ => Task.CompletedTask,
            (_, _) => Task.FromResult(CloudSaveSyncOutcome.Completed(new SaveSyncReport([]))),
            force ?? ((_, _, _, _) => Task.FromResult(CloudSaveSyncOutcome.Completed(new SaveSyncReport([])))),
            updateOverride ?? ((_, _) => { }),
            IsManagedTransportAvailable: true,
            ConnectGoogleDriveManagedAsync: (_, _, _, _) => Task.FromResult(CloudSaveSyncConnectResult.Connected));
    }

    private static TexturePackSettingsContext CreateTextureContext()
    {
        var result = new TexturePackInventoryResult(
            TexturePackLibraryMap.Empty,
            [new TexturePackPlatformState(
                "gamecube",
                "GameCube",
                "/dolphin/Load/Textures",
                false,
                TexturePackRootStatus.Ready,
                false,
                TexturePackLoadingStatus.Enabled,
                null)]);
        return new TexturePackSettingsContext(
            () => result,
            () => true,
            _ => Task.FromResult(result),
            (_, _) => { },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["gamecube"] = "Use the Dolphin-detected folder",
            },
            () => new Dictionary<long, string>());
    }

    private sealed class RecordingConfigurationStore : IEmulatorConfigurationStore
    {
        public int BatchSaveCalls { get; private set; }
        public IReadOnlyList<EmulatorConfiguration> SavedConfigurations { get; private set; } = [];
        public Dictionary<string, string> ActiveEmulators { get; } = new(StringComparer.Ordinal);

        public EmulatorConfiguration? Get(string systemId) => null;

        public void Save(EmulatorConfiguration configuration)
        {
        }

        public void SaveAll(IReadOnlyList<EmulatorConfiguration> configurations)
        {
            BatchSaveCalls++;
            SavedConfigurations = configurations.ToArray();
        }

        public void SetActiveEmulator(string systemId, string emulatorId) =>
            ActiveEmulators[systemId] = emulatorId;
    }

    private sealed class RecordingMetadataPreferences : IMetadataPreferencesService
    {
        public bool AutomaticallyFetchAfterImport { get; private set; }
        public bool ConsentPromptShown => true;
        public bool WebImageSearchEnabled { get; private set; } = true;

        public Task SaveAutomaticFetchAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            AutomaticallyFetchAfterImport = enabled;
            return Task.CompletedTask;
        }

        public Task SaveWebImageSearchAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            WebImageSearchEnabled = enabled;
            return Task.CompletedTask;
        }

        public Task RecordConsentAsync(
            MetadataConsentChoice choice,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingOnScreenKeyboardService : IOnScreenKeyboardService
    {
        public bool IsSupported => true;
        public int Requests { get; private set; }
        public OnScreenKeyboardRequest? LastRequest { get; private set; }

        public bool TryShow(OnScreenKeyboardRequest request)
        {
            Requests++;
            LastRequest = request;
            return true;
        }
    }

    // The update rows only need a coordinator whose live download state can be driven directly, so
    // these stubs satisfy its dependencies without performing a real check, download, or persist.
    private sealed class StubUpdateService : IUpdateService
    {
        public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<UpdateCheckResult>(new UpdateCheckResult.UpToDate(SemanticVersion.Zero));

        public Task<StagedUpdate> DownloadAndStageAsync(
            UpdateCheckResult.UpdateAvailable update,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new StagedUpdate(update.Version, "/staged"));
    }

    private sealed class StubUpdateApplier : IUpdateApplier
    {
        public bool CanApply(out string? reason)
        {
            reason = null;
            return true;
        }

        public void ApplyAndRelaunch(StagedUpdate staged)
        {
        }
    }

    private sealed class StubSettingsService : ISettingsService
    {
        private AppSettings _current = new();

        public AppSettings Load() => _current;

        public void Save(AppSettings settings) => _current = settings;
    }
}
