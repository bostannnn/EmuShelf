using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views.Handheld;
using EmuShelf.Rendering.Stage.BigType;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

/// <summary>
/// M47 B11 (the owner on the Thor: "a pixel or two stuck on top for a while"): a rolling text's frames, composed exactly as the native
/// screens draw a roll (each piece of the old or new picture moved by its offset and cut to its window, <see cref="BigTypeRollDraw"/>),
/// from real pictures of the Stage's line and second line. No pixel of either text lies outside the text's own box; once a character's
/// old glyph has had its time to leave, no pixel of it is left; an arriving glyph shows nothing before it starts to rise; and once the
/// roll ends the frame is the new picture alone.
/// </summary>
public class HandheldBigTypeRollTests
{
    private sealed record Picture(int Width, int Height, byte[] Alpha, StageText Cells);

    // A one-line text as the Stage pictures it: the face, its size and weight, white on nothing, laid out in a window (as the line is).
    private static async Task<Picture> PictureOf(string text, double size, double? width = null, bool wrap = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily(HandheldTokens.FaceSource),
            FontSize = size,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            FontFeatures = FontFeatureCollection.Parse("+tnum"),
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        if (width is { } w)
            block.MaxWidth = w;
        var window = new Window { Content = block, Width = 1920, Height = 400 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
            var pw = (int)Math.Ceiling(block.Bounds.Width);
            var ph = (int)Math.Ceiling(block.Bounds.Height);
            using var target = new RenderTargetBitmap(new PixelSize(pw, ph));
            target.Render(block);
            var rgba = new byte[pw * ph * 4];
            var handle = GCHandle.Alloc(rgba, GCHandleType.Pinned);
            try
            {
                target.CopyPixels(new PixelRect(0, 0, pw, ph), handle.AddrOfPinnedObject(), rgba.Length, pw * 4);
            }
            finally
            {
                handle.Free();
            }
            var alpha = new byte[pw * ph];
            for (var i = 0; i < alpha.Length; i++)
                alpha[i] = rgba[(i * 4) + 3];
            var cells = HandheldTextCells.Of(block, block) ?? throw new InvalidOperationException("no cells");
            return new Picture(pw, ph, alpha, cells);
        }
        finally
        {
            window.Close();
        }
    }

    // The frame as the compositor draws it: each piece's picture moved down by its offset, cut to its window, at its opacity.
    // Which pixels each source left (old, new), as the most alpha either reached.
    private static (byte[] Old, byte[] New) Compose(Picture old, Picture next, IReadOnlyList<BigTypeRollPiece> pieces, int width, int height)
    {
        var fromOld = new byte[width * height];
        var fromNew = new byte[width * height];
        foreach (var piece in pieces)
        {
            var source = piece.Source == BigTypeRollSource.Old ? old : next;
            var into = piece.Source == BigTypeRollSource.Old ? fromOld : fromNew;
            // The scissor's rounding, as DrawRoll rounds it outward to whole pixels.
            var left = (int)MathF.Floor(piece.ClipX);
            var right = (int)MathF.Ceiling(piece.ClipX + piece.ClipWidth);
            var top = (int)MathF.Floor(piece.ClipY);
            var bottom = (int)MathF.Ceiling(piece.ClipY + piece.ClipHeight);
            var shift = (int)MathF.Round(piece.OffsetY);
            for (var y = Math.Max(0, top); y < Math.Min(height, bottom); y++)
            {
                var sy = y - shift;
                if (sy < 0 || sy >= source.Height)
                    continue;
                for (var x = Math.Max(0, left); x < Math.Min(width, Math.Min(right, source.Width)); x++)
                {
                    var a = (byte)Math.Round(source.Alpha[(sy * source.Width) + x] * piece.Opacity);
                    var i = (y * width) + x;
                    into[i] = Math.Max(into[i], a);
                }
            }
        }
        return (fromOld, fromNew);
    }

    public static IEnumerable<object[]> Changes =>
    [
        // The empty slot's line to a folder's (the owner's case), and back; a game's line to "Not played yet"; a second line.
        ["Press A to make a folder here", "12 games", 42.0],
        ["12 games", "Press A to make a folder here", 42.0],
        ["8 h 40 min · 6 / 20 achievements", "Not played yet", 42.0],
        ["NetherSX2 · last played today", "Last played: Okami", 36.0],
        // The clock rolling a digit.
        ["6:39 PM", "6:40 PM", 92.0],
    ];

    [AvaloniaTheory]
    [MemberData(nameof(Changes))]
    public async Task ARoll_LeavesNoPixelBehind_AndEndsOnTheNewTextAlone(string from, string to, double size)
    {
        var old = await PictureOf(from, size);
        var next = await PictureOf(to, size);
        await AssertRollClean(old, next, $"\"{from}\" → \"{to}\"");
    }

    [AvaloniaFact]
    public async Task AMessageOnTwoLines_RollsOutWhole()
    {
        // A message on the line runs to two lines (M47 B10): its cells are as tall as both.
        var old = await PictureOf("Could not start NetherSX2: the emulator is not installed. Choose one in Settings → Emulators.", 42, width: 980, wrap: true);
        var next = await PictureOf("12 games", 42);
        Assert.True(old.Cells.Cells[0].Height > 1.5f * old.Cells.Cells[0].Em, "the message did not wrap");
        await AssertRollClean(old, next, "a two-line message → \"12 games\"");
    }

    private static Task AssertRollClean(Picture old, Picture next, string what)
    {
        // The text's own box: its pictures and every character's window (a wrapped line's trailing space reaches past its picture).
        var cells = old.Cells.Cells.Concat(next.Cells.Cells).ToList();
        var width = (int)Math.Ceiling(Math.Max(Math.Max(old.Width, next.Width), cells.Max(cell => cell.X + cell.Width)));
        var height = (int)Math.Ceiling(Math.Max(Math.Max(old.Height, next.Height), cells.Max(cell => cell.Y + cell.Height)));
        var plan = BigTypeMotion.RollPlan(old.Cells.Text, next.Cells.Text);
        var end = BigTypeMotion.RollMilliseconds(plan.Length);
        for (var since = 0f; since < end + 40f; since += 4f)
        {
            var pieces = BigTypeRollDraw.Pieces(old.Cells, next.Cells, 0f, 0f, next.Width, next.Height, since);
            if (pieces is null)
            {
                Assert.True(since >= end - 0.01f, $"{what}: the roll ended early, at {since} ms");
                continue;
            }
            // Every piece inside the text's own box.
            foreach (var piece in pieces)
            {
                Assert.True(piece.ClipX >= -0.5f && piece.ClipY >= -0.5f && piece.ClipX + piece.ClipWidth <= width + 0.5f &&
                    piece.ClipY + piece.ClipHeight <= height + 0.5f, $"{what}: a piece at {since} ms is cut outside the text's box ({piece})");
            }
            var (fromOld, fromNew) = Compose(old, next, pieces, width, height);
            Dump(what, since, fromOld, fromNew, width, height);
            for (var i = 0; i < plan.Length; i++)
            {
                var stagger = Math.Min(i * BigTypeMotion.DigitStagger, BigTypeMotion.DigitStaggerMax);
                var cell = i < old.Cells.Cells.Count ? old.Cells.Cells[i] : next.Cells.Cells[i];
                var x0 = (int)MathF.Floor(cell.X);
                var x1 = (int)MathF.Ceiling(cell.X + cell.Width);
                // A changed character's old glyph, once its time to leave is over: nothing of it.
                if (plan[i] == BigTypeRollCell.Changed && since >= stagger + BigTypeMotion.DigitOutMilliseconds)
                    AssertClear(fromOld, width, height, x0, x1, $"{what}: {since} ms, the old '{old.Cells.Cells[i].Character}' (cell {i}) left pixels behind");
                // An arriving glyph before it starts to rise: nothing of it yet.
                if (plan[i] is BigTypeRollCell.Changed or BigTypeRollCell.Arriving && since < stagger + BigTypeMotion.DigitInDelay)
                    AssertClear(fromNew, width, height, (int)MathF.Floor(next.Cells.Cells[i].X), (int)MathF.Ceiling(next.Cells.Cells[i].X + next.Cells.Cells[i].Width),
                        $"{what}: {since} ms, the new '{next.Cells.Cells[i].Character}' (cell {i}) shows before it rises");
            }
        }
        // After the roll the compositor draws the new picture as it is: nothing of the old remains.
        Assert.Null(BigTypeRollDraw.Pieces(old.Cells, next.Cells, 0f, 0f, next.Width, next.Height, end));
        return Task.CompletedTask;

        // Frames to look at, only on request (EMUSHELF_ROLL_FRAMES): the old text red, the new white, every 20 ms.
        static void Dump(string what, float since, byte[] fromOld, byte[] fromNew, int width, int height)
        {
            if (Environment.GetEnvironmentVariable("EMUSHELF_ROLL_FRAMES") is not { Length: > 0 } directory || since % 20f != 0f)
                return;
            Directory.CreateDirectory(directory);
            using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), Avalonia.Platform.PixelFormat.Rgba8888,
                Avalonia.Platform.AlphaFormat.Opaque);
            using (var buffer = bitmap.Lock())
            {
                var data = new byte[width * height * 4];
                for (var i = 0; i < width * height; i++)
                {
                    data[(i * 4) + 0] = (byte)Math.Max(fromOld[i], fromNew[i]);
                    data[(i * 4) + 1] = fromNew[i];
                    data[(i * 4) + 2] = fromNew[i];
                    data[(i * 4) + 3] = 255;
                }
                Marshal.Copy(data, 0, buffer.Address, data.Length);
            }
            var name = new string(what.Where(char.IsLetterOrDigit).Take(24).ToArray());
            bitmap.Save(Path.Combine(directory, $"{name}-{since:000}.png"));
        }

        static void AssertClear(byte[] alpha, int width, int height, int x0, int x1, string message)
        {
            var seen = 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = Math.Max(0, x0); x < Math.Min(width, x1); x++)
                {
                    if (alpha[(y * width) + x] > 8)
                        seen++;
                }
            }
            Assert.True(seen == 0, $"{message} ({seen} pixels)");
        }
    }
}
