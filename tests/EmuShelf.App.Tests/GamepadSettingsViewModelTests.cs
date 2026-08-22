using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Input;
using EmuShelf.Core.Launching;
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

        // Same stable id and command as Desktop's PS3-row "Sync RPCS3 library" button — now reachable
        // on a controller because Emulators is a couch section instead of Desktop-only.
        var row = viewModel.Rows.Single(candidate => candidate.Key == "emulators.playstation3.sync");
        await row.SelectCommand.ExecuteAsync(null);

        Assert.Equal(1, synced);
        Assert.Equal("RPCS3 library sync complete — 2 added", viewModel.StatusText);
    }

    [AvaloniaFact]
    public void EmulatorsSection_OmitsRpcs3Sync_WhenTheMaintenanceActionIsUnavailable()
    {
        var maintenance = new LibraryMaintenanceActions(
            (_, _) => Task.FromResult(string.Empty),
            _ => Task.FromResult(string.Empty));
        using var viewModel = CreateGamepadSettings(maintenance);
        viewModel.SelectedSection = SettingsSection.Emulators;

        Assert.DoesNotContain(viewModel.Rows, row => row.Key == "emulators.playstation3.sync");
    }

    [AvaloniaFact]
    public async Task AndroidEmulatorChoice_ListsStandaloneAndCoreEntriesAndPersistsThePair()
    {
        using var viewModel = CreateGamepadSettings(
            androidEmulatorChoices: AndroidEmulatorChoiceCatalog.BySystem);
        viewModel.SelectedSection = SettingsSection.Emulators;

        var settingsRow = viewModel.Settings.Rows.Single(row => row.SystemId == "nds");
        Assert.Equal(
            ["WatermelonDS", "RetroArch · melonDS DS", "RetroArch · melonDS", "RetroArch · DeSmuME"],
            settingsRow.AvailableChoices.Select(choice => choice.DisplayName));
        Assert.Equal("watermelonds", settingsRow.EmulatorId);
        Assert.Null(settingsRow.SelectedChoice?.CorePath);
        var emulatorRow = viewModel.Rows.Single(row => row.Key == "emulators.nds.emulator");
        Assert.Equal("WatermelonDS", emulatorRow.Value);

        // A opens an explicit list without changing the value. Down + A chooses the first
        // RetroArch-core-as-emulator item.
        await emulatorRow.SelectCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsChoicePickerOpen);
        Assert.Equal(
            ["WatermelonDS", "RetroArch · melonDS DS", "RetroArch · melonDS", "RetroArch · DeSmuME"],
            viewModel.ChoiceOptions.Select(option => option.DisplayName));
        Assert.Equal("watermelonds", settingsRow.EmulatorId);
        viewModel.Dispatch(GamepadAction.NavigateDown);
        viewModel.Dispatch(GamepadAction.Confirm);
        Assert.False(viewModel.IsChoicePickerOpen);
        var expectedPath = AndroidRetroArchCoreCatalog.BySystem["nds"][0].Path;
        Assert.Equal("retroarch", settingsRow.EmulatorId);
        Assert.Equal(expectedPath, settingsRow.CorePath);

        // Direct Left/Right adjustment is symmetric and wraps, so a quick change never requires
        // blindly pressing A through the whole list.
        viewModel.Dispatch(GamepadAction.NavigateLeft);
        Assert.Equal("watermelonds", settingsRow.EmulatorId);
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
    public void EmulatorChoice_IsOnlyProjectedWhenAndroidSuppliesTheCatalog()
    {
        using var viewModel = CreateGamepadSettings();
        viewModel.SelectedSection = SettingsSection.Emulators;

        Assert.DoesNotContain(viewModel.Rows, row => row.Key.EndsWith(".emulator", StringComparison.Ordinal));
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
        IReadOnlyDictionary<string, IReadOnlyList<EmulatorChoice>>? androidEmulatorChoices = null) => new(
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
            androidEmulatorChoices: androidEmulatorChoices);

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
