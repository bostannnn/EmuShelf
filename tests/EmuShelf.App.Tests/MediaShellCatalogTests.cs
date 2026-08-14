using System.Numerics;
using EmuShelf.Rendering.Models;
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

    /// <summary>
    /// The GBA label must stay on the shell rather than reaching the board behind its pins.
    /// </summary>
    /// <remarks>
    /// A panel is projected in object space onto anything front-facing inside its rectangle, and a
    /// Game Pak's board and the inside of its back wall qualify: they face the player through the
    /// pin opening. Printed, they put a band of cover art straight across the contacts — invisible
    /// at the resting pose and obvious as soon as the hero is pitched toward the player, which is
    /// how it shipped. The depth allowance is what excludes them, and this pins both of its edges.
    /// </remarks>
    [Fact]
    public void GbaLabelSkipsTheBoardBehindTheContactPins()
    {
        var allowance = DepthAllowance(MediaShell.GbaCartridge);
        var surfaces = CoverPanelSurfaces(MediaShell.GbaCartridge);
        var printed = surfaces.Where(surface => surface.Depth <= allowance).ToList();
        var skipped = surfaces.Where(surface => surface.Depth > allowance).ToList();

        // The interior really is inside the rectangle, and carries more area than the label itself,
        // so a regression here would not be subtle.
        Assert.True(
            skipped.Sum(surface => surface.Area) > printed.Sum(surface => surface.Area),
            "The GBA's interior surfaces no longer fall inside the label rectangle; if the model or "
            + "the panel changed, this test is no longer guarding anything.");

        // Both edges of the allowance, so it cannot drift into either surface.
        Assert.True(
            printed.Max(surface => surface.Depth) < allowance * 0.85f,
            $"The label recess sits {printed.Max(surface => surface.Depth):F4} behind the panel "
            + $"plane, too close to the {allowance:F4} allowance to survive a re-export.");
        Assert.True(
            skipped.Min(surface => surface.Depth) > allowance * 1.5f,
            $"The nearest excluded surface is only {skipped.Min(surface => surface.Depth):F4} "
            + $"behind the panel plane against a {allowance:F4} allowance.");
    }

    /// <summary>
    /// The sibling of the test above: an allowance tightened far enough to clip a shell's own
    /// printed face would fix the GBA by deleting every label.
    /// </summary>
    [Fact]
    public void EveryShellStillPrintsItsWholeCoverPanel()
    {
        foreach (var shell in MediaShellCatalog.All)
        {
            var allowance = DepthAllowance(shell);
            var placement = MediaShellCatalog.Place(
                MediaShellCatalog.Definition(shell).CoverPanel, MediaShellCatalog.Load(shell));
            var panelArea = placement.UEdge.Length() * placement.VEdge.Length();
            var printedArea = CoverPanelSurfaces(shell)
                .Where(surface => surface.Depth <= allowance)
                .Sum(surface => surface.Area);

            Assert.True(
                printedArea > panelArea * 0.8f,
                $"{shell} prints only {printedArea:F3} of its {panelArea:F3} cover panel; its "
                + $"depth allowance of {allowance:F4} is clipping the face the label sits on.");
        }
    }

    private static float DepthAllowance(MediaShell shell)
    {
        var definition = MediaShellCatalog.Definition(shell);
        var asset = MediaShellCatalog.Load(shell);
        var normal = MediaShellCatalog.Place(definition.CoverPanel, asset).Normal;
        return definition.PanelDepthFraction * MathF.Abs(Vector3.Dot(asset.Size, normal));
    }

    /// <summary>
    /// The shell's triangles that the cover panel projects onto, each with how far behind the
    /// panel's plane it lies and how much of the panel it covers.
    /// </summary>
    /// <remarks>
    /// A per-triangle approximation of what the fragment shader does per fragment, and it is
    /// deliberately the shader's own inputs rather than the winding: the facing test reads the
    /// authored vertex normals, because that is what <c>vObjectNormal</c> interpolates, and these
    /// models are exported double-sided with winding that does not always agree. It stays an
    /// approximation in two ways — a triangle counts as inside the rectangle when its centroid is,
    /// and the shader tests a normal interpolated across the face rather than one flat value. Both
    /// only matter at a panel's boundary, and neither is load-bearing for the margins asserted here.
    /// </remarks>
    private static List<(float Depth, float Area)> CoverPanelSurfaces(MediaShell shell)
    {
        var definition = MediaShellCatalog.Definition(shell);
        var asset = MediaShellCatalog.Load(shell);
        var placement = MediaShellCatalog.Place(definition.CoverPanel, asset);
        var surfaces = new List<(float Depth, float Area)>();

        foreach (var mesh in asset.Meshes)
        {
            for (var index = 0; index + 2 < mesh.Indices.Length; index += 3)
            {
                var (a, aNormal) = VertexAt(mesh, mesh.Indices[index]);
                var (b, bNormal) = VertexAt(mesh, mesh.Indices[index + 1]);
                var (c, cNormal) = VertexAt(mesh, mesh.Indices[index + 2]);

                var normal = aNormal + bNormal + cNormal;
                if (normal.LengthSquared() <= 1e-12f)
                {
                    continue;
                }

                var facing = Vector3.Dot(Vector3.Normalize(normal), placement.Normal);
                if (facing < 0.5f)
                {
                    continue;
                }

                var local = ((a + b + c) / 3f) - placement.Origin;
                var u = Vector3.Dot(local, placement.UEdge) / placement.UEdge.LengthSquared();
                var v = Vector3.Dot(local, placement.VEdge) / placement.VEdge.LengthSquared();
                if (u is < 0f or > 1f || v is < 0f or > 1f)
                {
                    continue;
                }

                // Area in the panel's plane, so a surface seen edge-on counts for little.
                var area = Vector3.Cross(b - a, c - a).Length() * 0.5f * facing;
                surfaces.Add((-Vector3.Dot(local, placement.Normal), area));
            }
        }

        return surfaces;
    }

    private static (Vector3 Position, Vector3 Normal) VertexAt(MeshGeometry mesh, uint index)
    {
        var offset = (int)index * MeshGeometry.FloatsPerVertex;
        return (
            new Vector3(
                mesh.Vertices[offset], mesh.Vertices[offset + 1], mesh.Vertices[offset + 2]),
            new Vector3(
                mesh.Vertices[offset + 3], mesh.Vertices[offset + 4], mesh.Vertices[offset + 5]));
    }
}
