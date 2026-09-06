using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using EmuShelf.App.Rendering;
using EmuShelf.Rendering.Models;

namespace EmuShelf.App.Tests;

public sealed class ShelfTexturePreparationTests
{
    private static WriteableBitmap Image() =>
        new(new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

    private static async Task Drain(ShelfTexturePreparation preparation)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!preparation.IsIdle)
            await Task.Delay(10, timeout.Token);
    }

    [AvaloniaFact]
    public async Task ConversionRunsOffUiThread_AndReusesOwnedPixels()
    {
        using var image = Image();
        using (var pixels = image.Lock())
            Marshal.Copy(new byte[] { 10, 20, 30, 128 }, 0, pixels.Address, 4);
        var calls = 0;
        var onUiThread = true;
        var preparation = new ShelfTexturePreparation(() => { }, source =>
        {
            Interlocked.Increment(ref calls);
            onUiThread = Dispatcher.UIThread.CheckAccess();
            ShelfTexturePreparation.TryBuildFaceTexture(source, out var texture);
            return texture;
        });
        preparation.Update([image, image]);
        await Drain(preparation);
        var texture = Assert.IsType<TextureImage>(preparation.Get(image));
        image.Dispose();
        preparation.Update([image]);
        Assert.Same(texture, preparation.Get(image));
        Assert.Equal(16, texture.Rgba.Length);
        Assert.Equal(new byte[] { 59, 39, 19, 128 }, texture.Rgba.Take(4));
        Assert.Equal(1, calls);
        Assert.False(onUiThread);
    }

    [AvaloniaFact]
    public async Task ClearDiscardsRunningAndQueuedWork_WithoutPublishingStalePixels()
    {
        using var first = Image();
        using var second = Image();
        using var queued = Image();
        using var release = new ManualResetEventSlim();
        var changes = 0;
        var calls = 0;
        var preparation = new ShelfTexturePreparation(() => changes++, _ =>
        {
            Interlocked.Increment(ref calls);
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return new TextureImage { Width = 1, Height = 1, Rgba = [1, 2, 3, 4] };
        });
        preparation.Update([first, second, queued]);
        preparation.Clear();
        release.Set();
        await Drain(preparation);
        Assert.Equal(2, calls);
        Assert.Equal(0, changes);
        Assert.Null(preparation.Get(first));
        Assert.Null(preparation.Get(queued));
    }

    [AvaloniaFact]
    public async Task ReplacingPendingSourcesDropsOldCompletion_AndPumpsReplacement()
    {
        using var old = Image();
        using var replacement = Image();
        using var release = new ManualResetEventSlim();
        var changes = 0;
        var preparation = new ShelfTexturePreparation(() => changes++, source =>
        {
            if (ReferenceEquals(source, old) && !release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException();
            return new TextureImage { Width = 1, Height = 1, Rgba = [1, 2, 3, 4] };
        });
        preparation.Update([old]);
        preparation.Update([replacement]);
        release.Set();
        await Drain(preparation);
        Assert.Null(preparation.Get(old));
        Assert.NotNull(preparation.Get(replacement));
        Assert.Equal(1, changes);
    }

    [AvaloniaFact]
    public async Task PendingReplacementKeepsPreviousFace_ButFailureOrRemovalClearsIt()
    {
        using var image = Image();
        using var release = new ManualResetEventSlim();
        var previous = new TextureImage { Width = 1, Height = 1, Rgba = [1, 2, 3, 4] };
        var preparation = new ShelfTexturePreparation(() => { }, _ =>
        {
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return null;
        });
        preparation.Update([image]);
        Assert.Same(previous, preparation.Get(image, previous));
        Assert.Null(preparation.Get(null, previous));
        release.Set();
        await Drain(preparation);
        Assert.Null(preparation.Get(image, previous));
    }

    [AvaloniaFact]
    public async Task FailedConversionIsNotRetriedEveryFrame()
    {
        using var image = Image();
        var calls = 0;
        var preparation = new ShelfTexturePreparation(() => { }, _ =>
        {
            Interlocked.Increment(ref calls);
            throw new ObjectDisposedException("cover");
        });
        preparation.Update([image]);
        await Drain(preparation);
        preparation.Update([image]);
        await Drain(preparation);
        Assert.Equal(1, calls);
        Assert.Null(preparation.Get(image));
    }
}
