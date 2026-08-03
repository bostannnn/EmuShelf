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
    private CancellationTokenSource? _run;

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

    /// <summary>Replace values ScreenScraper already owns, instead of only filling blanks.</summary>
    [ObservableProperty]
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
        StatusMessage = "Only empty values are filled in unless you choose to replace them. "
            + "Unmatched games are skipped — nothing is guessed.";
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
        var includeFields = IncludeMetadata ? null : new HashSet<GameMetadataField>();
        var includeMedia = SelectedMediaKinds();
        var progress = new Progress<GameScrapeBatchProgress>(OnProgress);

        GameScrapeBatchSummary summary;
        try
        {
            summary = await Task.Run(
                () => _batch.RunAsync(_gameIds, _settings, mode, includeFields, includeMedia, progress, _run.Token),
                _run.Token);
        }
        catch (OperationCanceledException)
        {
            summary = new GameScrapeBatchSummary(_gameIds.Count, GameScrapeBatchStopReason.Cancelled, []);
        }
        catch (Exception ex)
        {
            _logger.Error("The ScreenScraper batch failed.", ex);
            State = GameBatchScraperState.Done;
            StatusMessage = "The batch could not be completed.";
            return;
        }

        AppliedChanges = summary.Applied > 0;
        // Mark Done before writing the summary so a late progress callback (Progress<T> delivers
        // asynchronously when there is no captured SynchronizationContext) is ignored by OnProgress
        // rather than clobbering the final message back to "Scraping… N of N".
        State = GameBatchScraperState.Done;
        ProgressCompleted = ProgressTotal;
        CurrentGameTitle = null;
        StatusMessage = Summarize(summary);
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
        return kinds;
    }

    private void OnProgress(GameScrapeBatchProgress progress)
    {
        // A progress report queued before completion can be delivered after the run finishes; once
        // the batch is Done its summary is authoritative, so drop the stale update.
        if (State != GameBatchScraperState.Running)
            return;

        ProgressCompleted = progress.Completed;
        ProgressTotal = progress.Total;
        CurrentGameTitle = progress.CurrentGameTitle;
        StatusMessage = $"Scraping… {progress.Completed} of {progress.Total}";
    }

    private static string Summarize(GameScrapeBatchSummary summary)
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
        var parts = new List<string> { $"{summary.Applied} scraped" };
        if (summary.NoMatch > 0)
            parts.Add($"{summary.NoMatch} no match");
        if (summary.Unsupported > 0)
            parts.Add($"{summary.Unsupported} unsupported");
        if (summary.Failed > 0)
            parts.Add($"{summary.Failed} failed");
        if (summary.NotProcessed > 0)
            parts.Add($"{summary.NotProcessed} not reached");
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
