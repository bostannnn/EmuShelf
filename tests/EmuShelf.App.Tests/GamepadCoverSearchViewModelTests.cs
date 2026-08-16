using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Metadata;

namespace EmuShelf.App.Tests;

public sealed class GamepadCoverSearchViewModelTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "EmuShelfGamepadCoverSearchTests",
        Guid.NewGuid().ToString("N"));

    public GamepadCoverSearchViewModelTests() => Directory.CreateDirectory(_directory);

    [AvaloniaFact]
    public void NoResults_RingWalksSearchFieldThenSearchThenChooseLocal_AndClamps()
    {
        // The wrapper owns and disposes the search view model, so it is not `using` here.
        var search = CreateSearch([]);
        var handoffs = 0;
        using var viewModel = new GamepadCoverSearchViewModel(search, () => handoffs++);

        // Lands on the query field so the player can refine and search.
        Assert.Equal(GamepadCoverSearchTargetKind.SearchField, viewModel.FocusedKind);

        viewModel.MoveFocus(1);
        Assert.Equal(GamepadCoverSearchTargetKind.Search, viewModel.FocusedKind);
        viewModel.MoveFocus(1);
        Assert.Equal(GamepadCoverSearchTargetKind.ChooseLocal, viewModel.FocusedKind);
        // Clamps at the end rather than wrapping.
        viewModel.MoveFocus(1);
        Assert.Equal(GamepadCoverSearchTargetKind.ChooseLocal, viewModel.FocusedKind);

        // A on "Choose a file" hands off to Desktop; it does not try the OS picker in Gamepad mode.
        viewModel.Activate();
        Assert.Equal(1, handoffs);
    }

    [AvaloniaFact]
    public async Task Search_AddsCoverTiles_AndParksTheRingOnTheTopCover()
    {
        // The wrapper owns and disposes the search view model, so it is not `using` here.
        var search = CreateSearch([SearchResult("cover")]);
        using var viewModel = new GamepadCoverSearchViewModel(search, () => { });

        await viewModel.LoadAsync();

        Assert.Single(search.Results);
        Assert.Equal(GamepadCoverSearchTargetKind.Candidate, viewModel.FocusedKind);
        Assert.Same(search.Results[0], viewModel.FocusedItem);
        Assert.True(search.Results[0].IsFocused);

        // The tiles sit between Search and Choose-a-file: Down leaves the last tile for Choose-a-file.
        viewModel.MoveFocus(1);
        Assert.Equal(GamepadCoverSearchTargetKind.ChooseLocal, viewModel.FocusedKind);
        // Up from the top cover returns to the Search button.
        viewModel.MoveFocus(-1);
        viewModel.MoveFocus(-1);
        Assert.Equal(GamepadCoverSearchTargetKind.Search, viewModel.FocusedKind);
    }

    [AvaloniaFact]
    public async Task ActivatingACover_PicksIt_ResolvingToATemporaryDownload()
    {
        var result = SearchResult("cover");
        // The wrapper owns and disposes the search view model, so it is not `using` here.
        var search = CreateSearch([result]);
        using var viewModel = new GamepadCoverSearchViewModel(search, () => { });
        var picked = new TaskCompletionSource<PickedGameCover?>(TaskCreationOptions.RunContinuationsAsynchronously);
        search.CloseRequested += cover => picked.TrySetResult(cover);

        await viewModel.LoadAsync();
        Assert.Equal(GamepadCoverSearchTargetKind.Candidate, viewModel.FocusedKind);

        viewModel.Activate();

        var cover = await picked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(cover);
        Assert.True(cover.IsTemporary);
        Assert.Equal(result.ImageUri.ToString(), cover.SourceUri);
        Assert.True(File.Exists(cover.SourcePath));
    }

    [AvaloniaFact]
    public async Task StreamingResults_KeepTheRingOnTheFocusedCover_NotTheNewTopOne()
    {
        // "slow" ranks first (index 0) so it lands at the front once loaded, but "fast" (index 1)
        // finishes downloading first, so it is the only tile — and holds the ring — for a moment.
        var downloader = new GatedPreviewDownloader(_directory);
        var search = new CoverSearchViewModel(
            new GameCoverPickerContext("Example Game", "Dreamcast", 0.708),
            new RecordingSearchProvider([SearchResult("slow"), SearchResult("fast")]),
            downloader,
            () => Task.FromResult<string?>(null));
        using var viewModel = new GamepadCoverSearchViewModel(search, () => { });

        var loading = viewModel.LoadAsync();
        await downloader.FastPreviewWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => search.Results.Count == 1);
        var focusedFirst = viewModel.FocusedItem;
        Assert.Same(search.Results[0], focusedFirst);

        // The slower, higher-ranked cover now inserts at the front. The ring must stay on the tile it
        // was on, not snap back to the new top cover.
        downloader.ReleaseSlowPreview.TrySetResult();
        await loading;

        Assert.Equal(2, search.Results.Count);
        Assert.Same(focusedFirst, viewModel.FocusedItem);
        Assert.Equal("fast", ((CoverSearchResultViewModel)viewModel.FocusedItem!).Result.Title);
    }

    [AvaloniaFact]
    public async Task LosingAllResults_ReturnsTheRingToTheQueryField()
    {
        var provider = new RecordingSearchProvider([SearchResult("cover")]);
        // The wrapper owns and disposes the search view model, so it is not `using` here.
        var search = CreateSearch(provider);
        using var viewModel = new GamepadCoverSearchViewModel(search, () => { });

        await viewModel.LoadAsync();
        Assert.Equal(GamepadCoverSearchTargetKind.Candidate, viewModel.FocusedKind);

        // A fresh search that returns nothing clears the tiles; the ring must not strand on a gone tile.
        provider.SetResults([]);
        search.SearchText = "no-such-title";
        await search.SearchCommand.ExecuteAsync(null);

        Assert.Empty(search.Results);
        Assert.Equal(GamepadCoverSearchTargetKind.SearchField, viewModel.FocusedKind);
    }

    private CoverSearchViewModel CreateSearch(IReadOnlyList<ArtworkSearchResult> results) =>
        CreateSearch(new RecordingSearchProvider(results));

    private CoverSearchViewModel CreateSearch(RecordingSearchProvider provider) => new(
        new GameCoverPickerContext("Example Game", "Dreamcast", 0.708),
        provider,
        new RecordingDownloader(_directory),
        () => Task.FromResult<string?>(null));

    private static ArtworkSearchResult SearchResult(string name) => new(
        "web-search",
        new Uri($"https://images.example/{name}.png"),
        new Uri($"https://images.example/{name}-preview.png"),
        new Uri($"https://source.example/{name}"),
        600,
        900,
        name,
        ".png");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class RecordingSearchProvider(IReadOnlyList<ArtworkSearchResult> results) :
        IGameArtworkSearchProvider
    {
        private IReadOnlyList<ArtworkSearchResult> _results = results;

        public void SetResults(IReadOnlyList<ArtworkSearchResult> results) => _results = results;

        public Task<IReadOnlyList<ArtworkSearchResult>> SearchAsync(
            string title,
            string systemName,
            double preferredAspectRatio,
            CancellationToken cancellationToken = default) => Task.FromResult(_results);
    }

    private sealed class RecordingDownloader(string directory) : IRemoteArtworkDownloader
    {
        public async Task<DownloadedArtwork?> DownloadFirstAsync(
            IReadOnlyList<ArtworkCandidate> candidates,
            CancellationToken cancellationToken = default)
        {
            var candidate = candidates[0];
            var path = Path.Combine(directory, $"{Guid.NewGuid():N}.png");
            await File.WriteAllBytesAsync(path, TinyPng, cancellationToken);
            return new DownloadedArtwork(candidate, path);
        }
    }

    // Holds the "slow" preview until released, so the lower-ranked "fast" cover streams in first.
    private sealed class GatedPreviewDownloader(string directory) : IRemoteArtworkDownloader
    {
        public TaskCompletionSource FastPreviewWritten { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSlowPreview { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<DownloadedArtwork?> DownloadFirstAsync(
            IReadOnlyList<ArtworkCandidate> candidates,
            CancellationToken cancellationToken = default)
        {
            var candidate = candidates[0];
            if (candidate.SourceUri.AbsolutePath.Contains("slow", StringComparison.Ordinal))
                await ReleaseSlowPreview.Task.WaitAsync(cancellationToken);

            var path = Path.Combine(directory, $"{Guid.NewGuid():N}.png");
            await File.WriteAllBytesAsync(path, TinyPng, cancellationToken);
            if (candidate.SourceUri.AbsolutePath.Contains("fast", StringComparison.Ordinal))
                FastPreviewWritten.TrySetResult();
            return new DownloadedArtwork(candidate, path);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
}
