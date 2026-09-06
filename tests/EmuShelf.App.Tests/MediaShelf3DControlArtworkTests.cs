using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EmuShelf.App.Rendering;

namespace EmuShelf.App.Tests;

/// <summary>Disposed artwork must fail locally during background preparation, while empty
/// and live faces produce the expected upload data without a GL context.</summary>
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

        var built = ShelfTexturePreparation.TryBuildFaceTexture(bitmap, out var texture);

        // Contained: the face is skipped (keep the GPU's current texture) and nothing is thrown, so
        // the draw never counts a failure toward the flat-cover fallback.
        Assert.False(built);
        Assert.Null(texture);
    }

    [AvaloniaFact]
    public void FaceTexture_FromNoArtwork_SucceedsWithNoTexture()
    {
        var built = ShelfTexturePreparation.TryBuildFaceTexture(null, out var texture);

        Assert.True(built);
        Assert.Null(texture);
    }

    [AvaloniaFact]
    public void FaceTexture_FromLiveBitmap_SucceedsWithATexture()
    {
        using var bitmap = CreateBitmap();

        var built = ShelfTexturePreparation.TryBuildFaceTexture(bitmap, out var texture);

        Assert.True(built);
        Assert.NotNull(texture);
    }
}
