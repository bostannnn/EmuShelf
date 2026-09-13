using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;

namespace EmuShelf.Core.SecondScreen;

public sealed record CompanionMediaItem(string Title, GameMediaKind Kind, string? LocalPath, Uri? RemoteUri = null)
{
    public bool IsVideo => Kind == GameMediaKind.Video;
}

public sealed record CompanionMediaSet(long GameId, string Title, IReadOnlyList<CompanionMediaItem> Items)
{
    public CompanionMediaItem? Background => Items.FirstOrDefault(i => i.Kind == GameMediaKind.Fanart);
    public CompanionMediaItem? Logo => Items.FirstOrDefault(i => i.Kind == GameMediaKind.Wheel);
}

public interface ICompanionMediaSource
{
    event Action<long>? ArtworkChanged;
    /// <summary>Reads saved artwork; never starts a provider request.</summary>
    Task<CompanionMediaSet> GetAsync(Game game, CancellationToken token);
    Task<string?> GetLocalPathAsync(CompanionMediaItem item, CancellationToken token);
}

public interface ICompanionVideoPlayer : IDisposable
{
    void Play(string localPath);
    void Stop();
}

/// <summary>Explicit/import-time artwork enrichment, separate from presentation.</summary>
public interface IGameMediaEnricher
{
    bool Supports(Game game);
    bool HasMissingArtwork(Game game);
    Task<int> FetchMissingAsync(Game game, CancellationToken token);
}
