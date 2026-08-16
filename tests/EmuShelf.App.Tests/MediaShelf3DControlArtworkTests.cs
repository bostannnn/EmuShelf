using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EmuShelf.App.Controls;

namespace EmuShelf.App.Tests;

/// <summary>
/// Locks the per-face artwork-upload guard that stands between a cover swap (a scrape) and the
/// couch shelf blanking. The upload runs on the GL render thread; a bitmap the frame snapshot still
/// references can be disposed on the UI thread the instant a scrape replaces the cover, and reading
/// a disposed bitmap throws. That throw must be contained to the one face — not escape into the
/// render loop, where the consecutive-failure counter would drop the CRT and every 3D model to flat
/// covers for the session. This is the exact chain in the original bug report; the guard is
/// exercised here without a GL context via <see cref="MediaShelf3DControl.TryBuildFaceTexture"/>.
/// </summary>
public sealed class MediaShelf3DControlArtworkTests
{
    private static WriteableBitmap CreateBitmap() =>
        new(new PixelSize(4, 4), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

    [AvaloniaFact]
    public void FaceTexture_FromArtworkThatRacedDisposal_IsSkippedNotThrown()
    {
        var bitmap = CreateBitmap();
        // Simulate the scrape: the cover the snapshot still points at is disposed before the upload
        // reads it.
        bitmap.Dispose();

        var built = MediaShelf3DControl.TryBuildFaceTexture(bitmap, out var texture);

        // Contained: the face is skipped (keep the GPU's current texture) and nothing is thrown, so
        // the draw never counts a failure toward the flat-cover fallback.
        Assert.False(built);
        Assert.Null(texture);
    }

    [AvaloniaFact]
    public void FaceTexture_FromNoArtwork_SucceedsWithNoTexture()
    {
        var built = MediaShelf3DControl.TryBuildFaceTexture(null, out var texture);

        Assert.True(built);
        Assert.Null(texture);
    }

    [AvaloniaFact]
    public void FaceTexture_FromLiveBitmap_SucceedsWithATexture()
    {
        var bitmap = CreateBitmap();

        var built = MediaShelf3DControl.TryBuildFaceTexture(bitmap, out var texture);

        Assert.True(built);
        Assert.NotNull(texture);
    }
}
