using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Metadata;

namespace EmuShelf.App.Tests;

public sealed class CoverSearchViewModelTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "EmuShelfCoverSearchTests",
        Guid.NewGuid().ToString("N"));

    public CoverSearchViewModelTests() => Directory.CreateDirectory(_directory);

    [AvaloniaFact]
    public async Task Search_LoadsPreviewAndSelectionReturnsTemporaryOriginal()
    {
        var result = new ArtworkSearchResult(
            "web-search",
            new Uri("https://images.example/cover.png"),
            new Uri("https://images.example/preview.png"),
            new Uri("https://source.example/game"),
            600,
            900,
            "Example cover",
            ".png");
        var provider = new RecordingSearchProvider([result]);
        var downloader = new RecordingDownloader(_directory);
        using var viewModel = new CoverSearchViewModel(
            new GameCoverPickerContext("Example Game", "Dreamcast", 0.708),
            provider,
            downloader,
            () => Task.FromResult<string?>(null));
        PickedGameCover? selection = null;
        viewModel.CloseRequested += picked => selection = picked;

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Results);
        Assert.Equal("source.example", viewModel.Results[0].SourceText);
        Assert.Equal("Example Game", provider.Title);
        Assert.Equal("Dreamcast", provider.SystemName);
        Assert.Equal(0.708, provider.PreferredAspectRatio);
        Assert.True(viewModel.ShowResults);

        await viewModel.Results[0].SelectCommand.ExecuteAsync(null);

        Assert.NotNull(selection);
        Assert.True(selection.IsTemporary);
        Assert.Equal(result.ImageUri.ToString(), selection.SourceUri);
        Assert.True(File.Exists(selection.SourcePath));
    }

    [AvaloniaFact]
    public async Task ChooseLocalImage_ReturnsNonTemporarySelection()
    {
        var localPath = Path.Combine(_directory, "local.png");
        await File.WriteAllBytesAsync(localPath, TinyPng);
        using var viewModel = new CoverSearchViewModel(
            new GameCoverPickerContext("Example", "PlayStation", 1),
            new RecordingSearchProvider([]),
            new RecordingDownloader(_directory),
            () => Task.FromResult<string?>(localPath));
        PickedGameCover? selection = null;
        viewModel.CloseRequested += picked => selection = picked;

        await viewModel.ChooseLocalImageCommand.ExecuteAsync(null);

        Assert.Equal(localPath, selection!.SourcePath);
        Assert.False(selection.IsTemporary);
    }

    [AvaloniaFact]
    public async Task Search_ShowsFastPreviewBeforeSlowerPreviewFinishesAndPreservesRankOrder()
    {
        var slow = SearchResult("slow", 600, 900);
        var fast = SearchResult("fast", 600, 900);
        var downloader = new GatedPreviewDownloader(_directory);
        using var viewModel = new CoverSearchViewModel(
            new GameCoverPickerContext("Example", "PlayStation", 1),
            new RecordingSearchProvider([slow, fast]),
            downloader,
            () => Task.FromResult<string?>(null));

        var search = viewModel.SearchCommand.ExecuteAsync(null);
        await downloader.FastPreviewWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => viewModel.Results.Count == 1);

        Assert.True(viewModel.IsSearching);
        Assert.True(viewModel.ShowResults);
        Assert.Equal("fast", viewModel.Results[0].Result.Title);

        downloader.ReleaseSlowPreview.TrySetResult();
        await search;

        Assert.Equal(["slow", "fast"], viewModel.Results.Select(item => item.Result.Title));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class RecordingSearchProvider(IReadOnlyList<ArtworkSearchResult> results) :
        IGameArtworkSearchProvider
    {
        public string? Title { get; private set; }
        public string? SystemName { get; private set; }
        public double PreferredAspectRatio { get; private set; }

        public Task<IReadOnlyList<ArtworkSearchResult>> SearchAsync(
            string title,
            string systemName,
            double preferredAspectRatio,
            CancellationToken cancellationToken = default)
        {
            Title = title;
            SystemName = systemName;
            PreferredAspectRatio = preferredAspectRatio;
            return Task.FromResult(results);
        }
    }

    private sealed class RecordingDownloader(string directory) : IRemoteArtworkDownloader
    {
        public async Task<DownloadedArtwork?> DownloadFirstAsync(
            IReadOnlyList<ArtworkCandidate> candidates,
            CancellationToken cancellationToken = default)
        {
            var candidate = Assert.Single(candidates);
            var path = Path.Combine(directory, $"{Guid.NewGuid():N}.png");
            await File.WriteAllBytesAsync(path, TinyPng, cancellationToken);
            return new DownloadedArtwork(candidate, path);
        }
    }

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
            var candidate = Assert.Single(candidates);
            if (candidate.SourceUri.AbsolutePath.Contains("slow", StringComparison.Ordinal))
                await ReleaseSlowPreview.Task.WaitAsync(cancellationToken);

            var path = Path.Combine(directory, $"{Guid.NewGuid():N}.png");
            await File.WriteAllBytesAsync(path, TinyPng, cancellationToken);
            if (candidate.SourceUri.AbsolutePath.Contains("fast", StringComparison.Ordinal))
                FastPreviewWritten.TrySetResult();
            return new DownloadedArtwork(candidate, path);
        }
    }

    private static ArtworkSearchResult SearchResult(string name, int width, int height) => new(
        "web-search",
        new Uri($"https://images.example/{name}.png"),
        new Uri($"https://images.example/{name}-preview.png"),
        null,
        width,
        height,
        name,
        ".png");

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
}
