using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Storage;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// The pre-boot half of the Android setup wizard: the two steps that must be answered before the app can
/// open its database — all-files access and the data folder. It shows the same rail, rows and legend as
/// the in-app half (the couch Settings projection in setup mode) so the user walks one wizard, with the
/// later steps listed but dimmed. On completion it hands the base directory back and the head restarts
/// into the shell, where the remaining steps open automatically. Pure orchestration over
/// <see cref="IDataLocationBootstrap"/>: no Avalonia or Android type, so it is unit-testable.
/// </summary>
public sealed partial class SetupWizardViewModel : ViewModelBase, IGamepadSettingsRowHost
{
    private readonly IDataLocationBootstrap _bootstrap;
    private readonly Action<string> _onCompleted;
    private readonly IAppLogger _logger;
    private readonly List<SetupStep> _liveSteps = [];
    private int _index;
    private bool _completed;
    private string? _existingDataFolder;
    private bool _existingProbed;

    public ObservableCollection<GamepadSettingsRowViewModel> Rows { get; } = [];

    /// <summary>The step rail + START chip, bound by the shared rail view.</summary>
    public SetupWizardRailModel Rail { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FocusedRow))]
    public partial int FocusedRowIndex { get; set; }

    /// <summary>Bumped whenever focus should be revealed (the view scrolls the focused row into view).</summary>
    [ObservableProperty]
    public partial int FocusRevision { get; set; }

    /// <summary>A user-facing line: why this screen is showing, or why a pick was refused.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>True while a pick is in flight, so the rows disable.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Whether the all-files grant is currently held.</summary>
    [ObservableProperty]
    public partial bool IsPermissionGranted { get; set; }

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);

    public GamepadSettingsRowViewModel? FocusedRow =>
        Rows.Count == 0 ? null : Rows[Math.Clamp(FocusedRowIndex, 0, Rows.Count - 1)];

    public SetupStep CurrentStep => _liveSteps[_index];

    public string Title => CurrentStep switch
    {
        SetupStep.StorageAccess => "Storage access",
        _ => "Data folder",
    };

    public string Description => CurrentStep switch
    {
        SetupStep.StorageAccess =>
            "EmuShelf needs to read your games and keep its library on this device's storage. Android asks you to allow this once.",
        _ => "Where EmuShelf keeps its library, covers, settings and saves. Your game files are never moved.",
    };

    public SetupWizardViewModel(
        IDataLocationBootstrap bootstrap,
        DataLocationOnboardingReason reason,
        Action<string> onCompleted,
        IAppLogger? logger = null)
    {
        _bootstrap = bootstrap;
        _onCompleted = onCompleted;
        _logger = logger ?? NullAppLogger.Instance;
        IsPermissionGranted = bootstrap.IsStoragePermissionGranted;

        if (bootstrap.RequiresStoragePermission)
            _liveSteps.Add(SetupStep.StorageAccess);
        _liveSteps.Add(SetupStep.DataFolder);

        // The whole wizard is listed so this page reads as its first two steps, not a separate screen.
        // Steps that need the composed app are dimmed; the ones that only exist on some devices (a second
        // screen, close-on-return) appear only where they will actually be asked.
        foreach (var step in _liveSteps)
            Rail.Steps.Add(new SetupStepViewModel(step));
        if (bootstrap.ShowSecondScreenReturnStep)
            Rail.Steps.Add(new SetupStepViewModel(SetupStep.SecondScreen) { IsDimmed = true });
        if (bootstrap.RequiresStoragePermission)
            Rail.Steps.Add(new SetupStepViewModel(SetupStep.ClosingGames) { IsDimmed = true });
        Rail.Steps.Add(new SetupStepViewModel(SetupStep.GamesAndEmulators) { IsDimmed = true });
        Rail.Steps.Add(new SetupStepViewModel(SetupStep.Saves) { IsDimmed = true });
        Rail.StartCommand = new RelayCommand(Advance);

        StatusMessage = reason switch
        {
            // A folder was chosen before, but the access that makes it readable is no longer held.
            DataLocationOnboardingReason.StoragePermissionMissing =>
                "EmuShelf lost access to its data folder. Allow access again to continue.",
            // The chosen folder is unreachable now (card removed, deleted, remounted).
            DataLocationOnboardingReason.LocationUnavailable =>
                "EmuShelf's data folder can't be reached. Reconnect its storage, or choose a new folder.",
            _ => string.Empty,
        };

        // Start past the storage step when the grant is already held (a lost folder, or a platform that
        // needs no grant).
        _index = _liveSteps.Count > 1 && IsPermissionGranted ? 1 : 0;
        bootstrap.StoragePermissionMaybeChanged += OnStoragePermissionMaybeChanged;
        Rebuild();
    }

    private void OnStoragePermissionMaybeChanged() => RefreshPermissionState();

    /// <summary>
    /// Re-reads the platform state. The Android head calls this when EmuShelf returns to the foreground —
    /// after the user flips the all-files switch in Android settings and comes back. If the pointer now
    /// resolves (the grant restored a known folder, the mirror is readable after a reinstall, the card is
    /// back) nothing is left to ask and the wizard completes on its own; otherwise a freshly held grant
    /// advances to the folder step.
    /// </summary>
    public void RefreshPermissionState()
    {
        if (_completed)
            return;

        var resolution = _bootstrap.Resolve();
        if (resolution.IsResolved)
        {
            _logger.Information($"Data folder resolved on foreground return: '{resolution.BaseDirectory}'.");
            Complete(resolution.BaseDirectory!);
            return;
        }

        var wasGranted = IsPermissionGranted;
        IsPermissionGranted = _bootstrap.IsStoragePermissionGranted;
        if (IsPermissionGranted && !wasGranted && CurrentStep == SetupStep.StorageAccess)
        {
            StatusMessage = string.Empty;
            _existingProbed = false;
            SelectStep(_index + 1);
            return;
        }

        Rebuild();
    }

    /// <summary>
    /// Routes a couch controller action here. This page shows before the shared shell (and its dispatcher)
    /// exists, so the Android head points its key-event bridge at it. Returns true when consumed.
    /// </summary>
    public bool DispatchGamepadAction(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.NavigateUp:
                MoveFocus(-1);
                return true;
            case GamepadAction.NavigateDown:
                MoveFocus(1);
                return true;
            case GamepadAction.NavigateLeft:
            case GamepadAction.NavigateRight:
            case GamepadAction.PreviousPlatform:
            case GamepadAction.NextPlatform:
                return true;
            case GamepadAction.Confirm:
                if (FocusedRow is { } row)
                    _ = FocusAndActivateAsync(row);
                return true;
            case GamepadAction.Menu:
                Advance();
                return true;
            case GamepadAction.Cancel:
                Back();
                return true;
            default:
                return false;
        }
    }

    public async Task FocusAndActivateAsync(GamepadSettingsRowViewModel row)
    {
        var index = Rows.IndexOf(row);
        if (index < 0)
            return;
        SetFocus(index);
        if (row.CanActivate && row.Activate is { } activate)
            await activate();
    }

    private void MoveFocus(int delta)
    {
        if (Rows.Count == 0)
            return;
        SetFocus(Math.Clamp(FocusedRowIndex + Math.Sign(delta), 0, Rows.Count - 1));
    }

    private void SetFocus(int index)
    {
        FocusedRowIndex = index;
        for (var i = 0; i < Rows.Count; i++)
            Rows[i].IsFocused = i == FocusedRowIndex;
        FocusRevision++;
    }

    /// <summary>START: the next step. The folder step has no "next" — its rows complete the wizard.</summary>
    private void Advance()
    {
        if (CurrentStep == SetupStep.StorageAccess && IsPermissionGranted && _index < _liveSteps.Count - 1)
            SelectStep(_index + 1);
    }

    /// <summary>B: the previous step, if there is one. The first step has nothing to go back to.</summary>
    private void Back()
    {
        if (_index > 0)
            SelectStep(_index - 1);
    }

    private void SelectStep(int index)
    {
        _index = Math.Clamp(index, 0, _liveSteps.Count - 1);
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        Rebuild();
    }

    private void Rebuild()
    {
        var specs = (CurrentStep == SetupStep.StorageAccess ? StorageRows() : DataFolderRows()).ToList();
        Rows.Clear();
        foreach (var spec in specs)
            Rows.Add(new GamepadSettingsRowViewModel(this, spec));
        SetFocus(0);
        RefreshRail();
    }

    private IEnumerable<GamepadSettingsRowSpec> StorageRows()
    {
        yield return IsPermissionGranted
            ? new GamepadSettingsRowSpec(
                "setup.storage.grant",
                "Allow access to all files",
                "Allowed. Press START to continue.",
                "Allowed",
                GamepadSettingsRowKind.Information)
            : new GamepadSettingsRowSpec(
                "setup.storage.grant",
                "Allow access to all files",
                "Not allowed yet. A opens Android's permission page for EmuShelf.",
                "A OPEN",
                GamepadSettingsRowKind.Action,
                Activate: () =>
                {
                    _bootstrap.RequestStoragePermission();
                    // The answer is observed when EmuShelf regains the foreground (RefreshPermissionState).
                    return Task.CompletedTask;
                },
                IsWarning: true);
        yield return new GamepadSettingsRowSpec(
            "setup.storage.why",
            "What this is used for",
            "Reading your games where they are, and writing only inside EmuShelf's own folder. Games are never moved or deleted.",
            string.Empty,
            GamepadSettingsRowKind.Information);
    }

    private IEnumerable<GamepadSettingsRowSpec> DataFolderRows()
    {
        var enabled = !IsBusy && (!_bootstrap.RequiresStoragePermission || IsPermissionGranted);

        if (!_existingProbed && enabled)
        {
            _existingProbed = true;
            _existingDataFolder = _bootstrap.FindExistingDataFolder();
        }

        if (_existingDataFolder is { } existing)
        {
            // A library from a previous install: the first, focused row, so a reinstall is one press.
            yield return new GamepadSettingsRowSpec(
                "setup.folder.existing",
                "Use your existing library",
                $"Found at {existing}",
                "A USE",
                GamepadSettingsRowKind.Action,
                IsEnabled: enabled,
                Activate: () => CompleteWithAsync(() => _bootstrap.UseExistingFolderAsync(existing)));
        }

        if (_bootstrap.RecommendedBaseDirectory is { } recommended)
        {
            yield return new GamepadSettingsRowSpec(
                "setup.folder.recommended",
                "Create a new folder here",
                $"{recommended} on this device's storage",
                "A CREATE",
                GamepadSettingsRowKind.Action,
                IsEnabled: enabled,
                Activate: () => CompleteWithAsync(_bootstrap.UseRecommendedFolderAsync));
        }

        yield return new GamepadSettingsRowSpec(
            "setup.folder.pick",
            "Pick another folder",
            "Android's folder picker. Download, Documents and the top level can't be picked.",
            "A PICK",
            GamepadSettingsRowKind.Action,
            IsEnabled: enabled,
            Activate: () => CompleteWithAsync(_bootstrap.PickFolderAsync));

        yield return new GamepadSettingsRowSpec(
            "setup.folder.note",
            "EmuShelf only writes inside this folder",
            "Your game files stay where they are and are never moved.",
            string.Empty,
            GamepadSettingsRowKind.Information);
    }

    private void RefreshRail()
    {
        foreach (var entry in Rail.Steps)
        {
            entry.IsCurrent = entry.Step == CurrentStep;
            switch (entry.Step)
            {
                case SetupStep.StorageAccess:
                    entry.Status = IsPermissionGranted ? "Allowed" : "Not allowed yet";
                    entry.IsDone = IsPermissionGranted;
                    entry.IsWarning = !IsPermissionGranted;
                    break;
                case SetupStep.DataFolder:
                    entry.Status = string.Empty;
                    break;
            }
        }

        if (CurrentStep == SetupStep.StorageAccess)
        {
            Rail.StartLabel = "Continue";
            Rail.StartDetail = IsPermissionGranted ? "Next: Data folder" : "Allow access first";
            Rail.IsStartEnabled = IsPermissionGranted;
        }
        else
        {
            Rail.StartLabel = "Continue";
            Rail.StartDetail = "Pick a folder first";
            Rail.IsStartEnabled = false;
        }
    }

    // Shared body for every folder action: run it, complete on success, surface a refusal, and never let
    // an exception leave the screen stuck.
    private async Task CompleteWithAsync(Func<Task<DataLocationPickResult>> action)
    {
        if (IsBusy || _completed)
            return;

        IsBusy = true;
        Rebuild();
        try
        {
            var result = await action();
            if (result.Succeeded)
            {
                _logger.Information($"Data folder chosen: '{result.BaseDirectory}'.");
                Complete(result.BaseDirectory!);
                return;
            }

            // A plain cancellation leaves the screen as-is; a validated rejection explains itself.
            if (result.Error is { } error)
            {
                _logger.Warning($"Data folder selection rejected: {error}");
                StatusMessage = error;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("The data-folder selection failed unexpectedly.", ex);
            StatusMessage = "Something went wrong setting that folder. Please try again.";
        }
        finally
        {
            IsBusy = false;
            if (!_completed)
                Rebuild();
        }
    }

    // The single exit: detach from the platform signal and hand the base directory to the composition
    // root. Guarded so a pick landing and a foreground re-resolve racing it complete once (the Android
    // handoff restarts the process).
    private void Complete(string baseDirectory)
    {
        if (_completed)
            return;
        _completed = true;
        _bootstrap.StoragePermissionMaybeChanged -= OnStoragePermissionMaybeChanged;
        _onCompleted(baseDirectory);
    }
}
