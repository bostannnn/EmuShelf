using System.Text.RegularExpressions;

namespace EmuShelf.App.Tests;

/// <summary>
/// No <c>Border</c> both casts a shadow and holds content.
/// </summary>
/// <remarks>
/// <para>
/// On Avalonia 12.1.0 a <c>Border</c> that carries a <c>BoxShadow</c> and contains content draws that
/// content offset from where it is laid out. It cost days: the couch menu's selector cards flew out
/// of the panel while their row had focus, and cover artwork sat outside its own tile — the two
/// symptoms looked unrelated and neither was a layout fault. Arranged geometry is correct, so every
/// assertion about position passes; <c>RenderTargetBitmap.Render(window)</c> is correct too, so the
/// window's own idea of what it is drawing looks fine. It only goes wrong through the compositor,
/// which means no runtime test in this repo can see it.
/// </para>
/// <para>
/// That is exactly why this is a source check rather than a rendering one. The construct is
/// invisible to everything else we have, so the only durable defence is refusing to let it back in.
/// The fix is always the same: cast the shadow from an empty sibling Border behind the content.
/// </para>
/// </remarks>
public class BoxShadowContainerTests
{
    /// <summary>
    /// The one known-safe exception: a shadowed Border with no border thickness and no padding.
    /// </summary>
    /// <remarks>
    /// Every case that misbehaved had a non-zero <c>BorderThickness</c> or <c>Padding</c> as well as
    /// the shadow, which matches the shape of Avalonia issue 18263 on the neighbouring <c>Effect</c>
    /// path. The sidebar's accent icon has neither and has never been reported wrong, so it stays —
    /// but it is listed here deliberately, so that if it ever does misbehave this is the first place
    /// anyone looks.
    /// </remarks>
    private static readonly string[] KnownSafe =
    [
        "Sidebar accent icon: 34x34 rounded square, no thickness or padding",
    ];

    [Fact]
    public void NoBorderCastsAShadowOverItsOwnContent()
    {
        var markup = ReadMainWindowMarkup();
        var offenders = new List<string>();

        foreach (Match match in Regex.Matches(markup, @"<Border\b((?:[^>])*?)(/?)>", RegexOptions.Singleline))
        {
            var attributes = match.Groups[1].Value;
            var selfClosing = match.Groups[2].Value == "/";
            if (selfClosing || !attributes.Contains("BoxShadow", StringComparison.Ordinal))
            {
                continue;
            }

            var line = markup[..match.Index].Count(character => character == '\n') + 1;
            var thickness = Attribute(attributes, "BorderThickness");
            var padding = Attribute(attributes, "Padding");

            if (!HasNonZero(thickness) && !HasNonZero(padding))
            {
                // The known-safe shape. Allowed, and documented above.
                continue;
            }

            offenders.Add(
                $"MainWindow.axaml line {line}: BoxShadow on a Border with content "
                + $"(BorderThickness=\"{thickness ?? "0"}\", Padding=\"{padding ?? "0"}\")");
        }

        Assert.True(
            offenders.Count == 0,
            "A Border must not cast a BoxShadow over its own content — on Avalonia 12 that content "
            + "is drawn offset from where it is laid out. Move the shadow to an empty sibling Border "
            + "behind it.\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>Styles must not reintroduce it either, which is how the menu's rows acquired one.</summary>
    [Fact]
    public void NoStyleGivesAShadowToAClassUsedAsAContainer()
    {
        var markup = ReadMainWindowMarkup();
        var offenders = new List<string>();

        foreach (Match style in Regex.Matches(markup, @"<Style Selector=""([^""]*)"">((?:.|\n)*?)</Style>"))
        {
            var selector = style.Groups[1].Value;
            var body = style.Groups[2].Value;
            var setter = Regex.Match(body, @"<Setter\s+Property=""BoxShadow""\s+Value=""([^""]*)""");
            if (!setter.Success || setter.Groups[1].Value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // The class the shadow lands on, e.g. "Border.cover-card-shadow" -> "cover-card-shadow".
            foreach (var target in Regex.Matches(selector, @"Border\.([A-Za-z0-9-]+)")
                         .Select(match => match.Groups[1].Value)
                         .Distinct())
            {
                if (UsedWithContent(markup, target))
                {
                    var line = markup[..style.Index].Count(character => character == '\n') + 1;
                    offenders.Add(
                        $"MainWindow.axaml line {line}: style '{selector}' puts a BoxShadow on "
                        + $"'{target}', which is used as a container somewhere in the file");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A style must not give a BoxShadow to a class that is used as a container.\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheKnownSafeExceptionIsStillDocumented() => Assert.NotEmpty(KnownSafe);

    private static bool UsedWithContent(string markup, string className)
    {
        foreach (Match match in Regex.Matches(markup, @"<Border\b((?:[^>])*?)(/?)>", RegexOptions.Singleline))
        {
            var attributes = match.Groups[1].Value;
            var classes = Attribute(attributes, "Classes");
            if (classes is null || match.Groups[2].Value == "/")
            {
                continue;
            }

            if (classes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(className))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Attribute(string attributes, string name) =>
        Regex.Match(attributes, name + @"=""([^""]*)""") is { Success: true } match
            ? match.Groups[1].Value
            : null;

    private static bool HasNonZero(string? value) =>
        value is not null && value.Any(character => char.IsDigit(character) && character != '0');

    private static string ReadMainWindowMarkup()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var path = Path.Combine(directory!.FullName, "src", "EmuShelf.App", "Views", "MainWindow.axaml");
        Assert.True(File.Exists(path), $"Could not find MainWindow.axaml (looked at {path}).");
        return File.ReadAllText(path);
    }
}
