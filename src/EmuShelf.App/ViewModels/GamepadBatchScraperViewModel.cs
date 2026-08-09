using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EmuShelf.App.ViewModels;

/// <summary>Which controller-focusable element in the batch-scrape overlay holds the ring.</summary>
public enum GamepadBatchScraperTargetKind
{
    None,
    RefreshToggle,
    Start,
    Cancel,
    Close,
}

/// <summary>
/// Controller-native presentation and linear D-pad focus around the shared
/// <see cref="GameBatchScraperViewModel"/>. It owns no scrape logic: it wraps the batch view model,
/// mirrors its state, and adds only the focus model (Up/Down moves; A activates) the Gamepad overlay
/// needs. The couch flow keeps the sensible defaults (fill missing values, all media) and exposes just
/// the one choice that changes outcomes — whether to replace values ScreenScraper already owns —
/// because per-field media selection stays a Desktop power-user control.
/// </summary>
public sealed partial class GamepadBatchScraperViewModel : ObservableObject, IDisposable
{
    private sealed class FocusTarget
    {
        public required GamepadBatchScraperTargetKind Kind { get; init; }
        public required Action<bool> SetFocused { get; init; }
        public Action? Activate { get; init; }
    }

    private readonly List<FocusTarget> _targets = [];
    private bool _disposed;

    public GameBatchScraperViewModel Batch { get; }

    [ObservableProperty] public partial bool IsRefreshFocused { get; set; }
    [ObservableProperty] public partial bool IsStartFocused { get; set; }
    [ObservableProperty] public partial bool IsCancelFocused { get; set; }
    [ObservableProperty] public partial bool IsCloseFocused { get; set; }

    public int FocusIndex { get; private set; } = -1;

    public GamepadBatchScraperTargetKind FocusedKind =>
        FocusIndex >= 0 && FocusIndex < _targets.Count ? _targets[FocusIndex].Kind : GamepadBatchScraperTargetKind.None;

    public GamepadBatchScraperViewModel(GameBatchScraperViewModel batch)
    {
        Batch = batch;
        Batch.PropertyChanged += OnBatchPropertyChanged;
        RebuildTargets();
    }

    /// <summary>D-pad Up/Down: move the focus ring one step within the current state's target list.</summary>
    public void MoveFocus(int delta) => SetFocus(FocusIndex + delta);

    /// <summary>A: activate the focused target — toggle the replace choice, or run a command.</summary>
    public void Activate()
    {
        if (FocusIndex >= 0 && FocusIndex < _targets.Count)
            _targets[FocusIndex].Activate?.Invoke();
    }

    private void OnBatchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GameBatchScraperViewModel.State))
            return;

        RebuildTargets();
    }

    private void RebuildTargets()
    {
        foreach (var target in _targets)
            target.SetFocused(false);
        _targets.Clear();
        FocusIndex = -1;

        switch (Batch.State)
        {
            case GameBatchScraperState.Configuring:
                _targets.Add(new FocusTarget
                {
                    Kind = GamepadBatchScraperTargetKind.RefreshToggle,
                    SetFocused = value => IsRefreshFocused = value,
                    Activate = () => Batch.RefreshOwnedValues = !Batch.RefreshOwnedValues,
                });
                _targets.Add(new FocusTarget
                {
                    Kind = GamepadBatchScraperTargetKind.Start,
                    SetFocused = value => IsStartFocused = value,
                    Activate = () => Execute(Batch.StartCommand),
                });
                // Land on Start so the common case (accept defaults, scrape) is one A press away, with
                // Up walking back to the replace-values toggle.
                SetFocus(_targets.Count - 1);
                break;

            case GameBatchScraperState.Running:
                _targets.Add(new FocusTarget
                {
                    Kind = GamepadBatchScraperTargetKind.Cancel,
                    SetFocused = value => IsCancelFocused = value,
                    Activate = () => Execute(Batch.CancelCommand),
                });
                SetFocus(0);
                break;

            case GameBatchScraperState.Done:
                _targets.Add(new FocusTarget
                {
                    Kind = GamepadBatchScraperTargetKind.Close,
                    SetFocused = value => IsCloseFocused = value,
                    Activate = () => Execute(Batch.CloseCommand),
                });
                SetFocus(0);
                break;
        }
    }

    private void SetFocus(int index)
    {
        if (FocusIndex >= 0 && FocusIndex < _targets.Count)
            _targets[FocusIndex].SetFocused(false);

        FocusIndex = _targets.Count == 0 ? -1 : Math.Clamp(index, 0, _targets.Count - 1);

        if (FocusIndex >= 0)
            _targets[FocusIndex].SetFocused(true);

        OnPropertyChanged(nameof(FocusIndex));
        OnPropertyChanged(nameof(FocusedKind));
    }

    private static void Execute(System.Windows.Input.ICommand command, object? parameter = null)
    {
        if (command.CanExecute(parameter))
            command.Execute(parameter);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Batch.PropertyChanged -= OnBatchPropertyChanged;
    }
}
