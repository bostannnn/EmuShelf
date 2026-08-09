using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Metadata;

namespace EmuShelf.App.ViewModels;

public sealed partial class CoverSearchResultViewModel : ObservableObject, IDisposable
{
    internal int SearchIndex { get; }
    public ArtworkSearchResult Result { get; }
    public Bitmap Preview { get; }
    public string ResolutionText => $"{Result.Width} × {Result.Height}";
    public string SourceText => Result.SourcePageUri?.Host ?? Result.ImageUri.Host;
    public IAsyncRelayCommand SelectCommand { get; }

    public CoverSearchResultViewModel(
        int searchIndex,
        ArtworkSearchResult result,
        Bitmap preview,
        Func<ArtworkSearchResult, Task> select)
    {
        SearchIndex = searchIndex;
        Result = result;
        Preview = preview;
        SelectCommand = new AsyncRelayCommand(() => select(Result));
    }

    public void Dispose() => Preview.Dispose();
}

public partial class CoverSearchViewModel : ViewModelBase, IDisposable
{
    private const int PreviewParallelism = 6;
    private readonly GameCoverPickerContext _context;
    private readonly IGameArtworkSearchProvider _searchProvider;
    private readonly IRemoteArtworkDownloader _downloader;
    private readonly Func<Task<string?>> _pickLocalImage;
    private readonly IAppLogger _logger;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _searchCancellation;

    public ObservableCollection<CoverSearchResultViewModel> Results { get; } = [];
    public string WindowTitle => $"Choose cover for {_context.GameTitle}";
    public string Description =>
        $"Search web images for {_context.SystemName}, or choose an image already on this device.";
    public string ResultsCountText => Results.Count == 1 ? "1 cover found" : $"{Results.Count} covers found";
    public bool ShowPrompt => !IsBusy && !HasSearched;
    public bool ShowNoResults => !IsSearching && HasSearched && Results.Count == 0;
    public bool ShowResults => Results.Count > 0;
    public bool ShowLoading => IsBusy && Results.Count == 0;
    public bool IsBusy => IsSearching || IsSelecting;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChooseLocalImageCommand))]
    public partial bool IsSearching { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChooseLocalImageCommand))]
    public partial bool IsSelecting { get; set; }

    [ObservableProperty]
    public partial bool HasSearched { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    public event Action<PickedGameCover?>? CloseRequested;

    public CoverSearchViewModel(
        GameCoverPickerContext context,
        IGameArtworkSearchProvider searchProvider,
        IRemoteArtworkDownloader downloader,
        Func<Task<string?>> pickLocalImage,
        IAppLogger? logger = null)
    {
        _context = context;
        _searchProvider = searchProvider;
        _downloader = downloader;
        _pickLocalImage = pickLocalImage;
        _logger = logger ?? NullAppLogger.Instance;
        SearchText = context.GameTitle;
    }

    private bool CanSearch() => !IsBusy && !string.IsNullOrWhiteSpace(SearchText);

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token);
        var cancellationToken = _searchCancellation.Token;

        ClearResults();
        IsSearching = true;
        HasSearched = false;
        StatusText = "Searching for covers…";
        NotifyStateChanged();
        try
        {
            var matches = await _searchProvider.SearchAsync(
                SearchText,
                _context.SystemName,
                _context.PreferredAspectRatio,
                cancellationToken);
            HasSearched = true;
            StatusText = matches.Count == 0
                ? "No usable cover images were returned. Try a shorter title."
                : "Loading cover previews…";
            NotifyStateChanged();

            using var gate = new SemaphoreSlim(PreviewParallelism, PreviewParallelism);
            var previewTasks = matches.Select((match, index) =>
                LoadPreviewAsync(index, match, gate, cancellationToken)).ToList();
            try
            {
                while (previewTasks.Count > 0)
                {
                    var completedTask = await Task.WhenAny(previewTasks);
                    previewTasks.Remove(completedTask);
                    var preview = await completedTask;
                    cancellationToken.ThrowIfCancellationRequested();
                    if (preview is null)
                        continue;

                    var insertIndex = 0;
                    while (insertIndex < Results.Count &&
                           Results[insertIndex].SearchIndex < preview.SearchIndex)
                    {
                        insertIndex++;
                    }
                    Results.Insert(insertIndex, preview);
                    StatusText = $"{ResultsCountText} · still loading…";
                    NotifyStateChanged();
                }
            }
            finally
            {
                await Task.WhenAll(previewTasks);
            }

            StatusText = Results.Count == 0
                ? "No usable cover images were returned. Try a shorter title."
                : ResultsCountText;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            HasSearched = true;
            StatusText = "Web cover search is unavailable right now. You can still choose a local image.";
            _logger.Warning("The user-driven web cover search failed.", ex);
        }
        finally
        {
            IsSearching = false;
            NotifyStateChanged();
        }
    }

    private async Task<CoverSearchResultViewModel?> LoadPreviewAsync(
        int searchIndex,
        ArtworkSearchResult result,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        var enteredGate = false;
        DownloadedArtwork? downloaded = null;
        try
        {
            await gate.WaitAsync(cancellationToken);
            enteredGate = true;
            downloaded = await _downloader.DownloadFirstAsync(
                [result.ThumbnailCandidate],
                cancellationToken);
            if (downloaded is null)
                return null;

            var bitmap = await Task.Run(
                () => SafeImageDecoder.DecodeToFit(downloaded.TemporaryPath, 300, 400),
                cancellationToken);
            return new CoverSearchResultViewModel(
                searchIndex,
                result,
                bitmap,
                SelectResultAsync);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not load a web-cover preview from {result.ThumbnailUri}.", ex);
            return null;
        }
        finally
        {
            if (enteredGate)
                gate.Release();
            if (downloaded is not null)
                DeletePreviewFile(downloaded.TemporaryPath);
        }
    }

    private void DeletePreviewFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"Could not remove a temporary web-cover preview at {path}.", ex);
        }
    }

    private async Task SelectResultAsync(ArtworkSearchResult result)
    {
        if (IsSelecting)
            return;

        _searchCancellation?.Cancel();
        IsSelecting = true;
        StatusText = "Downloading the selected cover…";
        NotifyStateChanged();
        try
        {
            // Prefer the full-resolution original, but fall back to the same proxied preview the
            // user is looking at. Source hosts routinely 404, hotlink-block, or hand back a
            // non-image page for the original even when the search engine's cached thumbnail still
            // loads, so "no longer available" must mean neither address yielded an image — not
            // merely that the pristine original moved.
            var downloaded = await _downloader.DownloadFirstAsync(
                [result.OriginalCandidate, result.ThumbnailCandidate],
                _lifetimeCancellation.Token);
            if (downloaded is null)
            {
                StatusText = "That image is no longer available. Choose another result.";
                return;
            }

            CloseRequested?.Invoke(new PickedGameCover(
                downloaded.TemporaryPath,
                IsTemporary: true,
                SourceUri: result.ImageUri.ToString()));
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            StatusText = "That cover could not be downloaded. Choose another result or a local image.";
            _logger.Warning($"Could not download the selected web cover from {result.ImageUri}.", ex);
        }
        finally
        {
            IsSelecting = false;
            NotifyStateChanged();
        }
    }

    private bool CanChooseLocalImage() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanChooseLocalImage))]
    private async Task ChooseLocalImageAsync()
    {
        var path = await _pickLocalImage();
        if (path is not null)
            CloseRequested?.Invoke(new PickedGameCover(path));
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);

    private void ClearResults()
    {
        foreach (var result in Results)
            result.Dispose();
        Results.Clear();
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(ShowPrompt));
        OnPropertyChanged(nameof(ShowNoResults));
        OnPropertyChanged(nameof(ShowResults));
        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(ResultsCountText));
    }

    public void Dispose()
    {
        _lifetimeCancellation.Cancel();
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _lifetimeCancellation.Dispose();
        ClearResults();
    }
}
