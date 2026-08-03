using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Metadata.ScreenScraper;

namespace EmuShelf.App.ViewModels;

/// <summary>Which controller-focusable element in the scraper overlay currently holds the ring.</summary>
public enum GamepadScraperTargetKind
{
    None,
    Field,
    Media,
    BoxArt,
    RefreshToggle,
    Apply,
    Username,
    Password,
    Connect,
    Compute,
    SearchField,
    Search,
    Candidate,
}

/// <summary>A title-search result rendered as a controller-selectable row (carries its own focus flag).</summary>
public sealed partial class GamepadScraperCandidateViewModel : ObservableObject
{
    public ScreenScraperGameMatch Match { get; }
    public string Name => Match.Name;
    public string? System => Match.System;
    public bool HasSystem => !string.IsNullOrWhiteSpace(Match.System);

    [ObservableProperty]
    public partial bool IsFocused { get; set; }

    public GamepadScraperCandidateViewModel(ScreenScraperGameMatch match) => Match = match;
}

/// <summary>
/// Controller-native presentation and D-pad focus around the shared <see cref="GameScraperViewModel"/>.
/// It owns no scrape logic: it wraps the existing view model, mirrors its state, and adds only the
/// linear focus model (D-pad Up/Down moves; A activates) the Gamepad overlay needs. Every command,
/// field, media row, candidate and state comes straight from the wrapped view model.
/// </summary>
public sealed partial class GamepadScraperViewModel : ObservableObject, IDisposable
{
    private sealed class FocusTarget
    {
        public required GamepadScraperTargetKind Kind { get; init; }
        public object? Item { get; init; }
        public required Action<bool> SetFocused { get; init; }
        public Action? Activate { get; init; }
    }

    private readonly List<FocusTarget> _targets = [];
    private bool _disposed;

    public GameScraperViewModel Scraper { get; }

    /// <summary>Title-search results, mirrored from the wrapped view model with focus flags added.</summary>
    public ObservableCollection<GamepadScraperCandidateViewModel> Candidates { get; } = [];

    /// <summary>Set once an apply wrote at least one field or image, so the host can refresh the library.</summary>
    public bool HasAppliedChanges { get; private set; }

    public int FocusIndex { get; private set; } = -1;

    public GamepadScraperTargetKind FocusedKind =>
        FocusIndex >= 0 && FocusIndex < _targets.Count ? _targets[FocusIndex].Kind : GamepadScraperTargetKind.None;

    /// <summary>The row or candidate under the focus ring, for the view and tests; null for scalar targets.</summary>
    public object? FocusedItem =>
        FocusIndex >= 0 && FocusIndex < _targets.Count ? _targets[FocusIndex].Item : null;

    // Scalar focus flags for controls that are not list items (text fields and command buttons).
    [ObservableProperty] public partial bool IsUsernameFocused { get; set; }
    [ObservableProperty] public partial bool IsPasswordFocused { get; set; }
    [ObservableProperty] public partial bool IsConnectFocused { get; set; }
    [ObservableProperty] public partial bool IsComputeFocused { get; set; }
    [ObservableProperty] public partial bool IsSearchFieldFocused { get; set; }
    [ObservableProperty] public partial bool IsSearchFocused { get; set; }
    [ObservableProperty] public partial bool IsRefreshFocused { get; set; }
    [ObservableProperty] public partial bool IsApplyFocused { get; set; }

    public GamepadScraperViewModel(GameScraperViewModel scraper)
    {
        Scraper = scraper;
        Scraper.PropertyChanged += OnScraperPropertyChanged;
        Scraper.Candidates.CollectionChanged += OnScraperCandidatesChanged;
        Scraper.CloseRequested += OnScraperCloseRequested;
        RebuildTargets();
        SetFocus(DefaultFocusIndex());
    }

    public Task LoadAsync() => Scraper.LoadAsync();

    /// <summary>D-pad Up/Down: move the focus ring one step within the current state's target list.</summary>
    public void MoveFocus(int delta) => SetFocus(FocusIndex + delta);

    /// <summary>A: activate the focused target — toggle a checkbox, or run a command/select a candidate.</summary>
    public void Activate()
    {
        if (FocusIndex >= 0 && FocusIndex < _targets.Count)
            _targets[FocusIndex].Activate?.Invoke();
    }

    private void OnScraperPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GameScraperViewModel.State))
            return;

        RebuildTargets();
        SetFocus(DefaultFocusIndex());
    }

    private void OnScraperCandidatesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncCandidates();
        RebuildTargets();

        // Once a search yields results, park the ring on the top candidate so the common case
        // (pick the right game) is one press away; with none, keep the query field focused.
        SetFocus(Candidates.Count > 0
            ? FirstTargetIndex(GamepadScraperTargetKind.Candidate)
            : DefaultFocusIndex());
    }

    private void OnScraperCloseRequested(GameScrapeApplyResult? result)
    {
        if (result is { } applied && (applied.MetadataApplied > 0 || applied.MediaImported > 0))
            HasAppliedChanges = true;
    }

    private void SyncCandidates()
    {
        foreach (var candidate in Candidates)
            candidate.IsFocused = false;
        Candidates.Clear();
        foreach (var match in Scraper.Candidates)
            Candidates.Add(new GamepadScraperCandidateViewModel(match));
    }

    private void RebuildTargets()
    {
        foreach (var target in _targets)
            target.SetFocused(false);
        _targets.Clear();
        FocusIndex = -1;

        switch (Scraper.State)
        {
            case GameScraperState.Ready or GameScraperState.Applying:
                foreach (var field in Scraper.Fields)
                    _targets.Add(ToggleTarget(GamepadScraperTargetKind.Field, field, field.CanApply,
                        () => field.IsSelected = !field.IsSelected, value => field.IsFocused = value));
                if (Scraper.BoxArtRow is { } boxArt)
                    _targets.Add(ToggleTarget(GamepadScraperTargetKind.BoxArt, boxArt, boxArt.CanApply,
                        () => boxArt.IsSelected = !boxArt.IsSelected, value => boxArt.IsFocused = value));
                foreach (var media in Scraper.OtherMedia)
                    _targets.Add(ToggleTarget(GamepadScraperTargetKind.Media, media, media.CanApply,
                        () => media.IsSelected = !media.IsSelected, value => media.IsFocused = value));
                _targets.Add(new FocusTarget
                {
                    Kind = GamepadScraperTargetKind.RefreshToggle,
                    SetFocused = value => IsRefreshFocused = value,
                    Activate = () => Scraper.RefreshOwnedValues = !Scraper.RefreshOwnedValues,
                });
                _targets.Add(new FocusTarget
                {
                    Kind = GamepadScraperTargetKind.Apply,
                    SetFocused = value => IsApplyFocused = value,
                    Activate = () => Execute(Scraper.ApplyCommand),
                });
                break;

            case GameScraperState.NotConnected or GameScraperState.ProviderDisabled:
                _targets.Add(new FocusTarget
                {
                    Kind = GamepadScraperTargetKind.Username,
                    SetFocused = value => IsUsernameFocused = value,
                });
                _targets.Add(new FocusTarget
                {
                    Kind = GamepadScraperTargetKind.Password,
                    SetFocused = value => IsPasswordFocused = value,
                });
                _targets.Add(new FocusTarget
                {
                    Kind = GamepadScraperTargetKind.Connect,
                    SetFocused = value => IsConnectFocused = value,
                    Activate = () => Execute(Scraper.ConnectCommand),
                });
                break;

            case GameScraperState.ConsentRequired:
                _targets.Add(new FocusTarget
                {
                    Kind = GamepadScraperTargetKind.Compute,
                    SetFocused = value => IsComputeFocused = value,
                    Activate = () => Execute(Scraper.ComputeFingerprintCommand),
                });
                break;

            case GameScraperState.NoMatch:
                _targets.Add(new FocusTarget
                {
                    Kind = GamepadScraperTargetKind.SearchField,
                    SetFocused = value => IsSearchFieldFocused = value,
                });
                _targets.Add(new FocusTarget
                {
                    Kind = GamepadScraperTargetKind.Search,
                    SetFocused = value => IsSearchFocused = value,
                    Activate = () => Execute(Scraper.SearchCommand),
                });
                foreach (var candidate in Candidates)
                    _targets.Add(new FocusTarget
                    {
                        Kind = GamepadScraperTargetKind.Candidate,
                        Item = candidate,
                        SetFocused = value => candidate.IsFocused = value,
                        Activate = () => Execute(Scraper.SelectCandidateCommand, candidate.Match),
                    });
                break;

            // Loading, Unsupported, Failure and Applied are read-only message states: B backs out.
            default:
                break;
        }
    }

    private FocusTarget ToggleTarget(
        GamepadScraperTargetKind kind,
        object row,
        bool canApply,
        Action toggle,
        Action<bool> setFocused) =>
        new()
        {
            Kind = kind,
            Item = row,
            SetFocused = setFocused,
            // Locked rows (user-owned / another provider's) present but can't be toggled.
            Activate = canApply ? toggle : null,
        };

    private int DefaultFocusIndex() => _targets.Count == 0 ? -1 : 0;

    private int FirstTargetIndex(GamepadScraperTargetKind kind)
    {
        var index = _targets.FindIndex(target => target.Kind == kind);
        return index < 0 ? DefaultFocusIndex() : index;
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

        Scraper.PropertyChanged -= OnScraperPropertyChanged;
        Scraper.Candidates.CollectionChanged -= OnScraperCandidatesChanged;
        Scraper.CloseRequested -= OnScraperCloseRequested;
        Scraper.Dispose();
    }
}
