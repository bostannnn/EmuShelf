using System.ComponentModel;
using Avalonia.Media.Imaging;
using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Services;

/// <summary>
/// UI-thread-only LRU for decoded 2D covers across library scopes. GameViewModel still owns
/// bitmap disposal; eviction clears its property so bindings never retain a disposed cover.
/// The pixel budget may be exceeded by realized tiles, which must not go blank under pressure.
/// Releasing their view leases immediately makes them eligible for eviction again.
/// </summary>
internal sealed class DecodedCoverCache(long budgetBytes)
{
    private sealed record Entry(GameViewModel Game, long Bytes);
    private readonly LinkedList<Entry> _lru = new();
    private readonly Dictionary<GameViewModel, LinkedListNode<Entry>> _entries = new();

    private long _retainedBytes;
    private int _entryCount;
    private int _evictions;
    // The device sampler reads these counters from a pool thread; never expose the dictionary there.
    internal long RetainedBytes => Interlocked.Read(ref _retainedBytes);
    internal int Count => Volatile.Read(ref _entryCount);
    internal int Evictions => Volatile.Read(ref _evictions);

    public void Set(GameViewModel game, Bitmap image)
    {
        Remove(game);
        game.CoverImage = image;
        var bytes = checked((long)image.PixelSize.Width * image.PixelSize.Height * 4);
        _entries.Add(game, _lru.AddFirst(new Entry(game, bytes)));
        Interlocked.Add(ref _retainedBytes, bytes);
        Volatile.Write(ref _entryCount, _entries.Count);
        game.PropertyChanged += OnGameChanged;
        Trim();
    }

    public void Touch(GameViewModel game)
    {
        if (!_entries.TryGetValue(game, out var node) || node == _lru.First)
            return;
        _lru.Remove(node);
        _lru.AddFirst(node);
    }

    private void OnGameChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not GameViewModel game)
            return;
        if (e.PropertyName == nameof(GameViewModel.CoverImage))
            Remove(game); // path replacement / scope disposal already disposes the old bitmap
        else if (e.PropertyName == nameof(GameViewModel.CoverConsumerCount))
        {
            if (game.CoverConsumerCount > 0)
                Touch(game);
            else
                Trim();
        }
    }

    private void Remove(GameViewModel game)
    {
        if (!_entries.Remove(game, out var node))
            return;
        game.PropertyChanged -= OnGameChanged;
        _lru.Remove(node);
        Interlocked.Add(ref _retainedBytes, -node.Value.Bytes);
        Volatile.Write(ref _entryCount, _entries.Count);
    }

    private void Trim()
    {
        var node = _lru.Last;
        while (RetainedBytes > budgetBytes && node is not null)
        {
            var previous = node.Previous;
            var game = node.Value.Game;
            if (game.CoverConsumerCount == 0)
            {
                Remove(game);
                Interlocked.Increment(ref _evictions);
                game.CoverImage = null;
            }
            node = previous;
        }
    }
}
