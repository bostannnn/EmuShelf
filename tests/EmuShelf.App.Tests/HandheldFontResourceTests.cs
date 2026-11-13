using System.Xml.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace EmuShelf.App.Tests;

/// <summary>
/// M47 R3: the Handheld face (M PLUS Rounded 1c, bundled whole) is declared in App.axaml as
/// <c>EmuHandheldFontFamily</c> and resolves to the bundled files, not to a fallback face.
/// </summary>
public class HandheldFontResourceTests
{
    private const string FaceName = "Rounded Mplus 1c";

    private static FontFamily LoadHandheldFamily()
    {
        // App.axaml's dictionary is filled by its compiled Initialize(), which also attaches the Debug
        // dev tools, and the test host runs its own TestApp. So read the declaration from App.axaml in
        // the source tree and parse it the way the XAML compiler does (FontFamily's string form).
        var appXaml = Path.Combine(RepositoryRoot(), "src", "EmuShelf.UI", "App.axaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var declared = XDocument.Load(appXaml).Descendants()
            .Single(element => (string?)element.Attribute(x + "Key") == "EmuHandheldFontFamily");
        Assert.Equal("FontFamily", declared.Name.LocalName);
        return FontFamily.Parse(declared.Value.Trim());
    }

    // Walks up from the test binaries to the directory holding the solution.
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EmuShelf.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static IReadOnlyList<ShapedTextRun> Shape(string text, Typeface typeface) =>
        new TextLayout(text, typeface, 48, Brushes.White)
            .TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>().ToList();

    [AvaloniaTheory]
    [InlineData(500)]
    [InlineData(700)]
    [InlineData(900)]
    public void HandheldFamily_ResolvesEachBundledWeight(int weight)
    {
        var family = LoadHandheldFamily();
        var glyphTypeface = new Typeface(family, FontStyle.Normal, (FontWeight)weight).GlyphTypeface;

        Assert.StartsWith(FaceName, glyphTypeface.FamilyName, StringComparison.Ordinal);
        Assert.Equal((FontWeight)weight, glyphTypeface.Weight);
    }

    [AvaloniaTheory]
    [InlineData("Dragon Quest VIII · Ōkami …")]
    [InlineData("0123456789 1:23 4/16")]
    [InlineData("ドラゴンクエスト")] // kana are in the whole font, so a Japanese title stays in this face
    public void LatinDigitsAndKana_ShapeWithTheHandheldFace_NotTheDefault(string text)
    {
        var family = LoadHandheldFamily();
        var handheld = new Typeface(family, FontStyle.Normal, FontWeight.Bold);
        var fallback = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

        var runs = Shape(text, handheld);
        Assert.NotEmpty(runs);
        Assert.All(runs, run =>
        {
            Assert.Same(handheld.GlyphTypeface, run.GlyphRun.GlyphTypeface);
            Assert.StartsWith(FaceName, run.GlyphRun.GlyphTypeface.FamilyName, StringComparison.Ordinal);
            Assert.DoesNotContain(run.GlyphRun.GlyphInfos, glyph => glyph.GlyphIndex == 0);
        });

        // And the default face is something else, so the assertions above are not passing by accident.
        Assert.All(Shape(text, fallback), run =>
            Assert.DoesNotContain(FaceName, run.GlyphRun.GlyphTypeface.FamilyName, StringComparison.Ordinal));
    }
}
