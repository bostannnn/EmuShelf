using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.ViewModels;

public enum GameScraperState
{
    Loading,
    Ready,
    NotConnected,
    ProviderDisabled,
    Unsupported,
    ConsentRequired,
    NoMatch,
    Failure,
    Applying,
    Applied,
}

/// <summary>One scalar/localized metadata field the provider proposes, with its current value.</summary>
public sealed partial class ScraperFieldRowViewModel : ObservableObject
{
    internal GameMetadataValue Value { get; }

    public string Label { get; }
    public string? CurrentValue { get; }
    public string ProposedValue { get; }
    public bool IsUserOwned { get; }
    public bool CanApply { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Gamepad presentation only: true when the controller's focus ring is on this row.</summary>
    [ObservableProperty]
    public partial bool IsFocused { get; set; }

    public ScraperFieldRowViewModel(string label, string? currentValue, bool isUserOwned, GameMetadataValue value)
    {
        Label = label;
        CurrentValue = currentValue;
        Value = value;
        ProposedValue = value.Value;
        IsUserOwned = isUserOwned;
        CanApply = !isUserOwned &&
            !string.Equals(currentValue, value.Value, StringComparison.Ordinal);
        IsSelected = CanApply;
    }
}

/// <summary>One media kind the provider proposes, with the game's current asset for that kind.</summary>
public sealed partial class ScraperMediaRowViewModel : ObservableObject, IDisposable
{
    internal GameMediaImport Import { get; }

    public string Label { get; }
    public GameMediaKind Kind { get; }
    public string? CurrentPath { get; }
    public Uri ProposedUri { get; }
    public string ProposedText { get; }
    public bool IsUserOwned { get; }
    public bool CanApply { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Gamepad presentation only: true when the controller's focus ring is on this row.</summary>
    [ObservableProperty]
    public partial bool IsFocused { get; set; }

    [ObservableProperty]
    public partial Bitmap? Preview { get; set; }

    public ScraperMediaRowViewModel(
        string label,
        GameMediaKind kind,
        GameMediaAsset? current,
        GameMediaImport import)
    {
        Label = label;
        Kind = kind;
        Import = import;
        CurrentPath = current?.LocalPath;
        ProposedUri = import.SourceUri;
        ProposedText = import is { Width: { } width, Height: { } height }
            ? $"New image from {import.SourceUri.Host} · {width}×{height}"
            : $"New image from {import.SourceUri.Host}";
        IsUserOwned = current is not null &&
            (current.Origin == GameMediaOrigin.User ||
             current.SelectionOrigin == GameMediaSelectionOrigin.User);
        CanApply = !IsUserOwned;
        IsSelected = CanApply;
    }

    public void Dispose()
    {
        Preview?.Dispose();
        Preview = null;
    }
}

/// <summary>
/// Shared, surface-agnostic view model for scraping one game with ScreenScraper. It loads a
/// non-mutating preview, lets the user pick which fields/media to apply, and drives the apply
/// service. Desktop and Gamepad render the same model; no provider rules live in the views.
/// </summary>
public sealed partial class GameScraperViewModel : ViewModelBase, IDisposable
{
    private const int PreviewParallelism = 3;

    private readonly long _gameId;
    private readonly IScreenScraperPreviewService _preview;
    private readonly IGameScrapeApplicationService _apply;
    private readonly IScreenScraperAccountService _account;
    private readonly IRemoteArtworkDownloader? _downloader;
    private ScreenScraperSettings _settings;
    private readonly IAppLogger _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private ScreenScraperGamePreview? _current;

    public string GameTitle { get; }

    public ObservableCollection<ScraperFieldRowViewModel> Fields { get; } = [];

    /// <summary>Every proposed media row, used for apply and preview loading (includes box art).</summary>
    public ObservableCollection<ScraperMediaRowViewModel> Media { get; } = [];

    /// <summary>Media shown in the list — everything except box art, which sits in the header safe-zone.</summary>
    public ObservableCollection<ScraperMediaRowViewModel> OtherMedia { get; } = [];

    public bool HasFields => Fields.Count > 0;
    public bool HasOtherMedia => OtherMedia.Count > 0;

    public GameScrapeApplyResult? LastApplyResult { get; private set; }

    public event Action<GameScrapeApplyResult?>? CloseRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoading))]
    [NotifyPropertyChangedFor(nameof(ShowData))]
    [NotifyPropertyChangedFor(nameof(ShowConsent))]
    [NotifyPropertyChangedFor(nameof(ShowConnect))]
    [NotifyPropertyChangedFor(nameof(ShowSearch))]
    [NotifyPropertyChangedFor(nameof(ShowMessage))]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(ComputeFingerprintCommand))]
    public partial GameScraperState State { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? QuotaText { get; set; }

    /// <summary>The matched box art, shown in the review header. Owned by the box-front media row.</summary>
    [ObservableProperty]
    public partial Bitmap? BoxArtPreview { get; set; }

    /// <summary>The box-front row, surfaced beside the cover safe-zone so it carries its own checkbox.</summary>
    [ObservableProperty]
    public partial ScraperMediaRowViewModel? BoxArtRow { get; set; }

    /// <summary>Refresh values ScreenScraper already owns instead of only filling empty ones.</summary>
    [ObservableProperty]
    public partial bool RefreshOwnedValues { get; set; }

    [ObservableProperty]
    public partial string Username { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial bool IsConnecting { get; set; }

    [ObservableProperty]
    public partial string ConnectStatus { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial string SearchQuery { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial bool IsSearching { get; set; }

    public ObservableCollection<ScreenScraperGameMatch> Candidates { get; } = [];

    public bool HasCandidates => Candidates.Count > 0;

    public bool ShowLoading => State == GameScraperState.Loading;
    public bool ShowData => State is GameScraperState.Ready or GameScraperState.Applying;
    public bool ShowConsent => State == GameScraperState.ConsentRequired;
    public bool ShowConnect => State is GameScraperState.NotConnected or GameScraperState.ProviderDisabled;
    public bool ShowSearch => State == GameScraperState.NoMatch;
    public bool ShowMessage => State is GameScraperState.Unsupported
        or GameScraperState.Failure or GameScraperState.Applied;
    public bool IsBusy => State is GameScraperState.Loading or GameScraperState.Applying;

    public GameScraperViewModel(
        long gameId,
        string gameTitle,
        IScreenScraperPreviewService preview,
        IGameScrapeApplicationService apply,
        IScreenScraperAccountService account,
        ScreenScraperSettings settings,
        IRemoteArtworkDownloader? downloader = null,
        IAppLogger? logger = null)
    {
        _gameId = gameId;
        GameTitle = gameTitle;
        _preview = preview;
        _apply = apply;
        _account = account;
        _settings = settings;
        _downloader = downloader;
        _logger = logger ?? NullAppLogger.Instance;
        SearchQuery = CleanSearchQuery(gameTitle);
    }

    // Opening the scraper is itself the consent to read the game's bytes for a hash, so the single
    // game flow fingerprints immediately instead of gating behind a second click.
    public Task LoadAsync() => LoadAsync(allowFingerprinting: true);

    public async Task LoadAsync(bool allowFingerprinting)
    {
        State = GameScraperState.Loading;
        StatusMessage = "Looking up this game on ScreenScraper…";
        ClearRows();

        ScreenScraperPreviewResult result;
        try
        {
            result = await _preview.PreviewAsync(_gameId, _settings, allowFingerprinting, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.Warning($"ScreenScraper preview failed for game {_gameId}.", ex);
            SetMessage(GameScraperState.Failure, "ScreenScraper could not be reached. Try again later.");
            return;
        }

        if (result.IsSuccess)
        {
            BuildFrom(result.Preview!);
            return;
        }

        var failureState = MapFailureState(result);
        SetMessage(failureState, FailureMessage(result));
        // No exact match — kick off a title search so candidates are ready for the user to pick.
        if (failureState == GameScraperState.NoMatch)
            _ = AutoSearchAsync();
    }

    /// <summary>Lets the user re-search even after a match, in case the wrong release was resolved.</summary>
    [RelayCommand]
    private Task SearchAgainAsync()
    {
        State = GameScraperState.NoMatch;
        StatusMessage = "Searching for a better match…";
        return AutoSearchAsync();
    }

    private bool CanSearch() => !IsSearching && !string.IsNullOrWhiteSpace(SearchQuery);

    // Manual search: the user typed the query, so it is used verbatim — a single request.
    [RelayCommand(CanExecute = nameof(CanSearch))]
    private Task SearchAsync() => RunSearchAsync([SearchQuery]);

    // Auto search: try a short, bounded ladder of progressively shorter queries so a subtitle or hack
    // tag can't defeat the search — at most a handful of requests, not one per dropped word.
    private Task AutoSearchAsync() => RunSearchAsync(BuildSearchLadder(SearchQuery));

    private async Task RunSearchAsync(IReadOnlyList<string> queries)
    {
        IsSearching = true;
        Candidates.Clear();
        OnPropertyChanged(nameof(HasCandidates));
        try
        {
            foreach (var query in queries)
            {
                if (string.IsNullOrWhiteSpace(query))
                    continue;

                var result = await _preview.SearchAsync(_gameId, query, _settings, _lifetime.Token);
                if (result.IsSuccess && result.Data is { Count: > 0 } candidates)
                {
                    SearchQuery = query;
                    foreach (var candidate in candidates)
                        Candidates.Add(candidate);
                    StatusMessage = Candidates.Count == 1
                        ? "1 result — is this the game?"
                        : $"{Candidates.Count} results — pick the right game.";
                    return;
                }
            }

            StatusMessage = "No results. Try a shorter or different title.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Warning($"ScreenScraper title search failed for game {_gameId}.", ex);
            StatusMessage = "The search could not be completed. Try again.";
        }
        finally
        {
            IsSearching = false;
            OnPropertyChanged(nameof(HasCandidates));
        }
    }

    // At most four distinct queries: the full title, roughly two-thirds, roughly a third, and one word.
    private static IReadOnlyList<string> BuildSearchLadder(string seed)
    {
        var words = seed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1)
            return [seed];

        var ladder = new List<string>();
        foreach (var take in new[] { words.Length, words.Length * 2 / 3, words.Length / 3, 1 })
        {
            var query = string.Join(' ', words[..Math.Clamp(take, 1, words.Length)]);
            if (!ladder.Contains(query))
                ladder.Add(query);
        }
        return ladder;
    }

    // A game filename carries region/version tags and hack-author credits that defeat the search;
    // strip them so the seeded query is close to the real title.
    private static string CleanSearchQuery(string title)
    {
        var cleaned = Regex.Replace(title, @"[\(\[\{].*?[\)\]\}]", " ");
        var byIndex = cleaned.IndexOf(" by ", StringComparison.OrdinalIgnoreCase);
        if (byIndex > 0)
            cleaned = cleaned[..byIndex];
        cleaned = Regex.Replace(cleaned, @"\bv?\d+(\.\d+)+\b", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim(' ', '-', '_');
        return cleaned.Length == 0 ? title.Trim() : cleaned;
    }

    [RelayCommand]
    private async Task SelectCandidateAsync(ScreenScraperGameMatch? candidate)
    {
        if (candidate is null)
            return;

        State = GameScraperState.Loading;
        StatusMessage = $"Loading {candidate.Name}…";
        try
        {
            var result = await _preview.PreviewByProviderGameIdAsync(
                _gameId, candidate.ProviderGameId, _settings, _lifetime.Token);
            if (result.IsSuccess)
                BuildFrom(result.Preview!);
            else
                SetMessage(MapFailureState(result), FailureMessage(result));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Warning($"Loading a ScreenScraper title-search result failed for game {_gameId}.", ex);
            SetMessage(GameScraperState.Failure, "That game could not be loaded. Pick another result.");
        }
    }

    private void BuildFrom(ScreenScraperGamePreview preview)
    {
        _current = preview;
        QuotaText = preview.Quota is { RequestsToday: { } used, MaxRequestsPerDay: { } max }
            ? $"ScreenScraper requests today: {used} / {max}"
            : null;

        foreach (var value in preview.Metadata)
        {
            var current = preview.ExistingDetails.Metadata.FirstOrDefault(existing =>
                existing.Field == value.Field &&
                LocaleEquals(existing.Locale, value.Locale));
            var row = new ScraperFieldRowViewModel(
                FieldLabel(value.Field, value.Locale),
                current?.Value,
                current?.Origin == GameMetadataValueOrigin.User,
                value);
            row.PropertyChanged += OnRowChanged;
            Fields.Add(row);
        }

        foreach (var (kind, candidate) in preview.Media.OrderBy(entry => entry.Key))
        {
            var current = preview.ExistingDetails.Media
                .FirstOrDefault(asset => asset.Kind == kind && asset.IsSelected);
            var row = new ScraperMediaRowViewModel(MediaLabel(kind), kind, current, ToImport(kind, candidate));
            row.PropertyChanged += OnRowChanged;
            Media.Add(row);
            if (kind == GameMediaKind.BoxFront)
                BoxArtRow = row;
            else
                OtherMedia.Add(row);
        }

        OnPropertyChanged(nameof(HasOtherMedia));

        if (Fields.Count == 0 && Media.Count == 0)
        {
            SetMessage(GameScraperState.NoMatch, "ScreenScraper matched this game but returned nothing new to apply.");
            return;
        }

        OnPropertyChanged(nameof(HasFields));
        State = GameScraperState.Ready;
        StatusMessage = "Review the changes, then apply.";
        _ = LoadMediaPreviewsAsync(_lifetime.Token);
    }

    private async Task LoadMediaPreviewsAsync(CancellationToken cancellationToken)
    {
        if (_downloader is null || Media.Count == 0)
            return;

        using var gate = new SemaphoreSlim(PreviewParallelism, PreviewParallelism);
        await Task.WhenAll(Media.Select(row => LoadPreviewAsync(row, gate, cancellationToken)));
    }

    private async Task LoadPreviewAsync(
        ScraperMediaRowViewModel row,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        DownloadedArtwork? downloaded = null;
        var entered = false;
        try
        {
            await gate.WaitAsync(cancellationToken);
            entered = true;
            downloaded = await _downloader!.DownloadFirstAsync(
                [new ArtworkCandidate(ScreenScraperProvider.Id, row.Import.SourceUri, row.Import.FileExtension)],
                cancellationToken);
            if (downloaded is null)
                return;

            var bitmap = await Task.Run(
                () => SafeImageDecoder.DecodeToFit(downloaded.TemporaryPath, 260, 340),
                cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                bitmap.Dispose();
            }
            else
            {
                row.Preview = bitmap;
                if (row.Kind == GameMediaKind.BoxFront)
                    BoxArtPreview = bitmap;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not load a ScreenScraper media preview from {row.ProposedUri}.", ex);
        }
        finally
        {
            if (entered)
                gate.Release();
            if (downloaded is not null)
                TryDeletePreviewFile(downloaded.TemporaryPath);
        }
    }

    private void TryDeletePreviewFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"Could not remove a temporary ScreenScraper preview at {path}.", ex);
        }
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ScraperFieldRowViewModel.IsSelected))
            ApplyCommand.NotifyCanExecuteChanged();
    }

    private bool CanApply() =>
        State == GameScraperState.Ready &&
        (Fields.Any(field => field.IsSelected) || Media.Any(media => media.IsSelected));

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (_current is null)
            return;

        var metadata = Fields.Where(field => field.IsSelected).Select(field => field.Value).ToList();
        var media = Media.Where(item => item.IsSelected).Select(item => item.Import).ToList();
        var mode = RefreshOwnedValues
            ? GameMetadataApplyMode.RefreshProviderOwned
            : GameMetadataApplyMode.FillMissing;

        State = GameScraperState.Applying;
        StatusMessage = "Applying…";
        try
        {
            var result = await _apply.ApplyAsync(
                new GameScrapeApplyRequest(_gameId, _current.Match, metadata, media, mode),
                _lifetime.Token);
            LastApplyResult = result;
            SetMessage(GameScraperState.Applied, SummarizeResult(result));
            CloseRequested?.Invoke(result);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Warning($"Applying ScreenScraper data failed for game {_gameId}.", ex);
            SetMessage(GameScraperState.Failure, "The changes could not be applied. Nothing was left half-written.");
        }
    }

    private bool CanConnect() => !IsConnecting;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password))
        {
            ConnectStatus = "Enter your ScreenScraper username and password.";
            return;
        }

        IsConnecting = true;
        ConnectStatus = "Connecting…";
        try
        {
            var summary = await _account.ConnectAsync(Username, Password, _lifetime.Token);
            if (summary.Result == ScreenScraperConnectionResult.Connected)
            {
                Password = string.Empty;
                ConnectStatus = string.Empty;
                // The account service just enabled the provider; reflect that for the reload.
                _settings = _settings with { Enabled = true };
                await LoadAsync();
                return;
            }

            ConnectStatus = ConnectFailureMessage(summary.Result);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Warning("Connecting a ScreenScraper account from the scraper failed.", ex);
            ConnectStatus = "Could not connect. Try again.";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private static string ConnectFailureMessage(ScreenScraperConnectionResult result) => result switch
    {
        ScreenScraperConnectionResult.AuthenticationFailed => "ScreenScraper rejected that username or password.",
        ScreenScraperConnectionResult.Offline => "Couldn't reach ScreenScraper. Check your connection.",
        ScreenScraperConnectionResult.RateLimited => "ScreenScraper is busy right now. Try again shortly.",
        ScreenScraperConnectionResult.QuotaExceeded => "Your ScreenScraper quota is used up. Try again later.",
        ScreenScraperConnectionResult.ProviderUnavailable =>
            "ScreenScraper isn't configured in this build (missing developer credentials).",
        ScreenScraperConnectionResult.LocalStorageFailed =>
            "Signed in, but the credentials couldn't be saved on this machine.",
        _ => "ScreenScraper is unavailable right now.",
    };

    private bool CanComputeFingerprint() => State == GameScraperState.ConsentRequired;

    [RelayCommand(CanExecute = nameof(CanComputeFingerprint))]
    private Task ComputeFingerprintAsync() => LoadAsync(allowFingerprinting: true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);

    private void SetMessage(GameScraperState state, string message)
    {
        State = state;
        StatusMessage = message;
    }

    private static string SummarizeResult(GameScrapeApplyResult result)
    {
        var parts = new List<string>();
        if (result.MetadataApplied > 0)
            parts.Add($"{result.MetadataApplied} field{(result.MetadataApplied == 1 ? "" : "s")}");
        if (result.MediaImported > 0)
            parts.Add($"{result.MediaImported} image{(result.MediaImported == 1 ? "" : "s")}");
        return parts.Count == 0
            ? "No changes were applied."
            : $"Applied {string.Join(" and ", parts)}.";
    }

    private static GameScraperState MapFailureState(ScreenScraperPreviewResult result) => result.Status switch
    {
        ScreenScraperPreviewStatus.NotConnected => GameScraperState.NotConnected,
        ScreenScraperPreviewStatus.ProviderDisabled => GameScraperState.ProviderDisabled,
        ScreenScraperPreviewStatus.UnsupportedSystem or
            ScreenScraperPreviewStatus.UnsupportedFormat => GameScraperState.Unsupported,
        ScreenScraperPreviewStatus.FingerprintConsentRequired => GameScraperState.ConsentRequired,
        ScreenScraperPreviewStatus.ProviderFailure when
            result.RequestStatus == ScreenScraperRequestStatus.NotFound => GameScraperState.NoMatch,
        _ => GameScraperState.Failure,
    };

    private static string FailureMessage(ScreenScraperPreviewResult result) => result.Status switch
    {
        ScreenScraperPreviewStatus.NotConnected => "Connect a ScreenScraper account in Settings first.",
        ScreenScraperPreviewStatus.ProviderDisabled => "ScreenScraper is turned off in Settings.",
        ScreenScraperPreviewStatus.UnsupportedSystem => "This platform is not mapped to ScreenScraper.",
        ScreenScraperPreviewStatus.UnsupportedFormat => "This file format can't be fingerprinted for a match.",
        ScreenScraperPreviewStatus.FingerprintConsentRequired =>
            "Matching needs to read this game's bytes to compute a hash. Compute it now?",
        ScreenScraperPreviewStatus.SourceMissing => "The game file is missing, so it can't be matched.",
        ScreenScraperPreviewStatus.SourceChanged => "The game file changed since it was last read.",
        ScreenScraperPreviewStatus.ProviderFailure => result.RequestStatus switch
        {
            ScreenScraperRequestStatus.NotFound => "ScreenScraper has no match for this game.",
            ScreenScraperRequestStatus.DailyQuotaExceeded => "Your ScreenScraper daily quota is used up. Try tomorrow.",
            ScreenScraperRequestStatus.RateLimited => "ScreenScraper is rate-limiting requests. Try again shortly.",
            ScreenScraperRequestStatus.AuthenticationFailed => "ScreenScraper rejected the account. Reconnect in Settings.",
            _ => result.Error ?? "ScreenScraper is unavailable right now.",
        },
        _ => result.Error ?? "ScreenScraper could not match this game.",
    };

    private static GameMediaImport ToImport(GameMediaKind kind, ScreenScraperMediaCandidate candidate) =>
        new(
            kind,
            candidate.SourceUri,
            candidate.FileExtension,
            ScreenScraperProvider.Id,
            candidate.ProviderMediaId,
            candidate.Region,
            candidate.Language,
            candidate.Width,
            candidate.Height,
            candidate.Crc32,
            candidate.Md5,
            candidate.Sha1);

    private static bool LocaleEquals(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string FieldLabel(GameMetadataField field, string? locale)
    {
        var name = field switch
        {
            GameMetadataField.Title => "Title",
            GameMetadataField.Developer => "Developer",
            GameMetadataField.Publisher => "Publisher",
            GameMetadataField.Genre => "Genre",
            GameMetadataField.Description => "Description",
            GameMetadataField.ReleaseDate => "Release date",
            GameMetadataField.Players => "Players",
            GameMetadataField.Rating => "Rating",
            _ => field.ToString(),
        };
        return string.IsNullOrWhiteSpace(locale) ? name : $"{name} ({locale.Trim().ToUpperInvariant()})";
    }

    private static string MediaLabel(GameMediaKind kind) => kind switch
    {
        GameMediaKind.BoxFront => "Box art",
        GameMediaKind.Screenshot => "Screenshot",
        GameMediaKind.Wheel => "Logo",
        GameMediaKind.Fanart => "Fan art",
        _ => kind.ToString(),
    };

    private void ClearRows()
    {
        // Drop the shared references before the owning rows dispose their bitmaps.
        BoxArtPreview = null;
        BoxArtRow = null;
        foreach (var field in Fields)
            field.PropertyChanged -= OnRowChanged;
        foreach (var item in Media)
        {
            item.PropertyChanged -= OnRowChanged;
            item.Dispose();
        }
        Fields.Clear();
        Media.Clear();
        OtherMedia.Clear();
        OnPropertyChanged(nameof(HasOtherMedia));
        _current = null;
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
