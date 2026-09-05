using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Storage;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// Drives the first-run "choose where EmuShelf keeps its data" screen. It orchestrates the platform steps
/// the <see cref="IDataLocationBootstrap"/> supplies — granting all-files access, then either accepting the
/// recommended folder in one tap or picking a different one via the system picker — and, on success, hands
/// the resolved base directory back so the shared composition root can build the app in-process. Pure
/// orchestration and observable state: no Avalonia or Android type appears here, so it is unit-testable.
/// </summary>
public sealed partial class OnboardingViewModel : ViewModelBase
{
    private readonly IDataLocationBootstrap _bootstrap;
    private readonly Action<string> _onCompleted;
    private readonly IAppLogger _logger;

    // The couch focus ring walks this ordered list of the actions that are live right now. Only the grant
    // step is live while access is outstanding; once it clears, the folder actions are.
    private enum OnboardingAction
    {
        Grant,
        UseRecommended,
        ChooseDifferent,
        EnableSecondScreenReturn,
    }

    private int _focusIndex;

    // Onboarding hands off exactly once. Two paths can reach completion within milliseconds of each other —
    // a folder pick landing, and the foreground-return re-resolve that follows the picker closing — and on
    // Android the handoff restarts the process, so a second call must be a no-op rather than a second restart.
    private bool _completed;

    /// <summary>Whether the all-files-access step is shown at all (true on Android, false where no grant is needed).</summary>
    public bool RequiresPermission => _bootstrap.RequiresStoragePermission;

    /// <summary>Whether a one-tap recommended folder exists on this platform (Android).</summary>
    public bool ShowRecommended => _bootstrap.RecommendedBaseDirectory is not null;

    /// <summary>The recommended folder path shown under its button (e.g. <c>/storage/emulated/0/EmuShelf</c>).</summary>
    public string RecommendedPath => _bootstrap.RecommendedBaseDirectory ?? string.Empty;

    /// <summary>Whether the grant is currently held; drives every action's enabled state and the step's checkmark.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseFolder))]
    [NotifyPropertyChangedFor(nameof(IsGrantStepActive))]
    [NotifyPropertyChangedFor(nameof(IsGrantFocused))]
    [NotifyPropertyChangedFor(nameof(IsRecommendedFocused))]
    [NotifyPropertyChangedFor(nameof(IsChooseFocused))]
    public partial bool IsPermissionGranted { get; set; }

    /// <summary>True while a pick is in flight, so the buttons disable and the UI shows a working state.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseFolder))]
    public partial bool IsBusy { get; set; }

    /// <summary>A user-facing line: the current instruction, or the reason a pick was rejected.</summary>
    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>
    /// The folder actions are offered only once every required gate is satisfied (any all-files grant, and
    /// the second-screen-return step on a device that has a second screen) and no pick is in flight.
    /// </summary>
    public bool CanChooseFolder =>
        (!RequiresPermission || IsPermissionGranted) && !IsSecondScreenReturnStepActive && !IsBusy;

    /// <summary>
    /// Whether the "grant all-files access" step is still outstanding. While it is, Grant is the only live
    /// action; once it clears, the folder actions are.
    /// </summary>
    public bool IsGrantStepActive => RequiresPermission && !IsPermissionGranted;

    /// <summary>
    /// Whether the second-screen-return step is a still-outstanding requirement. It is mandatory (not
    /// optional) on a device that actually has a second screen: an external-screen launch cannot return
    /// without the watcher, so onboarding does not complete until it is enabled. Devices with no second
    /// screen never see it, and the launch path re-checks readiness for a screen attached after onboarding.
    /// </summary>
    public bool IsSecondScreenReturnStepActive => ShowSecondScreenReturn && !IsSecondScreenReturnEnabled;

    /// <summary>The controller focus ring is on the Grant button.</summary>
    public bool IsGrantFocused => Focused == OnboardingAction.Grant;

    /// <summary>The controller focus ring is on the "Use recommended folder" button.</summary>
    public bool IsRecommendedFocused => Focused == OnboardingAction.UseRecommended;

    /// <summary>The controller focus ring is on the "Choose a different folder" button.</summary>
    public bool IsChooseFocused => Focused == OnboardingAction.ChooseDifferent;

    /// <summary>The controller focus ring is on the "Enable second-screen return" button.</summary>
    public bool IsSecondScreenReturnFocused => Focused == OnboardingAction.EnableSecondScreenReturn;

    /// <summary>
    /// Whether the second-screen-return step is shown on this device (a Thor with Screen-2). When shown it is
    /// a required gate — see <see cref="IsSecondScreenReturnStepActive"/> — not an optional extra. Read once
    /// at construction: the companion display is stable for the life of the onboarding screen, and the
    /// Android probe behind it (a display-manager query) is walked repeatedly by the focus ring.
    /// </summary>
    public bool ShowSecondScreenReturn { get; }

    /// <summary>Whether the second-screen return watcher is enabled; drives the step's Enable button vs. checkmark.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseFolder))]
    [NotifyPropertyChangedFor(nameof(IsSecondScreenReturnStepActive))]
    [NotifyPropertyChangedFor(nameof(IsSecondScreenReturnFocused))]
    public partial bool IsSecondScreenReturnEnabled { get; set; }

    public OnboardingViewModel(
        IDataLocationBootstrap bootstrap,
        DataLocationOnboardingReason reason,
        Action<string> onCompleted,
        IAppLogger? logger = null)
    {
        _bootstrap = bootstrap;
        _onCompleted = onCompleted;
        _logger = logger ?? NullAppLogger.Instance;
        IsPermissionGranted = bootstrap.IsStoragePermissionGranted;
        ShowSecondScreenReturn = bootstrap.ShowSecondScreenReturnStep;
        IsSecondScreenReturnEnabled = bootstrap.IsSecondScreenReturnEnabled;
        // The gate message applies only once the grant (which comes first) is satisfied; while the grant is
        // still outstanding its own instruction leads.
        StatusMessage = !IsGrantStepActive && IsSecondScreenReturnStepActive
            ? SecondScreenGateMessage
            : InitialMessageFor(reason);
        bootstrap.StoragePermissionMaybeChanged += OnStoragePermissionMaybeChanged;
    }

    // The actions that currently have a live button, in visual order — the set the focus ring walks. Each
    // required gate is exclusive while outstanding, in order: first the all-files grant, then the
    // second-screen-return step (mandatory on a device with a second screen), and only once both are
    // satisfied do the folder actions that complete onboarding appear.
    private IReadOnlyList<OnboardingAction> LiveActions
    {
        get
        {
            if (IsGrantStepActive)
                return [OnboardingAction.Grant];

            if (IsSecondScreenReturnStepActive)
                return [OnboardingAction.EnableSecondScreenReturn];

            var actions = new List<OnboardingAction>(2);
            if (ShowRecommended)
                actions.Add(OnboardingAction.UseRecommended);
            actions.Add(OnboardingAction.ChooseDifferent);
            return actions;
        }
    }

    private OnboardingAction Focused
    {
        get
        {
            var actions = LiveActions;
            return actions[Math.Clamp(_focusIndex, 0, actions.Count - 1)];
        }
    }

    private void OnStoragePermissionMaybeChanged() => RefreshPermissionState();

    /// <summary>
    /// Routes a couch controller action to the onboarding screen. This screen shows before the shared shell
    /// (and its <c>MainViewModel.DispatchGamepadAction</c>) exists, so the Android head points its key-event
    /// bridge here instead — otherwise the D-pad and A button would be dead on the first screen the user ever
    /// sees, on a gamepad-first device. Returns true when the action was consumed.
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
                return true;
            case GamepadAction.Confirm:
                ActivateFocused();
                return true;
            default:
                return false;
        }
    }

    private void MoveFocus(int delta)
    {
        var count = LiveActions.Count;
        if (count <= 1)
            return;

        _focusIndex = ((_focusIndex + delta) % count + count) % count;
        RaiseFocusChanged();
    }

    private void RaiseFocusChanged()
    {
        OnPropertyChanged(nameof(IsGrantFocused));
        OnPropertyChanged(nameof(IsRecommendedFocused));
        OnPropertyChanged(nameof(IsChooseFocused));
        OnPropertyChanged(nameof(IsSecondScreenReturnFocused));
    }

    private void ActivateFocused()
    {
        switch (Focused)
        {
            case OnboardingAction.Grant when IsGrantStepActive:
                GrantPermissionCommand.Execute(null);
                break;
            case OnboardingAction.UseRecommended when CanChooseFolder:
                UseRecommendedCommand.Execute(null);
                break;
            case OnboardingAction.ChooseDifferent when CanChooseFolder:
                ChooseDifferentCommand.Execute(null);
                break;
            case OnboardingAction.EnableSecondScreenReturn:
                RequestSecondScreenReturnCommand.Execute(null);
                break;
        }
    }

    private string InitialMessageFor(DataLocationOnboardingReason reason) => reason switch
    {
        // The pointer is gone entirely — the genuine first launch.
        DataLocationOnboardingReason.FirstRun =>
            "Choose where EmuShelf keeps its library, saves, settings, and downloaded artwork. "
            + "Keeping it on shared storage means it survives reinstalls.",
        // A folder was chosen before, but the access that makes it readable is no longer granted.
        DataLocationOnboardingReason.StoragePermissionMissing =>
            "EmuShelf needs all-files access to reach your data folder. Grant it, then continue.",
        // The chosen folder is unreachable now (card removed, deleted, remounted).
        DataLocationOnboardingReason.LocationUnavailable =>
            "EmuShelf's data folder can't be reached. Reconnect its storage, or choose a new folder.",
        _ => "Choose where EmuShelf keeps its data.",
    };

    /// <summary>
    /// Re-reads the grant from the platform. The Android head calls this when EmuShelf returns to the
    /// foreground, so flipping the system all-files switch and coming back updates the actions' state
    /// without a manual refresh.
    /// </summary>
    public void RefreshPermissionState()
    {
        // First: is there anything left to onboard? The pointer may have become readable behind our back —
        // the all-files grant was just flipped on and the pointer's shared-storage mirror is now visible
        // (a reinstall), the SD card holding the folder was remounted, or the verdict this process was
        // created with was simply stale. In every such case the user has nothing to choose, so complete
        // straight away instead of showing a folder picker they already answered.
        if (!_completed)
        {
            var resolution = _bootstrap.Resolve();
            if (resolution.IsResolved)
            {
                _logger.Information($"Data folder resolved on foreground return: '{resolution.BaseDirectory}'.");
                Complete(resolution.BaseDirectory!);
                return;
            }
        }

        // Setting IsPermissionGranted raises the focus/step properties. Reset the ring to the first live
        // action so it lands on the recommended button the moment the grant clears. Also re-read the
        // second-screen-return switch: enabling it in system Settings and returning drops that step and its
        // focus flag without a manual refresh, mirroring the grant.
        _focusIndex = 0;
        IsPermissionGranted = _bootstrap.IsStoragePermissionGranted;
        IsSecondScreenReturnEnabled = _bootstrap.IsSecondScreenReturnEnabled;
        RaiseFocusChanged();
        if (IsSecondScreenReturnStepActive)
            StatusMessage = SecondScreenGateMessage;
        else if (IsPermissionGranted && RequiresPermission)
            StatusMessage = "All-files access granted. Now choose where your data lives.";
    }

    // Shown while the mandatory second-screen-return step is the outstanding gate, so the disabled folder
    // actions read as "one more required step", not as a dead end.
    private const string SecondScreenGateMessage =
        "Enable second-screen return to finish setup — it lets a game played on the second screen return to "
        + "your library when it closes.";

    [RelayCommand]
    private void GrantPermission()
    {
        _bootstrap.RequestStoragePermission();
        // The result is observed when EmuShelf regains the foreground via RefreshPermissionState; there is
        // nothing to await here since the grant happens in the system Settings app.
    }

    [RelayCommand]
    private void RequestSecondScreenReturn()
    {
        _bootstrap.RequestSecondScreenReturn();
        // The result is observed when EmuShelf regains the foreground via RefreshPermissionState; the switch
        // is flipped in the system accessibility screen, so there is nothing to await here.
    }

    [RelayCommand]
    private Task UseRecommendedAsync() => CompleteWithAsync(_bootstrap.UseRecommendedFolderAsync);

    [RelayCommand]
    private Task ChooseDifferentAsync() => CompleteWithAsync(_bootstrap.PickFolderAsync);

    // The single exit: detach from the platform signal and hand the base directory to the composition root.
    private void Complete(string baseDirectory)
    {
        if (_completed)
            return;
        _completed = true;
        _bootstrap.StoragePermissionMaybeChanged -= OnStoragePermissionMaybeChanged;
        _onCompleted(baseDirectory);
    }

    // Shared body for both folder actions: run it, complete onboarding on success, surface a rejection
    // reason otherwise, and never let an exception leave the screen stuck.
    private async Task CompleteWithAsync(Func<Task<DataLocationPickResult>> action)
    {
        if (!CanChooseFolder)
            return;

        IsBusy = true;
        try
        {
            var result = await action();
            if (result.Succeeded)
            {
                _logger.Information($"Data folder chosen: '{result.BaseDirectory}'.");
                Complete(result.BaseDirectory!);
                return;
            }

            // A plain cancellation leaves the instruction as-is; a validated rejection explains itself.
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
        }
    }
}
