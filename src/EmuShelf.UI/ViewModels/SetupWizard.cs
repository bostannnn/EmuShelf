using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// The steps of the Android setup wizard, in the order they are walked. The first two run before the app
/// can boot (phase A, <see cref="SetupWizardViewModel"/>); the rest run inside the composed app as the
/// couch Settings projection in setup mode (<see cref="GamepadSettingsViewModel.IsSetupMode"/>). Both
/// phases list every step in one rail so the user sees one wizard, not two screens.
/// </summary>
public enum SetupStep
{
    StorageAccess,
    DataFolder,
    SecondScreen,
    ClosingGames,
    GamesAndEmulators,
    Saves,
}

/// <summary>Plain-language rail labels for the steps. One place, so both phases agree.</summary>
public static class SetupStepLabels
{
    public static string For(SetupStep step) => step switch
    {
        SetupStep.StorageAccess => "Storage access",
        SetupStep.DataFolder => "Data folder",
        SetupStep.SecondScreen => "Second screen",
        SetupStep.ClosingGames => "Closing games",
        SetupStep.GamesAndEmulators => "Games & emulators",
        SetupStep.Saves => "Saves",
        _ => step.ToString(),
    };
}

/// <summary>One entry in the setup rail: the step's name and its one-line outcome.</summary>
public sealed partial class SetupStepViewModel : ObservableObject
{
    public SetupStepViewModel(SetupStep step)
    {
        Step = step;
        Label = SetupStepLabels.For(step);
    }

    public SetupStep Step { get; }
    public string Label { get; }

    /// <summary>The outcome line under the label ("Allowed", "Off", "981 games"); empty for nothing yet.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>Status reports something the user still has to deal with (warning colour).</summary>
    [ObservableProperty]
    public partial bool IsWarning { get; set; }

    /// <summary>Status reports a satisfied outcome (success colour).</summary>
    [ObservableProperty]
    public partial bool IsDone { get; set; }

    /// <summary>The step the content column is showing.</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    /// <summary>A later-phase step that cannot be reached yet (phase A dims the in-app steps).</summary>
    [ObservableProperty]
    public partial bool IsDimmed { get; set; }
}

/// <summary>
/// What the shared <c>SetupWizardRailView</c> binds to: the step list plus the START chip that moves the
/// wizard forward. Both phases expose one of these, so the rail is one control with one look.
/// </summary>
public sealed partial class SetupWizardRailModel : ObservableObject
{
    public ObservableCollection<SetupStepViewModel> Steps { get; } = [];

    [ObservableProperty]
    public partial string StartLabel { get; set; } = "Continue";

    [ObservableProperty]
    public partial string StartDetail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsStartEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsStartFocused { get; set; }

    [ObservableProperty]
    public partial ICommand? StartCommand { get; set; }

    /// <summary>Marks <paramref name="current"/> as the shown step and every earlier listed step as done.</summary>
    public void SetCurrent(SetupStep current)
    {
        var passed = true;
        foreach (var step in Steps)
        {
            if (step.Step == current)
                passed = false;
            step.IsCurrent = step.Step == current;
            if (passed && !step.IsDimmed)
                step.IsDone = step.IsDone || string.IsNullOrEmpty(step.Status) is false;
        }
    }
}

/// <summary>
/// What the in-app half of the wizard needs from the host beyond the settings model: the second-screen
/// facts (Android's external-display probe) and how the data-folder step should read in the rail.
/// </summary>
public sealed record SetupWizardOptions(
    bool HasSecondScreen,
    Func<bool> IsSecondScreenReturnReady,
    Action RequestSecondScreenReturn,
    string DataFolderStatus);
