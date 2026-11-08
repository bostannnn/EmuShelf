using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Media.Imaging;
using EmuShelf.App.Services;
using EmuShelf.Core.Metadata;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// Scrape with ScreenScraper in Handheld mode (M45 H6c): Gamepad mode's <see cref="GamepadScraperViewModel"/> (its states,
/// its focus targets, its commands) with each target drawn as a row on the Launcher and the focused one's value, current
/// value or preview on the Stage. The username, password and title to search type on the drawn keyboard.
/// </summary>
public sealed class HandheldScraperPanel : HandheldPanelViewModel
{
    private readonly string _gameTitle;
    private GamepadScraperTargetKind _typing;
    private string _typingBefore = string.Empty;
    private bool _revealPassword;

    public HandheldScraperPanel(GamepadScraperViewModel details, string gameTitle)
    {
        Details = details;
        _gameTitle = gameTitle;
        details.PropertyChanged += OnDetailsChanged;
        details.Scraper.PropertyChanged += OnScraperChanged;
        details.Scraper.CloseRequested += OnScraperClosed;
        RebuildRows();
        SyncStatus();
    }

    /// <summary>Gamepad mode's scraper: the state, the targets and every command are its.</summary>
    public GamepadScraperViewModel Details { get; }

    private GameScraperViewModel Scraper => Details.Scraper;

    /// <summary>What an apply wrote ("Applied 3 fields and 1 image."), once it finished; the shell says it as it closes.</summary>
    public string? AppliedSummary { get; private set; }

    public override string Heading => "Scrape with ScreenScraper";

    public override string Subheading => _gameTitle;

    public override void Dispatch(GamepadAction action)
    {
        if (Keyboard is { } keyboard)
        {
            Apply(keyboard.Dispatch(action));
            return;
        }
        switch (action)
        {
            case GamepadAction.NavigateUp:
                Details.MoveFocus(-1);
                break;
            case GamepadAction.NavigateDown:
                Details.MoveFocus(1);
                break;
            case GamepadAction.Confirm:
                Activate();
                break;
            case GamepadAction.Cancel:
                RequestClose();
                return;
        }
        SyncRows();
        RaiseChanged();
    }

    public override void TapRow(HandheldPanelRowViewModel row)
    {
        var index = Rows.IndexOf(row);
        if (index < 0 || IsTyping)
            return;
        Details.FocusOn(index);
        Dispatch(GamepadAction.Confirm);
    }

    public override void PressKey(HandheldKeyViewModel key)
    {
        if (Keyboard is { } keyboard)
            Apply(keyboard.Press(key));
    }

    protected override void CancelTyping() => Apply(HandheldKeyOutcome.Cancel);

    private void Activate()
    {
        switch (Details.FocusedKind)
        {
            case GamepadScraperTargetKind.Username:
                StartTyping(GamepadScraperTargetKind.Username, Scraper.Username);
                return;
            case GamepadScraperTargetKind.Password:
                StartTyping(GamepadScraperTargetKind.Password, Scraper.Password);
                return;
            case GamepadScraperTargetKind.SearchField:
                StartTyping(GamepadScraperTargetKind.SearchField, Scraper.SearchQuery);
                return;
            default:
                Details.Activate();
                return;
        }
    }

    // ---- The drawn keyboard for the three text targets ----

    private void StartTyping(GamepadScraperTargetKind kind, string current)
    {
        _typing = kind;
        _typingBefore = current;
        Keyboard = new HandheldTextKeyboardViewModel(() => TypedText, text => TypedText = text, () => FieldLabel(kind),
            () => _revealPassword)
        {
            IsSecret = kind == GamepadScraperTargetKind.Password,
        };
        RaiseChanged();
    }

    private string TypedText
    {
        get => _typing switch
        {
            GamepadScraperTargetKind.Username => Scraper.Username,
            GamepadScraperTargetKind.Password => Scraper.Password,
            _ => Scraper.SearchQuery,
        };
        set
        {
            switch (_typing)
            {
                case GamepadScraperTargetKind.Username:
                    Scraper.Username = value;
                    break;
                case GamepadScraperTargetKind.Password:
                    Scraper.Password = value;
                    break;
                default:
                    Scraper.SearchQuery = value;
                    break;
            }
        }
    }

    private void Apply(HandheldKeyOutcome outcome)
    {
        switch (outcome)
        {
            case HandheldKeyOutcome.Commit:
                var searching = _typing == GamepadScraperTargetKind.SearchField;
                StopTyping();
                // Done on the title to search runs the search, as Enter does in Gamepad mode's field.
                if (searching && Scraper.SearchCommand.CanExecute(null))
                    Scraper.SearchCommand.Execute(null);
                break;
            case HandheldKeyOutcome.Cancel:
                TypedText = _typingBefore;
                StopTyping();
                break;
            case HandheldKeyOutcome.ToggleReveal:
                _revealPassword = !_revealPassword;
                Keyboard?.OnRevealChanged();
                break;
        }
        RaiseChanged();
    }

    private void StopTyping()
    {
        Keyboard = null;
        _typing = GamepadScraperTargetKind.None;
        SyncRows();
    }

    private static string FieldLabel(GamepadScraperTargetKind kind) => kind switch
    {
        GamepadScraperTargetKind.Username => "Username",
        GamepadScraperTargetKind.Password => "Password",
        _ => "Game title",
    };

    // ---- Rows: one per target, in the target list's order ----

    private void OnDetailsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GamepadScraperViewModel.TargetsVersion):
                RebuildRows();
                RaiseChanged();
                break;
            case nameof(GamepadScraperViewModel.FocusIndex):
                Focus(Details.FocusIndex);
                RaiseChanged();
                break;
        }
    }

    private void OnScraperChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GameScraperViewModel.StatusMessage):
            case nameof(GameScraperViewModel.ConnectStatus):
            case nameof(GameScraperViewModel.IsConnecting):
            case nameof(GameScraperViewModel.IsSearching):
            case nameof(GameScraperViewModel.State):
            case nameof(GameScraperViewModel.QuotaText):
                SyncStatus();
                RaiseChanged();
                break;
            case nameof(GameScraperViewModel.CoverArtPreview):
            case nameof(GameScraperViewModel.RefreshOwnedValues):
            case nameof(GameScraperViewModel.Username):
            case nameof(GameScraperViewModel.Password):
            case nameof(GameScraperViewModel.SearchQuery):
                SyncRows();
                RaiseChanged();
                break;
        }
    }

    private void OnScraperClosed(GameScrapeApplyResult? result)
    {
        // An apply finished: the shell closes the panel and says what was written. (A cancel is B, which closes anyway.)
        if (result is null)
            return;
        AppliedSummary = Scraper.StatusMessage;
        RequestClose();
    }

    private void RebuildRows()
    {
        foreach (var row in Rows)
        {
            if (row.Source is ScraperMediaRowViewModel media)
                media.PropertyChanged -= OnMediaChanged;
        }
        var rows = Details.Targets.Select(target => ToRow(target.Kind, target.Item)).ToList();
        SetRows(rows, Details.FocusIndex);
        SyncRows();
    }

    private HandheldPanelRowViewModel ToRow(GamepadScraperTargetKind kind, object? item)
    {
        switch (item)
        {
            case ScraperFieldRowViewModel field:
                return new HandheldPanelRowViewModel(HandheldPanelRowKind.Toggle, field.Label, field);
            case ScraperMediaRowViewModel media:
                media.PropertyChanged += OnMediaChanged;
                return new HandheldPanelRowViewModel(HandheldPanelRowKind.Toggle,
                    kind == GamepadScraperTargetKind.Cover ? Scraper.CoverArtLabel : media.Label, media)
                {
                    HasImageSlot = true,
                };
            case GamepadScraperCandidateViewModel candidate:
                return new HandheldPanelRowViewModel(HandheldPanelRowKind.Item, candidate.Name, candidate) { Detail = candidate.System };
        }
        return kind switch
        {
            GamepadScraperTargetKind.RefreshToggle =>
                new HandheldPanelRowViewModel(HandheldPanelRowKind.Toggle, "Replace values ScreenScraper already set", kind),
            GamepadScraperTargetKind.Apply => new HandheldPanelRowViewModel(HandheldPanelRowKind.Button, "Apply selected", kind) { IsPrimary = true },
            GamepadScraperTargetKind.Username or GamepadScraperTargetKind.Password or GamepadScraperTargetKind.SearchField =>
                new HandheldPanelRowViewModel(HandheldPanelRowKind.Text, FieldLabel(kind), kind),
            GamepadScraperTargetKind.Connect => new HandheldPanelRowViewModel(HandheldPanelRowKind.Button, "Connect", kind) { IsPrimary = true },
            GamepadScraperTargetKind.Compute =>
                new HandheldPanelRowViewModel(HandheldPanelRowKind.Button, "Compute hash and match", kind) { IsPrimary = true },
            GamepadScraperTargetKind.Search => new HandheldPanelRowViewModel(HandheldPanelRowKind.Button, "Search", kind) { IsPrimary = true },
            _ => new HandheldPanelRowViewModel(HandheldPanelRowKind.Button, kind.ToString(), kind),
        };
    }

    private void OnMediaChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScraperMediaRowViewModel.Preview) or nameof(ScraperMediaRowViewModel.IsSelected))
        {
            SyncRows();
            RaiseChanged();
        }
    }

    /// <summary>Copies each target's current state onto its row (a check flipped, a name typed, a preview loaded).</summary>
    private void SyncRows()
    {
        foreach (var row in Rows)
        {
            switch (row.Source)
            {
                case ScraperFieldRowViewModel field:
                    row.Value = Short(field.ProposedValue, 28);
                    row.IsOn = field.IsSelected;
                    row.IsEnabled = field.CanApply;
                    row.Detail = field.IsUserOwned ? "Yours, kept as it is" : null;
                    break;
                case ScraperMediaRowViewModel media:
                    row.IsOn = media.IsSelected;
                    row.IsEnabled = media.CanApply;
                    row.Image = ReferenceEquals(media, Scraper.CoverArtRow) ? Scraper.CoverArtPreview ?? media.Preview : media.Preview;
                    row.Detail = media.IsUserOwned ? "Kept — your image" : Short(media.ProposedText, 40);
                    break;
                case GamepadScraperTargetKind.RefreshToggle:
                    row.IsOn = Scraper.RefreshOwnedValues;
                    break;
                case GamepadScraperTargetKind.Username:
                    row.Value = string.IsNullOrEmpty(Scraper.Username) ? "Not set" : Scraper.Username;
                    break;
                case GamepadScraperTargetKind.Password:
                    row.Value = string.IsNullOrEmpty(Scraper.Password) ? "Not set" : new string('●', Math.Min(Scraper.Password.Length, 12));
                    break;
                case GamepadScraperTargetKind.SearchField:
                    row.Value = string.IsNullOrEmpty(Scraper.SearchQuery) ? "Type a title" : Short(Scraper.SearchQuery, 28);
                    break;
            }
        }
    }

    private void SyncStatus()
    {
        var connect = Scraper.ShowConnect && !string.IsNullOrEmpty(Scraper.ConnectStatus) ? Scraper.ConnectStatus : null;
        Status = connect ?? Scraper.StatusMessage;
        IsBusy = Scraper.IsBusy || Scraper.IsSearching || Scraper.IsConnecting;
    }

    // ---- The Stage ----

    public override HandheldPanelCard Card
    {
        get
        {
            var eyebrow = $"ScreenScraper · {_gameTitle}";
            if (Keyboard is { } keyboard)
                return new HandheldPanelCard(eyebrow, FieldLabel(_typing),
                    keyboard.IsSecret && !keyboard.IsRevealed ? new string('●', Math.Min(keyboard.Text.Length, 12)) : keyboard.Text,
                    _typing == GamepadScraperTargetKind.SearchField ? "Done searches ScreenScraper for this title." : null,
                    Glyph: HandheldGlyph.Scrape);
            switch (FocusedRow?.Source)
            {
                case ScraperFieldRowViewModel scraped:
                    return new HandheldPanelCard(eyebrow, scraped.Label, scraped.ProposedValue,
                        string.IsNullOrWhiteSpace(scraped.CurrentValue) ? "Not set yet" : $"Now: {scraped.CurrentValue}",
                        !scraped.CanApply ? scraped.IsUserOwned ? "You set this yourself, so it stays as it is." : "Kept as it is."
                            : scraped.IsSelected ? "Will be applied" : "Will be left as it is",
                        Glyph: HandheldGlyph.Scrape);
                case ScraperMediaRowViewModel media:
                    var preview = ReferenceEquals(media, Scraper.CoverArtRow) ? Scraper.CoverArtPreview ?? media.Preview : media.Preview;
                    return new HandheldPanelCard(eyebrow, FocusedRow.Label, media.ProposedText,
                        media.CurrentPath is null ? "None yet" : "Replaces the one you have",
                        !media.CanApply ? "Kept — your image" : media.IsSelected ? "Will be applied" : "Will be left as it is",
                        Image: preview, Glyph: HandheldGlyph.Scrape);
                case GamepadScraperCandidateViewModel candidate:
                    return new HandheldPanelCard(eyebrow, candidate.Name, candidate.System, "A loads this game's data to review.",
                        Glyph: HandheldGlyph.Scrape);
                case GamepadScraperTargetKind.RefreshToggle:
                    return new HandheldPanelCard(eyebrow, "Replace values ScreenScraper already set", Scraper.RefreshOwnedValues ? "On" : "Off",
                        "Off fills only what is missing. On also replaces what ScreenScraper wrote before; your own edits always stay.",
                        Glyph: HandheldGlyph.Scrape);
                case GamepadScraperTargetKind.Apply:
                    var fields = Scraper.Fields.Count(row => row.IsSelected);
                    var images = Scraper.Media.Count(media => media.IsSelected);
                    return new HandheldPanelCard(eyebrow, "Apply selected", $"{Count(fields, "field")} · {Count(images, "image")}",
                        Scraper.StatusMessage, Scraper.QuotaText, Image: Scraper.CoverArtPreview, Glyph: HandheldGlyph.Scrape);
                case GamepadScraperTargetKind.Username or GamepadScraperTargetKind.Password or GamepadScraperTargetKind.Connect:
                    return new HandheldPanelCard(eyebrow, "Connect your ScreenScraper account", Scraper.Username,
                        "Sign in with your screenscraper.fr username and password.", Status, IsWarning: !string.IsNullOrEmpty(Scraper.ConnectStatus),
                        Glyph: HandheldGlyph.Scrape);
                case GamepadScraperTargetKind.Compute:
                    return new HandheldPanelCard(eyebrow, "Compute hash and match", null, Scraper.StatusMessage, Glyph: HandheldGlyph.Scrape);
                case GamepadScraperTargetKind.SearchField or GamepadScraperTargetKind.Search:
                    return new HandheldPanelCard(eyebrow, "Search by title", Scraper.SearchQuery, Scraper.StatusMessage, Glyph: HandheldGlyph.Scrape);
            }
            return new HandheldPanelCard(eyebrow, _gameTitle, null, Scraper.StatusMessage,
                IsWarning: Scraper.State is GameScraperState.Failure or GameScraperState.Unsupported, Glyph: HandheldGlyph.Scrape);
        }
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    public override IEnumerable<HandheldHint> Hints
    {
        get
        {
            if (Keyboard is { } keyboard)
                return keyboard.Hints(_typing == GamepadScraperTargetKind.SearchField ? "Search" : "Done");
            var hints = new List<HandheldHint>();
            switch (FocusedRow?.Kind)
            {
                case HandheldPanelRowKind.Toggle when FocusedRow.IsEnabled:
                    hints.Add(new HandheldHint("A", "Switch"));
                    break;
                case HandheldPanelRowKind.Text:
                    hints.Add(new HandheldHint("A", "Type"));
                    break;
                case HandheldPanelRowKind.Button:
                case HandheldPanelRowKind.Item:
                    hints.Add(new HandheldHint("A", "Select"));
                    break;
            }
            hints.Add(new HandheldHint("B", Scraper.State is GameScraperState.Applied or GameScraperState.Failure ? "Close" : "Back"));
            return hints;
        }
    }

    protected override void DisposeCore()
    {
        foreach (var row in Rows)
        {
            if (row.Source is ScraperMediaRowViewModel media)
                media.PropertyChanged -= OnMediaChanged;
        }
        Details.PropertyChanged -= OnDetailsChanged;
        Details.Scraper.PropertyChanged -= OnScraperChanged;
        Details.Scraper.CloseRequested -= OnScraperClosed;
    }
}

/// <summary>
/// Scraping a whole folder in Handheld mode (M45 H6c): Gamepad mode's <see cref="GamepadBatchScraperViewModel"/>
/// (configure, run, done) with its targets as rows on the Launcher and the run's progress on the Stage.
/// </summary>
public sealed class HandheldBatchScraperPanel : HandheldPanelViewModel
{
    private readonly string _folderName;

    public HandheldBatchScraperPanel(GamepadBatchScraperViewModel details, string folderName)
    {
        Details = details;
        _folderName = folderName;
        details.PropertyChanged += OnDetailsChanged;
        details.Batch.PropertyChanged += OnBatchChanged;
        details.Batch.CloseRequested += OnBatchClosed;
        RebuildRows();
        SyncStatus();
    }

    public GamepadBatchScraperViewModel Details { get; }

    private GameBatchScraperViewModel Batch => Details.Batch;

    /// <summary>What the run did, once it is done ("Scraped 12 of 14 …"); the shell says it as the panel closes.</summary>
    public string? FinishedSummary => Batch.IsDone ? $"{Batch.DoneTitle}. {Batch.StatusMessage}".Trim() : null;

    public override string Heading => "Scrape with ScreenScraper";

    public override string Subheading => $"{_folderName} · {Batch.Heading}";

    public override void Dispatch(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.NavigateUp:
                Details.MoveFocus(-1);
                break;
            case GamepadAction.NavigateDown:
                Details.MoveFocus(1);
                break;
            case GamepadAction.Confirm:
                Details.Activate();
                break;
            case GamepadAction.Cancel:
                // A running batch is left alone (Stop is a row); before it starts or once it is done, B closes.
                if (!Batch.IsRunning)
                {
                    RequestClose();
                    return;
                }
                break;
        }
        SyncRows();
        RaiseChanged();
    }

    public override void TapRow(HandheldPanelRowViewModel row)
    {
        var index = Rows.IndexOf(row);
        if (index < 0)
            return;
        Details.FocusOn(index);
        Dispatch(GamepadAction.Confirm);
    }

    private void OnDetailsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GamepadBatchScraperViewModel.TargetsVersion):
                RebuildRows();
                RaiseChanged();
                break;
            case nameof(GamepadBatchScraperViewModel.FocusIndex):
                Focus(Details.FocusIndex);
                RaiseChanged();
                break;
        }
    }

    private void OnBatchChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GameBatchScraperViewModel.ProgressCompleted):
            case nameof(GameBatchScraperViewModel.ProgressTotal):
            case nameof(GameBatchScraperViewModel.CurrentGameTitle):
            case nameof(GameBatchScraperViewModel.StatusMessage):
            case nameof(GameBatchScraperViewModel.State):
            case nameof(GameBatchScraperViewModel.RefreshOwnedValues):
                SyncStatus();
                SyncRows();
                RaiseChanged();
                break;
        }
    }

    private void OnBatchClosed() => RequestClose();

    private void RebuildRows()
    {
        var rows = Details.Targets.Select(kind => kind switch
        {
            GamepadBatchScraperTargetKind.RefreshToggle =>
                new HandheldPanelRowViewModel(HandheldPanelRowKind.Toggle, "Replace values ScreenScraper already set", kind),
            GamepadBatchScraperTargetKind.Start =>
                new HandheldPanelRowViewModel(HandheldPanelRowKind.Button, "Start scraping", kind) { IsPrimary = true },
            GamepadBatchScraperTargetKind.Cancel => new HandheldPanelRowViewModel(HandheldPanelRowKind.Button, "Stop", kind),
            _ => new HandheldPanelRowViewModel(HandheldPanelRowKind.Button, "Done", kind) { IsPrimary = true },
        }).ToList();
        SetRows(rows, Details.FocusIndex);
        SyncRows();
    }

    private void SyncRows()
    {
        foreach (var row in Rows)
        {
            switch (row.Source)
            {
                case GamepadBatchScraperTargetKind.RefreshToggle:
                    row.IsOn = Batch.RefreshOwnedValues;
                    break;
                case GamepadBatchScraperTargetKind.Start:
                    row.Detail = Batch.PendingCount == 1 ? "1 game to look up" : $"{Batch.PendingCount} games to look up";
                    break;
            }
        }
    }

    private void SyncStatus()
    {
        Status = Batch.StatusMessage;
        IsBusy = Batch.IsRunning;
    }

    public override HandheldPanelCard Card
    {
        get
        {
            var eyebrow = $"ScreenScraper · {_folderName}";
            if (Batch.IsRunning)
            {
                var total = Math.Max(1, Batch.ProgressTotal);
                return new HandheldPanelCard(eyebrow, Batch.CurrentGameTitle ?? "Starting…",
                    $"{Batch.ProgressCompleted} of {Batch.ProgressTotal}", Batch.StatusMessage,
                    Progress: Batch.ProgressCompleted / (double)total, Glyph: HandheldGlyph.Scrape);
            }
            if (Batch.IsDone)
                return new HandheldPanelCard(eyebrow, Batch.DoneTitle, null, Batch.StatusMessage, Progress: 1, Glyph: HandheldGlyph.Scrape);
            var skipped = Batch.AlreadyScrapedCount > 0 && !Batch.RefreshOwnedValues
                ? $"{Batch.AlreadyScrapedCount} already scraped are skipped."
                : null;
            return Details.FocusedKind == GamepadBatchScraperTargetKind.RefreshToggle
                ? new HandheldPanelCard(eyebrow, "Replace values ScreenScraper already set", Batch.RefreshOwnedValues ? "On" : "Off",
                    "Off fills only what is missing. On also replaces what ScreenScraper wrote before; your own edits always stay.",
                    skipped, Glyph: HandheldGlyph.Scrape)
                : new HandheldPanelCard(eyebrow, Batch.Heading,
                    Batch.PendingCount == 1 ? "1 game to look up" : $"{Batch.PendingCount} games to look up",
                    "Matches by hash and serial only. Unmatched games are skipped — nothing is guessed.", skipped,
                    Glyph: HandheldGlyph.Scrape);
        }
    }

    public override IEnumerable<HandheldHint> Hints
    {
        get
        {
            yield return new HandheldHint("A", Details.FocusedKind switch
            {
                GamepadBatchScraperTargetKind.RefreshToggle => "Switch",
                GamepadBatchScraperTargetKind.Start => "Start",
                GamepadBatchScraperTargetKind.Cancel => "Stop",
                _ => "Done",
            });
            if (!Batch.IsRunning)
                yield return new HandheldHint("B", Batch.IsDone ? "Close" : "Back");
        }
    }

    protected override void DisposeCore()
    {
        Details.PropertyChanged -= OnDetailsChanged;
        Details.Batch.PropertyChanged -= OnBatchChanged;
        Details.Batch.CloseRequested -= OnBatchClosed;
    }
}

/// <summary>
/// Set cover in Handheld mode (M45 H6c): Gamepad mode's <see cref="GamepadCoverSearchViewModel"/> web image search, the
/// results as rows with their thumbnails on the Launcher and the focused cover large on the Stage. The title to search
/// types on the drawn keyboard. Choosing a local file is left out: there is no Desktop to hand off to.
/// </summary>
public sealed class HandheldCoverSearchPanel : HandheldPanelViewModel
{
    private readonly string _gameTitle;
    private string _typingBefore = string.Empty;

    public HandheldCoverSearchPanel(GamepadCoverSearchViewModel details, string gameTitle)
    {
        Details = details;
        _gameTitle = gameTitle;
        details.PropertyChanged += OnDetailsChanged;
        details.Search.PropertyChanged += OnSearchChanged;
        details.Search.CloseRequested += OnPicked;
        RebuildRows();
        SyncStatus();
    }

    public GamepadCoverSearchViewModel Details { get; }

    private CoverSearchViewModel Search => Details.Search;

    /// <summary>The cover picked and downloaded; the shell sets it on the game as the panel closes.</summary>
    public PickedGameCover? Picked { get; private set; }

    public override string Heading => "Set cover";

    public override string Subheading => _gameTitle;

    public override void Dispatch(GamepadAction action)
    {
        if (Keyboard is { } keyboard)
        {
            Apply(keyboard.Dispatch(action));
            return;
        }
        switch (action)
        {
            case GamepadAction.NavigateUp:
                Details.MoveFocus(-1);
                break;
            case GamepadAction.NavigateDown:
                Details.MoveFocus(1);
                break;
            case GamepadAction.Confirm when Details.FocusedKind == GamepadCoverSearchTargetKind.SearchField:
                _typingBefore = Search.SearchText;
                Keyboard = new HandheldTextKeyboardViewModel(() => Search.SearchText, text => Search.SearchText = text,
                    () => "Search for");
                break;
            case GamepadAction.Confirm:
                Details.Activate();
                break;
            case GamepadAction.Cancel:
                // Nothing is changed until a cover is picked; B leaves it as it was (a download in flight is dropped).
                RequestClose();
                return;
        }
        RaiseChanged();
    }

    public override void TapRow(HandheldPanelRowViewModel row)
    {
        var index = Rows.IndexOf(row);
        if (index < 0 || IsTyping)
            return;
        Details.FocusOn(index);
        Dispatch(GamepadAction.Confirm);
    }

    public override void PressKey(HandheldKeyViewModel key)
    {
        if (Keyboard is { } keyboard)
            Apply(keyboard.Press(key));
    }

    protected override void CancelTyping() => Apply(HandheldKeyOutcome.Cancel);

    private void Apply(HandheldKeyOutcome outcome)
    {
        switch (outcome)
        {
            case HandheldKeyOutcome.Commit:
                Keyboard = null;
                SyncRows();
                if (Search.SearchCommand.CanExecute(null))
                    Search.SearchCommand.Execute(null);
                break;
            case HandheldKeyOutcome.Cancel:
                Search.SearchText = _typingBefore;
                Keyboard = null;
                SyncRows();
                break;
        }
        RaiseChanged();
    }

    private void OnPicked(PickedGameCover? picked)
    {
        if (picked is null)
            return;
        Picked = picked;
        RequestClose();
    }

    private void OnDetailsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GamepadCoverSearchViewModel.TargetsVersion):
                RebuildRows();
                RaiseChanged();
                break;
            case nameof(GamepadCoverSearchViewModel.FocusIndex):
                Focus(Details.FocusIndex);
                RaiseChanged();
                break;
        }
    }

    private void OnSearchChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CoverSearchViewModel.StatusText):
            case nameof(CoverSearchViewModel.IsSearching):
            case nameof(CoverSearchViewModel.IsSelecting):
            case nameof(CoverSearchViewModel.HasSearched):
                SyncStatus();
                RaiseChanged();
                break;
            case nameof(CoverSearchViewModel.SearchText):
                SyncRows();
                RaiseChanged();
                break;
        }
    }

    private void RebuildRows()
    {
        var rows = Details.Targets.Select(target => target.Item is CoverSearchResultViewModel result
            ? new HandheldPanelRowViewModel(HandheldPanelRowKind.Item, result.ResolutionText, result)
            {
                Detail = result.SourceText,
                Image = result.Preview,
                HasImageSlot = true,
            }
            : target.Kind == GamepadCoverSearchTargetKind.SearchField
                ? new HandheldPanelRowViewModel(HandheldPanelRowKind.Text, "Search for", target.Kind)
                : new HandheldPanelRowViewModel(HandheldPanelRowKind.Button, "Search", target.Kind) { IsPrimary = true }).ToList();
        SetRows(rows, Details.FocusIndex);
        SyncRows();
    }

    private void SyncRows()
    {
        foreach (var row in Rows)
        {
            if (row.Source is GamepadCoverSearchTargetKind.SearchField)
                row.Value = string.IsNullOrEmpty(Search.SearchText) ? "Type a title" : Short(Search.SearchText, 28);
        }
    }

    private void SyncStatus()
    {
        Status = !string.IsNullOrEmpty(Search.StatusText) ? Search.StatusText
            : Search.ShowResults ? Search.ResultsCountText
            : Search.ShowNoResults ? "No covers found. Try a shorter or different title."
            : Search.IsBusy ? "Searching…" : null;
        IsBusy = Search.IsBusy;
    }

    public override HandheldPanelCard Card
    {
        get
        {
            var eyebrow = $"Set cover · {_gameTitle}";
            if (Keyboard is { } keyboard)
                return new HandheldPanelCard(eyebrow, "Search for", keyboard.Text, "Done searches the web for covers.", Glyph: HandheldGlyph.Cover);
            if (FocusedRow?.Source is CoverSearchResultViewModel result)
            {
                var number = Search.Results.IndexOf(result) + 1;
                return new HandheldPanelCard(eyebrow, $"Cover {number} of {Search.Results.Count}", result.ResolutionText,
                    $"From {result.SourceText}", Search.IsSelecting ? "Downloading…" : "A sets it as the cover.", Image: result.Preview,
                    Glyph: HandheldGlyph.Cover);
            }
            return new HandheldPanelCard(eyebrow, "Search the web for a cover", Search.SearchText, Status,
                IsWarning: Search.ShowNoResults, Glyph: HandheldGlyph.Cover);
        }
    }

    public override IEnumerable<HandheldHint> Hints
    {
        get
        {
            if (Keyboard is { } keyboard)
                return keyboard.Hints("Search");
            return
            [
                new HandheldHint("A", Details.FocusedKind switch
                {
                    GamepadCoverSearchTargetKind.SearchField => "Type",
                    GamepadCoverSearchTargetKind.Candidate => "Use this cover",
                    _ => "Search",
                }),
                new HandheldHint("B", "Back"),
            ];
        }
    }

    protected override void DisposeCore()
    {
        Details.PropertyChanged -= OnDetailsChanged;
        Details.Search.PropertyChanged -= OnSearchChanged;
        Details.Search.CloseRequested -= OnPicked;
    }
}

/// <summary>
/// A game's achievements while browsing, in Handheld mode (M45 H6c): Gamepad mode's full-screen
/// <see cref="AchievementDetailsViewModel"/> (RetroAchievements or Steam; All / Locked / Unlocked, sort, refresh, reveal)
/// as a list of badges on the Launcher and the focused one large on the Stage with its description and when it was earned.
/// </summary>
public sealed class HandheldAchievementsPanel : HandheldPanelViewModel
{
    private static readonly AchievementDisplayFilter[] Filters =
        [AchievementDisplayFilter.All, AchievementDisplayFilter.Locked, AchievementDisplayFilter.Unlocked];

    private readonly List<AchievementRowViewModel> _watched = [];

    public HandheldAchievementsPanel(AchievementDetailsViewModel details)
    {
        Details = details;
        details.PropertyChanged += OnDetailsChanged;
        RebuildRows(keep: null);
        SyncStatus();
    }

    public AchievementDetailsViewModel Details { get; }

    public override string Heading => "Achievements";

    public override string Subheading => Details.GameTitle;

    private AchievementRowViewModel? FocusedAchievement => FocusedRow?.Source as AchievementRowViewModel;

    public override void Dispatch(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.NavigateUp when FocusIndex > 0:
                Focus(FocusIndex - 1);
                LoadBadges();
                break;
            case GamepadAction.NavigateDown when FocusIndex < Rows.Count - 1:
                Focus(FocusIndex + 1);
                LoadBadges();
                break;
            case GamepadAction.PreviousPlatform:
                Details.CycleFilterCommand.Execute(-1);
                break;
            case GamepadAction.NextPlatform:
                Details.CycleFilterCommand.Execute(1);
                break;
            case GamepadAction.Search:
                Details.CycleSortCommand.Execute(null);
                break;
            case GamepadAction.Actions:
                if (Details.RefreshCommand.CanExecute(null))
                    Details.RefreshCommand.Execute(null);
                break;
            case GamepadAction.Confirm when FocusedAchievement is { CanReveal: true } hidden:
                hidden.RevealCommand.Execute(null);
                break;
            case GamepadAction.Cancel:
                RequestClose();
                return;
        }
        RaiseChanged();
    }

    public override void TapChip(HandheldPanelChipViewModel chip)
    {
        var index = Chips.IndexOf(chip);
        if (index < 0)
            return;
        Details.SelectedFilter = Filters[index];
        RaiseChanged();
    }

    public override void TapRow(HandheldPanelRowViewModel row)
    {
        var index = Rows.IndexOf(row);
        if (index < 0)
            return;
        // A tap selects; a second tap on a hidden one reveals it, as A does.
        if (index != FocusIndex)
        {
            Focus(index);
            LoadBadges();
            RaiseChanged();
            return;
        }
        Dispatch(GamepadAction.Confirm);
    }

    private void OnDetailsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            // The shown list is replaced whole once a refresh (or a new filter or sort) is in place; the rows follow it, not
            // the full list, which a refresh clears and refills one row at a time before the shown list catches up.
            case nameof(AchievementDetailsViewModel.VisibleAchievements):
                RebuildRows(keep: FocusedAchievement);
                SyncStatus();
                RaiseChanged();
                break;
            case nameof(AchievementDetailsViewModel.StatusText):
            case nameof(AchievementDetailsViewModel.IsRefreshing):
            case nameof(AchievementDetailsViewModel.UnlockedCount):
            case nameof(AchievementDetailsViewModel.TotalCount):
            case nameof(AchievementDetailsViewModel.EarnedPoints):
            case nameof(AchievementDetailsViewModel.HasLoadedSnapshot):
                SyncStatus();
                RaiseChanged();
                break;
        }
    }

    private void RebuildRows(AchievementRowViewModel? keep)
    {
        foreach (var achievement in _watched)
            achievement.PropertyChanged -= OnAchievementChanged;
        _watched.Clear();
        var visible = Details.VisibleAchievements.ToList();
        var rows = new List<HandheldPanelRowViewModel>(visible.Count);
        foreach (var achievement in visible)
        {
            achievement.PropertyChanged += OnAchievementChanged;
            _watched.Add(achievement);
            var row = new HandheldPanelRowViewModel(HandheldPanelRowKind.Item, achievement.Title, achievement) { HasImageSlot = true };
            Sync(row, achievement);
            rows.Add(row);
        }
        // The focus stays on the same achievement (by id: a refresh brings new rows for the same set) where it still is.
        var focus = keep is null ? 0 : Math.Max(0, visible.FindIndex(row => row.AchievementId == keep.AchievementId));
        SetRows(rows, focus);
        SetChips([Details.AllFilterText, Details.LockedFilterText, Details.UnlockedFilterText], Array.IndexOf(Filters, Details.SelectedFilter));
        EmptyText = Details.HasAchievements ? Details.FilterEmptyStateText : Details.EmptyStateTitle;
        OnPropertyChanged(nameof(HasEmptyText));
        LoadBadges();
    }

    private void LoadBadges() =>
        AchievementRowViewModel.LoadBadgeWindow(Details.VisibleAchievements, FocusedAchievement);

    private static void Sync(HandheldPanelRowViewModel row, AchievementRowViewModel achievement)
    {
        row.Label = achievement.Title;
        row.Image = achievement.Badge;
        row.IsDimmed = !achievement.IsUnlocked;
        row.Detail = achievement.IsUnlocked ? achievement.EarnedText : achievement.CanReveal ? "Hidden" : achievement.UnlockStateText;
        row.Value = achievement.HasPoints ? achievement.PointsText : null;
    }

    private void OnAchievementChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not AchievementRowViewModel achievement || Rows.FirstOrDefault(row => ReferenceEquals(row.Source, achievement)) is not { } row)
            return;
        Sync(row, achievement);
        if (ReferenceEquals(row, FocusedRow))
            RaiseChanged();
    }

    private void SyncStatus()
    {
        var progress = Details.SupportsPoints ? $"{Details.ProgressText} · {Details.PointsText}" : Details.ProgressText;
        Status = Details.HasStatus ? Details.StatusText : progress;
        IsBusy = Details.IsRefreshing;
        if (Chips.Count == Filters.Length)
        {
            Chips[0].Label = Details.AllFilterText;
            Chips[1].Label = Details.LockedFilterText;
            Chips[2].Label = Details.UnlockedFilterText;
        }
    }

    public override HandheldPanelCard Card
    {
        get
        {
            var eyebrow = $"Achievements · {Details.GameTitle}";
            if (FocusedAchievement is not { } achievement)
            {
                return new HandheldPanelCard(eyebrow, Details.HasAchievements ? Details.FilterEmptyStateText : Details.EmptyStateTitle,
                    Details.HasAchievements ? Details.ProgressText : null,
                    Details.HasAchievements ? null : Details.EmptyStateDescription, Details.HasStatus ? Details.StatusText : null,
                    Glyph: HandheldGlyph.Trophy);
            }
            var state = achievement.IsUnlocked
                ? achievement.IsHardcore ? $"{achievement.EarnedText} · hardcore" : achievement.EarnedText
                : achievement.UnlockStateText;
            return new HandheldPanelCard(eyebrow, achievement.Title, achievement.HasPoints ? achievement.PointsText : null,
                achievement.Description, state, Image: achievement.Badge, Glyph: HandheldGlyph.Trophy, IsSquareImage: true);
        }
    }

    public override IEnumerable<HandheldHint> Hints
    {
        get
        {
            if (FocusedAchievement is { CanReveal: true })
                yield return new HandheldHint("A", "Reveal");
            yield return new HandheldHint("RB", "Filter");
            yield return new HandheldHint("X", $"Sort: {Details.SortText}");
            yield return new HandheldHint("Y", "Refresh");
            yield return new HandheldHint("B", "Back");
        }
    }

    protected override void DisposeCore()
    {
        foreach (var achievement in _watched)
            achievement.PropertyChanged -= OnAchievementChanged;
        _watched.Clear();
        Details.PropertyChanged -= OnDetailsChanged;
    }
}
