using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.ViewModels;

public enum GameBatchScraperState
{
    Configuring,
    Running,
    Done,
}

/// <summary>
/// Configures and runs a hash/serial-only batch scrape over a set of games, reporting cancellable
/// progress and a per-outcome summary. It never title-searches; unmatched games are simply reported.
/// </summary>
public sealed partial class GameBatchScraperViewModel : ViewModelBase
{
    private readonly IReadOnlyList<long> _gameIds;
    private readonly IScreenScraperBatchService _batch;
    private readonly ScreenScraperSettings _settings;
    private readonly IAppLogger _logger;
    // Games whose previous scrape already pulled everything the provider offers, so a fill-missing run
    // would skip them. Pre-counted once at construction (one query) so the UI can show real remaining
    // work up front instead of the raw selection count, and dropped from the run so the progress bar
    // measures only the games actually being scraped.
    private readonly IReadOnlySet<long> _alreadyScraped;
    // How many games this run dropped up front as already up to date, folded into the final summary.
    private int _preSkippedUpToDate;
    // The finished run's stop reason, driving the Done-state title. Null means an unexpected failure.
    private GameScrapeBatchStopReason? _doneStopReason;
    private CancellationTokenSource? _run;
    // Serialises OnProgress against the run's finalisation. Progress<T> without a captured
    // SynchronizationContext (e.g. under test) delivers callbacks on the thread pool, so a progress
    // report queued mid-run can land after the summary is written; the lock makes the "is the run still
    // Running?" check and the status write atomic, so a late report can never clobber the final summary.
    private readonly object _statusGate = new();

    public string SystemName { get; }

    public int GameCount => _gameIds.Count;

    public string Heading => GameCount == 1
        ? $"Scrape 1 {SystemName} game"
        : $"Scrape {GameCount} {SystemName} games";

    public bool AppliedChanges { get; private set; }

    public event Action? CloseRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfiguring))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsDone))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial GameBatchScraperState State { get; set; } = GameBatchScraperState.Configuring;

    [ObservableProperty]
    public partial bool IncludeMetadata { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludeBoxArt { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludeScreenshot { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludeWheel { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludeFanart { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludeTitleScreen { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludeBoxBack { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludeBoxSpine { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludePhysicalMedia { get; set; } = true;

    [ObservableProperty]
    public partial bool IncludePhysicalMediaTexture { get; set; } = true;

    /// <summary>Replace values ScreenScraper already owns, instead of only filling blanks.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingCount))]
    public partial bool RefreshOwnedValues { get; set; }

    [ObservableProperty]
    public partial int ProgressCompleted { get; set; }

    [ObservableProperty]
    public partial int ProgressTotal { get; set; }

    [ObservableProperty]
    public partial string? CurrentGameTitle { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    public bool IsConfiguring => State == GameBatchScraperState.Configuring;
    public bool IsRunning => State == GameBatchScraperState.Running;
    public bool IsDone => State == GameBatchScraperState.Done;

    /// <summary>Selected games a fill-missing run would skip because they are already up to date.</summary>
    public int AlreadyScrapedCount => _alreadyScraped.Count;

    /// <summary>How many games the next run will actually scrape: the whole selection when refreshing
    /// owned values, otherwise the selection minus the games already up to date.</summary>
    public int PendingCount => RefreshOwnedValues ? GameCount : GameCount - _alreadyScraped.Count;

    /// <summary>Done-state heading, so a cancelled or halted run doesn't read as "Batch complete".</summary>
    public string DoneTitle => _doneStopReason switch
    {
        GameScrapeBatchStopReason.Completed => "Batch complete",
        GameScrapeBatchStopReason.Cancelled => "Batch cancelled",
        _ => "Batch stopped",
    };

    public GameBatchScraperViewModel(
        IReadOnlyList<long> gameIds,
        string systemName,
        IScreenScraperBatchService batch,
        ScreenScraperSettings settings,
        IAppLogger? logger = null)
    {
        _gameIds = gameIds;
        SystemName = systemName;
        _batch = batch;
        _settings = settings;
        _logger = logger ?? NullAppLogger.Instance;
        ProgressTotal = gameIds.Count;
        _alreadyScraped = PreCountAlreadyScraped(batch, gameIds);
        UpdateConfiguringStatus();
    }

    // A quick local read (one query) of how many selected games are already up to date. A failure here
    // is non-fatal: fall back to "nothing skipped" so the batch still runs, just without the head-start
    // count. Never let it stop the scraper from opening.
    private IReadOnlySet<long> PreCountAlreadyScraped(IScreenScraperBatchService batch, IReadOnlyList<long> gameIds)
    {
        try
        {
            return batch.GetAlreadyScrapedGameIds(gameIds);
        }
        catch (Exception ex)
        {
            _logger.Warning("Could not pre-count already-scraped games for the batch.", ex);
            return new HashSet<long>();
        }
    }

    // Configuring-state guidance. Leads with the real remaining work so a re-run over a mostly-scraped
    // library doesn't look like it will re-do everything.
    private void UpdateConfiguringStatus()
    {
        if (RefreshOwnedValues)
        {
            StatusMessage = "Values ScreenScraper already set will be replaced, not just blanks filled. "
                + "Unmatched games are skipped — nothing is guessed.";
            return;
        }

        if (_alreadyScraped.Count == 0)
        {
            StatusMessage = "Only empty values are filled in unless you choose to replace them. "
                + "Unmatched games are skipped — nothing is guessed.";
        }
        else if (PendingCount == 0)
        {
            StatusMessage = GameCount == 1
                ? "This game is already up to date. Turn on “Replace values” to scrape it again."
                : $"All {GameCount} are already up to date. Turn on “Replace values” to scrape them again.";
        }
        else
        {
            StatusMessage = $"{_alreadyScraped.Count} already up to date — {PendingCount} to scrape. "
                + "Only empty values are filled; unmatched games are skipped.";
        }
    }

    partial void OnRefreshOwnedValuesChanged(bool value)
    {
        if (IsConfiguring)
            UpdateConfiguringStatus();
    }

    private bool CanStart() => State == GameBatchScraperState.Configuring && _gameIds.Count > 0;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        _run = new CancellationTokenSource();
        State = GameBatchScraperState.Running;
        ProgressCompleted = 0;
        CurrentGameTitle = null;
        StatusMessage = "Starting…";

        var mode = RefreshOwnedValues
            ? GameMetadataApplyMode.RefreshProviderOwned
            : GameMetadataApplyMode.FillMissing;

        // Drop the already-up-to-date games before the run (fill-missing only) so the progress total and
        // bar measure only the games actually being scraped, not the whole selection. They are tallied
        // separately in the summary. A refresh run re-queries everything, so nothing is dropped.
        var idsToRun = mode == GameMetadataApplyMode.FillMissing && _alreadyScraped.Count > 0
            ? _gameIds.Where(id => !_alreadyScraped.Contains(id)).ToList()
            : _gameIds;
        _preSkippedUpToDate = _gameIds.Count - idsToRun.Count;
        ProgressTotal = idsToRun.Count;

        var includeFields = IncludeMetadata ? null : new HashSet<GameMetadataField>();
        var includeMedia = SelectedMediaKinds();
        var progress = new Progress<GameScrapeBatchProgress>(OnProgress);

        GameScrapeBatchSummary summary;
        try
        {
            summary = await Task.Run(
                () => _batch.RunAsync(idsToRun, _settings, mode, includeFields, includeMedia, progress, _run.Token),
                _run.Token);
        }
        catch (OperationCanceledException)
        {
            summary = new GameScrapeBatchSummary(idsToRun.Count, GameScrapeBatchStopReason.Cancelled, []);
        }
        catch (Exception ex)
        {
            _logger.Error("The ScreenScraper batch failed.", ex);
            lock (_statusGate)
            {
                _doneStopReason = null;
                State = GameBatchScraperState.Done;
                StatusMessage = "The batch could not be completed.";
                OnPropertyChanged(nameof(DoneTitle));
            }
            return;
        }

        AppliedChanges = summary.Applied > 0;
        // Finalise under the gate so any in-flight progress callback either ran already or, seeing the run
        // is no longer Running, drops itself — the summary is authoritative and can't be clobbered back to
        // "Scraping… N of N".
        lock (_statusGate)
        {
            _doneStopReason = summary.StopReason;
            State = GameBatchScraperState.Done;
            ProgressCompleted = ProgressTotal;
            CurrentGameTitle = null;
            StatusMessage = Summarize(summary);
            OnPropertyChanged(nameof(DoneTitle));
        }
    }

    private IReadOnlySet<GameMediaKind> SelectedMediaKinds()
    {
        var kinds = new HashSet<GameMediaKind>();
        if (IncludeBoxArt)
            kinds.Add(GameMediaKind.BoxFront);
        if (IncludeScreenshot)
            kinds.Add(GameMediaKind.Screenshot);
        if (IncludeWheel)
            kinds.Add(GameMediaKind.Wheel);
        if (IncludeFanart)
            kinds.Add(GameMediaKind.Fanart);
        if (IncludeTitleScreen)
            kinds.Add(GameMediaKind.TitleScreen);
        if (IncludeBoxBack)
            kinds.Add(GameMediaKind.BoxBack);
        if (IncludeBoxSpine)
            kinds.Add(GameMediaKind.BoxSpine);
        if (IncludePhysicalMedia)
            kinds.Add(GameMediaKind.PhysicalMedia);
        if (IncludePhysicalMediaTexture)
            kinds.Add(GameMediaKind.PhysicalMediaTexture);
        return kinds;
    }

    private void OnProgress(GameScrapeBatchProgress progress)
    {
        // A progress report queued before completion can be delivered after the run finishes; once the
        // batch is Done its summary is authoritative, so drop the stale update. The gate makes this check
        // and the writes atomic against finalisation, so the drop decision can never race the summary.
        lock (_statusGate)
        {
            if (State != GameBatchScraperState.Running)
                return;

            ProgressCompleted = progress.Completed;
            ProgressTotal = progress.Total;
            CurrentGameTitle = progress.CurrentGameTitle;
            StatusMessage = $"Scraping… {progress.Completed} of {progress.Total}";
        }
    }

    private string Summarize(GameScrapeBatchSummary summary)
    {
        var prefix = summary.StopReason switch
        {
            GameScrapeBatchStopReason.Cancelled => "Cancelled. ",
            GameScrapeBatchStopReason.QuotaExhausted => "Stopped — ScreenScraper quota reached. ",
            GameScrapeBatchStopReason.RateLimited => "Stopped — ScreenScraper is rate-limiting. ",
            GameScrapeBatchStopReason.NotConnected => "Stopped — account not connected. ",
            GameScrapeBatchStopReason.ProviderDisabled => "Stopped — ScreenScraper is disabled. ",
            _ => string.Empty,
        };
        // "Already up to date" folds two do-nothing cases: games dropped before the run because a prior
        // scrape already pulled everything the provider offers, plus any the run matched but found nothing
        // new to add. Only name buckets that actually have games, so a run doesn't lead with an alarming
        // "0 scraped" when everything was already up to date (or failed).
        var upToDate = _preSkippedUpToDate + summary.AlreadyComplete;
        var parts = new List<string>();
        if (summary.Applied > 0)
            parts.Add($"{summary.Applied} scraped");
        if (upToDate > 0)
            parts.Add($"{upToDate} already up to date");
        if (summary.NoMatch > 0)
            parts.Add($"{summary.NoMatch} no match");
        if (summary.Unsupported > 0)
            parts.Add($"{summary.Unsupported} unsupported");
        if (summary.Failed > 0)
            parts.Add($"{summary.Failed} failed");
        // Only appears on an early stop (cancel/quota/rate limit); a completed run reaches every game.
        if (summary.NotProcessed > 0)
            parts.Add($"{summary.NotProcessed} not scraped");
        if (parts.Count == 0)
            parts.Add("nothing to scrape");
        return prefix + string.Join(", ", parts) + ".";
    }

    [RelayCommand]
    private void Cancel()
    {
        if (State == GameBatchScraperState.Running)
            _run?.Cancel();
        else
            CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();
}
