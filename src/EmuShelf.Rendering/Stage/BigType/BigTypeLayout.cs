namespace EmuShelf.Rendering.Stage.BigType;

/// <summary>A word measured once at a reference size: its width, and where each of its letters starts.</summary>
public sealed record BigTypeWord(string Text, float Width, IReadOnlyList<float> LetterX);

/// <summary>A word placed by the fit: which line it is on and where it starts, at the fitted size.</summary>
public readonly record struct BigTypePlacedWord(int Word, int Line, float X);

/// <summary>The fitted name: its size, its line count and every word's place (in canvas pixels from the box's left).</summary>
public sealed record BigTypeFit(float Size, int Lines, IReadOnlyList<BigTypePlacedWord> Words);

/// <summary>
/// Fits a name into the Stage's box the way the mock does (<c>fitWord</c>): words never break, lines wrap greedily, and
/// the size is the largest that keeps every word within the width, at most <c>maxLines</c> lines, and the lines (at
/// line height 0.9) within the height. Widths scale with the size, so the words are measured once.
/// </summary>
public static class BigTypeLayout
{
    public const float LineHeight = 0.9f;

    public static BigTypeFit Fit(IReadOnlyList<BigTypeWord> words, float spaceWidth, float referenceSize, float boxWidth, float boxHeight,
        int maxLines = 3, float minSize = 56f, float maxSize = 840f)
    {
        if (words.Count == 0)
            return new BigTypeFit(minSize, 0, []);
        float lo = minSize, hi = maxSize;
        for (var i = 0; i < 14; i++)
        {
            var mid = (lo + hi) / 2f;
            if (Fits(words, spaceWidth, referenceSize, mid, boxWidth, boxHeight, maxLines))
                lo = mid;
            else
                hi = mid;
        }
        var size = MathF.Floor(lo);
        var placed = Wrap(words, spaceWidth * size / referenceSize, size / referenceSize, boxWidth);
        return new BigTypeFit(size, placed.Count == 0 ? 0 : placed[^1].Line + 1, placed);
    }

    private static bool Fits(IReadOnlyList<BigTypeWord> words, float spaceWidth, float referenceSize, float size, float boxWidth,
        float boxHeight, int maxLines)
    {
        var scale = size / referenceSize;
        if (words.Any(word => word.Width * scale > boxWidth + 0.5f))
            return false;
        var placed = Wrap(words, spaceWidth * scale, scale, boxWidth);
        var lines = placed[^1].Line + 1;
        return lines <= maxLines && lines * LineHeight * size <= boxHeight + 0.5f;
    }

    private static List<BigTypePlacedWord> Wrap(IReadOnlyList<BigTypeWord> words, float space, float scale, float boxWidth)
    {
        var placed = new List<BigTypePlacedWord>(words.Count);
        var line = 0;
        var x = 0f;
        for (var i = 0; i < words.Count; i++)
        {
            var width = words[i].Width * scale;
            if (i > 0)
            {
                if (x + space + width > boxWidth + 0.5f)
                {
                    line++;
                    x = 0f;
                }
                else
                {
                    x += space;
                }
            }
            placed.Add(new BigTypePlacedWord(i, line, x));
            x += width;
        }
        return placed;
    }
}
