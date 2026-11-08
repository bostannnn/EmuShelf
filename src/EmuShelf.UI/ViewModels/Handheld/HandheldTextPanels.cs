using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using EmuShelf.App.Services;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// Search in Handheld mode (M45 H6c, the mockup's "keyboard on the bottom, results fanning on the top"): the drawn keyboard
/// on the Launcher, and on the Stage how many games match with the first covers fanned out, following every key. Done
/// opens the matches as a folder (A there opens a game's Title page); a search with no matches says so and stays open.
/// Titles match the way Gamepad mode's search matches them (the library's title or the name shown).
/// </summary>
public sealed class HandheldSearchPanel : HandheldPanelViewModel
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
        }, () => "Search");
        if (query.Trim().Length > 0)
            Schedule(TimeSpan.Zero);
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
            try
            {
                foreach (var entry in entries)
                {
                    if (await _library.LoadCoverAsync(entry, FanDecodeWidth, fullSize: false, token) is { } cover)
                        covers.Add(cover);
                }
            }
            catch (OperationCanceledException)
            {
                foreach (var cover in covers)
                    cover.Dispose();
                throw;
            }
            finally
            {
                _library.ReleaseGames(entries);
            }
            if (token.IsCancellationRequested || IsDisposed)
            {
                foreach (var cover in covers)
                    cover.Dispose();
                token.ThrowIfCancellationRequested();
                return;
            }
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

    public override IEnumerable<HandheldHint> Hints => Keyboard?.Hints("Show") ?? [];

    protected override void DisposeCore()
    {
        _search?.Cancel();
        _search?.Dispose();
        _search = null;
        ResultsRequested = null;
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
