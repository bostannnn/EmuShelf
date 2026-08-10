using System.Numerics;
using EmuShelf.App.Rendering;
using EmuShelf.Rendering.Shells;

namespace EmuShelf.App.Tests;

/// <summary>
/// Covers the parts of the couch shelf's 3D hero that can be checked without a GPU: which console
/// maps to which medium, how each shell is oriented once loaded, and where artwork lands on it.
/// The shading itself is verified by looking at <c>tools/EmuShelf.Rendering.Preview</c>'s output,
/// the same way the SDL input path is treated.
/// </summary>
public class MediaShellTests
{
    [Theory]
    [InlineData("snes", MediaShell.SnesCartridge)]
    [InlineData("gba", MediaShell.GbaCartridge)]
    // One shell, four consoles: they all shipped in the same keep case.
    [InlineData("playstation2", MediaShell.DiscKeepCase)]
    [InlineData("playstation3", MediaShell.DiscKeepCase)]
    [InlineData("gamecube", MediaShell.DiscKeepCase)]
    [InlineData("wii", MediaShell.DiscKeepCase)]
    public void ForSystem_MapsAConsoleToItsMedium(string systemId, MediaShell expected) =>
        Assert.Equal(expected, MediaShellMap.ForSystem(systemId));

    // PS1 and Dreamcast used jewel cases and PSP used a UMD case — genuinely different shapes, so
    // they keep flat covers rather than borrowing a case that is not theirs. Arcade has no
    // packaging at all.
    [Theory]
    [InlineData("playstation")]
    [InlineData("dreamcast")]
    [InlineData("psp")]
    [InlineData("arcade")]
    [InlineData("nes")]
    [InlineData("nds")]
    public void ForSystem_LeavesUnauthoredSystemsOnFlatCovers(string systemId) =>
        Assert.Null(MediaShellMap.ForSystem(systemId));

    [Theory]
    [InlineData(MediaShell.SnesCartridge)]
    [InlineData(MediaShell.GbaCartridge)]
    [InlineData(MediaShell.DiscKeepCase)]
    public void Load_NormalisesEveryShellToOneUnitTallAndCentred(MediaShell shell)
    {
        var model = MediaShellCatalog.Load(shell);

        // A shared camera framing only works if every shell arrives in the same canonical space.
        Assert.Equal(1f, model.Size.Y, 3);
        Assert.Equal(0f, (model.BoundsMin.Y + model.BoundsMax.Y) / 2f, 3);
        Assert.Equal(0f, (model.BoundsMin.X + model.BoundsMax.X) / 2f, 2);
        Assert.True(model.Meshes.Count > 0);
    }

    /// <summary>
    /// A Game Pak is wider than it is tall, with its contact fingers along the bottom edge.
    /// </summary>
    /// <remarks>
    /// Regression test. The cartridge was first oriented with a cyclic axis permutation, which
    /// stood it on a short edge and left the gold contacts running up its left-hand side. The
    /// orientation is only correct when the cartridge is landscape.
    /// </remarks>
    [Fact]
    public void Load_StandsTheGbaCartridgeOnItsLongEdge()
    {
        var model = MediaShellCatalog.Load(MediaShell.GbaCartridge);

        Assert.True(
            model.Size.X > model.Size.Y,
            $"A GBA cartridge is wider than tall; got {model.Size.X} x {model.Size.Y}.");
        // And it is a cartridge, not a slab: far thinner than it is wide.
        Assert.True(model.Size.Z < model.Size.X / 4f);
    }

    [Fact]
    public void Load_StandsTheSnesCartridgeOnItsLongEdge()
    {
        var model = MediaShellCatalog.Load(MediaShell.SnesCartridge);

        Assert.True(
            model.Size.X > model.Size.Y,
            $"A SNES cartridge is wider than tall; got {model.Size.X} x {model.Size.Y}.");
    }

    [Fact]
    public void Load_KeepsTheDiscCaseTallerThanItIsWide()
    {
        var model = MediaShellCatalog.Load(MediaShell.DiscKeepCase);

        // Close to a real 135 x 190 x 14mm keep case. The tolerance is real rather than sloppy: the
        // model stands about 194mm tall including the lip around the lid, so its proportions land
        // near the nominal figures without matching them exactly.
        Assert.True(model.Size.X < model.Size.Y);
        Assert.InRange(model.Size.X, 0.66f, 0.74f);
        Assert.InRange(model.Size.Z, 0.05f, 0.09f);
    }

    /// <summary>
    /// Each face's artwork runs left-to-right as somebody standing in front of THAT face sees it.
    /// </summary>
    /// <remarks>
    /// Regression test. Placement originally hard-coded a u axis per face, which mirrored the back
    /// and the spine — a defect invisible head-on and obvious the moment the shell is turned round.
    /// </remarks>
    [Theory]
    [InlineData(ArtFace.Front, 1f, 0f, 0f)]
    [InlineData(ArtFace.Back, -1f, 0f, 0f)]
    [InlineData(ArtFace.Spine, 0f, 0f, 1f)]
    public void Place_RunsArtworkLeftToRightAsThatFaceIsSeen(
        ArtFace face, float expectedX, float expectedY, float expectedZ)
    {
        var model = MediaShellCatalog.Load(MediaShell.DiscKeepCase);
        var placement = MediaShellCatalog.Place(ArtPanel.Full(face), model);

        var direction = Vector3.Normalize(placement.UEdge);
        var expected = new Vector3(expectedX, expectedY, expectedZ);

        Assert.Equal(expected.X, direction.X, 3);
        Assert.Equal(expected.Y, direction.Y, 3);
        Assert.Equal(expected.Z, direction.Z, 3);
    }

    [Fact]
    public void Place_PutsArtworkOnTheSurfaceItBelongsTo()
    {
        var model = MediaShellCatalog.Load(MediaShell.DiscKeepCase);

        var front = MediaShellCatalog.Place(ArtPanel.Full(ArtFace.Front), model);
        Assert.Equal(model.BoundsMax.Z, front.Origin.Z, 3);
        Assert.Equal(Vector3.UnitZ, front.Normal);

        var back = MediaShellCatalog.Place(ArtPanel.Full(ArtFace.Back), model);
        Assert.Equal(model.BoundsMin.Z, back.Origin.Z, 3);

        var spine = MediaShellCatalog.Place(ArtPanel.Full(ArtFace.Spine), model);
        Assert.Equal(model.BoundsMin.X, spine.Origin.X, 3);
    }

    [Fact]
    public void Place_SpansTheFaceItIsGiven()
    {
        var model = MediaShellCatalog.Load(MediaShell.DiscKeepCase);
        var placement = MediaShellCatalog.Place(ArtPanel.Full(ArtFace.Front), model);

        // v is always the shell's height, and a full panel covers all of it.
        Assert.Equal(model.Size.Y, placement.VEdge.Length(), 3);
        Assert.Equal(model.Size.X, placement.UEdge.Length(), 3);
    }

    [Fact]
    public void Place_HonoursAPanelInset()
    {
        var model = MediaShellCatalog.Load(MediaShell.DiscKeepCase);

        var full = MediaShellCatalog.Place(ArtPanel.Full(ArtFace.Front), model);
        var inset = MediaShellCatalog.Place(ArtPanel.Full(ArtFace.Front, inset: 0.1f), model);

        Assert.True(inset.UEdge.Length() < full.UEdge.Length());
        Assert.True(inset.VEdge.Length() < full.VEdge.Length());
    }

    [Fact]
    public void CoverPanel_StaysInsideEveryShell()
    {
        foreach (var shell in MediaShellCatalog.All)
        {
            var model = MediaShellCatalog.Load(shell);
            var placement = MediaShellCatalog.Place(
                MediaShellCatalog.Definition(shell).CoverPanel, model);

            // The far corner of the artwork must still land on the shell, or a label would hang off
            // the edge of the medium it is printed on.
            var corner = placement.Origin + placement.UEdge + placement.VEdge;
            Assert.InRange(corner.X, model.BoundsMin.X - 0.001f, model.BoundsMax.X + 0.001f);
            Assert.InRange(corner.Y, model.BoundsMin.Y - 0.001f, model.BoundsMax.Y + 0.001f);
        }
    }
}
