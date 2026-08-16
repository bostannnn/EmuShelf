using Avalonia;
using Avalonia.Logging;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace EmuShelf.App.Rendering;

/// <summary>
/// Captures a live Avalonia subtree into a pixel buffer the GL render thread can upload.
/// </summary>
/// <remarks>
/// This exists because Avalonia invokes <c>OnOpenGlRender</c> on the render thread, and the visual
/// tree may only be touched from the UI thread. So the capture runs on a UI-thread timer and hands
/// the render thread a plain byte buffer under a lock. Trying to render the visual directly from
/// inside the GL callback is the obvious shape and is a data race.
///
/// The capture is deliberately lower resolution than the window. It is cheaper, and it is also more
/// faithful: a CRT does not resolve UI text crisply, so downsampling the chrome before it enters the
/// tube is part of the effect rather than a concession to it.
/// </remarks>
internal sealed class ChromeSnapshot : IDisposable
{
    /// <summary>Longest edge the chrome is captured at, before the tube softens it further.</summary>
    private const int MaximumEdge = 1280;

    /// <summary>Avalonia log area for the chrome-capture geometry trace; captured to Logs/.</summary>
    internal const string ChromeLogArea = "EmuShelf.Shelf3D";

    private readonly Lock _gate = new();
    private readonly DispatcherTimer _timer;
    private readonly Func<Visual?> _source;
    private readonly Action? _onCaptured;

    private RenderTargetBitmap? _target;
    private WriteableBitmap? _staging;
    // Two buffers so the UI thread's copy and the render thread's upload never wait on each other:
    // the capture fills the back buffer with no lock held, and only the swap is synchronised. With a
    // single buffer the full-window memcpy sat inside the same lock as glTexSubImage2D, so a capture
    // could stall the UI thread behind a GPU upload every frame.
    private byte[]? _back;
    private byte[]? _front;
    // What the buffers were allocated for, versus what the front buffer actually holds. Publishing
    // the new size at allocation time rather than at swap time would advertise a resized geometry
    // for a frame the front buffer is still holding at the old one.
    private PixelSize _allocatedSize;
    private PixelSize _size;
    private bool _hasNewFrame;
    private volatile bool _disposed;
    // The source (GamepadRoot) bounds the last diagnostic line reported. The captured chrome carries
    // the platform rail into the tube; if it is captured at desktop size while the tube surface is
    // full screen (or vice versa) the warped rail lands at the wrong scale — a "doubled platform row"
    // ingredient. Logged only when it changes, so Logs/ shows the capture tracking the resize.
    private Size _lastLoggedSourceBounds;

    /// <param name="onCaptured">
    /// Invoked on the UI thread after each capture tick. It lets the owner drive the render that
    /// uploads the capture: a still (non-animated) presentation redraws only on demand, so without
    /// this the couch chrome kept live behind it — overlays and toasts that move without touching the
    /// shelf — would be captured but never uploaded until the next shelf change.
    /// </param>
    public ChromeSnapshot(Func<Visual?> source, TimeSpan interval, Action? onCaptured = null)
    {
        _source = source;
        _onCaptured = onCaptured;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = interval };
        _timer.Tick += (_, _) =>
        {
            Capture();
            _onCaptured?.Invoke();
        };
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    /// <summary>
    /// Hands the newest capture to the caller, or reports that nothing has changed since last time.
    /// </summary>
    /// <remarks>
    /// Returns the buffer itself rather than a copy: the caller is the GL thread, it uploads
    /// immediately inside the lock's scope, and copying a megabyte per frame to avoid a borrow that
    /// lasts one <c>glTexImage2D</c> would cost more than the capture did.
    /// </remarks>
    public bool TryTake(Action<byte[], PixelSize> upload)
    {
        lock (_gate)
        {
            if (!_hasNewFrame || _front is null || _size.Width <= 0 || _size.Height <= 0)
            {
                return false;
            }

            upload(_front, _size);
            _hasNewFrame = false;
            return true;
        }
    }

    private void Capture()
    {
        if (_disposed || _source() is not { } visual)
        {
            return;
        }

        var bounds = visual.Bounds;
        if (bounds.Width < 1 || bounds.Height < 1)
        {
            return;
        }

        var scale = Math.Min(1.0, MaximumEdge / Math.Max(bounds.Width, bounds.Height));
        var size = new PixelSize(
            Math.Max(1, (int)Math.Round(bounds.Width * scale)),
            Math.Max(1, (int)Math.Round(bounds.Height * scale)));

        if (bounds.Size != _lastLoggedSourceBounds)
        {
            Logger.TryGet(LogEventLevel.Information, ChromeLogArea)?.Log(
                null,
                "Couch chrome captured from {BoundsW}x{BoundsH} dip source into {PixelW}x{PixelH} px.",
                bounds.Width,
                bounds.Height,
                size.Width,
                size.Height);
            _lastLoggedSourceBounds = bounds.Size;
        }

        try
        {
            EnsureTargets(size, scale);
            if (_target is null || _staging is null || _back is null)
            {
                return;
            }

            // Clear first. RenderTargetBitmap is reused every tick and Render(Visual) does not
            // promise to clear it — only CreateDrawingContext(clear: true) does. Without this, every
            // partly transparent thing in the couch tree composites onto the previous capture
            // instead of replacing it: the overlay scrim is #B9000000, so the image darkened frame
            // by frame and anything that moved — the focus ring above all — smeared a trail of ghosts
            // behind it.
            using (_target.CreateDrawingContext(true))
            {
            }

            _target.Render(visual);

            // Outside the lock: this is the expensive part, and nothing else touches the back buffer.
            using (var locked = _staging.Lock())
            {
                _target.CopyPixels(locked);
                CopyRows(locked, _back, size);
            }

            lock (_gate)
            {
                (_front, _back) = (_back, _front);
                _size = size;
                _hasNewFrame = true;
            }
        }
        catch (Exception)
        {
            // A capture that fails is a frame the tube shows without chrome, not a crash. The most
            // likely cause is the visual being mid-teardown as couch mode closes, which resolves
            // itself on the next tick.
        }
    }

    /// <summary>
    /// Copies a locked framebuffer into a tightly packed buffer.
    /// </summary>
    /// <remarks>
    /// Row by row unless the strides already agree. A locked framebuffer is free to pad its rows,
    /// and a flat copy of width*height*4 bytes from a padded surface shears the image progressively
    /// down the screen — which would have been an occasional, resolution-dependent bug rather than
    /// an obvious one.
    /// </remarks>
    private static void CopyRows(ILockedFramebuffer locked, byte[] destination, PixelSize size)
    {
        var stride = size.Width * 4;
        if (locked.RowBytes == stride)
        {
            System.Runtime.InteropServices.Marshal.Copy(
                locked.Address, destination, 0, Math.Min(destination.Length, stride * size.Height));
            return;
        }

        for (var y = 0; y < size.Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(
                locked.Address + (y * locked.RowBytes), destination, y * stride, stride);
        }
    }

    private void EnsureTargets(PixelSize size, double scale)
    {
        if (_target is not null && _allocatedSize == size && _back is not null && _front is not null)
        {
            return;
        }

        _target?.Dispose();
        _staging?.Dispose();

        // The DPI is what makes this a downscale rather than a crop. Avalonia derives the render
        // scaling from dpi/96, so a target left at 96 maps one logical unit to one pixel and simply
        // stops when the bitmap runs out — capturing the top-left corner of the couch screen and
        // throwing away the rest. Baking the reduction factor into the DPI fits the whole tree into
        // the smaller surface instead.
        var dpi = new Vector(96 * scale, 96 * scale);
        _target = new RenderTargetBitmap(size, dpi);
        _staging = new WriteableBitmap(size, dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        _back = new byte[size.Width * size.Height * 4];
        _front = new byte[_back.Length];
        _allocatedSize = size;
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();

        lock (_gate)
        {
            _target?.Dispose();
            _staging?.Dispose();
            _target = null;
            _staging = null;
            _back = null;
            _front = null;
            _allocatedSize = default;
            _hasNewFrame = false;
        }
    }
}
