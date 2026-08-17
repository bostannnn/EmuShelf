using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EmuShelf.App.ViewModels;

/// <summary>Which controller-focusable element in the cover-search overlay holds the ring.</summary>
public enum GamepadCoverSearchTargetKind
{
    None,
    SearchField,
    Search,
    Candidate,
    ChooseLocal,
}

/// <summary>
/// Controller-native presentation and D-pad focus around the shared <see cref="CoverSearchViewModel"/>.
/// It owns no search or download logic: it wraps the existing view model, reuses its results and
/// commands, and adds only the linear focus model (Up/Down moves; A activates) the Gamepad overlay
/// needs. Choosing a local file is not controller-safe (it needs the OS file picker), so that target
/// hands off to Desktop through the supplied callback.
/// </summary>
public sealed partial class GamepadCoverSearchViewModel : ObservableObject, IDisposable
{
    private sealed class FocusTarget
    {
        public required GamepadCoverSearchTargetKind Kind { get; init; }
        public object? Item { get; init; }
        public required Action<bool> SetFocused { get; init; }
        public Action? Activate { get; init; }
    }

    private readonly List<FocusTarget> _targets = [];
    private readonly Action _chooseLocalOnDesktop;
    private bool _disposed;

    public CoverSearchViewModel Search { get; }

    public int FocusIndex { get; private set; } = -1;

    public GamepadCoverSearchTargetKind FocusedKind =>
        FocusIndex >= 0 && FocusIndex < _targets.Count ? _targets[FocusIndex].Kind : GamepadCoverSearchTargetKind.None;

    /// <summary>The result tile under the focus ring, for the view and tests; null for scalar targets.</summary>
    public object? FocusedItem =>
        FocusIndex >= 0 && FocusIndex < _targets.Count ? _targets[FocusIndex].Item : null;

    // Scalar focus flags for the controls that are not result tiles.
    [ObservableProperty] public partial bool IsSearchFieldFocused { get; set; }
    [ObservableProperty] public partial bool IsSearchFocused { get; set; }
    [ObservableProperty] public partial bool IsChooseLocalFocused { get; set; }

    public GamepadCoverSearchViewModel(CoverSearchViewModel search, Action chooseLocalOnDesktop)
    {
        Search = search;
        _chooseLocalOnDesktop = chooseLocalOnDesktop;
        Search.Results.CollectionChanged += OnResultsChanged;
        RebuildTargets();
        SetFocus(0);
    }

    /// <summary>Runs the initial search using the game's title (which the wrapped view model seeded),
    /// so the overlay lands on results instead of an empty prompt.</summary>
    public Task LoadAsync() => Search.SearchCommand.ExecuteAsync(null);

    /// <summary>Hands the local-file pick off to Desktop (the OS file picker is not controller-safe).
    /// Bound by the overlay's "Choose a file" button so pointer and D-pad take the same path.</summary>
    [RelayCommand]
    private void ChooseLocal() => _chooseLocalOnDesktop();

    /// <summary>D-pad Up/Down: move the focus ring one step across the current target list.</summary>
    public void MoveFocus(int delta) => SetFocus(FocusIndex + delta);

    /// <summary>A: activate the focused target — run the search, pick a cover, or hand off to Desktop.</summary>
    public void Activate()
    {
        if (FocusIndex >= 0 && FocusIndex < _targets.Count)
            _targets[FocusIndex].Activate?.Invoke();
    }

    private void OnResultsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Capture what the ring was on BEFORE the rebuild resets it. Previews stream in one at a time,
        // so this fires repeatedly during a single search — the ring must not jump on every insert.
        var previousItem = FocusedItem;
        var previousKind = FocusedKind;
        var hadCandidates = _targets.Exists(target => target.Kind == GamepadCoverSearchTargetKind.Candidate);
        RebuildTargets();

        // If the ring was on a specific cover that still exists, keep it there despite the new insert.
        if (previousItem is CoverSearchResultViewModel focusedResult && Search.Results.Contains(focusedResult))
        {
            SetFocus(_targets.FindIndex(target => ReferenceEquals(target.Item, focusedResult)));
            return;
        }

        // The first cover(s) just appeared: park the ring on the top one so accepting it is one press
        // away. Otherwise keep the same scalar target — but if a candidate ring's covers all vanished
        // (a fresh search that returned nothing), fall back to the query field to refine and try again.
        var target = !hadCandidates && Search.Results.Count > 0
            ? GamepadCoverSearchTargetKind.Candidate
            : previousKind == GamepadCoverSearchTargetKind.Candidate
                ? GamepadCoverSearchTargetKind.SearchField
                : previousKind;
        SetFocus(FirstTargetIndex(target));
    }

    private void RebuildTargets()
    {
        foreach (var target in _targets)
            target.SetFocused(false);
        _targets.Clear();
        FocusIndex = -1;

        _targets.Add(new FocusTarget
        {
            Kind = GamepadCoverSearchTargetKind.SearchField,
            SetFocused = value => IsSearchFieldFocused = value,
        });
        _targets.Add(new FocusTarget
        {
            Kind = GamepadCoverSearchTargetKind.Search,
            SetFocused = value => IsSearchFocused = value,
            Activate = () => Execute(Search.SearchCommand),
        });
        foreach (var result in Search.Results)
        {
            _targets.Add(new FocusTarget
            {
                Kind = GamepadCoverSearchTargetKind.Candidate,
                Item = result,
                SetFocused = value => result.IsFocused = value,
                Activate = () => Execute(result.SelectCommand),
            });
        }
        _targets.Add(new FocusTarget
        {
            Kind = GamepadCoverSearchTargetKind.ChooseLocal,
            SetFocused = value => IsChooseLocalFocused = value,
            Activate = () => Execute(ChooseLocalCommand),
        });
    }

    private int FirstTargetIndex(GamepadCoverSearchTargetKind kind)
    {
        var index = _targets.FindIndex(target => target.Kind == kind);
        return index < 0 ? 0 : index;
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
        OnPropertyChanged(nameof(FocusedItem));
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

        Search.Results.CollectionChanged -= OnResultsChanged;
        Search.Dispose();
    }
}
