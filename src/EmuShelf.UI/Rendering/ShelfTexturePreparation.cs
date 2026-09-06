using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using EmuShelf.Rendering.Models;

namespace EmuShelf.App.Rendering;

/// <summary>
/// UI-owned, bounded preparation queue. Workers read bitmap pixels; published textures own their
/// bytes, so disposing an old cover cannot invalidate a render-thread snapshot.
/// </summary>
internal sealed class ShelfTexturePreparation(Action changed, Func<IImage, TextureImage?>? convert = null)
{
    private readonly Dictionary<IImage, Entry> _entries = new(ReferenceEqualityComparer.Instance);
    private readonly Queue<Entry> _queue = new();
    private int _active;
    private const int WorkerLimit = 2;

    // A replacement keeps the same game's previous face until its conversion finishes. Truly
    // missing or failed sources return null so removed artwork cannot linger indefinitely.
    public TextureImage? Get(IImage? image, TextureImage? previous = null) =>
        image is not null && _entries.TryGetValue(image, out var entry)
            ? entry.Completed ? entry.Texture : previous
            : null;

    public void Update(IEnumerable<IImage> prioritizedImages)
    {
        var wanted = new HashSet<IImage>(ReferenceEqualityComparer.Instance);
        _queue.Clear();
        foreach (var image in prioritizedImages)
        {
            if (!wanted.Add(image)) continue;
            if (!_entries.TryGetValue(image, out var entry))
                _entries[image] = entry = new Entry(image);
            if (!entry.Started) _queue.Enqueue(entry);
        }
        foreach (var image in _entries.Keys.ToArray())
            if (!wanted.Contains(image)) _entries.Remove(image);
        Pump();
    }

    internal bool IsIdle => _active == 0 && _queue.Count == 0;

    public void Clear()
    {
        _entries.Clear();
        _queue.Clear();
    }

    private void Pump()
    {
        while (_active < WorkerLimit && _queue.TryDequeue(out var entry))
        {
            entry.Started = true;
            _active++;
            _ = PrepareAsync(entry);
        }
    }

    private async Task PrepareAsync(Entry entry)
    {
        TextureImage? texture = null;
        try
        {
            texture = await Task.Run(() =>
            {
                if (convert is not null) return convert(entry.Source);
                TryBuildFaceTexture(entry.Source, out var result);
                return result;
            }).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A replaced or evicted bitmap may be disposed before the worker reads it. Treat this
            // source as unavailable, without taking down GL or retrying it on every animation tick.
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _active--;
            if (_entries.TryGetValue(entry.Source, out var current) && ReferenceEquals(current, entry))
            {
                entry.Texture = texture;
                entry.Completed = true;
                changed();
            }
            Pump();
        });
    }

    private sealed class Entry(IImage source)
    {
        public IImage Source { get; } = source;
        public bool Started { get; set; }
        public bool Completed { get; set; }
        public TextureImage? Texture { get; set; }
    }
    // Conversion is called only by preparation workers; the GL callback receives owned bytes.
    internal static bool TryBuildFaceTexture(object? artwork, out TextureImage? texture)
    {
        try
        {
            texture = artwork is Bitmap bitmap ? ToTextureImage(bitmap) : null;
            return true;
        }
        catch (Exception)
        {
            texture = null;
            return false;
        }
    }

    private static TextureImage? ToTextureImage(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return null;
        }

        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(
                new PixelRect(0, 0, size.Width, size.Height),
                handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        var swapRedAndBlue = bitmap.Format != PixelFormat.Rgba8888;
        var premultiplied = bitmap.AlphaFormat != AlphaFormat.Unpremul;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            if (swapRedAndBlue)
            {
                (pixels[index], pixels[index + 2]) = (pixels[index + 2], pixels[index]);
            }

            var alpha = pixels[index + 3];
            if (premultiplied && alpha is > 0 and < 255)
            {
                pixels[index] = (byte)Math.Min(255, pixels[index] * 255 / alpha);
                pixels[index + 1] = (byte)Math.Min(255, pixels[index + 1] * 255 / alpha);
                pixels[index + 2] = (byte)Math.Min(255, pixels[index + 2] * 255 / alpha);
            }
        }

        return new TextureImage { Width = size.Width, Height = size.Height, Rgba = pixels };
    }

}
