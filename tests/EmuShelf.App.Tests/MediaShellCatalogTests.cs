using EmuShelf.Rendering.Shells;

namespace EmuShelf.App.Tests;

/// <summary>
/// Holds each authored shell to the real object's proportions.
/// </summary>
/// <remarks>
/// The three models were authored lying in three different directions, and each carries its own
/// orientation matrix to stand it up. Getting one wrong fails nothing — the shell loads, lights and
/// renders perfectly happily, just turned on its side — so the only other detector is somebody
/// noticing that a cartridge looks a bit wide.
/// <para>
/// The reference dimensions below are the load-bearing part, and they are easy to get backwards: a
/// Game Boy Advance Game Pak is <em>landscape</em>, wider than it is tall, unlike almost every other
/// cartridge. Writing them transposed produces a test that fails against correct code and argues
/// convincingly for breaking it. If one of these ever fails, check the real object's proportions
/// before touching an orientation matrix — and note that the model itself carries the answer: the
/// long axis of its label quad is its width, and its contact fingers run along its bottom edge.
/// </para>
/// </remarks>
public class MediaShellCatalogTests
{
    // Width x height of the real article, in millimetres.
    [Theory]
    [InlineData(MediaShell.SnesCartridge, 129d, 87d)]
    // Landscape: a Game Pak is ~57mm across and ~35mm tall. Not a typo.
    [InlineData(MediaShell.GbaCartridge, 57d, 35d)]
    // Portrait, and the same 57 x 65mm shell for Game Boy and Game Boy Color alike — the near
    // reciprocal of the Game Pak above, which is what a swapped source file would look like.
    [InlineData(MediaShell.GbcCartridge, 57d, 65d)]
    [InlineData(MediaShell.DiscKeepCase, 135d, 190d)]
    public void ShellStandsUpAtTheRealObjectsProportions(MediaShell shell, double width, double height)
    {
        var size = MediaShellCatalog.Load(shell).Size;

        // Canonical space is one unit tall by construction.
        Assert.Equal(1d, size.Y, 3);

        var expected = width / height;
        var actual = size.X / size.Y;

        // Generous: these are scans, not CAD, and the moulded lip on a cartridge legitimately
        // overhangs its nominal footprint. Tight enough that a 90-degree error cannot pass — the
        // failure mode is always a ratio and its reciprocal.
        Assert.True(
            Math.Abs(actual - expected) < 0.20d,
            $"{shell} loads at width/height {actual:F3}; the real object is {expected:F3}. "
            + $"A ratio near {1d / expected:F3} means its orientation matrix is a quarter-turn out.");
    }

    [Theory]
    [InlineData(MediaShell.SnesCartridge, 20d, 87d)]
    [InlineData(MediaShell.GbaCartridge, 8d, 35d)]
    [InlineData(MediaShell.GbcCartridge, 8d, 65d)]
    [InlineData(MediaShell.DiscKeepCase, 14d, 190d)]
    public void ShellIsAsThickAsTheRealObject(MediaShell shell, double depth, double height)
    {
        var size = MediaShellCatalog.Load(shell).Size;

        // Depth catches the axis swap that width alone can miss: a cartridge turned face-down keeps
        // a plausible width/height and becomes absurdly thick.
        Assert.True(
            Math.Abs((size.Z / size.Y) - (depth / height)) < 0.12d,
            $"{shell} loads {size.Z / size.Y:F3} deep per unit of height; the real object is "
            + $"{depth / height:F3}.");
    }

    [Fact]
    public void CoverPanelSitsOnTheFrontFace()
    {
        foreach (var shell in MediaShellCatalog.All)
        {
            var definition = MediaShellCatalog.Definition(shell);

            // The renderer only ever shows the cover on the face pointing at the player.
            Assert.Equal(ArtFace.Front, definition.CoverPanel.Face);
            Assert.True(definition.CoverPanel.MinU < definition.CoverPanel.MaxU, $"{shell} u");
            Assert.True(definition.CoverPanel.MinV < definition.CoverPanel.MaxV, $"{shell} v");
        }
    }

    [Fact]
    public void SnesBodyUsesARestrainedPlasticCalibration()
    {
        var snes = MediaShellCatalog.Definition(MediaShell.SnesCartridge);

        Assert.InRange(snes.BodyRoughnessScale, 1.05f, 1.20f);
        Assert.InRange(snes.DielectricReflectance, 0.03f, 0.04f);
        Assert.InRange(snes.AmbientIntensity, 0.60f, 0.80f);
        Assert.InRange(snes.ShadowFillOcclusion, 0.45f, 0.70f);
        Assert.InRange(snes.CavityStrength, 0.20f, 0.40f);
        Assert.InRange(snes.NormalStrength, 0.60f, 0.85f);

        // The correction is specific to the sourced SNES scan, not a global dimming of all media.
        Assert.Equal(1f, MediaShellCatalog.Definition(MediaShell.GbaCartridge).BodyRoughnessScale);
        Assert.Equal(1f, MediaShellCatalog.Definition(MediaShell.DiscKeepCase).BodyRoughnessScale);
    }
}
