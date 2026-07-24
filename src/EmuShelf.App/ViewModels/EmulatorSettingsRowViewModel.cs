using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Systems;
using EmuShelf.Infrastructure.Launching;

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
    public bool IsFlatpakTarget => TargetKind == "Flatpak";
    public bool IsDirectTarget => !IsFlatpakTarget;
    public ObservableCollection<LibretroCoreOption> AvailableCores { get; } = [];
    public ObservableCollection<LibretroCoreOption> FilteredCores { get; } = [];
    public ObservableCollection<string> AvailableFlatpakApplicationIds { get; } = [];
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

    /// <summary>Either Direct (binary/AppImage) or Flatpak. A Flatpak id is never inferred.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFlatpakTarget))]
    [NotifyPropertyChangedFor(nameof(IsDirectTarget))]
    public partial string TargetKind { get; set; } = "Direct";

    [ObservableProperty]
    public partial string FlatpakAppId { get; set; } = string.Empty;

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
        TargetKind = configuration?.LaunchTarget is FlatpakApplicationTarget ? "Flatpak" : "Direct";
        FlatpakAppId = (configuration?.LaunchTarget as FlatpakApplicationTarget)?.AppId ?? string.Empty;
        foreach (var appId in new FlatpakApplicationDiscovery().FindInstalledForEmulator(EmulatorId))
            AvailableFlatpakApplicationIds.Add(appId);
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

    partial void OnTargetKindChanged(string value)
    {
        OnPropertyChanged(nameof(IsFlatpakTarget));
        OnPropertyChanged(nameof(IsDirectTarget));
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

        try
        {
            foreach (var core in CoreSearchDirectories(emulatorDirectory)
                         .Where(Directory.Exists)
                         .SelectMany(Directory.EnumerateFiles)
                         .Where(path => Path.GetExtension(path) is ".dll" or ".dylib" or ".so")
                         .DistinctBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
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

    // RetroArch keeps cores beside the executable in portable and AppImage-extracted layouts,
    // but a system or AppImage install on Linux/macOS keeps them under the user's RetroArch
    // config directory instead, so an adjacent-only scan leaves the picker empty on the Deck.
    // The Flatpak core layout is deliberately excluded: Flatpak RetroArch is unsupported and its
    // cores live in a private sandbox path that must not be inferred.
    private static IEnumerable<string> CoreSearchDirectories(string emulatorDirectory)
    {
        yield return Path.Combine(emulatorDirectory, "cores");
        if (OperatingSystem.IsWindows())
            yield break;

        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
                yield break;
            configHome = Path.Combine(home, ".config");
        }

        yield return Path.Combine(configHome, "retroarch", "cores");
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
        IsFlatpakTarget || string.IsNullOrWhiteSpace(ExecutablePath) ? null : ExecutablePath.Trim(),
        LaunchArguments)
    {
        LaunchTarget = IsFlatpakTarget
            ? (string.IsNullOrWhiteSpace(FlatpakAppId) ? null : new FlatpakApplicationTarget(FlatpakAppId.Trim()))
            : (string.IsNullOrWhiteSpace(ExecutablePath) ? null : new DirectExecutableTarget(ExecutablePath.Trim())),
        EmulatorId = EmulatorId,
        EmulatorInstallationId = EmulatorInstallationId,
        CorePath = string.IsNullOrWhiteSpace(CorePath) ? null : CorePath.Trim(),
    };

    public sealed record LibretroCoreOption(string Name, string Path);
}
