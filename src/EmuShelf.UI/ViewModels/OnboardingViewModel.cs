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
    }

    private int _focusIndex;

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

    /// <summary>The folder actions are offered only once any required grant is held and no pick is in flight.</summary>
    public bool CanChooseFolder => (!RequiresPermission || IsPermissionGranted) && !IsBusy;

    /// <summary>
    /// Whether the "grant all-files access" step is still outstanding. While it is, Grant is the only live
    /// action; once it clears, the folder actions are.
    /// </summary>
    public bool IsGrantStepActive => RequiresPermission && !IsPermissionGranted;

    /// <summary>The controller focus ring is on the Grant button.</summary>
    public bool IsGrantFocused => Focused == OnboardingAction.Grant;

    /// <summary>The controller focus ring is on the "Use recommended folder" button.</summary>
    public bool IsRecommendedFocused => Focused == OnboardingAction.UseRecommended;

    /// <summary>The controller focus ring is on the "Choose a different folder" button.</summary>
    public bool IsChooseFocused => Focused == OnboardingAction.ChooseDifferent;

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
        StatusMessage = InitialMessageFor(reason);
        bootstrap.StoragePermissionMaybeChanged += OnStoragePermissionMaybeChanged;
    }

    // The actions that currently have a live button, in visual order — the set the focus ring walks.
    private IReadOnlyList<OnboardingAction> LiveActions => IsGrantStepActive
        ? [OnboardingAction.Grant]
        : ShowRecommended
            ? [OnboardingAction.UseRecommended, OnboardingAction.ChooseDifferent]
            : [OnboardingAction.ChooseDifferent];

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
        // Setting IsPermissionGranted raises the focus/step properties. Reset the ring to the first live
        // action so it lands on the recommended button the moment the grant clears.
        _focusIndex = 0;
        IsPermissionGranted = _bootstrap.IsStoragePermissionGranted;
        if (IsPermissionGranted && RequiresPermission)
            StatusMessage = "All-files access granted. Now choose where your data lives.";
    }

    [RelayCommand]
    private void GrantPermission()
    {
        _bootstrap.RequestStoragePermission();
        // The result is observed when EmuShelf regains the foreground via RefreshPermissionState; there is
        // nothing to await here since the grant happens in the system Settings app.
    }

    [RelayCommand]
    private Task UseRecommendedAsync() => CompleteWithAsync(_bootstrap.UseRecommendedFolderAsync);

    [RelayCommand]
    private Task ChooseDifferentAsync() => CompleteWithAsync(_bootstrap.PickFolderAsync);

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
                _bootstrap.StoragePermissionMaybeChanged -= OnStoragePermissionMaybeChanged;
                _onCompleted(result.BaseDirectory!);
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
