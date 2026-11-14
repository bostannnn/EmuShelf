using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// Where each character of a pictured piece's text sits on the canvas (M47 B5), so the native screens can roll the characters that
/// change between two pictures of it. Every visible one-line <see cref="TextBlock"/> in the piece, in order; each character's window
/// is its advance across and its text block's height down.
/// </summary>
internal static class HandheldTextCells
{
    public static StageText? Of(Visual piece, Visual canvas)
    {
        var cells = new List<StageTextCell>();
        IEnumerable<TextBlock> blocks = piece is TextBlock own ? [own] : piece.GetVisualDescendants().OfType<TextBlock>();
        foreach (var block in blocks)
        {
            if (!block.IsEffectivelyVisible || string.IsNullOrEmpty(block.Text) || block.Bounds.Height < 1)
                continue;
            var text = block.Text;
            var layout = block.TextLayout;
            for (var i = 0; i < text.Length; i++)
            {
                try
                {
                    _ = layout.HitTestTextRange(i, 1);
                }
                catch (System.Exception)
                {
                    return null; // a text the layout cannot measure character by character is simply swapped
                }
                var rect = layout.HitTestTextRange(i, 1).FirstOrDefault();
                var x = rect.X + block.Padding.Left;
                if (HandheldStageShots.ToCanvas(block, new Point(x, 0), canvas) is not { } at ||
                    HandheldStageShots.ToCanvas(block, new Point(x + rect.Width, block.Bounds.Height), canvas) is not { } end)
                    return null;
                var scale = (end.Y - at.Y) / block.Bounds.Height;
                cells.Add(new StageTextCell(text[i], (float)at.X, (float)at.Y, (float)(end.X - at.X), (float)(end.Y - at.Y), (float)(block.FontSize * scale)));
            }
        }
        return cells.Count == 0 ? null : new StageText(cells);
    }
}
