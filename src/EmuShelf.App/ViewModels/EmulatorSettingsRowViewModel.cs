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

    public string SystemId { get; }
    public string SystemName { get; }
    public string SystemShortName { get; }
    public string AccentColor { get; }
    public string EmulatorName { get; }
    public string DefaultLaunchArguments { get; }

    [ObservableProperty]
    public partial string ExecutablePath { get; set; }

    [ObservableProperty]
    public partial string LaunchArguments { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRescan))]
    [NotifyCanExecuteChangedFor(nameof(RescanLibraryCommand))]
    public partial bool IsMaintenanceBlocked { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMaintenanceStatus))]
    public partial string MaintenanceStatusText { get; set; } = string.Empty;

    public bool CanRescan => _rescanLibrary is not null && !IsMaintenanceBlocked;
    public bool HasMaintenanceStatus => !string.IsNullOrWhiteSpace(MaintenanceStatusText);

    public EmulatorSettingsRowViewModel(
        GameSystem system,
        EmulatorDefinition emulator,
        EmulatorConfiguration? configuration,
        IDialogService dialogs,
        Func<EmulatorSettingsRowViewModel, Task>? rescanLibrary = null,
        bool isExpanded = false,
        IAppLogger? logger = null)
    {
        _dialogs = dialogs;
        _logger = logger ?? NullAppLogger.Instance;
        _rescanLibrary = rescanLibrary;
        SystemId = system.Id;
        SystemName = system.Name;
        SystemShortName = system.ShortName;
        AccentColor = system.AccentColor;
        EmulatorName = emulator.Name;
        DefaultLaunchArguments = emulator.DefaultLaunchArguments;
        ExecutablePath = configuration?.ExecutablePath ?? string.Empty;
        LaunchArguments = configuration?.LaunchArguments ?? emulator.DefaultLaunchArguments;
        IsExpanded = isExpanded;
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
    private void ResetArguments() => LaunchArguments = DefaultLaunchArguments;

    [RelayCommand(CanExecute = nameof(CanRescan))]
    private Task RescanLibraryAsync() =>
        _rescanLibrary?.Invoke(this) ?? Task.CompletedTask;

    public EmulatorConfiguration ToConfiguration() => new(
        SystemId,
        string.IsNullOrWhiteSpace(ExecutablePath) ? null : ExecutablePath.Trim(),
        LaunchArguments);
}
