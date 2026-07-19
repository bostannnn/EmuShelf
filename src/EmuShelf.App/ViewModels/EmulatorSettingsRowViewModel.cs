using System.Collections.ObjectModel;
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
    public ObservableCollection<LibretroCoreOption> AvailableCores { get; } = [];
    public ObservableCollection<LibretroCoreOption> FilteredCores { get; } = [];
    public string ExecutableDescription => IsExecutableShared
        ? "Shared executable"
        : "Executable";
    public bool HasCorePath => !string.IsNullOrWhiteSpace(CorePath);
    public string CoreFileName => HasCorePath
        ? Path.GetFileName(CorePath.Trim())
        : "No core selected";

    internal event Action<EmulatorSettingsRowViewModel, string>? ExecutablePathEdited;

    [ObservableProperty]
    public partial string ExecutablePath { get; set; }

    [ObservableProperty]
    public partial string LaunchArguments { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCorePath))]
    [NotifyPropertyChangedFor(nameof(CoreFileName))]
    [NotifyCanExecuteChangedFor(nameof(ClearCoreCommand))]
    public partial string CorePath { get; set; }

    [ObservableProperty]
    public partial LibretroCoreOption? SelectedCore { get; set; }

    [ObservableProperty]
    public partial string CoreSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRescan))]
    [NotifyPropertyChangedFor(nameof(CanSyncLibrary))]
    [NotifyCanExecuteChangedFor(nameof(RescanLibraryCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncLibraryCommand))]
    public partial bool IsMaintenanceBlocked { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMaintenanceStatus))]
    public partial string MaintenanceStatusText { get; set; } = string.Empty;

    public bool HasRescanLibrary => _rescanLibrary is not null;
    public bool CanRescan => HasRescanLibrary && !IsMaintenanceBlocked;
    public bool HasSyncLibrary => _syncLibrary is not null;
    public bool CanSyncLibrary => HasSyncLibrary && !IsMaintenanceBlocked;
    public bool HasMaintenanceStatus => !string.IsNullOrWhiteSpace(MaintenanceStatusText);

    public EmulatorSettingsRowViewModel(
        GameSystem system,
        EmulatorDefinition emulator,
        EmulatorConfiguration? configuration,
        IDialogService dialogs,
        Func<EmulatorSettingsRowViewModel, Task>? rescanLibrary = null,
        Func<EmulatorSettingsRowViewModel, Task>? syncLibrary = null,
        bool isExpanded = false,
        string? emulatorInstallationId = null,
        bool isExecutableShared = false,
        IAppLogger? logger = null)
    {
        _dialogs = dialogs;
        _logger = logger ?? NullAppLogger.Instance;
        _rescanLibrary = rescanLibrary;
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
        RefreshAvailableCores();
        IsExpanded = isExpanded;
    }

    partial void OnExecutablePathChanged(string value)
    {
        RefreshAvailableCores();
        ExecutablePathEdited?.Invoke(this, value);
    }

    partial void OnCorePathChanged(string value)
    {
        SelectedCore = AvailableCores.FirstOrDefault(option =>
            string.Equals(option.Path, value, StringComparison.OrdinalIgnoreCase));
    }

    partial void OnSelectedCoreChanged(LibretroCoreOption? value)
    {
        if (value is not null && !string.Equals(CorePath, value.Path, StringComparison.OrdinalIgnoreCase))
            CorePath = value.Path;
    }

    partial void OnCoreSearchTextChanged(string value) => RefreshFilteredCores();

    private void RefreshAvailableCores()
    {
        AvailableCores.Clear();
        FilteredCores.Clear();
        if (!RequiresCorePath || string.IsNullOrWhiteSpace(ExecutablePath))
            return;

        var emulatorDirectory = Path.GetDirectoryName(ExecutablePath);
        if (string.IsNullOrWhiteSpace(emulatorDirectory))
            return;

        var coresDirectory = Path.Combine(emulatorDirectory, "cores");
        try
        {
            if (!Directory.Exists(coresDirectory))
                return;

            foreach (var core in Directory.EnumerateFiles(coresDirectory)
                         .Where(path => Path.GetExtension(path) is ".dll" or ".dylib" or ".so")
                         .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                AvailableCores.Add(new LibretroCoreOption(Path.GetFileName(core), core));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"Could not list RetroArch cores for {SystemName}.", ex);
        }

        SelectedCore = AvailableCores.FirstOrDefault(option =>
            string.Equals(option.Path, CorePath, StringComparison.OrdinalIgnoreCase));
        RefreshFilteredCores();
    }

    private void RefreshFilteredCores()
    {
        FilteredCores.Clear();
        var filter = CoreSearchText.Trim();
        foreach (var core in AvailableCores.Where(core =>
                     string.IsNullOrWhiteSpace(filter) ||
                     core.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            FilteredCores.Add(core);
        }
    }

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

    [RelayCommand(CanExecute = nameof(HasCorePath))]
    private void ClearCore() => CorePath = string.Empty;

    [RelayCommand]
    private void ResetArguments() => LaunchArguments = DefaultLaunchArguments;

    [RelayCommand(CanExecute = nameof(CanRescan))]
    private Task RescanLibraryAsync() =>
        _rescanLibrary?.Invoke(this) ?? Task.CompletedTask;

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

    public sealed record LibretroCoreOption(string Name, string Path);
}
