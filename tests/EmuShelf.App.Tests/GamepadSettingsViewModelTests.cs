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
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.Tests;

public sealed class GamepadSettingsViewModelTests
{
    private readonly FakeDialogService _dialogs = new();
    private readonly RecordingConfigurationStore _configurations = new();

    [AvaloniaFact]
    public void ShoulderSections_ExcludeDeferredEmulatorsAndRestoreEachRowsFocus()
    {
        using var viewModel = CreateGamepadSettings(
            retroAchievements: CreateRetroAchievementsContext(),
            cloudSaves: CreateCloudContext(),
            texturePacks: CreateTextureContext());

        Assert.Equal(
            [SettingsSection.General, SettingsSection.RetroAchievements, SettingsSection.Saves, SettingsSection.TexturePacks],
            viewModel.Sections);
        Assert.DoesNotContain(SettingsSection.Emulators, viewModel.Sections);

        viewModel.Dispatch(GamepadAction.NavigateDown);
        var rememberedGeneralRow = viewModel.FocusedRow!.Key;
        Assert.True(viewModel.Dispatch(GamepadAction.NextPlatform));
        Assert.Equal(SettingsSection.RetroAchievements, viewModel.SelectedSection);
        viewModel.Dispatch(GamepadAction.NavigateDown);
        var rememberedRetroRow = viewModel.FocusedRow!.Key;

        viewModel.Dispatch(GamepadAction.PreviousPlatform);
        Assert.Equal(rememberedGeneralRow, viewModel.FocusedRow!.Key);
        viewModel.Dispatch(GamepadAction.NextPlatform);
        Assert.Equal(rememberedRetroRow, viewModel.FocusedRow!.Key);

        viewModel.SelectedSection = SettingsSection.TexturePacks;
        viewModel.Dispatch(GamepadAction.NextPlatform);
        Assert.Equal(SettingsSection.TexturePacks, viewModel.SelectedSection);
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

        Assert.Contains(SettingsSection.ScreenScraper, viewModel.Sections);

        viewModel.SelectedSection = SettingsSection.ScreenScraper;
        Assert.True(viewModel.IsScreenScraperSection);
        Assert.Equal("ScreenScraper", viewModel.SectionTitle);

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

        Assert.DoesNotContain(SettingsSection.ScreenScraper, viewModel.Sections);
    }

    [AvaloniaFact]
    public async Task GeneralPill_ShowsRescanStatus_NotAFinishedMetadataFetchProgressLine()
    {
        // The General section collapses metadata and maintenance into one pill. Before the fix, a
        // completed metadata fetch left its live "Fetching N of N" line set and the pill (which ranks
        // progress text ahead of status) kept showing it, so a later rescan looked like it did nothing.
        var maintenance = new LibraryMaintenanceActions(
            RescanSystem: (_, _) => Task.FromResult(string.Empty),
            RescanAll: _ => Task.FromResult("Rescan complete — no new games"),
            FetchAllMetadata: progress =>
            {
                progress.Report(new MetadataEnrichmentProgress(40, 40, "Final Fantasy"));
                return Task.FromResult("Added 40 covers");
            });
        using var viewModel = CreateGamepadSettings(maintenance);
        viewModel.SelectedSection = SettingsSection.General;

        await viewModel.Rows.Single(row => row.Key == "general.fetch-metadata").SelectCommand.ExecuteAsync(null);
        // The completion summary shows — not the frozen "Fetching 40 of 40" progress line.
        Assert.Equal("Added 40 covers", viewModel.StatusText);
        Assert.Equal(string.Empty, viewModel.Settings.MetadataProgressText);
        Assert.False(viewModel.Settings.HasMetadataProgress);

        await viewModel.Rows.Single(row => row.Key == "general.rescan").SelectCommand.ExecuteAsync(null);
        // The rescan's own status wins; the earlier metadata summary no longer masks it.
        Assert.Equal("Rescan complete — no new games", viewModel.StatusText);
    }

    [AvaloniaFact]
    public async Task GeneralSection_ExposesRpcs3LibrarySync_TheOnlyControllerPathToImportPs3()
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
        viewModel.SelectedSection = SettingsSection.General;

        var row = viewModel.Rows.Single(candidate => candidate.Key == "general.sync-rpcs3");
        await row.SelectCommand.ExecuteAsync(null);

        Assert.Equal(1, synced);
        Assert.Equal("RPCS3 library sync complete — 2 added", viewModel.StatusText);
    }

    [AvaloniaFact]
    public void GeneralSection_OmitsRpcs3Sync_WhenTheMaintenanceActionIsUnavailable()
    {
        var maintenance = new LibraryMaintenanceActions(
            (_, _) => Task.FromResult(string.Empty),
            _ => Task.FromResult(string.Empty));
        using var viewModel = CreateGamepadSettings(maintenance);
        viewModel.SelectedSection = SettingsSection.General;

        Assert.DoesNotContain(viewModel.Rows, row => row.Key == "general.sync-rpcs3");
    }

    [AvaloniaFact]
    public async Task SaveRow_UsesExistingPersistenceAndReportsSavedClose()
    {
        bool? showEmpty = null;
        var metadata = new RecordingMetadataPreferences();
        var maintenance = CreateMaintenance(value => showEmpty = value);
        var settings = CreateSettings(maintenance: maintenance, metadataPreferences: metadata);
        using var viewModel = new GamepadSettingsViewModel(settings);
        bool? closedAsSaved = null;
        viewModel.CloseRequested += saved => closedAsSaved = saved;

        await viewModel.Rows.Single(row => row.Key == "general.empty-platforms").SelectCommand.ExecuteAsync(null);
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

        Assert.Equal("Matched", viewModel.Settings.TextureStatusFilter);
        Assert.Equal(status.Key, viewModel.FocusedRow!.Key);
        // Left now moves focus to the section rail rather than cycling the choice, which advances
        // only with A/Right (NeoStation-style); the value is left unchanged.
        viewModel.Dispatch(GamepadAction.NavigateLeft);
        Assert.True(viewModel.IsRailFocused);
        Assert.Equal("Matched", viewModel.Settings.TextureStatusFilter);
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
    public void GeneralUpdateRow_WhileDownloading_ShowsCoordinatorLiveProgressText()
    {
        var coordinator = CreateUpdateCoordinator();
        using var viewModel = CreateGamepadSettings(updates: coordinator);
        string CheckRowHint() => viewModel.Rows.Single(row => row.Key == "general.check-updates").Description;

        // Idle: the check row falls back to its static prompt.
        Assert.Equal("Look on GitHub for a newer EmuShelf.", CheckRowHint());

        // A download begins: the coordinator drives the live percentage on its own object, which must
        // rebuild the row so its hint reflects the moving progress rather than a static line.
        coordinator.IsBusy = true;
        coordinator.StatusText = "Downloading update… 42%";
        Assert.Equal("Downloading update… 42%", CheckRowHint());

        coordinator.StatusText = "Downloading update… 87%";
        Assert.Equal("Downloading update… 87%", CheckRowHint());

        // Once the download settles the row returns to the static status the Desktop view model owns.
        coordinator.IsBusy = false;
        Assert.Equal("Look on GitHub for a newer EmuShelf.", CheckRowHint());
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
        AppUpdateCoordinator? updates = null) => new(
            CreateSettings(maintenance, metadataPreferences, retroAchievements, cloudSaves, texturePacks, screenScraper, updates),
            onScreenKeyboard);

    private EmulatorSettingsViewModel CreateSettings(
        LibraryMaintenanceActions? maintenance = null,
        IMetadataPreferencesService? metadataPreferences = null,
        RetroAchievementsSettingsContext? retroAchievements = null,
        CloudSaveSyncSettingsContext? cloudSaves = null,
        TexturePackSettingsContext? texturePacks = null,
        ScreenScraperSettingsContext? screenScraper = null,
        AppUpdateCoordinator? updates = null) => new(
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
            updates: updates);

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
            RemoteName = connected ? "emushelf-gdrive" : null,
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
            true,
            "/portable/rclone",
            "/portable/Logs/save-sync.log",
            () => [platform],
            (_, _) => Task.FromResult<string?>("/detected/pcsx2"),
            (_, _, _, _) => Task.FromResult(CloudSaveSyncConnectResult.Connected),
            _ => Task.CompletedTask,
            (_, _) => Task.FromResult(CloudSaveSyncOutcome.Completed(new SaveSyncReport([]))),
            force ?? ((_, _, _, _) => Task.FromResult(CloudSaveSyncOutcome.Completed(new SaveSyncReport([])))),
            updateOverride ?? ((_, _) => { }),
            _ => Task.FromResult(true));
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

        public EmulatorConfiguration? Get(string systemId) => null;

        public void Save(EmulatorConfiguration configuration)
        {
        }

        public void SaveAll(IReadOnlyList<EmulatorConfiguration> configurations) => BatchSaveCalls++;
    }

    private sealed class RecordingMetadataPreferences : IMetadataPreferencesService
    {
        public bool AutomaticallyFetchAfterImport { get; private set; }
        public bool ConsentPromptShown => true;

        public Task SaveAutomaticFetchAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            AutomaticallyFetchAfterImport = enabled;
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
