using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.App.Services;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// Search in Handheld mode (M45 H6c, the mockup's "keyboard on the bottom, results fanning on the top"): the drawn keyboard
/// on the Launcher, and on the Stage how many games match with the first covers fanned out, following every key. Done
/// opens the matches as a folder (A there opens a game's Title page); a search with no matches says so and stays open.
/// Titles match the way Gamepad mode's search matches them (the library's title or the name shown).
/// </summary>
public sealed partial class HandheldSearchPanel : HandheldPanelViewModel
{
    /// <summary>How many covers fan out on the Stage.</summary>
    public const int FanSize = 5;

    /// <summary>The covers decode at about the size the fan draws them.</summary>
    public const int FanDecodeWidth = 300;

    private readonly IHandheldLibrary _library;
    private readonly TimeProvider _clock;
    private string _query;
    private CancellationTokenSource? _search;
    private IReadOnlyList<long> _results = [];
    private IReadOnlyList<string> _titles = [];
    private List<Bitmap> _fan = [];
    private string? _searchedQuery;

    public HandheldSearchPanel(IHandheldLibrary library, string query, TimeProvider? clock = null)
    {
        _library = library;
        _clock = clock ?? TimeProvider.System;
        _query = query;
        Keyboard = new HandheldTextKeyboardViewModel(() => _query, text =>
        {
            _query = text;
            Schedule(Debounce);
        }, () => "Search")
        {
            // Big Type (M47 B9): Search types by the mock's rule.
            AutoCapital = HandheldTokens.IsBigType,
        };
        if (query.Trim().Length > 0)
            Schedule(TimeSpan.Zero);
    }

    // ---- Big Type's results above the keys (M47 B9, the mock's kbSearch, focusRes and kbKey) ----

    /// <summary>The first matches as home's tiles above the keys (at most <see cref="HandheldTokens.ResultsMax"/>); Big Type only.</summary>
    public ObservableCollection<HandheldSearchHitViewModel> Hits { get; } = [];

    /// <summary>Counts each new set of hits (the view deals them in again only when the set changed).</summary>
    public int HitsVersion { get; private set; }

    /// <summary>Whether the pad is on the hits (up from the top row of keys) rather than the keys.</summary>
    public bool IsOnHits { get; private set; }

    public int HitIndex { get; private set; }

    public HandheldSearchHitViewModel? FocusedHit => IsOnHits && HitIndex < Hits.Count ? Hits[HitIndex] : null;

    /// <summary>A on a hit (or a second tap): the shell closes Search and plays the game, as A on a game does (M47 B8).</summary>
    public event Action<HandheldGameEntry>? PlayRequested;

    /// <summary>The Big Type Stage's line while typing (the mock's kbMeta): what to do, how many games match, or that none do.</summary>
    public string BigTypeLine
    {
        get
        {
            var query = _query.Trim();
            if (query.Length == 0)
                return "Type part of a name";
            if (_searchedQuery is null)
                return _shownLine ?? string.Empty;
            return _shownLine = _results.Count == 0 ? "Nothing found" : HandheldText.GameCount(_results.Count);
        }
    }

    // The line holds what the last search found until the next one lands (no "searching" flicker on every key).
    private string? _shownLine;

    // A hit's middle across, for the nearest key below it (and the nearest hit above a key).
    private static double HitCentre(int index) => HandheldTokens.ResultsLeft + (index * HandheldTokens.ResultsPitch) + (HandheldTokens.TileHome / 2);

    private void FocusHit(int index)
    {
        IsOnHits = true;
        HitIndex = index;
        for (var i = 0; i < Hits.Count; i++)
            Hits[i].IsFocused = i == index;
        if (Keyboard is { } keyboard)
            keyboard.Blur();
    }

    private void LeaveHits()
    {
        if (Keyboard is not { } keyboard)
            return;
        var centre = HitCentre(HitIndex);
        IsOnHits = false;
        foreach (var hit in Hits)
            hit.IsFocused = false;
        keyboard.FocusAt(0, HandheldBigTypeKeys.Nearest(keyboard.KeyRows[0].Keys, centre));
    }

    /// <summary>A tap on a hit: the first focuses it, a second plays it (the mock's tapRes).</summary>
    public void TapHit(HandheldSearchHitViewModel hit)
    {
        var index = Hits.IndexOf(hit);
        if (index < 0)
            return;
        if (IsOnHits && HitIndex == index)
        {
            PlayRequested?.Invoke(hit.Entry);
            return;
        }
        FocusHit(index);
        RaiseChanged();
    }

    // The pad on the hits: along them, back down (or B) to the nearest key, A plays, Done still opens every match as a folder.
    private void DispatchHits(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.NavigateLeft when HitIndex > 0:
                FocusHit(HitIndex - 1);
                break;
            case GamepadAction.NavigateRight when HitIndex < Hits.Count - 1:
                FocusHit(HitIndex + 1);
                break;
            case GamepadAction.NavigateDown:
            case GamepadAction.Cancel:
                LeaveHits();
                break;
            case GamepadAction.Confirm when FocusedHit is { } hit:
                PlayRequested?.Invoke(hit.Entry);
                return;
            case GamepadAction.Search:
            case GamepadAction.Menu:
                Pending = CommitAsync();
                return;
            default:
                Bump();
                return;
        }
        RaiseChanged();
    }

    /// <summary>How long typing pauses before the library is searched (zero in tests).</summary>
    public TimeSpan Debounce { get; init; } = TimeSpan.FromMilliseconds(250);

    public string Query => _query;

    /// <summary>The matching games' ids, A–Z, for the query searched last.</summary>
    public IReadOnlyList<long> Results => _results;

    /// <summary>The search in flight (tests wait on it).</summary>
    public Task Pending { get; private set; } = Task.CompletedTask;

    /// <summary>Done with matches: the shell closes the panel and opens them as a folder.</summary>
    public event Action<string, IReadOnlyList<long>>? ResultsRequested;

    public override string Heading => "Search";

    public override void Dispatch(GamepadAction action)
    {
        if (HandheldTokens.IsBigType && Keyboard is { } typing)
        {
            if (IsOnHits)
            {
                DispatchHits(action);
                return;
            }
            // Up from the top row of keys: the nearest hit, if there are any (the mock's kbKey).
            if (action == GamepadAction.NavigateUp && typing.KeyRow == 0)
            {
                if (Hits.Count == 0)
                {
                    Bump();
                    return;
                }
                var centre = typing.FocusedKey.CentreX;
                var nearest = 0;
                for (var i = 1; i < Hits.Count; i++)
                {
                    if (Math.Abs(HitCentre(i) - centre) < Math.Abs(HitCentre(nearest) - centre))
                        nearest = i;
                }
                FocusHit(nearest);
                RaiseChanged();
                return;
            }
        }
        if (Keyboard is { } keyboard)
            Apply(keyboard.Dispatch(action));
    }

    public override void PressKey(HandheldKeyViewModel key)
    {
        if (Keyboard is not { } keyboard)
            return;
        if (IsOnHits)
        {
            IsOnHits = false;
            foreach (var hit in Hits)
                hit.IsFocused = false;
        }
        Apply(keyboard.Press(key));
    }

    private void Apply(HandheldKeyOutcome outcome)
    {
        switch (outcome)
        {
            case HandheldKeyOutcome.Commit when HandheldTokens.IsBigType && (_query.Trim().Length == 0 || _results.Count == 0):
                // The mock's closeKb(true): Done with nothing found leaves Search; with matches, the app's folder of every match.
                if (_query.Trim().Length == 0 || string.Equals(_searchedQuery, _query.Trim(), StringComparison.Ordinal))
                {
                    RequestClose();
                    return;
                }
                Pending = CommitAsync();
                return;
            case HandheldKeyOutcome.Commit:
                Pending = CommitAsync();
                return;
            case HandheldKeyOutcome.Cancel:
                RequestClose();
                return;
        }
        RaiseChanged();
    }

    private async Task CommitAsync()
    {
        var query = _query.Trim();
        if (query.Length == 0)
        {
            Status = "Type part of a game's name first.";
            RaiseChanged();
            return;
        }
        // Done never waits on the pause: whatever was typed is searched now.
        if (!string.Equals(_searchedQuery, query, StringComparison.Ordinal))
            await SearchNowAsync(query);
        if (IsDisposed || !string.Equals(_searchedQuery, _query.Trim(), StringComparison.Ordinal))
            return;
        if (_results.Count == 0)
        {
            if (HandheldTokens.IsBigType)
            {
                RequestClose();
                return;
            }
            Status = $"No games match “{query}”.";
            RaiseChanged();
            return;
        }
        ResultsRequested?.Invoke(query, _results);
    }

    private void Schedule(TimeSpan delay)
    {
        _search?.Cancel();
        _search?.Dispose();
        var cancellation = new CancellationTokenSource();
        _search = cancellation;
        Status = null;
        Pending = RunAsync(delay, cancellation.Token);
    }

    private async Task RunAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, _clock, token);
            await SearchCoreAsync(_query.Trim(), token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private Task SearchNowAsync(string query)
    {
        _search?.Cancel();
        _search?.Dispose();
        var cancellation = new CancellationTokenSource();
        _search = cancellation;
        return SearchCoreSafeAsync(query, cancellation.Token);
    }

    private async Task SearchCoreSafeAsync(string query, CancellationToken token)
    {
        try
        {
            await SearchCoreAsync(query, token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SearchCoreAsync(string query, CancellationToken token)
    {
        if (query.Length == 0)
        {
            IsBusy = false;
            ReplaceFan([], [], [], null);
            ReplaceHits([], []);
            return;
        }
        IsBusy = true;
        try
        {
            var ids = await _library.SearchAsync(query, token);
            token.ThrowIfCancellationRequested();
            // The first few covers fan out on the Stage; their entries go as soon as the covers are decoded.
            var entries = await _library.GetGamesByIdAsync(ids.Take(FanSize).ToList(), token);
            var covers = new List<Bitmap>();
            // Big Type keeps the entries and each one's cover (or none) for its hits: the Stage shows the focused one, and A plays it.
            var keep = HandheldTokens.IsBigType;
            var each = new List<Bitmap?>();
            var kept = false;
            try
            {
                foreach (var entry in entries)
                {
                    var cover = await _library.LoadCoverAsync(entry, FanDecodeWidth, fullSize: false, token);
                    each.Add(cover);
                    if (cover is not null)
                        covers.Add(cover);
                }
                kept = keep && !token.IsCancellationRequested && !IsDisposed;
            }
            catch (OperationCanceledException)
            {
                foreach (var cover in covers)
                    cover.Dispose();
                throw;
            }
            finally
            {
                if (!kept)
                    _library.ReleaseGames(entries);
            }
            if (token.IsCancellationRequested || IsDisposed)
            {
                foreach (var cover in covers)
                    cover.Dispose();
                token.ThrowIfCancellationRequested();
                return;
            }
            // The hits take the new covers before the fan lets the old ones go.
            if (kept)
                ReplaceHits(entries, each);
            ReplaceFan(ids, entries.Select(entry => entry.Title).ToList(), covers, query);
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsBusy = false;
        }
    }

    private void ReplaceFan(IReadOnlyList<long> ids, IReadOnlyList<string> titles, List<Bitmap> covers, string? query)
    {
        // The card stops showing the old covers before they are disposed.
        var previous = _fan;
        _fan = covers;
        _results = ids;
        _titles = titles;
        _searchedQuery = query;
        RaiseChanged();
        foreach (var cover in previous)
            cover.Dispose();
    }

    public override HandheldPanelCard Card
    {
        get
        {
            var query = _query.Trim();
            if (query.Length == 0)
                return new HandheldPanelCard("Search", "Search your library", null, "Type part of a game's name.", Status,
                    Glyph: HandheldGlyph.Search);
            var searched = string.Equals(_searchedQuery, query, StringComparison.Ordinal);
            var count = !searched ? "Searching…"
                : _results.Count == 0 ? "No games match"
                : HandheldText.GameCount(_results.Count);
            // The first three by name, then how many more; the hints already say Done (X) shows them all.
            var description = searched && _titles.Count > 0
                ? string.Join(", ", _titles.Take(3)) + (_results.Count > 3 ? $" and {_results.Count - 3} more" : string.Empty)
                : null;
            return new HandheldPanelCard("Search", $"“{query}”", count, description, Status,
                Fan: searched ? _fan : null, IsWarning: searched && _results.Count == 0, Glyph: HandheldGlyph.Search);
        }
    }

    public override IEnumerable<HandheldHint> Hints => IsOnHits
        ? [new HandheldHint("A", "Play"), new HandheldHint("B", "Back")]
        : Keyboard?.Hints("Show") ?? [];

    /// <summary>
    /// The hits for a new search (the mock's kbSearch): rebuilt only when the games are not the ones shown, so typing on into the same
    /// matches deals nothing in again. Their covers are the fan's (disposed with it); their entries are kept until replaced.
    /// </summary>
    private void ReplaceHits(IReadOnlyList<HandheldGameEntry> entries, IReadOnlyList<Bitmap?> covers)
    {
        var shown = entries.Take(HandheldTokens.ResultsMax).ToList();
        if (shown.Select(entry => entry.Id).SequenceEqual(Hits.Select(hit => hit.Entry.Id)))
        {
            // The same games: the old hits stay; their covers are the old fan's, so they take the new fan's pictures in place.
            for (var i = 0; i < Hits.Count; i++)
                Hits[i].Cover = covers[i];
            _library.ReleaseGames(entries);
            return;
        }
        var focused = IsOnHits;
        _library.ReleaseGames(Hits.Select(hit => hit.Entry).ToList());
        Hits.Clear();
        for (var i = 0; i < shown.Count; i++)
            Hits.Add(new HandheldSearchHitViewModel(shown[i], covers[i], i));
        if (entries.Count > shown.Count)
            _library.ReleaseGames(entries.Skip(shown.Count).ToList());
        HitsVersion++;
        if (focused)
        {
            // The hits the pad was on have gone: it goes back to the keys.
            IsOnHits = false;
            if (Hits.Count > 0)
                FocusHit(Math.Min(HitIndex, Hits.Count - 1));
            else if (Keyboard is { } keyboard)
                keyboard.FocusAt(0, 0);
        }
        TakeArrivalFocus();
    }

    protected override void DisposeCore()
    {
        _search?.Cancel();
        _search?.Dispose();
        _search = null;
        ResultsRequested = null;
        PlayRequested = null;
        if (Hits.Count > 0)
            _library.ReleaseGames(Hits.Select(hit => hit.Entry).ToList());
        Hits.Clear();
        foreach (var cover in _fan)
            cover.Dispose();
        _fan = [];
    }
}

/// <summary>
/// Edit title in Handheld mode (M45 H6c): the game's name on the drawn keyboard, with the game on the Stage under the name
/// as typed. Done renames it in EmuShelf's library (Gamepad mode's rename); the game's file keeps its own name.
/// </summary>
public sealed class HandheldTitleEditPanel : HandheldPanelViewModel
{
    private readonly HandheldGameEntry _game;
    private string _text;

    public HandheldTitleEditPanel(HandheldGameEntry game, string title)
    {
        _game = game;
        _text = title;
        Keyboard = new HandheldTextKeyboardViewModel(() => _text, text => _text = text, () => "Title");
    }

    public HandheldGameEntry Game => _game;

    public string Text => _text;

    /// <summary>The game's cover for the Stage. Owned by the panel (the shell decodes it), disposed with it.</summary>
    public Bitmap? Cover
    {
        get => _cover;
        set
        {
            if (ReferenceEquals(_cover, value))
                return;
            var previous = _cover;
            _cover = value;
            RaiseChanged();
            previous?.Dispose();
        }
    }

    private Bitmap? _cover;

    /// <summary>Done with a name: the shell closes the panel and renames the game.</summary>
    public event Action<string>? Committed;

    public override string Heading => "Edit title";

    public override string Subheading => _game.SystemName;

    public override void Dispatch(GamepadAction action)
    {
        if (Keyboard is { } keyboard)
            Apply(keyboard.Dispatch(action));
    }

    public override void PressKey(HandheldKeyViewModel key)
    {
        if (Keyboard is { } keyboard)
            Apply(keyboard.Press(key));
    }

    private void Apply(HandheldKeyOutcome outcome)
    {
        switch (outcome)
        {
            case HandheldKeyOutcome.Commit:
                var title = _text.Trim();
                if (title.Length == 0)
                {
                    Status = "A game title cannot be empty.";
                    break;
                }
                Committed?.Invoke(title);
                return;
            case HandheldKeyOutcome.Cancel:
                RequestClose();
                return;
            case HandheldKeyOutcome.Changed:
                Status = null;
                break;
        }
        RaiseChanged();
    }

    public override HandheldPanelCard Card =>
        new($"Edit title · {_game.SystemName}", _text.Trim().Length == 0 ? "No title yet" : _text, null,
            "The name EmuShelf shows for this game. Its file keeps its own name.", Status, Image: _cover,
            IsWarning: Status is not null, Glyph: HandheldGlyph.Edit);

    public override IEnumerable<HandheldHint> Hints => Keyboard?.Hints() ?? [];

    protected override void DisposeCore()
    {
        Committed = null;
        var cover = _cover;
        _cover = null;
        cover?.Dispose();
    }
}

/// <summary>One of Search's hits above the Big Type keys (M47 B9): the game, its cover (or none), and whether the pad is on it.</summary>
public sealed partial class HandheldSearchHitViewModel(HandheldGameEntry entry, Bitmap? cover, int index) : ObservableObject
{
    public HandheldGameEntry Entry { get; } = entry;

    public int Index { get; } = index;

    public string Title => Entry.Title;

    /// <summary>Its cover, owned by the search's fan (disposed with it); null shows the title on the tile.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    public partial Bitmap? Cover { get; set; } = cover;

    public bool HasCover => Cover is not null;

    public double X => HandheldTokens.ResultsLeft + (Index * HandheldTokens.ResultsPitch);

    /// <summary>Its place as a margin from the Launcher's top left.</summary>
    public Avalonia.Thickness Place => new(X, HandheldTokens.ResultsTop, 0, 0);

    [ObservableProperty]
    public partial bool IsFocused { get; set; }
}
