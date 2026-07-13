using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Systems;

namespace EmuShelf.App.ViewModels;

public partial class EmulatorSettingsViewModel : ViewModelBase
{
    private readonly IEmulatorConfigurationStore _configurations;
    private readonly LibraryMaintenanceActions? _maintenance;
    private readonly IAppLogger _logger;

    public ObservableCollection<EmulatorSettingsRowViewModel> Rows { get; }
    public event Action<bool>? CloseRequested;

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsMaintainingLibrary { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMaintenanceStatus))]
    public partial string MaintenanceStatusText { get; set; } = string.Empty;

    public bool CanRescanAll => !IsWorking && _maintenance is not null;
    public bool IsWorking => IsSaving || IsMaintainingLibrary;
    public bool HasMaintenanceStatus => !string.IsNullOrWhiteSpace(MaintenanceStatusText);

    public EmulatorSettingsViewModel(
        IReadOnlyList<GameSystem> systems,
        IReadOnlyList<EmulatorDefinition> emulators,
        IReadOnlyDictionary<string, EmulatorConfiguration?> configured,
        IEmulatorConfigurationStore configurations,
        IDialogService dialogs,
        LibraryMaintenanceActions? maintenance = null,
        IAppLogger? logger = null)
    {
        _configurations = configurations;
        _maintenance = maintenance;
        _logger = logger ?? NullAppLogger.Instance;
        Rows = new ObservableCollection<EmulatorSettingsRowViewModel>(systems.Select((system, index) =>
        {
            var emulator = emulators.First(candidate => candidate.Supports(system.Id));
            configured.TryGetValue(system.Id, out var configuration);
            return new EmulatorSettingsRowViewModel(
                system,
                emulator,
                configuration,
                dialogs,
                maintenance is null ? null : RescanSystemAsync,
                isExpanded: index == 0,
                logger: _logger);
        }));
    }

    partial void OnIsSavingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsWorking));
        OnPropertyChanged(nameof(CanRescanAll));
        UpdateRowMaintenanceState();
    }

    partial void OnIsMaintainingLibraryChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRescanAll));
        OnPropertyChanged(nameof(IsWorking));
        UpdateRowMaintenanceState();
    }

    [RelayCommand]
    private Task RescanAllAsync() => RunMaintenanceAsync(
        _maintenance?.RescanAll,
        message => MaintenanceStatusText = message);

    private Task RescanSystemAsync(EmulatorSettingsRowViewModel row) => RunMaintenanceAsync(
        _maintenance is null ? null : () => _maintenance.RescanSystem(row.SystemId),
        message => row.MaintenanceStatusText = message);

    private async Task RunMaintenanceAsync(
        Func<Task<string>>? action,
        Action<string> report)
    {
        if (action is null || IsWorking)
            return;

        IsMaintainingLibrary = true;
        report("Rescanning remembered folders…");
        try
        {
            report(await action());
        }
        catch (Exception ex)
        {
            _logger.Error("Library maintenance failed from Settings.", ex);
            report($"Rescan failed: {ex.Message}");
        }
        finally
        {
            IsMaintainingLibrary = false;
        }
    }

    private void UpdateRowMaintenanceState()
    {
        foreach (var row in Rows)
            row.IsMaintenanceBlocked = IsWorking;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsWorking)
            return;

        IsSaving = true;
        StatusText = string.Empty;
        try
        {
            var configurations = Rows.Select(row => row.ToConfiguration()).ToArray();
            await Task.Run(() => _configurations.SaveAll(configurations));
            CloseRequested?.Invoke(true);
        }
        catch (Exception ex)
        {
            _logger.Error("Could not save emulator settings.", ex);
            StatusText = $"Could not save emulator settings: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
