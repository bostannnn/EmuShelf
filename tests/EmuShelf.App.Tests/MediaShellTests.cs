using System.Numerics;
using Avalonia.Headless.XUnit;
using EmuShelf.App.Controls;
using EmuShelf.App.Rendering;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Library;
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
    [InlineData(1280u, 800u, 2304u, 1440u)]
    [InlineData(1920u, 1080u, 2560u, 1440u)]
    [InlineData(3840u, 2160u, 3840u, 2160u)]
    public void SceneSize_AdaptsSupersamplingToTheOutputResolution(
        uint width,
        uint height,
        uint expectedWidth,
        uint expectedHeight)
    {
        var actual = EmuShelf.Rendering.MediaShellRenderer.SceneSize(width, height);

        Assert.Equal(expectedWidth, actual.Width);
        Assert.Equal(expectedHeight, actual.Height);
    }

    [Theory]
    [InlineData(1u, 256u)]
    [InlineData(256u, 256u)]
    [InlineData(257u, 512u)]
    [InlineData(1440u, 1536u)]
    public void SceneTargetCapacity_RoundsToStableResizeBuckets(uint value, uint expected) =>
        Assert.Equal(expected, EmuShelf.Rendering.MediaShellRenderer.RoundUp(value, 256));

    [Theory]
    [InlineData(3840u, 2560u, true)]
    [InlineData(2560u, 1536u, true)]
    [InlineData(2560u, 2048u, false)]
    [InlineData(2560u, 2560u, false)]
    public void SceneTargetCapacity_ShrinksOnlyAfterMaterialOverAllocation(
        uint capacity,
        uint desired,
        bool expected) =>
        Assert.Equal(
            expected,
            EmuShelf.Rendering.MediaShellRenderer.IsExcessivelyOversized(capacity, desired));

    [Theory]
    [InlineData(1f, 0.8f, 0.3f)]
    [InlineData(0.5f, 0.31f, 0.15f)]
    [InlineData(0f, -0.18f, 0f)]
    public void OutgoingShelfPose_BlendsFromTheCapturedAngleToTheNeighbour(
        float focus,
        float expectedYaw,
        float expectedPitch)
    {
        var actual = MediaShelf3DControl.ResolvePose(
            focus,
            isFocused: false,
            focusedYaw: 0f,
            focusedPitch: 0f,
            new PhysicalShelfDeparturePose(1, 0.8f, 0.3f));

        Assert.Equal(expectedYaw, actual.Yaw, 3);
        Assert.Equal(expectedPitch, actual.Pitch, 3);
    }

    /// <summary>
    /// Regression test. The arriving medium used to take the focused angle the instant selection
    /// changed — while it was still a slot away — so one d-pad step turned the outgoing cartridge
    /// smoothly and snapped the incoming one through the gap between the two rest poses.
    /// </summary>
    [Theory]
    [InlineData(0f, -0.18f, 0f)]
    [InlineData(0.5f, -0.3f, -0.05f)]
    [InlineData(1f, -0.42f, -0.1f)]
    public void IncomingShelfPose_ArrivesAtTheFocusedAngleAsItReachesCentre(
        float focus,
        float expectedYaw,
        float expectedPitch)
    {
        var actual = MediaShelf3DControl.ResolvePose(
            focus,
            isFocused: true,
            MediaRotationModel.RestYaw,
            MediaRotationModel.RestPitch,
            departure: null);

        Assert.Equal(expectedYaw, actual.Yaw, 3);
        Assert.Equal(expectedPitch, actual.Pitch, 3);
    }

    /// <summary>
    /// Focus may not change an item's scale, so the only thing separating the selected medium from
    /// its neighbours is how much of the studio key it stands in.
    /// </summary>
    [Fact]
    public void ShelfExposure_FallsOffAwayFromFocus()
    {
        var focused = EmuShelf.Rendering.MediaShellRenderer.ExposureForFocus(1f);
        var halfway = EmuShelf.Rendering.MediaShellRenderer.ExposureForFocus(0.5f);
        var neighbour = EmuShelf.Rendering.MediaShellRenderer.ExposureForFocus(0f);

        Assert.Equal(1f, focused, 3);
        Assert.True(neighbour < halfway && halfway < focused);
        // Enough separation to read at couch distance without losing the neighbours' artwork.
        Assert.InRange(neighbour, 0.35f, 0.65f);
        Assert.Equal(neighbour, EmuShelf.Rendering.MediaShellRenderer.ExposureForFocus(-2f), 3);
    }

    [Fact]
    public async Task PrepareAsync_CachesOneImmutableDecodedAsset()
    {
        var first = await MediaShellCatalog.PrepareAsync(MediaShell.CoverCard);
        var second = await MediaShellCatalog.PrepareAsync(MediaShell.CoverCard);

        Assert.Same(first, second);
        Assert.True(MediaShellCatalog.TryGetPrepared(MediaShell.CoverCard, out var prepared));
        Assert.Same(first, prepared);
    }

    [Fact]
    public void MaterialVariants_DistinguishSharedKeepCaseFinishes()
    {
        var ps2 = EmuShelf.Rendering.MediaShellRenderer.MaterialVariantAppearance.For("ps2-black");
        var ps3 = EmuShelf.Rendering.MediaShellRenderer.MaterialVariantAppearance.For("ps3-clear");
        var wii = EmuShelf.Rendering.MediaShellRenderer.MaterialVariantAppearance.For("wii-white");

        Assert.NotEqual(ps2.BodyTint, ps3.BodyTint);
        Assert.NotEqual(ps3.BodyTint, wii.BodyTint);
        Assert.True(ps3.ReflectanceScale > ps2.ReflectanceScale);
        Assert.True(ps3.RoughnessScale < ps2.RoughnessScale);
    }

    [AvaloniaFact]
    public void ShelfHero_UsesAuthoredShellWithoutCoverArt()
    {
        var game = new GameViewModel(
            new Game
            {
                Id = 1,
                SystemId = "snes",
                Path = "Game.sfc",
                Title = "Game",
                DateAdded = DateTimeOffset.UtcNow,
            },
            "Super Nintendo", "SNES", "#7A5AF8");

        game.IsFocused = true;

        Assert.False(game.HasCoverImage);
        Assert.True(game.ShelfUses3DHero);
    }

    [Fact]
    public void CartridgeArtwork_UsesSupportTextureOrAuthoredBlank_NeverBoxArt()
    {
        var cartridge = MediaShellMap.ProfileForSystem("snes", 1.43);
        var coverCard = MediaShellMap.ProfileForSystem("playstation", 1.0);

        Assert.Equal(
            ShelfArtworkKind.PhysicalMediaTexture,
            MediaShelf3DControl.ArtworkKindFor(cartridge, hasDecodedPhysicalArtwork: true));
        Assert.Equal(
            ShelfArtworkKind.None,
            MediaShelf3DControl.ArtworkKindFor(cartridge, hasDecodedPhysicalArtwork: false));
        Assert.Equal(
            ShelfArtworkKind.Cover,
            MediaShelf3DControl.ArtworkKindFor(coverCard, hasDecodedPhysicalArtwork: false));
    }

    [Theory]
    [InlineData("snes", MediaShell.SnesCartridge)]
    [InlineData("gba", MediaShell.GbaCartridge)]
    // One temporary geometry family; profiles still retain the systems' different metrics/materials.
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

    [Fact]
    public void MetricProfiles_KeepCaseLargeSnesMediumAndGbaSmall()
    {
        var keepCase = MediaShellMap.ProfileForSystem("playstation2", 0.708);
        var snes = MediaShellMap.ProfileForSystem("snes", 1.43);
        var gba = MediaShellMap.ProfileForSystem("gba", 1.42);

        Assert.Equal(1f, keepCase.HeightInShelfUnits, 3);
        Assert.True(snes.HeightInShelfUnits < keepCase.HeightInShelfUnits);
        Assert.True(gba.HeightInShelfUnits < snes.HeightInShelfUnits);
        Assert.True(snes.WidthInShelfUnits > gba.WidthInShelfUnits);
        Assert.Equal(1.235f, snes.PresentationScale, 3);
        Assert.True(snes.FloorClearanceInShelfUnits > gba.FloorClearanceInShelfUnits);
        Assert.Equal(0f, keepCase.FloorClearanceInShelfUnits, 3);
        Assert.Equal(PhysicalArtworkSlots.CartridgeSupport, snes.ArtworkSlots);
        Assert.Equal(
            PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine,
            keepCase.ArtworkSlots);
    }

    /// <summary>
    /// A profile's measured dimensions must agree with the proportions of the asset it describes.
    /// </summary>
    /// <remarks>
    /// Regression test, and the reason this defect could hide. The scene scales each axis of a
    /// shell onto its profile independently, so a profile that disagrees with its asset does not
    /// look like a size mistake — the model is silently distorted instead, and every downstream
    /// judgement about lighting, label placement and framing is then made on a deformed cartridge.
    /// The SNES profile recorded an 87mm height for a shell whose own width and depth ratios agree
    /// with 129 x 20mm, which stretched it 12% vertically.
    ///
    /// Two systems are knowingly excluded, and both are exclusions rather than oversights.
    /// <c>gba</c>: 85 x 60mm is not a Game Pak's shape either, but correcting it also resizes the
    /// cartridge on screen, so it belongs with that asset's pass. <c>playstation3</c>: its shorter
    /// Blu-ray profile is deliberately applied to shared DVD-case geometry until a PS3 case is
    /// authored, so it is distorted on purpose and is the reason that geometry is called temporary.
    /// </remarks>
    [Theory]
    [InlineData("snes")]
    [InlineData("playstation2")]
    [InlineData("gamecube")]
    [InlineData("wii")]
    public void MetricProfiles_MatchTheProportionsOfTheirAuthoredAsset(string systemId)
    {
        var profile = MediaShellMap.ProfileForSystem(systemId, 0.708);
        var asset = MediaShellCatalog.Load(profile.Shell);

        var profileWidthRatio = profile.DimensionsMillimetres.X / profile.DimensionsMillimetres.Y;
        var profileDepthRatio = profile.DimensionsMillimetres.Z / profile.DimensionsMillimetres.Y;

        // 3% covers the keep case, whose lid lip makes it stand slightly taller than nominal.
        Assert.Equal(asset.Size.X / asset.Size.Y, profileWidthRatio, 0.03f * profileWidthRatio);
        Assert.Equal(asset.Size.Z / asset.Size.Y, profileDepthRatio, 0.03f * profileDepthRatio);
    }

    [Fact]
    public void MetricProfile_UsesAThinCoverCardForUnauthoredSystems()
    {
        var profile = MediaShellMap.ProfileForSystem("playstation", 1.0);

        Assert.Equal(MediaShell.CoverCard, profile.Shell);
        Assert.Equal(1f, profile.WidthInShelfUnits, 3);
        Assert.Equal(1f, profile.HeightInShelfUnits, 3);
        Assert.True(profile.DepthInShelfUnits < 0.03f);
    }

    [Fact]
    public void MetricProfile_DistinguishesTheShorterPs3Case()
    {
        var ps2 = MediaShellMap.ProfileForSystem("playstation2", 0.708);
        var ps3 = MediaShellMap.ProfileForSystem("playstation3", 0.708);

        Assert.True(ps3.HeightInShelfUnits < ps2.HeightInShelfUnits);
        Assert.NotEqual(ps2.MaterialVariant, ps3.MaterialVariant);
    }

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
        Assert.InRange(model.Size.X, 1.64f, 1.69f);
        Assert.InRange(model.Size.Z, 0.25f, 0.27f);
        Assert.Equal(33833, model.Meshes.Sum(mesh => mesh.TriangleCount));
        Assert.Single(model.Materials);
        Assert.Equal(3, model.Textures.Count);
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

    [Fact]
    public void Load_CreatesTheFallbackCoverCardAsClosedSceneGeometry()
    {
        var model = MediaShellCatalog.Load(MediaShell.CoverCard);

        Assert.Single(model.Meshes);
        Assert.Equal(12, model.Meshes[0].TriangleCount);
        Assert.Equal(1f, model.Size.Y, 3);
        Assert.True(model.Size.Z > 0f);
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

    [Fact]
    public void SnesCoverPanel_UsesRoundedBodyAttachedDecalEdges()
    {
        var snes = MediaShellCatalog.Definition(MediaShell.SnesCartridge).CoverPanel;

        Assert.Equal(ArtFace.Front, snes.Face);
        Assert.InRange(snes.CornerRadius, 0.05f, 0.10f);
        Assert.Equal(0f, MediaShellCatalog.Definition(MediaShell.GbaCartridge).CoverPanel.CornerRadius);
        Assert.Equal(0f, MediaShellCatalog.Definition(MediaShell.DiscKeepCase).CoverPanel.CornerRadius);
    }
}
