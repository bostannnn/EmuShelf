using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Media;
using EmuShelf.App.ViewModels;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

public sealed partial class HandheldBigTypeName
{
    private static readonly HashSet<string> EnglishLeadWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "of", "to", "in", "on", "at", "by", "for", "from", "with", "and", "or", "vs.",
    };

    // Library titles have no language tag. Honour explicitly nonbreaking spaces in any language;
    // for ordinary spacing use the UI language's punctuation convention.
    internal static BigTypeName FitGameTitle(string text, Typeface face, double width, double height, string language)
    {
        var english = language is "en" or "iv" || Regex.IsMatch(text, @"\b(the|with|from|and)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var display = Regex.Replace(text, @"[ \t]+(?=[,.:;!?%…)\]}»”’。，！？])", match =>
        {
            var mark = text[match.Index + match.Length];
            if (!english && language == "fr" && ":;!?%»".Contains(mark))
                return ";!?".Contains(mark) ? "\u202f" : "\u00a0";
            return !english && mark == '%' ? "\u00a0" : string.Empty;
        });
        string[] Phrases(string part)
        {
            var words = part.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var groups = new List<string>(words.Length);
            for (var i = 0; i < words.Length; i++)
            {
                var start = i;
                while (english && i + 1 < words.Length && EnglishLeadWords.Contains(words[i]))
                    i++;
                groups.Add(string.Join(" ", words, start, i - start + 1));
            }
            return groups.ToArray();
        }

        var separator = display.IndexOf(": ", StringComparison.Ordinal);
        if (separator < 1 || string.IsNullOrWhiteSpace(display[(separator + 1)..]))
            return FitParts(text, Phrases(display), face, width, height, HandheldTokens.StageNameMaxLines, 1, HandheldTokens.StageNameMaxSize);

        var main = display[..(separator + 1)];
        var subtitle = display[(separator + 1)..].Trim();
        var mainPhrases = Phrases(main);
        var subPhrases = Phrases(subtitle);
        var headingBox = height * 0.59;
        var heading = FitParts(main, mainPhrases, face, width, headingBox, 1, 1, HandheldTokens.StageNameMaxSize);
        // A long main title can take a second line. Leave at least one line for its subtitle.
        const double singleLineFloor = 120;
        if (heading.Size < singleLineFloor && mainPhrases.Length > 1)
        {
            headingBox = height * 0.64;
            heading = FitParts(main, mainPhrases, face, width, headingBox, 2, 1, HandheldTokens.StageNameMaxSize);
        }
        var headingHeight = heading.Lines * heading.Size * BigTypeLayout.LineHeight;
        var subtitleHeight = height - headingHeight;
        var subtitleMaxSize = Math.Max(1, heading.Size * 0.78);
        var sub = FitParts(subtitle, subPhrases, face, width, subtitleHeight, 1, 1, subtitleMaxSize);
        if (heading.Lines == 1 && sub.Size < Math.Min(singleLineFloor, subtitleMaxSize * 0.9))
            sub = FitParts(subtitle, subPhrases, face, width, subtitleHeight, 2, 1, subtitleMaxSize);

        var subHeight = sub.Lines * sub.Size * BigTypeLayout.LineHeight;
        // Both fits are bottom-anchored. Stack them as a single bottom-anchored title.
        var headingShift = height - subHeight - headingBox;
        var placed = heading.Words.Select(word => word with { Top = word.Top + headingShift })
            .Concat(sub.Words.Select(word => word with { Top = word.Top + headingHeight, Line = word.Line + heading.Lines }))
            .ToList();
        return new BigTypeName(text, face, heading.Size, heading.Lines + sub.Lines, placed);
    }
}
