using System.Linq;

namespace EmuShelf.App.Tests.Handheld;

// M47 B0: the frame comparison B1 proves itself with, checked without rendering anything.
public class FrameComparisonTests
{
    private static FramePixels Frame(int width, int height, byte seed = 7)
    {
        var frame = FramePixels.Blank(width, height);
        for (var i = 0; i < frame.Pixels.Length; i++)
            frame.Pixels[i] = (byte)((i * 31) + seed);
        return frame;
    }

    private static FramePixels Copy(FramePixels frame) => frame with { Pixels = (byte[])frame.Pixels.Clone() };

    [Fact]
    public void IdenticalFrames_Pass()
    {
        var a = Frame(6, 4);
        var result = FrameComparison.Compare(a, Copy(a));

        Assert.True(result.IsIdentical);
        Assert.Equal(0, result.DifferingPixels);
        Assert.Null(result.SizeMismatch);
        Assert.Null(result.Diff);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void OneChangedChannelOfOnePixel_Fails_AndIsMarkedInTheDiff(int channel)
    {
        var a = Frame(6, 4);
        var b = Copy(a);
        var pixel = (2 * 6) + 3; // row 2, column 3
        b.Pixels[(pixel * FramePixels.BytesPerPixel) + channel] ^= 1;

        var result = FrameComparison.Compare(a, b);

        Assert.False(result.IsIdentical);
        Assert.Equal(1, result.DifferingPixels);
        var diff = Assert.IsType<FramePixels>(result.Diff);
        Assert.Equal((6, 4), (diff.Width, diff.Height));
        var marked = Enumerable.Range(0, 6 * 4)
            .Where(p => diff.Pixels[p * 4] == 255 && diff.Pixels[(p * 4) + 1] == 0 && diff.Pixels[(p * 4) + 2] == 255)
            .ToList();
        Assert.Equal([pixel], marked);
        Assert.All(Enumerable.Range(0, 6 * 4), p => Assert.Equal(255, diff.Pixels[(p * 4) + 3]));
    }

    [Fact]
    public void EveryDifferingPixelIsCounted()
    {
        var a = Frame(10, 10);
        var b = Copy(a);
        for (var p = 0; p < 100; p += 3)
            b.Pixels[p * 4] ^= 0x80;

        Assert.Equal(34, FrameComparison.Compare(a, b).DifferingPixels);
    }

    [Theory]
    [InlineData(6, 5)]
    [InlineData(7, 4)]
    [InlineData(4, 6)]
    public void DifferentSizes_Fail(int width, int height)
    {
        var result = FrameComparison.Compare(Frame(6, 4), Frame(width, height));

        Assert.False(result.IsIdentical);
        Assert.Equal($"6x4 became {width}x{height}", result.SizeMismatch);
        Assert.Null(result.Diff);
    }
}
