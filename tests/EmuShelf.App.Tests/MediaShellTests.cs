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
    [InlineData("playstation2")]
    [InlineData("playstation3")]
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

    /// <summary>
    /// The launch lift must stay inside the frame the shelf camera actually shows.
    /// </summary>
    /// <remarks>
    /// These two were tuned independently and silently disagreed: the camera now pulls back only as
    /// far as the tallest medium requires, so the headroom above a cartridge is a fraction of what
    /// it was under the old fixed distance, and a lift sized for that distance carried the medium
    /// out through the top of the frame on its way up. Checked at 16:10 as well as 16:9 because the
    /// Steam Deck's shorter panel is the tighter of the two.
    /// </remarks>
    [Theory]
    [InlineData(1280f / 800f)]
    [InlineData(1920f / 1080f)]
    public void LaunchChoreography_StaysInsideTheShelfCameraFrame(float aspect)
    {
        var profile = MediaShellMap.ProfileForSystem("snes", 1.43);
        var asset = MediaShellCatalog.Load(profile.Shell);
        var band = profile.HeightInShelfUnits + profile.FloorClearanceInShelfUnits;
        var (view, projection, _) = EmuShelf.Rendering.MediaShellRenderer.ShelfCamera(aspect, band);
        var viewProjection = view * projection;

        var transition = new PhysicalShelfLaunchTransitionModel();
        transition.Start(1, MediaRotationModel.RestYaw, MediaRotationModel.RestPitch);

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
    public void MetricProfile_DistinguishesPs3ByFinishNotByADistortedHeight()
    {
        var ps2 = MediaShellMap.ProfileForSystem("playstation2", 0.708);
        var ps3 = MediaShellMap.ProfileForSystem("playstation3", 0.708);

        // PS3's real Blu-ray case is shorter, but expressing that on shared DVD geometry distorted
        // it; the difference now lives in the finish until a Blu-ray shell is authored.
        Assert.Equal(ps2.HeightInShelfUnits, ps3.HeightInShelfUnits, 3);
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
    /// The DS download is four cards in one file; only one may reach the scene.
    /// </summary>
    /// <remarks>
    /// Regression test for the dedupe. Loading the source whole draws four cartridges side by side,
    /// and the duplicates are detached by clearing their node's mesh reference rather than deleting
    /// anything, so this also guards against a prep that silently stops detaching them.
    /// </remarks>
    [Fact]
    public void Load_KeepsASingleDsCard()
    {
        var model = MediaShellCatalog.Load(MediaShell.DsCard);

        Assert.Single(model.Meshes);
        // 33.4 x 35mm: very slightly taller than wide.
        Assert.InRange(model.Size.X, 0.94f, 0.98f);
        Assert.True(model.Size.Z < 0.08f, $"A DS card is thin; got {model.Size.Z}.");
    }

    /// <summary>
    /// The shipped DS asset must carry no trace of the Super Mario 64 artwork it was modelled from.
    /// </summary>
    /// <remarks>
    /// This one is subtler than the other shells: no triangle in any of the four copies samples that
    /// island, so the artwork never rendered and the card looked clean. It was still sitting in the
    /// texture that ships inside the binary, which is what the licence actually turns on, so it is
    /// masked anyway.
    /// </remarks>
    [Fact]
    public void DsLabelIsland_CarriesNoSourceArtwork()
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

        // As with the Mega Drive shell, this walks the requested rectangle out to its edges.
        const float u0 = 0.06f, u1 = 0.48f, v0 = 0.03f, v1 = 0.48f;
        var reference = Sample(0.25f, 0.25f);
        for (var u = u0; u <= u1; u += 0.01f)
        {
            foreach (var v in new[] { v0, (v0 + v1) * 0.5f, v1 })
            {
                Assert.True(
                    Sample(u, v) == reference,
                    $"The DS label island still varies at ({u:F2},{v:F2}); artwork was not removed.");
            }
        }

        for (var v = v0; v <= v1; v += 0.01f)
        {
            foreach (var u in new[] { u0, u1 })
            {
                Assert.True(
                    Sample(u, v) == reference,
                    $"The DS label edge still varies at ({u:F2},{v:F2}); the mask is too small.");
            }
        }
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
        // Cartridge labels are printed stickers with rounded corners; a keep case's sleeve is cut
        // square to the case, so it is the one shell where a radius would be wrong.
        Assert.True(
            MediaShellCatalog.Definition(MediaShell.GbaCartridge).CoverPanel.CornerRadius > 0f);
        Assert.Equal(0f, MediaShellCatalog.Definition(MediaShell.DiscKeepCase).CoverPanel.CornerRadius);
    }
}
