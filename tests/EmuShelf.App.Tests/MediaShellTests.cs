using System.Numerics;
using Avalonia.Headless.XUnit;
using EmuShelf.App.Controls;
using EmuShelf.App.Rendering;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Library;
using EmuShelf.Integrations.Systems;
using EmuShelf.Rendering.Models;
using EmuShelf.Rendering.Preview;
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

    /// <summary>
    /// A shell's panel proportions are unknown until its asset has decoded.
    /// </summary>
    /// <remarks>
    /// Regression test for a first-frame defect. The blank label is drawn to fit the panel, so it
    /// cannot be drawn before this returns a value — and the shelf originally warmed labels only
    /// when its game list changed, which happens before decoding finishes. The result was a
    /// cartridge wearing a bare accent tint until the player changed platform, which rebuilt the
    /// layout late enough to succeed. Anything depending on this must also retry once preparation
    /// completes.
    /// </remarks>
    [Fact]
    public async Task TryGetPanelAspect_IsOnlyAvailableAfterTheAssetIsPrepared()
    {
        Assert.True(MediaShellCatalog.TryGetPanelAspect(MediaShell.CoverCard) is null or > 0f);

        await MediaShellCatalog.PrepareAsync(MediaShell.SnesCartridge);
        var aspect = MediaShellCatalog.TryGetPanelAspect(MediaShell.SnesCartridge);

        Assert.NotNull(aspect);
        // The SNES label is a wide landscape strip; anything near square means the panel is wrong.
        Assert.InRange(aspect!.Value, 2.5f, 3.3f);
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
        var psp = EmuShelf.Rendering.MediaShellRenderer.MaterialVariantAppearance.For("psp-clear");

        Assert.NotEqual(ps2.BodyTint, ps3.BodyTint);
        Assert.NotEqual(ps3.BodyTint, wii.BodyTint);
        Assert.True(ps3.ReflectanceScale > ps2.ReflectanceScale);
        Assert.True(ps3.RoughnessScale < ps2.RoughnessScale);

        // PSP shares the clear-plastic family with PS3 and must not fall through to Default, which
        // is how an unrecognised variant string fails — silently, as untinted stock plastic.
        Assert.NotEqual(EmuShelf.Rendering.MediaShellRenderer.MaterialVariantAppearance.Default, psp);
        Assert.NotEqual(ps3.BodyTint, psp.BodyTint);
        Assert.True(psp.ReflectanceScale > ps2.ReflectanceScale);
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
            MediaShelf3DControl.ArtworkKindFor(cartridge, ShelfArtworkFace.Front, true));
        // Never the box: an unscraped cartridge wears the drawn placeholder label instead.
        Assert.Equal(
            ShelfArtworkKind.PlaceholderLabel,
            MediaShelf3DControl.ArtworkKindFor(cartridge, ShelfArtworkFace.Front, false));
        Assert.Equal(
            ShelfArtworkKind.Cover,
            MediaShelf3DControl.ArtworkKindFor(coverCard, ShelfArtworkFace.Front, false));
    }

    /// <summary>
    /// A keep case wears three independently scraped faces, and coverage is uneven — the front is
    /// nearly always present, the spine rarely. A missing face keeps the platform tint; it must not
    /// blank the faces that did arrive, and it must never put the front's art on the back.
    /// </summary>
    [Fact]
    public void KeepCaseFaces_AreResolvedIndependently()
    {
        var keepCase = MediaShellMap.ProfileForSystem("playstation2", 0.708);

        Assert.Equal(
            ShelfArtworkKind.Cover,
            MediaShelf3DControl.ArtworkKindFor(keepCase, ShelfArtworkFace.Front, false));
        Assert.Equal(
            ShelfArtworkKind.PhysicalMediaTexture,
            MediaShelf3DControl.ArtworkKindFor(keepCase, ShelfArtworkFace.Back, true));
        Assert.Equal(
            ShelfArtworkKind.None,
            MediaShelf3DControl.ArtworkKindFor(keepCase, ShelfArtworkFace.Back, false));
        Assert.Equal(
            ShelfArtworkKind.PhysicalMediaTexture,
            MediaShelf3DControl.ArtworkKindFor(keepCase, ShelfArtworkFace.Spine, true));
    }

    /// <summary>
    /// A shell's extra panels must be declared in the order <see cref="ShelfArtworkFace"/> numbers
    /// them: cover first, then Back, then Spine.
    /// </summary>
    /// <remarks>
    /// The link between the two is positional and nothing else — the renderer hands panel <c>n</c>
    /// the artwork uploaded to slot <c>n</c>, and the app uploads by casting this enum. Declare a
    /// case's spine before its back and the scraped inlay is painted down the hinge while the spine
    /// strip is stretched over the back, with no error anywhere. The shell preview cannot catch it
    /// because it only ever supplies a front cover, which is exactly how the jewel case shipped
    /// with them swapped. Asserted for every shell, since the trap is open to all of them.
    /// </remarks>
    [Fact]
    public void ExtraPanels_AreDeclaredInArtworkFaceOrder()
    {
        foreach (var shell in MediaShellCatalog.All)
        {
            var extras = MediaShellCatalog.Definition(shell).ExtraPanels;
            for (var index = 0; index < extras.Count; index++)
            {
                var slot = (ShelfArtworkFace)(index + 1);
                var expected = slot switch
                {
                    ShelfArtworkFace.Back => ArtFace.Back,
                    ShelfArtworkFace.Spine => ArtFace.Spine,
                    _ => throw new InvalidOperationException(
                        $"{shell} declares an extra panel at slot {index + 1}, which no "
                        + "ShelfArtworkFace names; the app cannot upload artwork to it."),
                };

                Assert.True(
                    extras[index].Face == expected,
                    $"{shell}'s extra panel {index} is {extras[index].Face}, but the app uploads "
                    + $"{slot} artwork to that slot. Declare Back before Spine.");
            }
        }
    }

    /// <summary>A cartridge has no back or spine slot, so those faces stay bare however much art exists.</summary>
    [Theory]
    [InlineData(ShelfArtworkFace.Back)]
    [InlineData(ShelfArtworkFace.Spine)]
    public void CartridgeHasNoBackOrSpineFace(ShelfArtworkFace face) =>
        Assert.Equal(
            ShelfArtworkKind.None,
            MediaShelf3DControl.ArtworkKindFor(
                MediaShellMap.ProfileForSystem("snes", 1.43), face, hasDecodedArtwork: true));

    [Theory]
    [InlineData("snes", MediaShell.SnesCartridge)]
    [InlineData("gba", MediaShell.GbaCartridge)]
    [InlineData("nes", MediaShell.NesCartridge)]
    [InlineData("megadrive", MediaShell.MegaDriveCartridge)]
    [InlineData("nds", MediaShell.DsCard)]
    [InlineData("gbc", MediaShell.GbcCartridge)]
    // One geometry family, two consoles that really did share a case.
    [InlineData("playstation", MediaShell.JewelCase)]
    [InlineData("dreamcast", MediaShell.JewelCase)]
    // The one system whose "medium" is a machine: an arcade game never shipped to a player at all.
    [InlineData("arcade", MediaShell.ArcadeCabinet)]
    // The shorter Blu-ray case, authored separately once it became clear that one mesh could not
    // be both objects without lying about one of them.
    [InlineData("playstation3", MediaShell.BluRayCase)]
    // One temporary geometry family; profiles still retain the systems' different metrics/materials.
    [InlineData("playstation2", MediaShell.DiscKeepCase)]
    [InlineData("gamecube", MediaShell.DiscKeepCase)]
    [InlineData("wii", MediaShell.DiscKeepCase)]
    [InlineData("psp", MediaShell.DiscKeepCase)]
    public void ForSystem_MapsAConsoleToItsMedium(string systemId, MediaShell expected) =>
        Assert.Equal(expected, MediaShellMap.ForSystem(systemId));

    // What is left, and it is now one system: a 3DS card has no authored shell yet. PS1 and
    // Dreamcast were here until the jewel case was authored and both now share it; PSP was here
    // until it took the keep case, which is the one entry in this file that borrows a case that is
    // not its own — see MetricProfile_TakesARealUmdCasesShapeToKeepItsSleeveUndistorted for the
    // trade that made that worth doing rather than staying on a flat cover. Arcade was here on the
    // grounds that it has no packaging at all, which is true and turned out not to matter: the
    // machine is the medium.
    [Theory]
    [InlineData("3ds")]
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
        // The case's own three printed faces plus the disc inside it, which is a fourth scraped
        // face on the same game; DiscProfiles_ClaimTheSupportTextureForTheirDisc owns why.
        Assert.Equal(
            PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine
                | PhysicalArtworkSlots.DiscLabel,
            keepCase.ArtworkSlots);
    }

    /// <summary>
    /// The cartridges stand beside each other in the size order the real things do.
    /// </summary>
    /// <remarks>
    /// The gap the proportion test cannot see. A Mega Drive profile recorded 135 x 87mm for a
    /// 109 x 70mm cartridge; both have the same 1.553 ratio, so the shell was never distorted and
    /// nothing failed — it just stood a quarter too big, taller on the shelf than the SNES
    /// cartridge it is comfortably shorter than in life. Ratios check shape; only a measurement
    /// checks size, and on a shelf that shows several media at once size is the whole point.
    /// </remarks>
    [Fact]
    public void MetricProfiles_OrderTheCartridgesAsTheRealOnesStand()
    {
        var nes = MediaShellMap.ProfileForSystem("nes", 0.72);
        var snes = MediaShellMap.ProfileForSystem("snes", 1.43);
        var megaDrive = MediaShellMap.ProfileForSystem("megadrive", 1.43);
        var gba = MediaShellMap.ProfileForSystem("gba", 1.42);

        Assert.Equal(109f, megaDrive.DimensionsMillimetres.X, 1f);
        Assert.Equal(70f, megaDrive.DimensionsMillimetres.Y, 1f);

        // A cartridge that is 70mm tall cannot out-rank a 135mm NES or a 77.5mm SNES cartridge,
        // and it is still comfortably bigger than a Game Pak.
        Assert.True(megaDrive.HeightInShelfUnits < nes.HeightInShelfUnits);
        Assert.True(megaDrive.HeightInShelfUnits < snes.HeightInShelfUnits);
        Assert.True(megaDrive.HeightInShelfUnits > gba.HeightInShelfUnits);
        Assert.True(megaDrive.WidthInShelfUnits < snes.WidthInShelfUnits);
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
    /// with 129 x 20mm, which stretched it 12% vertically. PS3 was the second catch: its truthful
    /// Blu-ray height on shared DVD geometry came to a 13.7% stretch, and now renders undistorted.
    ///
    /// Every authored shell is now checked. GBA was the last exclusion — 85 x 60mm against an asset
    /// whose own ratio is 1.708, a 20% stretch — and it was fixed when that shell was replaced.
    /// </remarks>
    [Theory]
    [InlineData("snes")]
    [InlineData("nes")]
    [InlineData("gba")]
    [InlineData("megadrive")]
    [InlineData("nds")]
    [InlineData("gbc")]
    [InlineData("playstation")]
    [InlineData("dreamcast")]
    [InlineData("arcade")]
    [InlineData("playstation2")]
    [InlineData("playstation3")]
    [InlineData("gamecube")]
    [InlineData("wii")]
    // PSP is the one deliberate exclusion, and it is not the old kind. Every exclusion this theory
    // shed was a profile that disagreed with its asset by accident; PSP disagrees on purpose, to
    // keep its sleeve art undistorted, and the test below pins the exact disagreement so it cannot
    // drift. Adding "psp" here is therefore not the fix if this pair ever conflicts — read both.
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

    /// <summary>
    /// The launch lift must stay inside the frame the shelf camera actually shows.
    /// </summary>
    /// <remarks>
    /// These two were tuned independently and silently disagreed: the camera now pulls back only as
    /// far as the tallest medium requires, so the headroom above a cartridge is a fraction of what
    /// it was under the old fixed distance, and a lift sized for that distance carried the medium
    /// out through the top of the frame on its way up.
    ///
    /// Run for a portrait medium as well as a cartridge, because the two are framed by different
    /// axes and so have different headroom: a cartridge is held back by the frame's sides and keeps
    /// the height it had, while a portrait medium fills 62% of the frame and has correspondingly
    /// less room above it to rise into. A test that only asked about SNES would say nothing about
    /// the media the framing change actually moves.
    ///
    /// PSP and PS1 rather than PS2, which this asked about until disc games got their own
    /// choreography: a keep case now plays <see cref="PhysicalShelfLaunchStyle.Disc"/>, so asking
    /// how it fares under this sequence tests a composition the app never runs.
    ///
    /// The jewel case is the row that matters and the reason PS1 is here at all. The lift is
    /// absolute while the frame scales with the medium, so the tightest medium is the shortest
    /// height-led one — not the tallest, which is where this test had been looking. A 125mm jewel
    /// case gets two thirds of a keep case's frame and the same 0.10 lift, and it is what caps
    /// ShelfFrameFill at 0.55: it leaves the frame between 0.56 and 0.57, where a UMD case survives
    /// to 0.64 and a keep case under the disc sequence to 0.66.
    /// </remarks>
    [Theory]
    [InlineData("snes", 1.43, ShelfViewportAspects.SteamDeck)]
    [InlineData("snes", 1.43, ShelfViewportAspects.Narrowest)]
    [InlineData("psp", 0.581, ShelfViewportAspects.SteamDeck)]
    [InlineData("psp", 0.581, ShelfViewportAspects.Narrowest)]
    [InlineData("playstation", 0.9, ShelfViewportAspects.SteamDeck)]
    [InlineData("playstation", 0.9, ShelfViewportAspects.Narrowest)]
    public void LaunchChoreography_StaysInsideTheShelfCameraFrame(
        string systemId, double coverAspect, float aspect)
    {
        var profile = MediaShellMap.ProfileForSystem(systemId, coverAspect);
        var asset = MediaShellCatalog.Load(profile.Shell);
        var band = profile.HeightInShelfUnits + profile.FloorClearanceInShelfUnits;
        var (view, projection, _) = EmuShelf.Rendering.MediaShellRenderer.ShelfCamera(
            aspect, band, profile.TurningWidthInShelfUnits);
        var viewProjection = view * projection;

        var transition = new PhysicalShelfLaunchTransitionModel();
        transition.Start(
            1,
            PhysicalShelfLaunchStyle.Cartridge,
            MediaRotationModel.RestYaw,
            MediaRotationModel.RestPitch);

        var highest = float.NegativeInfinity;
        for (var step = 0; step < 400 && !transition.IsCommitted; step++)
        {
            transition.Update(16d);
            var pose = transition.Pose;
            var model = EmuShelf.Rendering.MediaShellRenderer.ShelfModel(
                new EmuShelf.Rendering.MediaShelfRenderItem(
                    1, profile, 0f, 1f, pose.Yaw, pose.Pitch, Vector3.One,
                    pose.VerticalOffset, pose.DepthOffset, pose.Scale),
                asset);

            foreach (var corner in Corners(asset.BoundsMin, asset.BoundsMax))
            {
                var world = Vector3.Transform(corner, model);
                var clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
                highest = MathF.Max(highest, clip.Y / clip.W);
            }
        }

        // Only the top edge: the medium is supposed to leave through the bottom on insertion.
        Assert.True(
            highest <= 1f,
            $"The medium reached {highest:F3} of the frame's half-height; above 1.0 it is clipped.");
    }

    /// <summary>
    /// Aspect ratios of the control the shelf scene is actually drawn into.
    /// </summary>
    /// <remarks>
    /// Not the display's. <c>GamepadShelfMediaHost</c> is 1248 x 560 inside a 1280 x 800 Steam Deck
    /// — 2.23, where the panel is 1.6 — because the shelf page spends a fixed 32 x 240px on margins
    /// and the title beneath the media. Measured by laying out the real MainWindow headlessly at
    /// 1024 x 640, 1280 x 800, 1920 x 1080 and 2560 x 1440, which bracket the viewport between 2.11
    /// and 2.48.
    ///
    /// Worth stating because testing the shelf camera at the panel's aspect is not a slightly
    /// pessimistic approximation, it is a different composition: a cartridge is width-led at every
    /// aspect the viewport can have, and at 1.6 it is 28% smaller than the app ever draws it.
    /// </remarks>
    private static class ShelfViewportAspects
    {
        /// <summary>1248 x 560, the reference viewport.</summary>
        public const float SteamDeck = 2.229f;

        /// <summary>2528 x 1200. The fixed chrome is proportionally smallest on a big display.</summary>
        public const float Narrowest = 2.107f;

        /// <summary>992 x 400, a small window.</summary>
        public const float Widest = 2.480f;
    }

    /// <summary>
    /// A disc case and a cartridge must end up comparably large on screen, not merely comparably
    /// tall.
    /// </summary>
    /// <remarks>
    /// The camera framed the tallest medium to half the viewport height and left width alone, which
    /// treats "how big does this look" as a question about height. It is not: measured in the shelf
    /// viewport a SNES cartridge is landscape and covered 20.6% of the frame under that rule, while
    /// a portrait PS2 keep case — the taller object of the two in real life — covered 8.6%. Hence
    /// the report that disc-based games were small in shelf mode after cartridges had been fixed.
    ///
    /// Area, deliberately, because it is the measure that caught this: every single-axis measure
    /// said the two were framed identically.
    ///
    /// Both bounds are absolute rather than a ratio, and asserted only at the reference viewport,
    /// because the ratio is aspect-dependent by design: a cartridge is width-led and a case is
    /// height-led, so they close and separate as the window changes shape. A single ratio spanning
    /// the viewport's whole range would have to sit near what the broken framing already scored.
    ///
    /// The case reaches half the cartridge, not parity. The height fill is capped at 0.56 by the
    /// PS1 jewel case's launch lift — see <see cref="MediaShellRenderer"/> — and parity needs about
    /// 0.77, so the rest is the choreography's to give.
    /// </remarks>
    [Fact]
    public void ShelfCamera_FramesADiscCaseComparablyToACartridge()
    {
        var cartridge = FrameCoverage("snes", 1.43, ShelfViewportAspects.SteamDeck);
        var keepCase = FrameCoverage("playstation2", 0.708, ShelfViewportAspects.SteamDeck);

        var cartridgeArea = cartridge.Width * cartridge.Height;
        var caseArea = keepCase.Width * keepCase.Height;

        // 0.105 measured, against 0.086 under the height-only rule. This is the regression guard:
        // the old framing fails it outright.
        Assert.True(
            caseArea >= 0.098f,
            $"A keep case covers only {caseArea:P1} of the shelf viewport.");
        Assert.True(
            caseArea >= cartridgeArea * 0.47f,
            $"A keep case covers {caseArea:P1} of the frame against a cartridge's {cartridgeArea:P1}.");
    }

    /// <summary>
    /// A cartridge keeps the framing the height-only rule gave it, to within a tenth of a percent.
    /// </summary>
    /// <remarks>
    /// The complaint this change answers was about disc cases, and cartridges had just been tuned
    /// and signed off — so growing them too would be an unrequested change, and shrinking them a
    /// regression. <see cref="MediaShellRenderer"/>'s width fill is calibrated to leave them where
    /// they were; this is the assertion that says so, and it is what fixes that constant at 0.368.
    ///
    /// The expected figures are measured silhouettes rather than the nominal fill — the near corner
    /// of a turned cartridge projects larger than its centre plane, which is why the height reads
    /// 0.549 under a rule that asked for 0.5. They hold at this viewport only: a cartridge is
    /// width-led, so its height share follows the window's shape.
    /// </remarks>
    [Fact]
    public void ShelfCamera_LeavesACartridgeWhereTheHeightOnlyRuleFramedIt()
    {
        var coverage = FrameCoverage("snes", 1.43, ShelfViewportAspects.SteamDeck);

        Assert.Equal(0.3747f, coverage.Width, 0.002f);
        Assert.Equal(0.5488f, coverage.Height, 0.002f);
    }

    /// <summary>
    /// No medium may be framed off the edges of the viewport it is drawn into.
    /// </summary>
    /// <remarks>
    /// The shelf viewport cannot currently be narrower than about 2.1, so 0.9 and 1.0 are not
    /// Steam Decks — they are the guard on the rule rather than on today's layout. Framing on
    /// height alone put a SNES cartridge at 82% of the frame's width at 1:1 and past its edges
    /// below that, so any future layout that gave the scene a squarer control clipped the medium
    /// against the sides. Nothing in the height-only rule knew the frame had sides at all.
    /// </remarks>
    [Theory]
    [InlineData("snes", 1.43)]
    [InlineData("playstation2", 0.708)]
    [InlineData("arcade", 0.75)]
    public void ShelfCamera_KeepsAMediumInsideANarrowViewport(string systemId, double coverAspect)
    {
        foreach (var aspect in new[]
                 {
                     0.9f, 1.0f, ShelfViewportAspects.Narrowest, ShelfViewportAspects.SteamDeck,
                     ShelfViewportAspects.Widest, 21f / 9f,
                 })
        {
            var coverage = FrameCoverage(systemId, coverAspect, aspect);
            Assert.True(
                coverage.Width <= 1f && coverage.Height <= 1f,
                $"{systemId} covers {coverage.Width:P1} x {coverage.Height:P1} at aspect {aspect:F2}.");
        }
    }

    /// <summary>
    /// The fraction of the frame's width and height a focused medium's silhouette spans, through
    /// the real shelf camera at its resting pose.
    /// </summary>
    private static (float Width, float Height) FrameCoverage(
        string systemId, double coverAspect, float aspect)
    {
        var profile = MediaShellMap.ProfileForSystem(systemId, coverAspect);
        var asset = MediaShellCatalog.Load(profile.Shell);
        var band = profile.HeightInShelfUnits + profile.FloorClearanceInShelfUnits;
        var (view, projection, _) = EmuShelf.Rendering.MediaShellRenderer.ShelfCamera(
            aspect, band, profile.TurningWidthInShelfUnits);
        var viewProjection = view * projection;
        var model = EmuShelf.Rendering.MediaShellRenderer.ShelfModel(
            new EmuShelf.Rendering.MediaShelfRenderItem(
                1, profile, 0f, 1f, MediaRotationModel.RestYaw, MediaRotationModel.RestPitch,
                Vector3.One),
            asset);

        var min = new Vector2(float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity);
        foreach (var corner in Corners(asset.BoundsMin, asset.BoundsMax))
        {
            var world = Vector3.Transform(corner, model);
            var clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
            var ndc = new Vector2(clip.X / clip.W, clip.Y / clip.W);
            min = Vector2.Min(min, ndc);
            max = Vector2.Max(max, ndc);
        }

        // NDC spans -1..1, so a full frame is two units on each axis.
        return ((max.X - min.X) * 0.5f, (max.Y - min.Y) * 0.5f);
    }

    /// <summary>
    /// The disc's launch puts a second body on screen, and it has to obey the same frame.
    /// </summary>
    /// <remarks>
    /// The sibling of the test above, and the one that pins the thing that most wants to go wrong
    /// here: the disc travels up and forward out of a case that is already at the top of its lift,
    /// and stepping toward the camera magnifies a rise that was already close to the ceiling. Both
    /// bodies are checked because the case is still on screen for the first half of the sequence.
    ///
    /// This is now what fixes <c>ShelfFrameFill</c>, having taken that job over from the cartridge
    /// test above: a disc case plays this sequence and not that one, and this is the taller
    /// excursion of the two. See <see cref="ShelfViewportAspects"/> for why the aspects are the
    /// shelf control's rather than the display's.
    /// </remarks>
    [Theory]
    [InlineData(ShelfViewportAspects.SteamDeck)]
    [InlineData(ShelfViewportAspects.Narrowest)]
    [InlineData(ShelfViewportAspects.Widest)]
    public void DiscLaunchChoreography_KeepsBothBodiesInsideTheShelfCameraFrame(float aspect)
    {
        var profile = MediaShellMap.ProfileForSystem("wii", 0.708);
        var caseAsset = MediaShellCatalog.Load(profile.Shell);
        var discAsset = MediaShellCatalog.Load(MediaShell.GameDisc);
        var band = profile.HeightInShelfUnits + profile.FloorClearanceInShelfUnits;
        var (view, projection, _) = EmuShelf.Rendering.MediaShellRenderer.ShelfCamera(
            aspect, band, profile.TurningWidthInShelfUnits);
        var viewProjection = view * projection;

        var transition = new PhysicalShelfLaunchTransitionModel();
        transition.Start(
            1, PhysicalShelfLaunchStyle.Disc, MediaRotationModel.RestYaw, MediaRotationModel.RestPitch);

        var highestCase = float.NegativeInfinity;
        var highestDisc = float.NegativeInfinity;
        for (var step = 0; step < 400 && !transition.IsCommitted; step++)
        {
            transition.Update(16d);
            var pose = transition.Pose;
            var item = new EmuShelf.Rendering.MediaShelfRenderItem(
                1, profile, 0f, 1f, pose.Yaw, pose.Pitch, Vector3.One,
                pose.VerticalOffset, pose.DepthOffset, pose.Scale,
                new EmuShelf.Rendering.MediaShelfDiscPose(
                    pose.Disc!.Value.HorizontalOffset,
                    pose.Disc!.Value.VerticalOffset,
                    pose.Disc!.Value.DepthOffset,
                    pose.Disc!.Value.Spin,
                    pose.Disc!.Value.Tilt,
                    pose.Disc!.Value.Scale));

            highestCase = MathF.Max(
                highestCase,
                HighestClipY(caseAsset, EmuShelf.Rendering.MediaShellRenderer.ShelfModel(item, caseAsset), viewProjection));
            highestDisc = MathF.Max(
                highestDisc,
                HighestClipY(discAsset, EmuShelf.Rendering.MediaShellRenderer.DiscModel(item, discAsset), viewProjection));
        }

        Assert.True(
            highestCase <= 1f,
            $"The case reached {highestCase:F3} of the frame's half-height; above 1.0 it is clipped.");
        Assert.True(
            highestDisc <= 1f,
            $"The disc reached {highestDisc:F3} of the frame's half-height; above 1.0 it is clipped.");
    }

    private static float HighestClipY(ModelAsset asset, Matrix4x4 model, Matrix4x4 viewProjection)
    {
        var highest = float.NegativeInfinity;
        foreach (var corner in Corners(asset.BoundsMin, asset.BoundsMax))
        {
            var world = Vector3.Transform(corner, model);
            var clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
            highest = MathF.Max(highest, clip.Y / clip.W);
        }

        return highest;
    }

    private static IEnumerable<Vector3> Corners(Vector3 min, Vector3 max)
    {
        foreach (var x in new[] { min.X, max.X })
        {
            foreach (var y in new[] { min.Y, max.Y })
            {
                foreach (var z in new[] { min.Z, max.Z })
                {
                    yield return new Vector3(x, y, z);
                }
            }
        }
    }

    /// <summary>
    /// The disc loads flat and round, which is entirely down to its orientation being right.
    /// </summary>
    /// <remarks>
    /// The source bakes an arbitrary rotation into its node chain, and the loader composes that
    /// into the vertices: without the correcting quaternion the disc arrives 1.83 wide per unit of
    /// height and nearly as deep — standing on a corner. Proportions are the cheapest way to catch
    /// that, and the reason this is measured rather than eyeballed is that a tumbled disc still
    /// renders as a plausible ellipse from the shelf camera.
    ///
    /// The hole is the other load-bearing part: a filled circle spinning about its own centre does
    /// not appear to move at all, so the whole spin-up would read as a still frame.
    /// </remarks>
    [Fact]
    public void GameDisc_LoadsFlatAndRoundAtRealDiscProportions()
    {
        var disc = MediaShellCatalog.Load(MediaShell.GameDisc);

        Assert.Equal(1f, disc.Size.X, 3);
        Assert.Equal(1f, disc.Size.Y, 3);
        // The source's own thickness, 1.8mm against a real disc's 1.2mm. Taken from the asset for
        // the reason every other profile is: a figure that disagrees with its mesh does not read as
        // a size error, it silently distorts the shell.
        Assert.InRange(disc.Size.Z, 0.010f, 0.020f);

        var radii = Vertices(disc).Select(vertex => new Vector2(vertex.X, vertex.Y).Length()).ToArray();
        // A 15mm hole on a 120mm disc is 0.0625 of the diameter; this one is within 1.5% of it.
        Assert.InRange(radii.Min(), 0.055f, 0.070f);
        Assert.Equal(0.5f, radii.Max(), 3);
    }

    /// <summary>
    /// The shipped disc carries no texture of any kind, and that is a licence requirement.
    /// </summary>
    /// <remarks>
    /// The source is a CC-BY compact disc whose maps carry "SONY CD-R 700MB" trade dress — in the
    /// base colour, and embossed a second time into the metallic/roughness map. Its two faces share
    /// one atlas with interleaved circular islands, so no rectangle can mask the branding without
    /// also clipping the data surface. The asset is prepared with every map stripped instead, which
    /// is why the disc's whole appearance is stated in its shell definition and its label comes from
    /// the game's own scraped artwork. A texture reappearing here means the prep step was skipped
    /// and third-party branding is in the build.
    /// </remarks>
    [Fact]
    public void GameDisc_ShipsGeometryOnlyWithNoSourceArtwork()
    {
        var disc = MediaShellCatalog.Load(MediaShell.GameDisc);

        Assert.Empty(disc.Textures);
        foreach (var material in disc.Materials)
        {
            Assert.Equal(-1, material.BaseColorTexture);
            Assert.Equal(-1, material.MetallicRoughnessTexture);
            Assert.Equal(-1, material.NormalTexture);
        }
    }

    /// <summary>
    /// Every fragment the label lands on has to face the player, or the print appears on the data
    /// side as well — the panel is projected in object space and cannot tell the two apart itself.
    /// </summary>
    [Fact]
    public void GameDisc_PutsItsLabelOnTheFaceThatFacesThePlayer()
    {
        var disc = MediaShellCatalog.Load(MediaShell.GameDisc);
        var placement = MediaShellCatalog.Place(
            MediaShellCatalog.Definition(MediaShell.GameDisc).CoverPanel, disc);

        Assert.Equal(Vector3.UnitZ, placement.Normal);

        // The label side's normals point at the player; the data side's point away.
        var mesh = disc.Meshes[0];
        var front = 0;
        var back = 0;
        for (var offset = 0; offset < mesh.Vertices.Length; offset += MeshGeometry.FloatsPerVertex)
        {
            var z = mesh.Vertices[offset + 2];
            var normalZ = mesh.Vertices[offset + 5];
            if (z > 0.001f && normalZ > 0.5f)
            {
                front++;
            }
            else if (z < -0.001f && normalZ < -0.5f)
            {
                back++;
            }
        }

        Assert.True(front > 0, "The disc has no front-facing label surface at all.");
        Assert.Equal(front, back);
    }

    /// <summary>
    /// The label's depth allowance has to reach the face the label is actually printed on.
    /// </summary>
    /// <remarks>
    /// A panel's plane sits at the model's furthest extent along its normal, and on this disc that
    /// is the raised stacking ring around the hub rather than the flat face beside it. An allowance
    /// derived from the disc's thickness rejected every front-facing surface in the panel and the
    /// label silently stopped drawing — the disc rendered as a bare mirror with no printing at all,
    /// which looks like a deliberate finish rather than a bug. Both bounds are asserted: it must
    /// reach the face, and it must stop well short of the data side on the other face.
    /// </remarks>
    [Fact]
    public void GameDisc_LabelAllowanceReachesTheFaceButNotTheDataSide()
    {
        var disc = MediaShellCatalog.Load(MediaShell.GameDisc);
        var definition = MediaShellCatalog.Definition(MediaShell.GameDisc);
        var placement = MediaShellCatalog.Place(definition.CoverPanel, disc);
        var allowance = definition.CoverPanel.MaxSurfaceDepth
            ?? throw new InvalidOperationException("The disc's label needs its own measured depth.");

        var deepest = 0f;
        var mesh = disc.Meshes[0];
        for (var offset = 0; offset < mesh.Vertices.Length; offset += MeshGeometry.FloatsPerVertex)
        {
            if (mesh.Vertices[offset + 5] <= 0.5f)
            {
                continue;
            }

            var position = new Vector3(
                mesh.Vertices[offset], mesh.Vertices[offset + 1], mesh.Vertices[offset + 2]);
            var local = position - placement.Origin;
            var u = Vector3.Dot(local, placement.UEdge) / placement.UEdge.LengthSquared();
            var v = Vector3.Dot(local, placement.VEdge) / placement.VEdge.LengthSquared();
            if (u is < 0f or > 1f || v is < 0f or > 1f)
            {
                continue;
            }

            deepest = MathF.Max(deepest, -Vector3.Dot(local, placement.Normal));
        }

        Assert.True(
            allowance > deepest,
            $"The label reaches {allowance:F4} but its own face lies {deepest:F4} behind the "
            + "panel plane, so nothing inside the panel is printed at all.");
        Assert.True(
            allowance < disc.Size.Z * 0.5f,
            $"The label reaches {allowance:F4} of a {disc.Size.Z:F4} thick disc, which is far "
            + "enough through it to print on the data side as well.");
    }

    /// <summary>
    /// The disc's label draws the scraped disc artwork, not the box scan the case is wearing.
    /// </summary>
    /// <remarks>
    /// The two shells are on screen together during a launch and belong to the same game, so they
    /// index the same uploaded set of faces. Slot 0 is the box front — a picture of the packaging,
    /// which is the one thing a disc's printed face is never a picture of. This pins the disc onto
    /// its own slot and pins that slot to the face the app uploads it under: they live in different
    /// assemblies, one as a literal and one as an enum, and nothing but this holds them together.
    /// </remarks>
    [Fact]
    public void GameDisc_DrawsTheScrapedDiscLabelNotTheBoxScan()
    {
        var disc = MediaShellCatalog.Definition(MediaShell.GameDisc);
        var keepCase = MediaShellCatalog.Definition(MediaShell.DiscKeepCase);

        Assert.Equal((int)ShelfArtworkFace.DiscLabel, disc.CoverArtIndex);
        Assert.Equal((int)ShelfArtworkFace.Front, keepCase.CoverArtIndex);
        Assert.NotEqual(keepCase.CoverArtIndex, disc.CoverArtIndex);

        // And the set the app uploads into has room for it.
        Assert.True((int)ShelfArtworkFace.DiscLabel < EmuShelf.Rendering.MediaShellRenderer.MaxArtworkFaces);
    }

    /// <summary>
    /// A disc system's scraped support texture is routed to the disc, and a cartridge's to itself.
    /// </summary>
    /// <remarks>
    /// The same ScreenScraper media kind means different things on different media: on a cartridge
    /// system its support art is the cartridge's own label and belongs on the shell's front, while
    /// on a disc system it is a picture of the disc inside the box. Before this it was scraped for
    /// PS2, GameCube and Wii, stored, and then never drawn anywhere — there was no disc to put it on.
    /// </remarks>
    [Fact]
    public void DiscProfiles_ClaimTheSupportTextureForTheirDisc()
    {
        foreach (var system in new[] { "playstation2", "playstation3", "gamecube", "wii" })
        {
            var profile = MediaShellMap.ProfileForSystem(system, 0.708);
            Assert.True(
                (profile.ArtworkSlots & PhysicalArtworkSlots.DiscLabel) != 0,
                $"{system} has a disc but no slot to print its scraped label on.");
            // Its case wears the box scan, not the disc art.
            Assert.True((profile.ArtworkSlots & PhysicalArtworkSlots.Front) != 0);
            Assert.True((profile.ArtworkSlots & PhysicalArtworkSlots.CartridgeSupport) == 0);
        }

        var cartridge = MediaShellMap.ProfileForSystem("snes", 1.43);
        Assert.True((cartridge.ArtworkSlots & PhysicalArtworkSlots.DiscLabel) == 0);
        Assert.True((cartridge.ArtworkSlots & PhysicalArtworkSlots.CartridgeSupport) != 0);
    }

    /// <summary>
    /// A GameCube game ships on an 80mm mini-disc, and the shared stand-in case cannot show that.
    /// The disc it gives up can, which is the one place the difference becomes visible.
    /// </summary>
    [Fact]
    public void MetricProfiles_GiveEachDiscSystemItsOwnDiscSize()
    {
        var wii = MediaShellMap.ProfileForSystem("wii", 0.708);
        var gamecube = MediaShellMap.ProfileForSystem("gamecube", 0.708);
        var snes = MediaShellMap.ProfileForSystem("snes", 1.43);

        Assert.True(wii.HasDisc);
        Assert.True(gamecube.HasDisc);
        Assert.False(snes.HasDisc);

        Assert.Equal(120f / 190f, wii.DiscDiameterInShelfUnits, 3);
        Assert.Equal(80f / 190f, gamecube.DiscDiameterInShelfUnits, 3);
        // The cases are identical, so the discs are the only thing that can tell them apart.
        Assert.Equal(wii.HeightInShelfUnits, gamecube.HeightInShelfUnits, 3);
    }

    /// <summary>
    /// Only the media that really give up a disc take the disc choreography, and every one of them
    /// declares a disc for it to lift out.
    /// </summary>
    [Fact]
    public void MetricProfiles_AgreeAboutWhichMediaOpen()
    {
        string[] systems =
            ["snes", "gba", "gbc", "nes", "megadrive", "nds", "playstation2", "playstation3",
             "gamecube", "wii", "playstation", "dreamcast"];

        foreach (var system in systems)
        {
            var profile = MediaShellMap.ProfileForSystem(system, 0.708);
            var style = PhysicalShelfLaunchStyles.ForAnimation(profile.InsertionAnimationId);

            Assert.Equal(
                style == PhysicalShelfLaunchStyle.Disc,
                profile.HasDisc);
        }
    }

    private static IEnumerable<Vector3> Vertices(ModelAsset asset)
    {
        foreach (var mesh in asset.Meshes)
        {
            for (var offset = 0;
                 offset + 2 < mesh.Vertices.Length;
                 offset += MeshGeometry.FloatsPerVertex)
            {
                yield return new Vector3(
                    mesh.Vertices[offset], mesh.Vertices[offset + 1], mesh.Vertices[offset + 2]);
            }
        }
    }

    [Fact]
    public void MetricProfile_UsesAThinCoverCardForUnauthoredSystems()
    {
        // 3DS, because this test keeps being retargeted as systems graduate: it asked about PS1
        // until the jewel case was authored, then PSP until this branch gave it the keep case.
        // The aspect is passed explicitly, so any unauthored id does — what it must not be is an
        // id that has since acquired a shell, which is a silently passing test, not a failing one.
        var profile = MediaShellMap.ProfileForSystem("3ds", 1.0);

        Assert.Equal(MediaShell.CoverCard, profile.Shell);
        Assert.Equal(1f, profile.WidthInShelfUnits, 3);
        Assert.Equal(1f, profile.HeightInShelfUnits, 3);
        Assert.True(profile.DepthInShelfUnits < 0.03f);
    }

    /// <summary>
    /// PS3 stands at a real Blu-ray case's height, on a real Blu-ray case's geometry.
    /// </summary>
    /// <remarks>
    /// This test used to assert the opposite — that PS3 stood at a DVD case's 190mm and was told
    /// apart by its finish alone — and it was right to, for as long as the two shared one mesh:
    /// the scene scales each axis independently, so the truthful height came out as a 13.7%
    /// stretch, and too tall beat the wrong shape. Inverted rather than deleted, because the pair
    /// of them is the record of why a shell was worth sourcing at all. See DECISIONS 2026-08-15.
    /// <para>
    /// The width is the part worth stating explicitly: a Blu-ray case is exactly as wide as a DVD
    /// case and differs only in height and thickness, so a PS3 case that came out narrower than a
    /// PS2 one would be wrong in a way "it is shorter now" would not catch.
    /// </para>
    /// </remarks>
    [Fact]
    public void MetricProfile_StandsThePs3CaseShorterOnItsOwnBluRayGeometry()
    {
        var ps2 = MediaShellMap.ProfileForSystem("playstation2", 0.708);
        var ps3 = MediaShellMap.ProfileForSystem("playstation3", 0.708);

        Assert.Equal(MediaShell.DiscKeepCase, ps2.Shell);
        Assert.Equal(MediaShell.BluRayCase, ps3.Shell);
        Assert.NotEqual(ps2.MaterialVariant, ps3.MaterialVariant);

        // 171.5 against 190mm, asserted as the ratio so the check survives a re-measurement of
        // either case — the same way PSP's is.
        Assert.Equal(171.5f / 190f, ps3.HeightInShelfUnits / ps2.HeightInShelfUnits, 0.001f);
        Assert.True(ps3.HeightInShelfUnits < ps2.HeightInShelfUnits);
        Assert.Equal(ps2.WidthInShelfUnits, ps3.WidthInShelfUnits, 3);
        Assert.True(ps3.DepthInShelfUnits < ps2.DepthInShelfUnits);
    }

    /// <summary>
    /// PSP borrows the disc case's geometry but must not borrow its size.
    /// </summary>
    /// <remarks>
    /// The whole reason a UMD case is worth rendering rather than leaving on a flat cover is that
    /// it is visibly smaller than the disc cases it shares a shelf with, so a profile that let it
    /// stand at 190mm would have bought the geometry and thrown away the point. This is the cheap
    /// half of PSP's contract and the one that would survive any later change of mind about the
    /// mesh squeeze: whatever the case's width ends up being, it is not a disc case's size.
    /// </remarks>
    [Fact]
    public void MetricProfile_StandsThePspCaseShorterThanADiscCase()
    {
        var ps2 = MediaShellMap.ProfileForSystem("playstation2", 0.708);
        var psp = MediaShellMap.ProfileForSystem("psp", 0.708);

        Assert.Equal(ps2.Shell, psp.Shell);
        Assert.True(psp.HeightInShelfUnits < ps2.HeightInShelfUnits);
        Assert.True(psp.WidthInShelfUnits < ps2.WidthInShelfUnits);
        Assert.NotEqual(ps2.MaterialVariant, psp.MaterialVariant);

        // A real UMD case is 178mm against a DVD case's 190mm, and that 6.3% is the difference the
        // shelf is being asked to show. Asserted as the ratio rather than the millimetres so the
        // check survives a later re-measurement of either case.
        Assert.Equal(178f / 190f, psp.HeightInShelfUnits / ps2.HeightInShelfUnits, 0.001f);
    }

    /// <summary>
    /// PSP takes a real UMD case's shape, and pays for it with a known squeeze of the shared mesh.
    /// </summary>
    /// <remarks>
    /// The counterweight to the proportion theory above, and the reason PSP is excluded from it.
    /// That theory is right for a cartridge, where the moulding is the object. A keep case is a flat
    /// sleeve filling nearly the whole silhouette with a rim a few pixels wide around it, so the
    /// question is not "is the mesh undistorted" but "which of the mesh and the artwork should carry
    /// the error". The keep case's cover panel is ArtFit.Stretch, so the profile's own width/height
    /// is the shape every scraped cover is pulled to: at 104mm that lands on a PSP box scan almost
    /// exactly, and at the asset's own 0.695 it would stretch every cover about 20% wider.
    ///
    /// Both were rendered before this was chosen. The assertions are the two halves of the trade —
    /// the art fits, and the mesh distortion is exactly the one accepted, not a new one.
    /// </remarks>
    [Fact]
    public void MetricProfile_TakesARealUmdCasesShapeToKeepItsSleeveUndistorted()
    {
        var profile = MediaShellMap.ProfileForSystem("psp", 0.581);
        var asset = MediaShellCatalog.Load(profile.Shell);

        var panelAspect = profile.DimensionsMillimetres.X / profile.DimensionsMillimetres.Y;
        var scrapedCoverAspect = (float)KnownSystems.All.Single(system => system.Id == "psp")
            .CoverAspectRatio;

        // What the squeeze buys. Sourced from KnownSystems rather than repeated as a literal, so
        // that re-measuring a UMD case in one place cannot leave the two silently disagreeing.
        Assert.Equal(scrapedCoverAspect, panelAspect, 0.01f * scrapedCoverAspect);

        // All three axes are a real UMD case, which is the whole point: this profile is not a
        // compromise between the case and the asset, it is the case, and the asset bends to it.
        Assert.Equal(new Vector3(104f, 178f, 15f), profile.DimensionsMillimetres);

        // What it costs, pinned so an accidental change cannot hide inside the accepted one. The
        // mesh is drawn at 84% of its authored width. Measured against the asset rather than
        // against the PS2 profile — those differ, 0.695 to 0.711, and taking the profile's figure
        // is how this was first written down as an 18% squeeze when the real one is 16%.
        Assert.Equal(0.841f, panelAspect / (asset.Size.X / asset.Size.Y), 0.005f);
    }

    /// <summary>
    /// The headless preview tool renders the profiles the app actually uses.
    /// </summary>
    /// <remarks>
    /// `EmuShelf.Rendering.Preview` hand-copies <see cref="MediaShellMap"/> because it cannot
    /// reference the app — EmuShelf.App is an Avalonia WinExe with a git-stamping build target, and
    /// dragging the whole UI into a headless tool to read one static table is the worse trade. This
    /// project can see both, so it is the only place the copy can be checked.
    ///
    /// It needs checking because it has gone stale twice, and both times silently. It kept
    /// pre-correction GBA and SNES figures for a whole milestone, so the acceptance shot was
    /// showing proportions the app had already abandoned — the one artefact a reviewer trusts to
    /// tell them what shipped, quietly showing something else. It was also still naming insertion
    /// animations (`case-vertical`, `cover-card`) that the app had renamed. A stale preview is
    /// worse than no preview: it launders a wrong render into an approved one.
    ///
    /// Order is deliberately not asserted. It is a review decision — the Mega Drive sits beside the
    /// SNES cartridge, PSP beside the PS2 case — and pinning it here would turn a framing choice
    /// into a test failure.
    /// </remarks>
    [Fact]
    public void PreviewShelf_RendersTheSameProfilesTheAppDoes()
    {
        Assert.NotEmpty(PreviewShelf.Entries);

        foreach (var entry in PreviewShelf.Entries)
        {
            var expected = MediaShellMap.ProfileForSystem(entry.SystemId, entry.CoverAspect);
            Assert.Equal(expected, entry.Profile);
        }
    }

    /// <summary>
    /// Every system with an authored shell appears in the acceptance shot.
    /// </summary>
    /// <remarks>
    /// The other half of the drift, and the one a value-by-value comparison cannot see: an entry
    /// that is simply absent. A shell nobody draws is a shell nobody reviews, and this list has
    /// twice been the reason a medium went unlooked-at — the Mega Drive was off the right-hand edge
    /// of the frame while carrying a profile a quarter too big, and the Game Boy shell landed in
    /// the same blind spot when it was appended last.
    /// </remarks>
    [Fact]
    public void PreviewShelf_DrawsEverySystemThatHasAnAuthoredShell()
    {
        var authored = MediaShellMap.MappedSystemIds.ToHashSet(StringComparer.Ordinal);

        var drawn = PreviewShelf.Entries
            .Select(entry => entry.SystemId)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(authored.Except(drawn));
    }

    /// <summary>
    /// Every system the shell table names is a system that exists.
    /// </summary>
    /// <remarks>
    /// The hole underneath the two tests above, and the reason they read the keys directly rather
    /// than filtering <see cref="KnownSystems"/> through <see cref="MediaShellMap.ForSystem"/> as
    /// they first did. An entry filed under an id no system has is unreachable, not wrong: nothing
    /// ever calls `ForSystem` with it, so the platform keeps a flat cover and looks precisely like
    /// one that was never given a shell. Filtering a known-systems list through the map cannot see
    /// that — the bad key is absent from both sides of the comparison — so a typo here would have
    /// passed every check in this file while silently removing a medium from the shelf.
    /// </remarks>
    [Fact]
    public void MediaShellMap_OnlyNamesSystemsThatExist()
    {
        var known = KnownSystems.All.Select(system => system.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(MediaShellMap.MappedSystemIds.Except(known));
    }

    [Theory]
    [InlineData(MediaShell.SnesCartridge)]
    [InlineData(MediaShell.GbaCartridge)]
    [InlineData(MediaShell.DiscKeepCase)]
    // The trimmed shell has to be normalised against what is left of it, not what was authored:
    // measuring the cut cabinet against the whole machine's height would leave it half a unit tall
    // and hovering above the shelf floor.
    [InlineData(MediaShell.ArcadeCabinet)]
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

    /// <summary>
    /// A NES cartridge is portrait — 120mm across, 135mm tall — and its label reads upright.
    /// </summary>
    /// <remarks>
    /// Regression test for the orientation, which is the part of sourcing a shell that cannot be
    /// reasoned out: this model's UV winding and its vertex normals disagreed about which way was
    /// up, and only rendering it settled the question. A quarter turn the wrong way lays the
    /// cartridge on its side with the label reading bottom-to-top.
    /// </remarks>
    [Fact]
    public void Load_StandsTheNesCartridgePortrait()
    {
        var model = MediaShellCatalog.Load(MediaShell.NesCartridge);

        Assert.True(
            model.Size.Y > model.Size.X,
            $"A NES cartridge is taller than it is wide; got {model.Size.X} x {model.Size.Y}.");
        Assert.InRange(model.Size.X, 0.87f, 0.91f);
        Assert.InRange(model.Size.Z, 0.12f, 0.15f);
        // Two materials: the shell, and the label plate whose artwork the prep flattens.
        Assert.Equal(2, model.Materials.Count);
    }

    /// <summary>
    /// The shipped NES asset must carry no trace of the game artwork its source was modelled from.
    /// </summary>
    /// <remarks>
    /// The author's CC BY licence covers the model, not Rare's Battletoads cover, so the label had
    /// to go before the derivative could be committed. It lived on its own material, so the check
    /// is simply that the material's maps are now flat.
    /// </remarks>
    [Fact]
    public void NesLabelPlate_CarriesNoSourceArtwork()
    {
        var model = MediaShellCatalog.Load(MediaShell.NesCartridge);
        var sticker = model.Materials.Single(
            material => string.Equals(material.Name, "sticker", StringComparison.OrdinalIgnoreCase));
        var texture = model.Textures[sticker.BaseColorTexture];

        var first = (texture.Rgba[0], texture.Rgba[1], texture.Rgba[2]);
        for (var offset = 0; offset < texture.Rgba.Length; offset += 4)
        {
            if ((texture.Rgba[offset], texture.Rgba[offset + 1], texture.Rgba[offset + 2]) != first)
            {
                Assert.Fail(
                    $"The NES label plate still varies at byte {offset}; its source artwork was not flattened.");
            }
        }
    }

    /// <summary>
    /// A Mega Drive cartridge is landscape, and this asset needs no reorientation at all.
    /// </summary>
    /// <remarks>
    /// Rolling it 180 degrees puts the MEGA DRIVE band at the top, where a European label carries
    /// it, which looks like the correction — but it turns Sonic upside down. The artwork's own
    /// orientation is the test, and identity is what keeps it upright.
    /// </remarks>
    [Fact]
    public void Load_LeavesTheMegaDriveCartridgeUpright()
    {
        var model = MediaShellCatalog.Load(MediaShell.MegaDriveCartridge);

        Assert.True(model.Size.X > model.Size.Y);
        Assert.InRange(model.Size.X, 1.53f, 1.58f);
        Assert.InRange(model.Size.Z, 0.15f, 0.19f);
    }

    /// <summary>
    /// The shipped Mega Drive asset must carry no trace of the Sonic 2 artwork it was modelled from.
    /// </summary>
    /// <remarks>
    /// This shell has one material and one atlas, so its label could not be removed the way NES's
    /// was — it needed a rectangle, which is the fallback precisely because a wrong one either
    /// leaves the artwork in the build or erases moulding. The assertion samples well inside that
    /// rectangle, away from the eroded edge, and requires it to be flat.
    /// </remarks>
    [Fact]
    public void MegaDriveLabelArea_CarriesNoSourceArtwork()
    {
        var model = MediaShellCatalog.Load(MediaShell.MegaDriveCartridge);
        var material = model.Materials.First(candidate => candidate.BaseColorTexture >= 0);
        var texture = model.Textures[material.BaseColorTexture];

        (byte R, byte G, byte B) Sample(float u, float v)
        {
            var x = Math.Clamp((int)(u * texture.Width), 0, texture.Width - 1);
            var y = Math.Clamp((int)(v * texture.Height), 0, texture.Height - 1);
            var offset = ((y * texture.Width) + x) * 4;
            return (texture.Rgba[offset], texture.Rgba[offset + 1], texture.Rgba[offset + 2]);
        }

        // The rectangle the prep was asked to clear, sampled to its very edges. Sampling the safe
        // middle is what let an eroded mask ship: it left a ring of the original artwork exactly
        // where this now looks.
        const float u0 = 0.11f, u1 = 0.71f, v0 = 0.59f, v1 = 0.97f;
        var reference = Sample(0.40f, 0.78f);
        for (var u = u0; u <= u1; u += 0.01f)
        {
            foreach (var v in new[] { v0, (v0 + v1) * 0.5f, v1 })
            {
                Assert.True(
                    Sample(u, v) == reference,
                    $"The Mega Drive label area still varies at ({u:F2},{v:F2}); artwork was not removed.");
            }
        }

        for (var v = v0; v <= v1; v += 0.01f)
        {
            foreach (var u in new[] { u0, u1 })
            {
                Assert.True(
                    Sample(u, v) == reference,
                    $"The Mega Drive label edge still varies at ({u:F2},{v:F2}); the mask is too small.");
            }
        }
    }

    /// <summary>
    /// The DS card loads upright, roughly square, and thin.
    /// </summary>
    /// <remarks>
    /// satchii_'s card carries its orientation in its node matrices — the raw accessor bounds say it
    /// is lying flat, and reading those rather than the loaded model cost a wrong rotation once — so
    /// it needs only a half turn about Y to bring the label round from -Z. A shell that loads on its
    /// side still fills a plausible-looking bounding box, so this pins the axes rather than trusting
    /// the render.
    /// </remarks>
    [Fact]
    public void Load_StandsTheDsCardUpright()
    {
        var model = MediaShellCatalog.Load(MediaShell.DsCard);

        // Near square: this asset is 0.960 W/H where a real 33.4 x 35mm card is 0.954.
        Assert.InRange(model.Size.X / model.Size.Y, 0.94f, 0.98f);
        Assert.True(
            model.Size.Z < 0.12f * model.Size.Y,
            $"A DS card is thin; got depth {model.Size.Z} against height {model.Size.Y}.");
    }

    /// <summary>
    /// The shipped DS asset must carry no trace of the Super Mario 64 DS label it was scanned from.
    /// </summary>
    /// <remarks>
    /// This model keeps its label on the same atlas and material as its body, so the clean route the
    /// NES shell takes — flattening a dedicated material — is not available and the label goes by a
    /// hand-read rectangle. That is the fallback, and the mode it fails in is a sliver left along one
    /// edge, so this walks the rectangle's edges rather than sampling its middle.
    /// </remarks>
    [Fact]
    public void DsLabelArea_CarriesNoSourceArtwork()
    {
        var model = MediaShellCatalog.Load(MediaShell.DsCard);
        var material = model.Materials.First(candidate => candidate.BaseColorTexture >= 0);
        var texture = model.Textures[material.BaseColorTexture];

        (byte R, byte G, byte B) Sample(float u, float v)
        {
            var x = Math.Clamp((int)(u * texture.Width), 0, texture.Width - 1);
            var y = Math.Clamp((int)(v * texture.Height), 0, texture.Height - 1);
            var offset = ((y * texture.Width) + x) * 4;
            return (texture.Rgba[offset], texture.Rgba[offset + 1], texture.Rgba[offset + 2]);
        }

        const float u0 = 0.0605f, u1 = 0.4795f, v0 = 0.0298f, v1 = 0.4795f;
        var reference = Sample((u0 + u1) * 0.5f, (v0 + v1) * 0.5f);
        for (var u = u0; u <= u1; u += 0.01f)
        {
            foreach (var v in new[] { v0, (v0 + v1) * 0.5f, v1 })
            {
                Assert.True(
                    Sample(u, v) == reference,
                    $"The DS label area still varies at ({u:F3},{v:F3}); artwork was not removed.");
            }
        }

        for (var v = v0; v <= v1; v += 0.01f)
        {
            foreach (var u in new[] { u0, u1 })
            {
                Assert.True(
                    Sample(u, v) == reference,
                    $"The DS label edge still varies at ({u:F3},{v:F3}); the mask is too small.");
            }
        }
    }

    /// <summary>
    /// The DS artwork panel has to land on the label's own footprint and stay off the bare plastic.
    /// </summary>
    /// <remarks>
    /// Unlike the template shell this replaced, the label here is a printed sticker rather than a
    /// moulded recess, so it carries the NINTENDO DS band itself and the panel runs to the sticker's
    /// own top edge. The footprint was measured twice and to the same place: off the atlas through
    /// the face quad's UV mapping, and off a straight-on render of the asset with its label still on.
    /// This pins the panel to that footprint, so a future re-fit cannot silently spill onto the
    /// plastic frame or shrink away from it.
    /// </remarks>
    [Fact]
    public void DsCoverPanel_CoversTheLabelFootprint()
    {
        var panel = MediaShellCatalog.Definition(MediaShell.DsCard).CoverPanel;

        Assert.InRange(panel.MaxV, 0.88f, 0.93f);
        Assert.InRange(panel.MinV, -0.83f, -0.78f);
        Assert.InRange(panel.MaxU, 0.81f, 0.86f);
        Assert.InRange(panel.MinU, -0.86f, -0.81f);
        // A DS label is chamfered at the bottom left; squaring it stops the card reading as a DS
        // card, and oversizing it bites a wedge out of the artwork.
        Assert.InRange(panel.CutCorner, 0.06f, 0.10f);
    }

    /// <summary>
    /// The masked rectangle has to contain the artwork panel on every side.
    /// </summary>
    /// <remarks>
    /// The two are derived from different things — the mask from the atlas, the panel from the
    /// geometry — so they never agree exactly, and whichever way they disagree is what shows. If the
    /// panel spills past the mask the source label reappears around EmuShelf's own artwork, which is
    /// the failure this shell shipped once as a paper-grey halo. Pinning the margin as well as the
    /// order keeps the fill's near-invisibility a property rather than a coincidence: it is the
    /// card's own plastic colour, so a millimetre of it around the label reads as plastic.
    /// </remarks>
    [Fact]
    public void DsMaskedRectangle_ContainsTheArtworkPanel()
    {
        var panel = MediaShellCatalog.Definition(MediaShell.DsCard).CoverPanel;

        // The prep command's --neutral-rect in panel coordinates. Not computed from the UV mapping:
        // that route put the bottom edge 0.02 out, because it cannot see the few texels of bleed the
        // prep grows the fill by. These come off a render of the asset prepared with a magenta fill,
        // which measures where the mask actually lands on the shipped shell. Recorded in DECISIONS
        // 2026-08-15 alongside the command itself.
        const float maskMinU = -0.919f, maskMaxU = 0.870f, maskMinV = -0.853f, maskMaxV = 0.953f;

        Assert.True(panel.MinU > maskMinU, "The DS panel reaches left of the masked rectangle.");
        Assert.True(panel.MaxU < maskMaxU, "The DS panel reaches right of the masked rectangle.");
        Assert.True(panel.MinV > maskMinV, "The DS panel reaches below the masked rectangle.");
        Assert.True(panel.MaxV < maskMaxV, "The DS panel reaches above the masked rectangle.");

        // A wide margin is its own failure: the fill only reads as plastic while it is a hairline.
        Assert.InRange(panel.MinU - maskMinU, 0.01f, 0.12f);
        Assert.InRange(maskMaxU - panel.MaxU, 0.01f, 0.12f);
        Assert.InRange(panel.MinV - maskMinV, 0.01f, 0.12f);
        Assert.InRange(maskMaxV - panel.MaxV, 0.01f, 0.12f);
    }

    /// <summary>
    /// The shipped GBA asset must carry no trace of the Pokémon FireRed label it was modelled from.
    /// </summary>
    /// <remarks>
    /// The first mask for this shell was two hundredths of a UV short on its right and bottom edges,
    /// which left an L-shaped sliver of the label around EmuShelf's own artwork — visible in the
    /// render, and the reason these checks walk the rectangle's edges rather than its middle.
    /// </remarks>
    [Fact]
    public void GbaLabelArea_CarriesNoSourceArtwork()
    {
        var model = MediaShellCatalog.Load(MediaShell.GbaCartridge);
        var material = model.Materials.First(candidate => candidate.BaseColorTexture >= 0);
        var texture = model.Textures[material.BaseColorTexture];

        (byte R, byte G, byte B) Sample(float u, float v)
        {
            var x = Math.Clamp((int)(u * texture.Width), 0, texture.Width - 1);
            var y = Math.Clamp((int)(v * texture.Height), 0, texture.Height - 1);
            var offset = ((y * texture.Width) + x) * 4;
            return (texture.Rgba[offset], texture.Rgba[offset + 1], texture.Rgba[offset + 2]);
        }

        const float u0 = 0.088f, u1 = 0.538f, v0 = 0.376f, v1 = 0.640f;
        var reference = Sample((u0 + u1) * 0.5f, (v0 + v1) * 0.5f);
        for (var u = u0; u <= u1; u += 0.01f)
        {
            foreach (var v in new[] { v0, (v0 + v1) * 0.5f, v1 })
            {
                Assert.True(
                    Sample(u, v) == reference,
                    $"The GBA label area still varies at ({u:F2},{v:F2}); artwork was not removed.");
            }
        }

        for (var v = v0; v <= v1; v += 0.01f)
        {
            foreach (var u in new[] { u0, u1 })
            {
                Assert.True(
                    Sample(u, v) == reference,
                    $"The GBA label edge still varies at ({u:F2},{v:F2}); the mask is too small.");
            }
        }
    }

    /// <summary>
    /// A Game Boy cartridge is portrait — 57mm across, 65mm tall — and needs no reorientation.
    /// </summary>
    /// <remarks>
    /// This is the check that the shell in <c>models/gbc</c> is the Game Boy cartridge it is now
    /// meant to be. The folder previously held a GBA Game Pak, whose 1.748 width/height is the
    /// reciprocal neighbourhood of this one's 0.885 — so a swap back would show up here rather than
    /// as a cartridge that merely looks a bit wide.
    /// </remarks>
    [Fact]
    public void Load_StandsTheGameBoyCartridgePortrait()
    {
        var model = MediaShellCatalog.Load(MediaShell.GbcCartridge);

        Assert.True(
            model.Size.Y > model.Size.X,
            $"A Game Boy cartridge is taller than it is wide; got {model.Size.X} x {model.Size.Y}.");
        Assert.InRange(model.Size.X, 0.86f, 0.91f);
        Assert.InRange(model.Size.Z, 0.12f, 0.16f);
    }

    /// <summary>
    /// The shipped Game Boy asset must carry no trace of the Super Mario Land 2 label it was
    /// modelled from.
    /// </summary>
    /// <remarks>
    /// One material and one shared atlas, like the Mega Drive shell, so the label went by masking a
    /// rectangle — the fallback technique, and the one that fails silently. The rectangle here was
    /// measured rather than eyeballed: the label is the only saturated island on an otherwise flat
    /// grey atlas, so its bounds came out of a sweep for pixels differing from the plastic. This
    /// walks that rectangle's edges, which is where a mask that is fractionally too small shows.
    /// </remarks>
    [Fact]
    public void GbcLabelArea_CarriesNoSourceArtwork()
    {
        var model = MediaShellCatalog.Load(MediaShell.GbcCartridge);
        var material = model.Materials.First(candidate => candidate.BaseColorTexture >= 0);
        var texture = model.Textures[material.BaseColorTexture];

        (byte R, byte G, byte B) Sample(float u, float v)
        {
            var x = Math.Clamp((int)(u * texture.Width), 0, texture.Width - 1);
            var y = Math.Clamp((int)(v * texture.Height), 0, texture.Height - 1);
            var offset = ((y * texture.Width) + x) * 4;
            return (texture.Rgba[offset], texture.Rgba[offset + 1], texture.Rgba[offset + 2]);
        }

        const float u0 = 0.510f, u1 = 0.848f, v0 = 0.169f, v1 = 0.4885f;
        var reference = Sample((u0 + u1) * 0.5f, (v0 + v1) * 0.5f);
        for (var u = u0; u <= u1; u += 0.01f)
        {
            foreach (var v in new[] { v0, (v0 + v1) * 0.5f, v1 })
            {
                Assert.True(
                    Sample(u, v) == reference,
                    $"The Game Boy label area still varies at ({u:F3},{v:F3}); artwork was not removed.");
            }
        }

        for (var v = v0; v <= v1; v += 0.01f)
        {
            foreach (var u in new[] { u0, u1 })
            {
                Assert.True(
                    Sample(u, v) == reference,
                    $"The Game Boy label edge still varies at ({u:F3},{v:F3}); the mask is too small.");
            }
        }
    }

    /// <summary>
    /// The jewel case must load landscape, shut, and carrying only the case.
    /// </summary>
    /// <remarks>
    /// Three defects in one source and all three silent. The download is a case with its disc lying
    /// in the tray, so without the drop it loads a case-and-disc diorama. It is posed for a product
    /// shot with its lid 25 degrees open, which measures 66mm thick against a real case's 10mm — and
    /// no profile can fix that, since the scene scales each axis independently and would squash the
    /// whole case rather than shut the lid. The depth bound is the one that matters: it is what
    /// fails if the lid ever stops being closed.
    /// </remarks>
    [Fact]
    public void Load_ClosesTheJewelCaseAndKeepsOnlyTheCase()
    {
        var model = MediaShellCatalog.Load(MediaShell.JewelCase);

        // A CD jewel case is landscape — 142mm across, 125mm tall — unlike every keep case.
        Assert.True(
            model.Size.X > model.Size.Y,
            $"A jewel case is wider than it is tall; got {model.Size.X} x {model.Size.Y}.");
        Assert.Equal(142f / 125f, model.Size.X / model.Size.Y, 0.01f);
        // Shut, this is 0.072. Ajar, as the source ships it, it is 0.533.
        Assert.True(
            model.Size.Z < 0.09f,
            $"The jewel case's lid is not shut: it loads {model.Size.Z} deep per unit of height.");
    }

    /// <summary>
    /// The shipped jewel case must carry no trace of the game its source was modelled around.
    /// </summary>
    /// <remarks>
    /// This shell shipped once with its artwork intact, on the argument that sodaraptor wrote the
    /// game as well as the case and licensed both. The licence was never the problem: it put one
    /// game's cover, back inlay, spine title and a fictional "DreamStation" console mark on every
    /// PS1 and Dreamcast game in the library. The three printed maps are masked by rectangle, which
    /// is the fallback precisely because a wrong one either leaves the artwork in the build or
    /// erases the plastic beside it — so this samples to the very edge of each, not its safe middle.
    /// </remarks>
    [Theory]
    // The lid, whose print starts at the plastic seam inboard of the hinge.
    [InlineData("01_-_Default", 0.19f, 0.99f)]
    // The tray inlay, which begins further out because the hinge is not in front of it.
    [InlineData("02_-_Default", 0.12f, 0.99f)]
    // The promo card behind the lid, whose every sampled texel is print.
    [InlineData("03_-_Default", 0.01f, 0.99f)]
    public void JewelCasePrintedArea_CarriesNoSourceArtwork(string materialName, float u0, float u1)
    {
        var model = MediaShellCatalog.Load(MediaShell.JewelCase);
        var material = model.Materials.Single(candidate => candidate.Name == materialName);
        var texture = model.Textures[material.BaseColorTexture];

        (byte R, byte G, byte B) Sample(float u, float v)
        {
            var x = Math.Clamp((int)(u * texture.Width), 0, texture.Width - 1);
            var y = Math.Clamp((int)(v * texture.Height), 0, texture.Height - 1);
            var offset = ((y * texture.Width) + x) * 4;
            return (texture.Rgba[offset], texture.Rgba[offset + 1], texture.Rgba[offset + 2]);
        }

        var reference = Sample((u0 + u1) * 0.5f, 0.5f);
        for (var u = u0; u <= u1; u += 0.01f)
        {
            foreach (var v in new[] { 0.01f, 0.5f, 0.99f })
            {
                Assert.True(
                    Sample(u, v) == reference,
                    $"'{materialName}' still varies at ({u:F2},{v:F2}); source artwork was not removed.");
            }
        }
    }

    /// <summary>
    /// Flattening the print must not take the case's own plastic with it.
    /// </summary>
    /// <remarks>
    /// The counterweight to the test above, and the reason the mask is a rectangle rather than the
    /// whole map: this model paints the clear outer edge and the moulded hinge teeth into the same
    /// atlas as the insert. They are the entire difference between a jewel case and a grey slab, and
    /// they live to the left of every mask. Three earlier candidates were rejected for going blank
    /// under exactly this check.
    /// </remarks>
    [Fact]
    public void JewelCaseHinge_SurvivesTheFlattening()
    {
        var model = MediaShellCatalog.Load(MediaShell.JewelCase);
        var material = model.Materials.Single(candidate => candidate.Name == "01_-_Default");
        var texture = model.Textures[material.BaseColorTexture];

        var samples = new HashSet<(byte, byte, byte)>();
        for (var u = 0.01f; u < 0.17f; u += 0.005f)
        {
            for (var v = 0.05f; v < 0.95f; v += 0.05f)
            {
                var x = Math.Clamp((int)(u * texture.Width), 0, texture.Width - 1);
                var y = Math.Clamp((int)(v * texture.Height), 0, texture.Height - 1);
                var offset = ((y * texture.Width) + x) * 4;
                samples.Add((texture.Rgba[offset], texture.Rgba[offset + 1], texture.Rgba[offset + 2]));
            }
        }

        Assert.True(
            samples.Count > 100,
            $"The jewel case's hinge and outer plastic are flat ({samples.Count} distinct colours); "
            + "the mask has eaten the detail the shell was chosen for.");
    }

    /// <summary>
    /// The arcade cabinet arrives as the top half of a machine, cut off under its control panel.
    /// </summary>
    /// <remarks>
    /// Two things are being checked, and only together do they mean the cut landed in the right
    /// place. The proportions say how much came off: the authored cabinet is 0.355 wide per unit of
    /// height, and halving its height doubles that to 0.711. And the material census says what
    /// survived — a cut a little too high takes the joysticks and buttons with it, which is the
    /// failure that still leaves a plausible-looking object.
    ///
    /// The clip is a real clip, not a triangle filter: the vertices left sitting exactly on the cut
    /// plane are the ones it created, and dropping straddling triangles instead would leave a torn
    /// edge and none of them.
    /// </remarks>
    [Fact]
    public void Load_CutsTheArcadeCabinetOffUnderItsControlPanel()
    {
        var model = MediaShellCatalog.Load(MediaShell.ArcadeCabinet);

        Assert.Equal(0.711f, model.Size.X / model.Size.Y, 0.02f);
        Assert.Equal(1.110f, model.Size.Z / model.Size.Y, 0.03f);

        // Everything that says "arcade machine" has to be above the cut.
        foreach (var part in new[] { "banner", "speakers", "screen", "game_panel", "butt_a", "butt_b" })
        {
            var material = model.Materials
                .Select((candidate, index) => (candidate, index))
                .Where(entry => string.Equals(
                    entry.candidate.Name, part, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.index)
                .DefaultIfEmpty(-1)
                .First();
            Assert.True(material >= 0, $"The cabinet lost its '{part}' material entirely.");
            Assert.True(
                model.Meshes.Any(mesh => mesh.MaterialIndex == material && mesh.TriangleCount > 0),
                $"The cut removed every triangle of the cabinet's '{part}'.");
        }

        var onTheCut = model.Meshes
            .SelectMany(mesh => Enumerable
                .Range(0, mesh.Vertices.Length / MeshGeometry.FloatsPerVertex)
                .Select(vertex => mesh.Vertices[(vertex * MeshGeometry.FloatsPerVertex) + 1]))
            .Count(y => MathF.Abs(y - model.BoundsMin.Y) < 1e-4f);
        Assert.True(
            onTheCut > 24,
            $"Only {onTheCut} vertices lie on the cut plane; straddling triangles are being dropped "
            + "rather than clipped, which leaves the cabinet with a ragged base.");
    }

    /// <summary>
    /// A cabinet's artwork goes on its screen, and nowhere else on the machine.
    /// </summary>
    /// <remarks>
    /// This is the whole reason a panel can name a material. The screen is two fifths of the
    /// cabinet's height, sunk a quarter of its depth behind the bezel, with a marquee above it and
    /// a control panel below — so a rectangle measured against the cabinet, however carefully, is a
    /// rectangle over all three of those as well. Naming the material both scopes the print to the
    /// glass and
    /// measures the rectangle against the glass, which is why the numbers in the catalogue are
    /// ±0.99 rather than four figures nobody could check.
    /// </remarks>
    [Fact]
    public void ArcadeArtwork_LandsOnTheScreenAndNowhereElse()
    {
        var model = MediaShellCatalog.Load(MediaShell.ArcadeCabinet);
        var panel = MediaShellCatalog.Definition(MediaShell.ArcadeCabinet).CoverPanel;

        Assert.Equal("screen", panel.Material);

        var (screenMin, screenMax) = MediaShellCatalog.MaterialBounds(model, "screen");
        var placement = MediaShellCatalog.Place(panel, model);

        // Against the screen, not the cabinet: the print covers nearly all of the glass, and the
        // glass is 80% of the cabinet's width but only two fifths of its height.
        Assert.Equal(screenMax.X - screenMin.X, placement.UEdge.Length(), 0.02f);
        Assert.True(
            placement.VEdge.Length() < model.Size.Y * 0.5f,
            "The screen print is half the height of the cabinet; the panel is being measured "
            + "against the whole model rather than the screen's own mesh.");

        // A cabinet screen is 4:3, which is also the shape of the title screen the arcade scraper
        // projects to the cover. It reads slightly wider than 1.333 because the tube leans back and
        // the panel is measured on the vertical, which is the same foreshortening the player sees.
        Assert.InRange(placement.UEdge.Length() / placement.VEdge.Length(), 1.33f, 1.45f);

        // The print sits on the glass, which is well inside the machine's own front face.
        Assert.True(
            screenMax.Z < model.BoundsMax.Z - (model.Size.Z * 0.2f),
            "The screen is no longer recessed inside the cabinet; the model changed.");
    }

    /// <summary>
    /// The cut leaves a floor, not a hole.
    /// </summary>
    /// <remarks>
    /// Shipped open once, on the reasoning that the cut face is the face the cabinet stands on. It
    /// is not: the medium turns as it launches, and an open shell reads as a cardboard mock-up the
    /// moment its underside comes round. The cap is the convex hull of the cut, so it is allowed to
    /// be larger than the true outline but never smaller than most of the footprint — a cap that
    /// silently degenerated to a sliver would still satisfy "a mesh exists".
    /// </remarks>
    [Fact]
    public void ArcadeCabinet_IsClosedWhereItWasCut()
    {
        var model = MediaShellCatalog.Load(MediaShell.ArcadeCabinet);

        var cap = model.Meshes.SingleOrDefault(mesh => Enumerable
            .Range(0, mesh.Vertices.Length / MeshGeometry.FloatsPerVertex)
            .All(vertex =>
                MathF.Abs(mesh.Vertices[(vertex * MeshGeometry.FloatsPerVertex) + 1] - model.BoundsMin.Y)
                    < 1e-4f
                && mesh.Vertices[(vertex * MeshGeometry.FloatsPerVertex) + 4] < -0.9f));
        Assert.NotNull(cap);

        var area = 0f;
        for (var index = 0; index + 2 < cap.Indices.Length; index += 3)
        {
            var a = VertexAt(cap, cap.Indices[index]);
            var b = VertexAt(cap, cap.Indices[index + 1]);
            var c = VertexAt(cap, cap.Indices[index + 2]);
            area += Vector3.Cross(b - a, c - a).Length() * 0.5f;
        }

        var footprint = model.Size.X * model.Size.Z;
        Assert.True(
            area > footprint * 0.5f,
            $"The base covers {area:F3} of the cabinet's {footprint:F3} footprint; the cut is "
            + "mostly open again.");

        // It wears the machine's own material rather than an invented one — and specifically the
        // cabinet body, which is what the cut is mostly made of. The material is chosen by which
        // one contributed most of the cut vertices, so a different trim height could in principle
        // hand the underside to the control panel's wood or the marquee's black.
        var material = model.Materials[cap.MaterialIndex];
        Assert.Equal("body_main", material.Name);

        // And the one texel it samples has to be the cabinet's plastic. A constant UV is only a
        // good idea while it lands somewhere representative; if it drifted onto a bright part of
        // the atlas the machine would stand on a glowing white base.
        var texture = model.Textures[material.BaseColorTexture];
        var u = Math.Clamp((int)(cap.Vertices[6] * texture.Width), 0, texture.Width - 1);
        var v = Math.Clamp((int)(cap.Vertices[7] * texture.Height), 0, texture.Height - 1);
        var texel = ((v * texture.Width) + u) * 4;
        Assert.True(
            texture.Rgba[texel] < 140 && texture.Rgba[texel + 1] < 140 && texture.Rgba[texel + 2] < 140,
            $"The base samples ({texture.Rgba[texel]}, {texture.Rgba[texel + 1]}, "
            + $"{texture.Rgba[texel + 2]}), which is not this cabinet's dark plastic.");
    }

    private static Vector3 VertexAt(MeshGeometry mesh, uint index)
    {
        var offset = (int)index * MeshGeometry.FloatsPerVertex;
        return new Vector3(
            mesh.Vertices[offset], mesh.Vertices[offset + 1], mesh.Vertices[offset + 2]);
    }

    /// <summary>
    /// A shelf row reserves what a medium sweeps when turned, not the width of its face.
    /// </summary>
    /// <remarks>
    /// Every medium on this shelf is turned — 0.18 radians at rest, and three full revolutions when
    /// the focused one launches. For packaging the difference is nothing, which is why the row
    /// reserved face width for a year without a complaint; the arcade cabinet is deeper than it is
    /// wide, and at face width its neighbours stood inside it.
    /// </remarks>
    [Fact]
    public void TurningWidth_CoversEveryAngleAMediumCanBeTurnedTo()
    {
        foreach (var systemId in new[] { "arcade", "playstation2", "snes", "nes", "gbc", "nds" })
        {
            var profile = MediaShellMap.ProfileForSystem(systemId, 0.708);
            for (var angle = 0f; angle < MathF.Tau; angle += 0.05f)
            {
                var swept = (profile.WidthInShelfUnits * MathF.Abs(MathF.Cos(angle)))
                    + (profile.DepthInShelfUnits * MathF.Abs(MathF.Sin(angle)));
                Assert.True(
                    swept <= profile.TurningWidthInShelfUnits + 1e-4f,
                    $"{systemId} sweeps {swept:F3} at {angle:F2} rad but the row reserves only "
                    + $"{profile.TurningWidthInShelfUnits:F3}.");
            }
        }

        // Both halves of the rule matter. The cabinet must reserve materially more than its face,
        // and packaging must not — inflating the whole shelf's spacing to fix one medium would
        // scatter every other row.
        var cabinet = MediaShellMap.ProfileForSystem("arcade", 1.333);
        var keepCase = MediaShellMap.ProfileForSystem("playstation2", 0.708);
        Assert.True(cabinet.TurningWidthInShelfUnits > cabinet.WidthInShelfUnits * 1.5f);
        Assert.Equal(
            keepCase.WidthInShelfUnits, keepCase.TurningWidthInShelfUnits,
            keepCase.WidthInShelfUnits * 0.01f);
    }

    /// <summary>
    /// The floor the shadows are painted on has to be bigger than the shadows.
    /// </summary>
    /// <remarks>
    /// Regression test, and an unusually cheap one for a defect that reached a user. The receiving
    /// plane's depth was a fixed 1.1 while its width followed the row — fine for packaging, and far
    /// too small for an arcade cabinet whose footprint is 1.4 deep, so its shadow's soft lobe ran
    /// off every side of the surface it was drawn on. What was left was the plane itself: a grey
    /// rectangle with four hard edges under the machine. It is invisible on the dark shelf every
    /// review render used, and obvious the moment the light theme is behind it.
    /// </remarks>
    [Fact]
    public void ShadowPlane_IsLargerThanTheShadowsItCarries()
    {
        var cabinet = MediaShellMap.ProfileForSystem("arcade", 1.333);
        var keepCase = MediaShellMap.ProfileForSystem("playstation2", 0.708);

        foreach (var profile in new[] { cabinet, keepCase })
        {
            var radius = new Vector2(
                profile.WidthInShelfUnits * 0.5f, profile.DepthInShelfUnits * 0.5f);
            var (centre, extent) = EmuShelf.Rendering.MediaShellRenderer.ShadowPlane(
                [new EmuShelf.Rendering.MediaShellRenderer.ShadowFootprint(Vector2.Zero, radius, 1f)]);

            // Two radii of clear surface on every side, which is where the cast lobe has faded out.
            Assert.True(
                extent.X >= radius.X * 2f && extent.Y >= radius.Y * 2f,
                $"A {profile.Shell} shadow of radius {radius} is drawn on a plane of half-extent "
                + $"{extent}; its falloff is being cut off by the plane's own edge.");
            Assert.True(
                MathF.Abs(centre.X) <= extent.X && MathF.Abs(centre.Y) <= extent.Y,
                $"The {profile.Shell} stands at {centre} on a plane of half-extent {extent}.");
        }
    }

    [Fact]
    public void MaterialBounds_RefusesAMaterialTheShellDoesNotHave()
    {
        var model = MediaShellCatalog.Load(MediaShell.ArcadeCabinet);

        var error = Assert.Throws<ArgumentException>(
            () => MediaShellCatalog.MaterialBounds(model, "marquee"));
        Assert.Contains("banner", error.Message);
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

    /// <summary>
    /// The keep case's three sheets meet round its corners instead of leaving bare plastic between
    /// them, and still stop at the opening edge.
    /// </summary>
    /// <remarks>
    /// Reported as "the DVD boxes have gaps between the images". They did: each panel printed only
    /// what lay within a millimetre of its own plane, and the rounded corner between the front face
    /// and the spine is 3.5mm across, so a band of it belonged to neither — 2.6mm of black moulding
    /// on the spine side and 3.25mm on the back side of a case 13.7mm thick, which reads as two
    /// pictures with a gap rather than as one sheet folded round.
    ///
    /// Stated as a walk round the shell's own cross-section rather than as bounds on the constants,
    /// because the constants are only ever right relative to this mesh's fillet: the front print has
    /// to reach at least as far round as the spine's begins. That is one comparison and it is the
    /// whole defect. The opening edge is asserted too, and pulls the other way — it is the one large
    /// face of a case that no sleeve wraps, so a fix that simply printed everything would pass the
    /// first assertion and be just as wrong.
    /// </remarks>
    [Fact]
    public void KeepCaseSleeve_MeetsRoundTheCornersAndStopsAtTheOpening()
    {
        var model = MediaShellCatalog.Load(MediaShell.DiscKeepCase);
        var definition = MediaShellCatalog.Definition(MediaShell.DiscKeepCase);
        var millimetres = 190f / model.Size.Y;

        var cover = PrintedSpanAtMidHeight(model, definition.CoverPanel);
        var back = PrintedSpanAtMidHeight(model, definition.ExtraPanels[0]);
        var spine = PrintedSpanAtMidHeight(model, definition.ExtraPanels[1]);

        // The spine is at -X, so "further round the corner" is a smaller x for the two sheets and a
        // larger one for the spine itself. Meeting means their spans overlap rather than abut,
        // which is what leaves the shader's later panel to win a fragment instead of nobody taking it.
        Assert.True(
            cover.Min.X <= spine.Max.X,
            $"The front sleeve stops at x {cover.Min.X * millimetres:F1}mm and the spine's print "
            + $"only begins at {spine.Max.X * millimetres:F1}mm, leaving "
            + $"{(cover.Min.X - spine.Max.X) * millimetres:F1}mm of bare case between them.");
        Assert.True(
            back.Min.X <= spine.Max.X,
            $"The back sleeve stops at x {back.Min.X * millimetres:F1}mm and the spine's print "
            + $"only begins at {spine.Max.X * millimetres:F1}mm, leaving "
            + $"{(back.Min.X - spine.Max.X) * millimetres:F1}mm of bare case between them.");

        // And each sheet stays on its own half of the case's thickness. This is the assertion that
        // pulls the other way, and it is why the fix could not simply be a larger allowance: the
        // opening edge is the one large face of a case no sleeve wraps, and reaching it means coming
        // round to the middle of the 13.7mm the case is thick. Printing everything would satisfy the
        // corners above and put cover art over the thumb notch.
        Assert.True(
            cover.Min.Z > model.BoundsMax.Z * 0.5f,
            $"The front sleeve reaches z {cover.Min.Z * millimetres:F1}mm, over half way through a "
            + $"{model.Size.Z * millimetres:F1}mm case, so it is wrapping onto the opening edge.");
        Assert.True(
            back.Max.Z < model.BoundsMin.Z * 0.5f,
            $"The back sleeve reaches z {back.Max.Z * millimetres:F1}mm, over half way through a "
            + $"{model.Size.Z * millimetres:F1}mm case, so it is wrapping onto the opening edge.");
    }

    /// <summary>
    /// The extent of what one panel actually prints, measured round the shell's cross-section at
    /// mid-height by the same three tests the fragment shader applies: facing the panel, inside its
    /// rectangle, and within its depth allowance of the panel's plane.
    /// </summary>
    /// <remarks>
    /// Sampled along the surface rather than at the vertices, because the shader shades fragments
    /// and this shell's fillet carries only a handful of edge loops: taken at vertices alone the
    /// back sleeve appears to stop 1.2mm short of the spine's when the surface between those
    /// vertices is printed the whole way. A test that cannot see what the shader prints would have
    /// been read as the gap still being there.
    /// </remarks>
    private static (Vector3 Min, Vector3 Max) PrintedSpanAtMidHeight(ModelAsset model, ArtPanel panel)
    {
        var placement = MediaShellCatalog.Place(panel, model);
        var allowance = panel.MaxSurfaceDepth
            ?? MediaShellCatalog.Definition(MediaShell.DiscKeepCase).PanelDepthFraction
                * MathF.Abs(Vector3.Dot(model.Size, Vector3.Abs(placement.Normal)));
        var height = (model.BoundsMin.Y + model.BoundsMax.Y) * 0.5f;

        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        foreach (var mesh in model.Meshes)
        {
            for (var triangle = 0; triangle < mesh.Indices.Length; triangle += 3)
            {
                var corners = new (Vector3 Position, Vector3 Normal)[3];
                for (var corner = 0; corner < 3; corner++)
                {
                    var offset = (int)mesh.Indices[triangle + corner] * MeshGeometry.FloatsPerVertex;
                    corners[corner] = (
                        new Vector3(
                            mesh.Vertices[offset], mesh.Vertices[offset + 1], mesh.Vertices[offset + 2]),
                        new Vector3(
                            mesh.Vertices[offset + 3], mesh.Vertices[offset + 4], mesh.Vertices[offset + 5]));
                }

                // Where this triangle crosses the mid-height plane, if it does.
                var crossings = new List<(Vector3 Position, Vector3 Normal)>(2);
                for (var edge = 0; edge < 3; edge++)
                {
                    var (from, next) = (corners[edge], corners[(edge + 1) % 3]);
                    if ((from.Position.Y - height) * (next.Position.Y - height) >= 0f)
                    {
                        continue;
                    }

                    var along = (height - from.Position.Y) / (next.Position.Y - from.Position.Y);
                    crossings.Add((
                        Vector3.Lerp(from.Position, next.Position, along),
                        Vector3.Lerp(from.Normal, next.Normal, along)));
                }

                if (crossings.Count != 2)
                {
                    continue;
                }

                const int samples = 64;
                for (var sample = 0; sample <= samples; sample++)
                {
                    var along = (float)sample / samples;
                    var position = Vector3.Lerp(crossings[0].Position, crossings[1].Position, along);
                    var normal = Vector3.Normalize(
                        Vector3.Lerp(crossings[0].Normal, crossings[1].Normal, along));
                    if (Vector3.Dot(normal, placement.Normal) < 0.5f)
                    {
                        continue;
                    }

                    var local = position - placement.Origin;
                    var u = Vector3.Dot(local, placement.UEdge) / placement.UEdge.LengthSquared();
                    var v = Vector3.Dot(local, placement.VEdge) / placement.VEdge.LengthSquared();
                    if (u is < 0f or > 1f || v is < 0f or > 1f)
                    {
                        continue;
                    }

                    if (-Vector3.Dot(local, placement.Normal) > allowance)
                    {
                        continue;
                    }

                    min = Vector3.Min(min, position);
                    max = Vector3.Max(max, position);
                }
            }
        }

        Assert.True(min.X <= max.X, $"The {panel.Face} panel prints on nothing at all.");
        return (min, max);
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

    /// <summary>
    /// The Mega Drive label is placed from the printed sticker's own measurements.
    /// </summary>
    /// <remarks>
    /// A Mega Drive label is a 75 x 68mm sheet on a 109 x 70mm cartridge whose top 7.7mm folds over
    /// the top edge. Those figures fix the panel completely, and the first rectangle disagreed with
    /// every one of them: a seventh too wide, and stopping short of the top edge the sheet actually
    /// runs over. Ranges rather than equalities, because the shell's rounded sides mean its
    /// bounding box is slightly wider than the flat face the label is stuck to.
    /// </remarks>
    [Fact]
    public void MegaDriveCoverPanel_MatchesARealCartridgeLabel()
    {
        var label = MediaShellCatalog.Definition(MediaShell.MegaDriveCartridge).CoverPanel;

        Assert.Equal(ArtFace.Front, label.Face);
        Assert.Equal(-label.MaxU, label.MinU, 3);
        // 75mm of a 109mm cartridge, which is what leaves the bare plastic shoulders either side.
        Assert.Equal(75f / 109f, label.MaxU, 0.03f);
        // The sheet runs over the top edge, so the panel ends there rather than at a margin below.
        Assert.Equal(1f, label.MaxV, 3);
        // 60.3mm of front label down a 70mm face, leaving the moulded band along the bottom.
        Assert.Equal(-1f + (2f * (1f - (60.3f / 70f))), label.MinV, 0.03f);
        Assert.Equal(7.7f / 68f, label.TopWrap, 0.02f);
    }

    /// <summary>
    /// The folded strip continues the front label across the top edge, at the same printed scale.
    /// </summary>
    /// <remarks>
    /// Two things make this a fold rather than a second sticker. It starts exactly at the front
    /// edge, so no plastic shows in the crease; and its length comes from the front panel's height
    /// and the fold fraction rather than from the model's depth, because this asset is about 12mm
    /// deep where a real cartridge is 17mm and a strip sized against it would print the title
    /// smaller than the label it belongs to.
    /// </remarks>
    [Fact]
    public void WrapPanel_ContinuesTheLabelOverTheTopEdgeAtTheSamePrintedScale()
    {
        var model = MediaShellCatalog.Load(MediaShell.MegaDriveCartridge);
        var label = MediaShellCatalog.Definition(MediaShell.MegaDriveCartridge).CoverPanel;

        var strip = MediaShellCatalog.TryWrapPanel(label, model);
        Assert.NotNull(strip);
        Assert.Equal(ArtFace.Top, strip!.Value.Face);
        Assert.Equal(label.MinU, strip.Value.MinU, 3);
        Assert.Equal(label.MaxU, strip.Value.MaxU, 3);

        // Anything describing how the sheet is printed has to survive the crease. A strip built
        // fresh would take ArtFit.Stretch while the face takes Cover, and the two halves of one
        // label would then be cropping the same scan differently.
        Assert.Equal(label.ArtFit, strip.Value.ArtFit);
        Assert.Equal(label.MaxSurfaceDepth, strip.Value.MaxSurfaceDepth);
        Assert.Equal(0f, strip.Value.TopWrap);

        var front = MediaShellCatalog.Place(label, model);
        var placement = MediaShellCatalog.Place(strip.Value, model);

        // On the top face, running backwards from the front edge, and the same width as the label.
        Assert.Equal(Vector3.UnitY, placement.Normal);
        Assert.Equal(model.BoundsMax.Y, placement.Origin.Y, 3);
        Assert.Equal(model.BoundsMax.Z, placement.Origin.Z, 3);
        Assert.Equal(-1f, Vector3.Normalize(placement.VEdge).Z, 3);
        Assert.Equal(front.UEdge.Length(), placement.UEdge.Length(), 3);
        Assert.True(placement.VEdge.Length() <= model.Size.Z + 0.001f);

        // One sheet: the strip is to the face what 7.7mm is to 60.3mm of a real label.
        var expected = front.VEdge.Length() * label.TopWrap / (1f - label.TopWrap);
        Assert.Equal(expected, placement.VEdge.Length(), 0.001f);
    }

    [Fact]
    public void WrapPanel_IsOnlyProducedForALabelThatFolds()
    {
        // The two shells whose real label is one sheet printed over the cartridge's top edge.
        MediaShell[] folding = [MediaShell.MegaDriveCartridge, MediaShell.NesCartridge];

        foreach (var shell in MediaShellCatalog.All)
        {
            var panel = MediaShellCatalog.Definition(shell).CoverPanel;
            if (folding.Contains(shell))
            {
                Assert.True(panel.TopWrap > 0f);
                Assert.NotNull(MediaShellCatalog.TryWrapPanel(panel, MediaShellCatalog.Load(shell)));
                continue;
            }

            Assert.Equal(0f, panel.TopWrap);
            Assert.Null(MediaShellCatalog.TryWrapPanel(panel, MediaShellCatalog.Load(shell)));
        }
    }

    /// <summary>
    /// The NES strip must span the fold this model actually has, and stop at the moulding behind it.
    /// </summary>
    /// <remarks>
    /// Unlike the Mega Drive, whose label was measured off the printed sheet, this plate is modelled
    /// with its fold — so the asset is the authority and both bounds can be checked against it. That
    /// is worth doing because neither is guessable and both are sub-millimetre, so both survive a
    /// glance at a render. Under-reaching leaves the blank plate showing along the fold's far edge,
    /// which is the pale lip this shell had; over-reaching prints the recess floor the label sits in,
    /// carrying the title strip past where the label ends.
    ///
    /// It also pins the crease. The front panel has to claim every fragment of the bend that still
    /// faces forward, or the two halves of the sheet leave a hairline of plate between them; MaxV was
    /// 0.58mm short of that and did exactly this.
    /// </remarks>
    [Fact]
    public void NesLabelFold_SpansThePlatesOwnFoldFromTheCrease()
    {
        var model = MediaShellCatalog.Load(MediaShell.NesCartridge);
        var label = MediaShellCatalog.Definition(MediaShell.NesCartridge).CoverPanel;

        var sticker = model.Materials
            .Select((material, index) => (material, index))
            .Single(entry => string.Equals(
                entry.material.Name, "sticker", StringComparison.OrdinalIgnoreCase))
            .index;

        // How far the fold runs back from the shell's front plane, and the highest point of the bend
        // that still faces forward — the last thing the front panel is responsible for.
        var foldReach = 0f;
        var frontFacingTop = float.MinValue;
        foreach (var mesh in model.Meshes.Where(mesh => mesh.MaterialIndex == sticker))
        {
            for (var i = 0; i < mesh.Vertices.Length; i += MeshGeometry.FloatsPerVertex)
            {
                var position = new Vector3(
                    mesh.Vertices[i], mesh.Vertices[i + 1], mesh.Vertices[i + 2]);
                var normal = Vector3.Normalize(new Vector3(
                    mesh.Vertices[i + 3], mesh.Vertices[i + 4], mesh.Vertices[i + 5]));

                if (normal.Z > 0.95f)
                {
                    continue;
                }

                foldReach = MathF.Max(foldReach, model.BoundsMax.Z - position.Z);
                if (normal.Z >= 0.5f)
                {
                    frontFacingTop = MathF.Max(frontFacingTop, position.Y);
                }
            }
        }

        Assert.True(foldReach > 0f, "This plate has no fold; the asset is not the one measured here.");

        // The shader's facing test hands over at 45 degrees, so the front panel must reach the last
        // fragment above that. Half a millimetre of slack on a 135mm cartridge.
        var crease = label.MaxV * (model.Size.Y * 0.5f);
        Assert.InRange(crease, frontFacingTop, frontFacingTop + (0.5f / 135f));

        var strip = MediaShellCatalog.TryWrapPanel(label, model);
        Assert.NotNull(strip);
        var reach = MediaShellCatalog.Place(strip!.Value, model).VEdge.Length();

        Assert.True(
            reach >= foldReach,
            $"The strip stops {(foldReach - reach) * 135f:F2}mm short of the fold's far edge, "
                + "which leaves the blank plate showing along the top of the cartridge.");
        Assert.True(
            reach <= foldReach + (0.5f / 135f),
            $"The strip runs {(reach - foldReach) * 135f:F2}mm past the fold onto the moulding.");
    }

    /// <summary>
    /// Every shell fits the panel budget the fragment shader declares.
    /// </summary>
    /// <remarks>
    /// A folding label costs a panel of its own, so the budget is no longer just the authored
    /// panels. The renderer resolves this when it uploads a shell, which is on the GL thread inside
    /// a frame — a definition that overran would surface as a broken render rather than as anything
    /// anyone could read. Counted here so it fails at the desk instead.
    /// </remarks>
    [Fact]
    public void EveryShell_FitsTheShaderPanelBudget()
    {
        foreach (var shell in MediaShellCatalog.All)
        {
            var definition = MediaShellCatalog.Definition(shell);
            var model = MediaShellCatalog.Load(shell);
            var folds = MediaShellCatalog.TryWrapPanel(definition.CoverPanel, model) is not null;

            var panels = 1 + definition.ExtraPanels.Count + (folds ? 1 : 0);
            Assert.True(
                panels <= EmuShelf.Rendering.MediaShellRenderer.MaxPanels,
                $"{shell} needs {panels} panels against a budget of "
                + $"{EmuShelf.Rendering.MediaShellRenderer.MaxPanels}.");
        }
    }

    /// <summary>
    /// A fold is only meaningful on the front face, and asking for one elsewhere is not silent.
    /// </summary>
    /// <remarks>
    /// The strip runs backwards from the shell's front edge. A fold requested on the back or the
    /// spine would be laid down against the wrong edge and would take the front label's share of
    /// the printed sheet with it — a wrong picture rather than a missing one, which is the kind
    /// that ships.
    /// </remarks>
    [Theory]
    [InlineData(ArtFace.Back)]
    [InlineData(ArtFace.Spine)]
    [InlineData(ArtFace.Top)]
    public void WrapPanel_RefusesToFoldAnythingButAFrontLabel(ArtFace face)
    {
        var model = MediaShellCatalog.Load(MediaShell.MegaDriveCartridge);
        var panel = new ArtPanel(face, -0.5f, 0.5f, -0.5f, 1f, TopWrap: 0.1f);

        Assert.Throws<ArgumentException>(() => MediaShellCatalog.TryWrapPanel(panel, model));
    }

    /// <summary>
    /// A folding label's artwork is fitted to the whole sheet, not to the part of it left on show.
    /// </summary>
    /// <remarks>
    /// Cropping a portrait box scan to the front panel and then folding a tenth of that away would
    /// crop the picture twice, losing the top of the art the fold was supposed to carry.
    /// </remarks>
    [Fact]
    public void SheetAspect_DescribesTheWholeLabelIncludingTheFold()
    {
        var model = MediaShellCatalog.Load(MediaShell.MegaDriveCartridge);
        var label = MediaShellCatalog.Definition(MediaShell.MegaDriveCartridge).CoverPanel;
        var front = MediaShellCatalog.Place(label, model);

        var sheet = MediaShellCatalog.TrySheetAspect(label, model);
        Assert.NotNull(sheet);

        var faceOnly = front.UEdge.Length() / front.VEdge.Length();
        Assert.True(sheet!.Value < faceOnly, "A sheet that folds is taller than the face it covers.");
        Assert.Equal(faceOnly * (1f - label.TopWrap), sheet.Value, 0.001f);
    }

    [Fact]
    public void SnesCoverPanel_UsesRoundedBodyAttachedDecalEdges()
    {
        var snes = MediaShellCatalog.Definition(MediaShell.SnesCartridge).CoverPanel;

        Assert.Equal(ArtFace.Front, snes.Face);
        Assert.InRange(snes.CornerRadius, 0.05f, 0.10f);
        // Cartridge labels are printed stickers with rounded corners; a keep case's sleeve is cut
        // square to the case, so it is the one shell where a radius would be wrong.
        Assert.True(
            MediaShellCatalog.Definition(MediaShell.GbaCartridge).CoverPanel.CornerRadius > 0f);
        Assert.Equal(0f, MediaShellCatalog.Definition(MediaShell.DiscKeepCase).CoverPanel.CornerRadius);
    }
}
