using System;
using System.Collections.Generic;
using System.Linq;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// The Big Type keyboard's keys (M47 B9, the mock's <c>buildKeys</c> and <c>mkKey</c>): three rows of letters, each centred on the
/// widest (which puts the mock's rows at its own indents), then Space, Delete and Done. Every keyboard the app has takes these places
/// (naming a folder, Search, Edit title, Settings' text rows, the scrapers' fields). What the app types beyond the mock's letters is on
/// two pages of characters behind the key at the end of the third row (and RB); a keyboard whose case is its own (not naming or Search)
/// has Shift at the start of that row (and LB). Places are on the 1240 × 1080 Launcher.
/// </summary>
public static class HandheldBigTypeKeys
{
    /// <summary>The mock's letters (<c>KROWS</c>).</summary>
    public static IReadOnlyList<string> Letters { get; } = ["QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM"];

    /// <summary>
    /// The app's characters beyond the letters, two pages of rows (the third row of each at most as long as the letters' third, so the
    /// row's edge keys keep their places): every character today's keyboards type (digits, the naming keyboard's <c>' - &amp;</c>, the
    /// text keyboard's symbols).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> Pages { get; } =
    [
        Letters,
        ["1234567890", "-_.@/:'&!?", ",;\"()#+"],
        ["$%^*=[]{}\\", "|<>"],
    ];

    /// <summary>The page key's label on each page: the page it goes to.</summary>
    public static string PageKeyLabel(int page) => ((page + 1) % Pages.Count) switch
    {
        0 => "ABC",
        1 => "123",
        _ => "#+=",
    };

    /// <summary>The longest third row (the letters'): the edge keys sit either side of it.</summary>
    private static int ThirdRowKeys => Letters[2].Length;

    /// <summary>
    /// A page's rows of keys, placed: the characters (shown as <paramref name="label"/> gives them), Shift first in the third row when
    /// <paramref name="shiftKey"/>, the page key last in it, then Space, Delete and Done.
    /// </summary>
    public static List<HandheldKeyRowViewModel> Build(int page, bool shiftKey, string shiftLabel, Func<char, string> label)
    {
        var rows = new List<HandheldKeyRowViewModel>();
        var characters = Pages[Math.Clamp(page, 0, Pages.Count - 1)];
        for (var r = 0; r < 3; r++)
        {
            var text = r < characters.Count ? characters[r] : string.Empty;
            var keys = text.Select(ch => new HandheldKeyViewModel(label(ch), HandheldKeyKind.Character) { Value = ch.ToString() }).ToList();
            PlaceCentred(keys, r);
            if (r == 2)
            {
                var edge = EdgeWidth;
                var y = RowTop(r);
                if (shiftKey)
                    keys.Insert(0, new HandheldKeyViewModel(shiftLabel, HandheldKeyKind.Shift) { X = HandheldTokens.KeyboardLeft, Y = y, Width = edge });
                keys.Add(new HandheldKeyViewModel(PageKeyLabel(page), HandheldKeyKind.Symbols)
                {
                    X = HandheldTokens.KeyboardLeft + HandheldTokens.KeyRowWidth - edge,
                    Y = y,
                    Width = edge,
                });
            }
            rows.Add(new HandheldKeyRowViewModel(keys));
        }
        var bottom = RowTop(3);
        var gap = HandheldTokens.KeyGap;
        var left = HandheldTokens.KeyboardLeft;
        rows.Add(new HandheldKeyRowViewModel(
        [
            new HandheldKeyViewModel("Space", HandheldKeyKind.Space) { Value = " ", X = left, Y = bottom, Width = HandheldTokens.KeySpaceWidth },
            new HandheldKeyViewModel("Delete", HandheldKeyKind.Delete)
            {
                X = left + HandheldTokens.KeySpaceWidth + gap, Y = bottom, Width = HandheldTokens.KeyActionWidth,
            },
            new HandheldKeyViewModel("Done", HandheldKeyKind.Done)
            {
                X = left + HandheldTokens.KeySpaceWidth + HandheldTokens.KeyActionWidth + (2 * gap), Y = bottom, Width = HandheldTokens.KeyActionWidth,
            },
        ]));
        return rows;
    }

    /// <summary>The edge keys' width: what is left beside the letters' third row.</summary>
    public static double EdgeWidth =>
        ((HandheldTokens.KeyRowWidth - ((ThirdRowKeys * HandheldTokens.KeyPitch) - HandheldTokens.KeyGap)) / 2) - HandheldTokens.KeyGap;

    /// <summary>Row <paramref name="row"/>'s top (0 to 2 the characters, 3 Space, Delete and Done; −1 the row above, a rename's colours).</summary>
    public static double RowTop(int row) => HandheldTokens.KeyboardTop + (row * HandheldTokens.KeyPitch);

    /// <summary>Where the first of <paramref name="count"/> keys goes in a row centred on the widest.</summary>
    public static double CentredLeft(int count) =>
        HandheldTokens.KeyboardLeft + ((HandheldTokens.KeyRowWidth - ((count * HandheldTokens.KeyPitch) - HandheldTokens.KeyGap)) / 2);

    private static void PlaceCentred(IList<HandheldKeyViewModel> keys, int row)
    {
        var left = CentredLeft(keys.Count);
        for (var i = 0; i < keys.Count; i++)
        {
            keys[i].X = left + (i * HandheldTokens.KeyPitch);
            keys[i].Y = RowTop(row);
            keys[i].Width = HandheldTokens.KeySide;
        }
    }

    /// <summary>
    /// The key in <paramref name="row"/> nearest across to <paramref name="centreX"/> (the mock's <c>nearest</c>: the first of equals).
    /// </summary>
    public static int Nearest(IReadOnlyList<HandheldKeyViewModel> row, double centreX)
    {
        var best = 0;
        for (var i = 1; i < row.Count; i++)
        {
            if (Math.Abs(row[i].CentreX - centreX) < Math.Abs(row[best].CentreX - centreX))
                best = i;
        }
        return best;
    }

    /// <summary>
    /// The mock's typing rule (naming and Search): a capital at the start and after a space, lower case otherwise; no space at the start
    /// and never two together; nothing past <paramref name="max"/>. Null when the key may not type here (the keyboard bumps).
    /// </summary>
    public static string? Typed(string text, string key, int max)
    {
        if (text.Length >= max)
            return null;
        if (key == " ")
            return text.Length == 0 || text.EndsWith(' ') ? null : text + " ";
        var capital = text.Length == 0 || text.EndsWith(' ');
        return text + (capital ? key.ToUpperInvariant() : key.ToLowerInvariant());
    }
}
