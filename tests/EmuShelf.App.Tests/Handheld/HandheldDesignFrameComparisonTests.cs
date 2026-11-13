using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace EmuShelf.App.Tests.Handheld;

// M47 B0: compares two renders of the Handheld design set (HandheldDesignSnapshotTests) pixel for pixel. B1 moves every
// inline colour, size and radius into resources and proves nothing moved by rendering before and after:
//
//   EMUSHELF_DESIGN_SNAPSHOT_DIR=/tmp/before dotnet test tests/EmuShelf.App.Tests --filter "FullyQualifiedName~DesignSnapshot"
//   (change the code)
//   EMUSHELF_DESIGN_SNAPSHOT_DIR=/tmp/after dotnet test tests/EmuShelf.App.Tests --filter "FullyQualifiedName~DesignSnapshot"
//   EMUSHELF_DESIGN_BASELINE_DIR=/tmp/before EMUSHELF_DESIGN_SNAPSHOT_DIR=/tmp/after \
//     dotnet test tests/EmuShelf.App.Tests --filter "FullyQualifiedName~DesignFrames_MatchBaseline"
//
// Use a fresh directory for each render. It returns at once unless both variables are set, so a normal `dotnet test` never
// runs it. A failure names every frame that differs with its count of differing pixels and writes a diff image for each
// into the newer directory's _diff folder; a frame missing from either side fails too.
public class HandheldDesignFrameComparisonTests
{
    private static string? Variable(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

    [AvaloniaFact]
    public void DesignFrames_MatchBaseline()
    {
        if (Variable("EMUSHELF_DESIGN_BASELINE_DIR") is not { } baseline || Variable("EMUSHELF_DESIGN_SNAPSHOT_DIR") is not { } candidate)
            return;

        var before = Frames(baseline);
        var after = Frames(candidate);
        Assert.True(before.Count > 0, $"No frames in the baseline {baseline}");
        var problems = new List<string>();
        problems.AddRange(before.Keys.Except(after.Keys).Order(StringComparer.Ordinal).Select(name => $"{name}: missing from {candidate}"));
        problems.AddRange(after.Keys.Except(before.Keys).Order(StringComparer.Ordinal).Select(name => $"{name}: not in the baseline"));

        var diffs = Path.Combine(candidate, "_diff");
        foreach (var name in before.Keys.Intersect(after.Keys).Order(StringComparer.Ordinal))
        {
            var result = FrameComparison.Compare(Load(before[name]), Load(after[name]));
            if (result.SizeMismatch is { } size)
            {
                problems.Add($"{name}: size {size}");
            }
            else if (result.Diff is { } diff)
            {
                Directory.CreateDirectory(diffs);
                var path = Path.Combine(diffs, name);
                Save(diff, path);
                problems.Add($"{name}: {result.DifferingPixels} differing pixels");
            }
        }

        TestContext.Current.SendDiagnosticMessage($"Compared {before.Count} baseline frames with {after.Count}: {problems.Count} problems.");
        if (problems.Count > 0)
        {
            var where = Directory.Exists(diffs) ? $"{Environment.NewLine}Diff images (differing pixels in magenta): {diffs}" : string.Empty;
            Assert.Fail($"{problems.Count} of {before.Count} frames differ from the baseline:{Environment.NewLine}" +
                string.Join(Environment.NewLine, problems) + where);
        }
    }

    private static Dictionary<string, string> Frames(string directory) =>
        Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.png", SearchOption.TopDirectoryOnly).ToDictionary(path => Path.GetFileName(path), StringComparer.Ordinal)
            : [];

    /// <summary>Decodes a PNG to packed four-byte pixels, in whatever order the decoder keeps them.</summary>
    internal static FramePixels Load(string path)
    {
        using var bitmap = new Bitmap(path);
        var (width, height) = (bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        var frame = FramePixels.Blank(width, height);
        var handle = GCHandle.Alloc(frame.Pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), frame.Pixels.Length, frame.Stride);
        }
        finally
        {
            handle.Free();
        }
        return frame;
    }

    internal static void Save(FramePixels frame, string path)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            for (var row = 0; row < frame.Height; row++)
                Marshal.Copy(frame.Pixels, row * frame.Stride, buffer.Address + (row * buffer.RowBytes), frame.Stride);
        }
        bitmap.Save(path);
    }
}
