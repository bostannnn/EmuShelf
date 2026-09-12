using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;

namespace EmuShelf.App.ViewModels;

/// <summary>One startup attempt per process, shared by recreated Android views.</summary>
public sealed partial class StartupViewModel(
    Func<Task> start,
    Action restart,
    Func<Task<string?>> chooseFolder,
    Action<Exception> reportFailure) : ViewModelBase
{
    private bool _started;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    public partial bool HasError { get; set; }

    public bool IsLoading => !HasError;

    [ObservableProperty]
    public partial bool IsChoosingFolder { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Opening your library…";

    [ObservableProperty]
    public partial string Diagnostics { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowDiagnostics { get; set; }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (_started)
            return;
        _started = true;
        try
        {
            await start();
        }
        catch (Exception ex)
        {
            // A diagnostic logger must never prevent the recovery screen from appearing.
            try { reportFailure(ex); } catch { }
            HasError = true;
            Status = "Your library couldn't be opened. Check that its storage is connected and has free space, then retry.";
            // Never display arbitrary exception messages: provider errors can contain credentials.
            Diagnostics = $"Startup error: {ex.GetType().Name}\nCheck the Logs folder in your EmuShelf data folder for details.\nIf the folder could not be opened, use Android logcat with the EmuShelfBoot tag.";
        }
    }

    [RelayCommand]
    private void Retry()
    {
        // Composition can have partially registered services. Retry in a fresh process.
        if (HasError && !IsChoosingFolder)
            restart();
    }

    [RelayCommand]
    private async Task ChooseFolderAsync()
    {
        if (!HasError || IsChoosingFolder)
            return;
        IsChoosingFolder = true;
        try
        {
            var error = await chooseFolder();
            if (error is not null)
                Status = error;
        }
        catch
        {
            Status = "The folder couldn't be selected. Check storage access and try again.";
        }
        finally { IsChoosingFolder = false; }
    }

    [RelayCommand]
    private void ToggleDiagnostics() => ShowDiagnostics = !ShowDiagnostics;

    public bool DispatchGamepadAction(GamepadAction action)
    {
        if (!HasError || IsChoosingFolder)
            return true;
        switch (action)
        {
            case GamepadAction.Confirm: RetryCommand.Execute(null); break;
            case GamepadAction.Search: ChooseFolderCommand.Execute(null); break;
            case GamepadAction.Actions: ToggleDiagnosticsCommand.Execute(null); break;
        }
        return true;
    }
}
