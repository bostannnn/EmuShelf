using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Systems;

namespace EmuShelf.App.ViewModels;

public partial class EmulatorSettingsRowViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    private readonly IAppLogger _logger;
    private readonly Func<EmulatorSettingsRowViewModel, Task>? _rescanLibrary;
    private readonly Func<EmulatorSettingsRowViewModel, Task>? _fetchMetadata;
    private readonly Func<EmulatorSettingsRowViewModel, Task>? _syncLibrary;

    public string SystemId { get; }
    public string SystemName { get; }
    public string SystemShortName { get; }
    public string AccentColor { get; }
    public string EmulatorName { get; }
    public string DefaultLaunchArguments { get; }
    public string EmulatorId { get; }
    public string EmulatorInstallationId { get; }
    public bool RequiresCorePath { get; }
    public bool IsExecutableShared { get; }
    public string ExecutableDescription => IsExecutableShared
        ? "Shared executable"
        : "Executable";

    internal event Action<EmulatorSettingsRowViewModel, string>? ExecutablePathEdited;

    [ObservableProperty]
    public partial string ExecutablePath { get; set; }

    [ObservableProperty]
    public partial string LaunchArguments { get; set; }

    [ObservableProperty]
    public partial string CorePath { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRescan))]
    [NotifyPropertyChangedFor(nameof(CanFetchMetadata))]
    [NotifyPropertyChangedFor(nameof(CanSyncLibrary))]
    [NotifyCanExecuteChangedFor(nameof(RescanLibraryCommand))]
    [NotifyCanExecuteChangedFor(nameof(FetchMetadataCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncLibraryCommand))]
    public partial bool IsMaintenanceBlocked { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMaintenanceStatus))]
    public partial string MaintenanceStatusText { get; set; } = string.Empty;

    public bool HasRescanLibrary => _rescanLibrary is not null;
    public bool CanRescan => HasRescanLibrary && !IsMaintenanceBlocked;
    public bool CanFetchMetadata => _fetchMetadata is not null && !IsMaintenanceBlocked;
    public bool HasSyncLibrary => _syncLibrary is not null;
    public bool CanSyncLibrary => HasSyncLibrary && !IsMaintenanceBlocked;
    public bool HasMaintenanceStatus => !string.IsNullOrWhiteSpace(MaintenanceStatusText);

    public EmulatorSettingsRowViewModel(
        GameSystem system,
        EmulatorDefinition emulator,
        EmulatorConfiguration? configuration,
        IDialogService dialogs,
        Func<EmulatorSettingsRowViewModel, Task>? rescanLibrary = null,
        Func<EmulatorSettingsRowViewModel, Task>? fetchMetadata = null,
        Func<EmulatorSettingsRowViewModel, Task>? syncLibrary = null,
        bool isExpanded = false,
        string? emulatorInstallationId = null,
        bool isExecutableShared = false,
        IAppLogger? logger = null)
    {
        _dialogs = dialogs;
        _logger = logger ?? NullAppLogger.Instance;
        _rescanLibrary = rescanLibrary;
        _fetchMetadata = fetchMetadata;
        _syncLibrary = syncLibrary;
        SystemId = system.Id;
        SystemName = system.Name;
        SystemShortName = system.ShortName;
        AccentColor = system.AccentColor;
        EmulatorName = emulator.Name;
        EmulatorId = configuration?.EmulatorId ?? emulator.Id;
        EmulatorInstallationId = configuration?.EmulatorInstallationId
            ?? emulatorInstallationId
            ?? emulator.Id;
        DefaultLaunchArguments = emulator.DefaultLaunchArguments;
        RequiresCorePath = emulator.RequiresCorePath;
        IsExecutableShared = isExecutableShared;
        ExecutablePath = configuration?.ExecutablePath ?? string.Empty;
        LaunchArguments = configuration?.LaunchArguments ?? emulator.DefaultLaunchArguments;
        CorePath = configuration?.CorePath ?? string.Empty;
        IsExpanded = isExpanded;
    }

    partial void OnExecutablePathChanged(string value) => ExecutablePathEdited?.Invoke(this, value);

    [RelayCommand]
    private async Task BrowseAsync()
    {
        try
        {
            var path = await _dialogs.PickEmulatorExecutableAsync(EmulatorName);
            if (path is not null)
                ExecutablePath = path;
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not choose the {EmulatorName} executable.", ex);
            MaintenanceStatusText = $"Could not open the executable picker: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task BrowseCoreAsync()
    {
        if (!RequiresCorePath)
            return;

        try
        {
            var path = await _dialogs.PickLibretroCoreAsync(SystemName);
            if (path is not null)
                CorePath = path;
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not choose the {SystemName} RetroArch core.", ex);
            MaintenanceStatusText = $"Could not open the core picker: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ResetArguments() => LaunchArguments = DefaultLaunchArguments;

    [RelayCommand(CanExecute = nameof(CanRescan))]
    private Task RescanLibraryAsync() =>
        _rescanLibrary?.Invoke(this) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanFetchMetadata))]
    private Task FetchMetadataAsync() =>
        _fetchMetadata?.Invoke(this) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanSyncLibrary))]
    private Task SyncLibraryAsync() =>
        _syncLibrary?.Invoke(this) ?? Task.CompletedTask;

    public EmulatorConfiguration ToConfiguration() => new(
        SystemId,
        string.IsNullOrWhiteSpace(ExecutablePath) ? null : ExecutablePath.Trim(),
        LaunchArguments)
    {
        EmulatorId = EmulatorId,
        EmulatorInstallationId = EmulatorInstallationId,
        CorePath = string.IsNullOrWhiteSpace(CorePath) ? null : CorePath.Trim(),
    };
}
