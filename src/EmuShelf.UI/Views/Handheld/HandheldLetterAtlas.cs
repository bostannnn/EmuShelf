using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// Letters as the native screens draw them (M47 B6, R1's route; shared since B8 by the Stage's name, a folder's heading on the native
/// Launcher and the launch title): each distinct letter rasterised once, white, into an ink-tight cell of one atlas, and every letter
/// placed where its control draws it at rest. Runs on the UI thread (it renders a bitmap).
/// </summary>
internal static class HandheldLetterAtlas
{
    /// <summary>One letter as its control draws it: its text (a grapheme), where its layout is drawn, its order (spaces not counted).</summary>
    public readonly record struct Placed(string Text, Point Origin, int Order, double? Size = null);

    // Atlas cells keep a margin round each letter's ink, so its soft edge is not cut.
    private const int CellPad = 2;

    // The atlas wraps to a new row past this width (every GLES 3 device takes 2048).
    private const int AtlasMaxWidth = 2048;

    /// <summary>The atlas for <paramref name="placed"/>, the control's top left at <paramref name="origin"/> on its canvas; null with nothing to draw.</summary>
    public static BigTypeLetters? Build(string text, IReadOnlyList<Placed> placed, Typeface typeface, double size, Point origin, float opacity = 1f)
    {
        var glyphs = new Dictionary<(string Text, double Size), (FormattedText Text, Rect Ink, int CellX, int CellY, int CellWidth, int CellHeight)>();
        int x = 0, y = 0, rowHeight = 0, atlasWidth = 0;
        foreach (var letter in placed)
        {
            var key = (letter.Text, Size: letter.Size ?? size);
            if (glyphs.ContainsKey(key))
                continue;
            var formatted = new FormattedText(letter.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, key.Size, Brushes.White);
            var ink = formatted.BuildGeometry(default)?.Bounds ?? default;
            if (ink.Width <= 0 || ink.Height <= 0)
                continue;
            var cellWidth = (int)Math.Ceiling(ink.Width) + (2 * CellPad);
            var cellHeight = (int)Math.Ceiling(ink.Height) + (2 * CellPad);
            if (x > 0 && x + cellWidth > AtlasMaxWidth)
            {
                x = 0;
                y += rowHeight;
                rowHeight = 0;
            }
            glyphs[key] = (formatted, ink, x, y, cellWidth, cellHeight);
            x += cellWidth;
            atlasWidth = Math.Max(atlasWidth, x);
            rowHeight = Math.Max(rowHeight, cellHeight);
        }
        var atlasHeight = y + rowHeight;
        if (glyphs.Count == 0 || atlasWidth == 0 || atlasHeight == 0)
            return null;
        using var bitmap = new RenderTargetBitmap(new PixelSize(atlasWidth, atlasHeight));
        using (var context = bitmap.CreateDrawingContext())
        // Grey-scale antialiasing: a picture for another surface must not carry sub-pixel colour (B4's black boxes on Android).
        using (context.PushRenderOptions(new RenderOptions { TextRenderingMode = TextRenderingMode.Antialias }))
        {
            foreach (var glyph in glyphs.Values)
                context.DrawText(glyph.Text, new Point(glyph.CellX + CellPad - glyph.Ink.X, glyph.CellY + CellPad - glyph.Ink.Y));
        }
        var letters = new List<BigTypeLetter>(placed.Count);
        foreach (var letter in placed)
        {
            if (!glyphs.TryGetValue((letter.Text, letter.Size ?? size), out var glyph))
                continue;
            letters.Add(new BigTypeLetter(
                (float)(origin.X + letter.Origin.X + glyph.Ink.X - CellPad), (float)(origin.Y + letter.Origin.Y + glyph.Ink.Y - CellPad),
                glyph.CellWidth, glyph.CellHeight, glyph.CellX, glyph.CellY, letter.Order));
        }
        return new BigTypeLetters(text, HandheldStageShots.Pixels(bitmap), letters, (float)size, opacity);
    }
}
