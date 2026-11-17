using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using EmuShelf.App.Services;
using EmuShelf.Integrations.Systems;
using EmuShelf.Rendering.Gl;
using EmuShelf.Rendering.Stage;
using EmuShelf.Rendering.Stage.Backdrops;

namespace EmuShelf.App.Tests.Handheld;

/// <summary>
/// M47 B13: the owner's decisions about the Stage's animated backdrops under Big Type (15 November 2026, backdrop-review.html): one
/// animation per console (the 3DS keeps today's three), Recent's own, the recolour toward the console's ground, Big Type's own draw
/// sizes, and Dreamcast's sea blue. Today's look keeps its lists, sizes and colours.
/// </summary>
public class HandheldBigTypeBackdropTests
{
    private const float Tolerance = 1e-5f;

    private static string Prototype(string file) =>
        File.ReadAllText(Path.Combine(HandheldGroundColourTests.RepositoryRoot(), "docs", "prototypes", "handheld-big-type", file));

    // ---- The recolour: CSS mix-blend-mode color at the pull's opacity (W3C Compositing and Blending Level 1) ----

    private static void Near(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
        Assert.Equal(expected.Z, actual.Z, Tolerance);
    }

    [Fact]
    public void Lum_IsTheSpecificationsWeights()
    {
        Assert.Equal(0.3f, BackdropRecolour.Lum(new Vector3(1, 0, 0)), Tolerance);
        Assert.Equal(0.59f, BackdropRecolour.Lum(new Vector3(0, 1, 0)), Tolerance);
        Assert.Equal(0.11f, BackdropRecolour.Lum(new Vector3(0, 0, 1)), Tolerance);
        Assert.Equal(0.362f, BackdropRecolour.Lum(new Vector3(0.2f, 0.4f, 0.6f)), Tolerance);
    }

    [Fact]
    public void Color_TakesTheSourcesHue_AndTheBackdropsLuminosity_ClippingAbove()
    {
        // Cb = (0.2, 0.4, 0.6), Lum 0.362; Cs = red, Lum 0.3. SetLum adds 0.062: (1.062, 0.062, 0.062), above 1, so ClipColor
        // takes it to L + (C - L)(1 - L)/(x - L) with L 0.362, x 1.062: (1, 0.0885714, 0.0885714), whose Lum is 0.362 again.
        var blended = BackdropRecolour.Color(new Vector3(0.2f, 0.4f, 0.6f), new Vector3(1, 0, 0));
        Near(new Vector3(1f, 0.0885714f, 0.0885714f), blended);
        Assert.Equal(0.362f, BackdropRecolour.Lum(blended), Tolerance);
    }

    [Fact]
    public void Color_ClipsBelowZero_TowardTheLuminosity()
    {
        // Cb grey 0.1 (Lum 0.1); Cs blue (Lum 0.11). SetLum gives (-0.01, -0.01, 0.99); below 0, so L + (C - L) L/(L - n) with
        // L 0.1, n -0.01: (0, 0, 0.9090909).
        Near(new Vector3(0f, 0f, 0.9090909f), BackdropRecolour.Color(new Vector3(0.1f), new Vector3(0, 0, 1)));
    }

    [Fact]
    public void Color_OfAGreySource_IsTheBackdropsLuminosityAsGrey_AndWhiteStaysWhite()
    {
        // Cb (0.9, 0.2, 0.1) has Lum 0.27 + 0.118 + 0.011 = 0.399.
        Near(new Vector3(0.399f), BackdropRecolour.Color(new Vector3(0.9f, 0.2f, 0.1f), new Vector3(0.5f)));
        // Lum 1 cannot carry any hue: white stays white whatever the colour.
        Near(Vector3.One, BackdropRecolour.Color(Vector3.One, new Vector3(25 / 255f, 128 / 255f, 152 / 255f)));
        Near(Vector3.Zero, BackdropRecolour.Color(Vector3.Zero, new Vector3(25 / 255f, 128 / 255f, 152 / 255f)));
    }

    [Fact]
    public void Apply_MixesTheBlendOverThePixel_ByThePull()
    {
        var pixel = new Vector3(0.2f, 0.4f, 0.6f);
        var red = new Vector3(1, 0, 0);
        // (1 - 0.5) Cb + 0.5 B: (0.6, 0.2442857, 0.3442857).
        Near(new Vector3(0.6f, 0.2442857f, 0.3442857f), BackdropRecolour.Apply(pixel, red, 0.5f));
        Near(new Vector3(1f, 0.0885714f, 0.0885714f), BackdropRecolour.Apply(pixel, red, 1f));
        Assert.Equal(pixel, BackdropRecolour.Apply(pixel, red, 0f));
    }

    [Fact]
    public void TheOutputPass_BlendsAsTheReferenceDoes_OnlyForBigType()
    {
        var bigType = ShaderLibrary.LoadWithDefines("backdrop-output.frag", GlslDialect.Es300, 3, 0, ["BACKDROP_BIGTYPE 1"], "backdrop-common");
        Assert.Contains("#define BACKDROP_BIGTYPE 1", bigType);
        Assert.Contains("0.3 * c.r + 0.59 * c.g + 0.11 * c.b", bigType);
        Assert.Contains("shown = mix(shown, blendColor(shown, uGround), uPull);", bigType);
        // Today's look compiles exactly what Load always gave: no defines.
        Assert.Equal(ShaderLibrary.Load("backdrop-output.frag", GlslDialect.Es300, 3, 0, "backdrop-common"),
            ShaderLibrary.LoadWithDefines("backdrop-output.frag", GlslDialect.Es300, 3, 0, [], "backdrop-common"));
    }

    // ---- Which animation each console shows ----

    public static TheoryData<string, string, float> OwnersChoices() => new()
    {
        { "nes", "nes-stars", 0.7f },
        { "snes", "tiles", 0.5f },
        { "gbc", "gbc-attr", 0.55f },
        { "gba", "gba-iso", 0.6f },
        { "gamecube", "gc-fur", 0f },
        { "nds", "ds-dots", 1f },
        { "wii", "wii-pointer-v2", 1f },
        { "megadrive", "md-pipe-v2", 1f },
        { "dreamcast", "dc-sea-v2", 0f },
        { "playstation", "tide", 0f },
        { "playstation2", "ps2-orbs-v3", 0f },
        { "psp", "psp-ribbon", 0.6f },
        { "playstation3", "ps3-sheet", 0f },
        { "arcade", "arc-vector-v3", 1f },
        { "steam", "steam-mesh", 0f },
        { BigTypeBackdrops.Recent, "arc-checker", 1f },
        // Part 2 (the owner, 16 November 2026): drawn from the colour they are given, so no recolour.
        { "3ds", "3ds-screen", 0f },
        { BigTypeBackdrops.Default, "lift-off", 0f },
    };

    [Theory]
    [MemberData(nameof(OwnersChoices))]
    public void EachConsole_ShowsExactlyTheOneAnimationChosen_AtItsPull(string owner, string id, float pull)
    {
        Assert.Equal([id], BigTypeBackdrops.IdsFor(owner)!);
        var entry = BigTypeBackdrops.Find(id)!;
        Assert.Equal(pull, entry.Pull);
        Assert.NotNull(BackdropCatalog.Find(id));
    }

    [Fact]
    public void The3ds_ShowsOnlyThroughTheScreen_AndTodaysLookKeepsItsThree()
    {
        Assert.Equal(["3ds-screen"], BigTypeBackdrops.IdsFor("3ds")!);
        Assert.Equal(BackdropUpscale.Sharp, BigTypeBackdrops.Find("3ds-screen")!.Upscale);
        Assert.Equal(["3ds-tiles", "3ds-floorwave", "3ds-tapwave"], KnownSystems.All.Single(system => system.Id == "3ds").Backdrops!);
        Assert.Null(BigTypeBackdrops.Find("3ds-tiles"));
    }

    [Fact]
    public void TheDefaultBackground_IsLiftOff_DrawnSharp_AndRecentKeepsItsChecker()
    {
        Assert.Equal(["lift-off"], BigTypeBackdrops.IdsFor(BigTypeBackdrops.Default)!);
        Assert.Equal(BackdropUpscale.Sharp, BigTypeBackdrops.Find("lift-off")!.Upscale);
        Assert.Equal(["arc-checker"], BigTypeBackdrops.IdsFor(BigTypeBackdrops.Recent)!);
        // Lift-off's passes share its own file, and only its passes do.
        Assert.Equal("backdrop-lift-off-common", BackdropCatalog.Find("lift-off")!.Include);
        Assert.All(BackdropCatalog.All.Where(spec => spec.Id != "lift-off"), spec => Assert.Null(spec.Include));
        foreach (var pass in BackdropCatalog.Find("lift-off")!.Passes)
            Assert.Contains("void main()", ShaderLibrary.LoadWithDefines(pass.Fragment, GlslDialect.Es300, 3, 0, ["BACKDROP_SHARP 1"],
                "backdrop-common", "backdrop-lift-off-common"));
    }

    [Fact]
    public void ANewColour_EasesOverTheFlood_InTheSameRunningBackdrop()
    {
        var spec = BackdropCatalog.Find("lift-off")!;
        var indigo = new Vector3(0x4A, 0x43, 0xB8) / 255f;
        var rose = new Vector3(0xB1, 0x5A, 0x7F) / 255f;
        var running = new BackdropInstance(spec, 0f, 0f, new BackdropLook(true, indigo));
        Assert.Equal(indigo, running.GroundAt(1_000f));

        running.SetGround(rose, 1_000f);
        var halfway = running.GroundAt(1_000f + (EmuShelf.Rendering.Stage.BigType.BigTypeMotion.FloodMilliseconds / 2f));
        Assert.True(halfway.X > indigo.X && halfway.X < rose.X, $"{halfway}");
        Assert.Equal(rose, running.GroundAt(1_000f + EmuShelf.Rendering.Stage.BigType.BigTypeMotion.FloodMilliseconds));

        // Held still (reduced motion): at once.
        running.SetGround(indigo, 5_000f, instantly: true);
        Assert.Equal(indigo, running.GroundAt(5_000f));
    }

    [Fact]
    public void EveryKnownSystem_HasABigTypeList_AndNothingElseDoes()
    {
        var owners = BigTypeBackdrops.All.Select(row => row.Owner).ToList();
        Assert.Equal(KnownSystems.All.Select(system => system.Id).Append(BigTypeBackdrops.Recent).Append(BigTypeBackdrops.Default).Order(),
            owners.Order());
        Assert.Null(BigTypeBackdrops.IdsFor("settings"));
        Assert.Null(BigTypeBackdrops.IdsFor(null));
    }

    [Fact]
    public void TodaysLook_KeepsItsRotatingLists()
    {
        // The present lists, written out: Classic shows exactly what it showed before M47 B13.
        var classic = new Dictionary<string, string[]>
        {
            ["nes"] = ["nes-flow", "nes-stars", "nes-meta", "nes-raster"],
            ["snes"] = ["runway", "snes-mosaic-v2", "snes-lake-v2", "tiles"],
            ["gbc"] = ["gbc-attr", "gbc-bands", "gbc-sheen-v2", "gbc-confetti-v2"],
            ["gba"] = ["gba-corridor-v2", "gba-iso"],
            ["gamecube"] = ["gc-caustics", "gc-cubes-v3", "gc-fur", "gc-lens"],
            ["nds"] = ["ds-ripples", "ds-ripplefloor-v2", "ds-dots", "ds-flip"],
            ["wii"] = ["wii-pointer-v2", "wii-tiles"],
            ["3ds"] = ["3ds-tiles", "3ds-floorwave", "3ds-tapwave"],
            ["megadrive"] = ["md-speed-v3", "md-planes", "md-pipe-v2"],
            ["dreamcast"] = ["dc-fog-v3", "dc-sea-v2", "dc-checker"],
            ["playstation"] = ["field", "tide", "frames", "ps1-terrace-v2", "ps1-pipe"],
            ["playstation2"] = ["ps2-towers", "ps2-trails-v3", "ps2-sea", "ps2-orbs-v3"],
            ["psp"] = ["psp-field-v2"],
            ["playstation3"] = ["ps3-sheet", "ps3-wire", "ps3-twin"],
            ["arcade"] = ["arc-vector-v3", "arc-bulbs-v2", "arc-checker", "arc-plating"],
            ["steam"] = ["steam-wall-v2", "steam-mesh", "steam-grid-v2"],
        };
        Assert.Equal(classic.Count, KnownSystems.All.Count);
        Assert.All(KnownSystems.All, system => Assert.Equal(classic[system.Id], system.Backdrops!));
    }

    // ---- Draw sizes, sharp and crisp ----

    [Fact]
    public void DrawSizes_AndPulls_AreTokensJs()
    {
        var tokens = Prototype("tokens.js");
        var block = Regex.Match(tokens, @"window\.BT_TOKENS\.backdrops\s*=\s*\[(?<body>.*?)\n\];", RegexOptions.Singleline).Groups["body"].Value;
        var rows = Regex.Matches(block,
                @"\{\s*owner:\s*'(?<owner>[^']+)',\s*id:\s*'(?<id>[^']+)',\s*pull:\s*(?<pull>[\d.]+),\s*size:\s*\[(?<w>\d+),\s*(?<h>\d+)\],\s*upscale:\s*'(?<up>\w+)'(,\s*bloomThreshold:\s*(?<bloom>\d+))?\s*\}")
            .Select(match => match.Groups)
            .ToList();
        var table = BigTypeBackdrops.All.SelectMany(row => row.Backdrops.Select(entry => (row.Owner, Entry: entry))).ToList();
        Assert.Equal(table.Count, rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var (owner, entry) = table[i];
            var row = rows[i];
            Assert.Equal(row["owner"].Value, owner);
            Assert.Equal(row["id"].Value, entry.Id);
            Assert.Equal(float.Parse(row["pull"].Value, CultureInfo.InvariantCulture), entry.Pull);
            Assert.Equal((int.Parse(row["w"].Value, CultureInfo.InvariantCulture), int.Parse(row["h"].Value, CultureInfo.InvariantCulture)),
                (entry.Width, entry.Height));
            Assert.Equal(row["up"].Value, entry.Upscale == BackdropUpscale.Crisp ? "crisp" : "sharp");
            Assert.Equal(row["bloom"].Success ? float.Parse(row["bloom"].Value, CultureInfo.InvariantCulture) / 255f : null, entry.BloomThreshold);
        }
    }

    [Fact]
    public void CrispBackdrops_KeepTheirConsolesSize_SharpOnesAreDrawnLarger_In16By9()
    {
        var crisp = new[] { "nes-stars", "tiles", "gbc-attr", "gba-iso", "ds-dots", "md-pipe-v2", "tide", "arc-vector-v3", "arc-checker" };
        foreach (var (_, backdrops) in BigTypeBackdrops.All)
            foreach (var entry in backdrops)
            {
                var spec = BackdropCatalog.Find(entry.Id)!;
                Assert.Equal(crisp.Contains(entry.Id) ? BackdropUpscale.Crisp : BackdropUpscale.Sharp, entry.Upscale);
                if (entry.Upscale == BackdropUpscale.Crisp)
                    Assert.Equal((spec.Width, spec.Height), (entry.Width, entry.Height));
                else
                    Assert.True(entry.Width > spec.Width && entry.Width <= BigTypeBackdrops.StageWidth, entry.Id);
                Assert.InRange(entry.Width / (double)entry.Height, 16.0 / 9.0 - 0.02, 16.0 / 9.0 + 0.02);
            }
    }

    [Fact]
    public void ACrispFrame_IsShownInWholePixels_TheEdgesCroppedWhenItDoesNotDivideTheStage()
    {
        Assert.Equal(6, BigTypeBackdrops.CrispScale(320, 180));
        Assert.Equal(Vector2.One, BigTypeBackdrops.CrispShown(320, 180));
        Assert.Equal(12, BigTypeBackdrops.CrispScale(160, 90));
        Assert.Equal(8, BigTypeBackdrops.CrispScale(240, 135));
        // 256 × 144 would be 7.5: shown at 8, 2048 × 1152 with 64 and 36 cropped each side, every pixel 8 × 8.
        Assert.Equal(8, BigTypeBackdrops.CrispScale(256, 144));
        Assert.Equal(new Vector2(1920f / 2048f, 1080f / 1152f), BigTypeBackdrops.CrispShown(256, 144));
    }

    [Fact]
    public void TheDrawSize_ComesFromTheTable_UnlessTheThorsMeasurementOverridesIt()
    {
        var orbs = BigTypeBackdrops.Find("ps2-orbs-v3")!;
        var spec = BackdropCatalog.Find("ps2-orbs-v3")!;
        Assert.Equal((orbs.Width, orbs.Height), BigTypeBackdrops.DrawSize(orbs, spec));
        try
        {
            BigTypeBackdrops.SharpWidthOverride = 960;
            Assert.Equal((960, 540), BigTypeBackdrops.DrawSize(orbs, spec));
            BigTypeBackdrops.SharpWidthOverride = 0;
            Assert.Equal((480, 270), BigTypeBackdrops.DrawSize(orbs, spec));
            // A crisp one is never resized.
            var tide = BigTypeBackdrops.Find("tide")!;
            Assert.Equal((320, 180), BigTypeBackdrops.DrawSize(tide, BackdropCatalog.Find("tide")!));
        }
        finally
        {
            BigTypeBackdrops.SharpWidthOverride = null;
        }
    }

    [Fact]
    public void ASharpBackdrop_CompilesItsOwnVariant_TodaysCompilesAsBefore()
    {
        foreach (var name in new[] { "backdrop-psp-ribbon-bg.frag", "backdrop-psp-ribbon.vert", "backdrop-psp-ribbon-edges.vert", "backdrop-psp-ribbon-edges.frag" })
            Assert.Contains("void main()", ShaderLibrary.LoadWithDefines(name, GlslDialect.Es300, 3, 0, ["BACKDROP_SHARP 1"], "backdrop-common"));
        var common = ShaderLibrary.Load("backdrop-common", GlslDialect.Es300, 3, 0);
        Assert.Contains("#ifdef BACKDROP_SHARP", common);
        Assert.Contains("#define pixelIndex(v) floor(v)", common);
    }

    // ---- Recent, the ink and Dreamcast ----

    [Fact]
    public void EveryAnimationBigTypeShows_HasItsOwnBakedAnswer_FromLegibilityJson()
    {
        using var json = JsonDocument.Parse(Prototype("legibility.json"));
        var rows = json.RootElement.GetProperty("bigType").EnumerateArray().ToList();
        var shown = BigTypeBackdrops.All.SelectMany(row => row.Backdrops.Select(entry => (row.Owner, entry.Id))).ToList();
        Assert.Equal(shown.Count, rows.Count);
        Assert.Equal(rows.Count, BackdropLegibility.BigTypeAll.Count);
        foreach (var row in rows)
        {
            var id = row.GetProperty("id").GetString()!;
            Assert.Contains((row.GetProperty("console").GetString()!, id), shown);
            var baked = BackdropLegibility.BigTypeAll[id];
            Assert.Equal(row.GetProperty("console").GetString(), baked.Console);
            Assert.Same(baked, BackdropLegibility.ForBigType(id));
            foreach (var (name, zone) in new[] { ("left", StageInkZone.Left), ("topRight", StageInkZone.TopRight), ("bottomRight", StageInkZone.BottomRight) })
            {
                var cell = row.GetProperty(name);
                Assert.Equal(cell.GetProperty("ink").GetString() == "dark", baked[zone].Dark);
                Assert.Equal((float)cell.GetProperty("veil").GetDouble(), baked[zone].Veil);
            }
            // The stress sheet's floor holds over the prototype and over the app's own frames.
            Assert.True(row.GetProperty("lowestContrast").GetDouble() >= 3.0, id);
            Assert.True(row.GetProperty("lowestContrastInTheApp").GetDouble() >= 3.0, id);
        }
        // An id Big Type does not show reads today's answer; Classic's table is unchanged in size.
        Assert.Same(BackdropLegibility.For("ps2-towers"), BackdropLegibility.ForBigType("ps2-towers"));
        Assert.Equal(53, BackdropLegibility.All.Count);
    }

    [Fact]
    public void TheStageInk_ReadsBigTypesAnswers_UnderBigType()
    {
        var deep = new Vector3(0.1f, 0.1f, 0.2f);
        var bigType = StageCompositor.ZoneInk(StageInkZone.Left, [("psp-ribbon", 1f)], deep, bigType: true);
        Assert.Equal(Vector3.One, bigType.Ink); // white ink over the ribbons
        // Today's look has no answer for it: the fallback (dark ink).
        Assert.Equal(deep, StageCompositor.ZoneInk(StageInkZone.Left, [("psp-ribbon", 1f)], deep).Ink);
    }

    [Fact]
    public void Dreamcast_IsSeaBlue_UnderBigType_AndKeepsItsAccentInTodaysLook()
    {
        var dreamcast = KnownSystems.All.Single(system => system.Id == "dreamcast");
        Assert.Equal("#0E7AB5", dreamcast.GroundColor);
        Assert.Equal("#F07C3E", dreamcast.AccentColor);
        Assert.Contains("dreamcast: '#0E7AB5'", Prototype("tokens.js"));
        Assert.True(HandheldGround.TryParse(dreamcast.GroundColor, out var sea));
        Assert.True(HandheldGround.TryParse(KnownSystems.All.Single(system => system.Id == "steam").GroundColor, out var steam));
        // The owner was told: 18.3 from Steam's colour, above the floor of 18.
        Assert.Equal(18.3, HandheldGround.Distance(sea, steam), 1);
    }

    [Fact]
    public void TheStageCommand_CarriesTheGround_ForTheRecolour()
    {
        var ground = new Vector3(14 / 255f, 122 / 255f, 181 / 255f);
        Assert.Equal(ground, new StageBackdropCommand(null, ["dc-sea-v2"], Ground: ground).Ground);
        Assert.Null(new StageBackdropCommand(null, ["dc-sea-v2"]).Ground);
    }
}

public partial class HandheldShellViewModelTests
{
    public class BigTypeBackdropsOnTheStage
    {
        private static async Task BigType(Func<Task> test)
        {
            HandheldTokens.Use(HandheldLook.BigType);
            try
            {
                await test();
            }
            finally
            {
                HandheldTokens.Use(HandheldLook.Classic);
            }
        }

        [AvaloniaFact]
        public Task UnderBigType_AConsoleShowsItsOneAnimation_AndRecentItsOwn() => BigType(async () =>
        {
            var shell = await CreateAsync(new FakeLibrary().WithPlatform("playstation2", 2));
            Assert.Equal(["ps2-orbs-v3"], shell.Stage.Backdrop!.Animated!);

            // The platform, then Recent.
            Press(shell, GamepadAction.NavigateRight);
            Assert.Equal(HandheldStageKind.Folder, shell.Stage.Kind);
            Assert.Equal("Recent", shell.Stage.Title);
            Assert.Equal(["arc-checker"], shell.Stage.Backdrop!.Animated!);
            Assert.Equal(Color.Parse("#39465F"), shell.Stage.Backdrop.Color);
        });

        [AvaloniaFact]
        public Task UnderBigType_SettingsAndActivity_ShowTheDefaultBackground_AsOneBackdrop() => BigType(async () =>
        {
            var shell = await CreateAsync(new FakeLibrary().WithPlatform("playstation2", 2));

            // The platform, Recent, then Settings and Activity (M46 P4).
            Press(shell, GamepadAction.NavigateRight, 2);
            Assert.Equal(HandheldStageKind.SettingsTile, shell.Stage.Kind);
            var settings = shell.Stage.Backdrop!;
            Assert.Equal(["lift-off"], settings.Animated!);
            Assert.Equal(HandheldTokens.AppGround, settings.Color);

            Press(shell, GamepadAction.NavigateRight);
            Assert.Equal(HandheldStageKind.ActivityTile, shell.Stage.Kind);
            // The same list instance: the native Stage keeps the one backdrop running and changes only its colour.
            Assert.Same(settings.Animated, shell.Stage.Backdrop!.Animated);
            Assert.Equal(shell.LauncherGround, shell.Stage.Backdrop.Color);

            // An empty slot after them has no platform either: the default background, in the colour the Launcher keeps.
            Press(shell, GamepadAction.NavigateRight);
            Assert.Equal(HandheldStageKind.EmptySlot, shell.Stage.Kind);
            Assert.Same(settings.Animated, shell.Stage.Backdrop!.Animated);
            Assert.Equal(shell.LauncherGround, shell.Stage.Backdrop.Color);
        });

        [AvaloniaFact]
        public Task UnderBigType_AConsolesFolderHeader_KeepsTheConsolesBackdrop() => BigType(async () =>
        {
            var shell = await CreateAsync(new FakeLibrary().WithPlatform("playstation2", 3));
            Press(shell, GamepadAction.Confirm);
            Assert.True(shell.IsFolderOpen);
            Press(shell, GamepadAction.NavigateUp);
            Assert.Equal(HandheldStageKind.Header, shell.Stage.Kind);
            Assert.Equal(["ps2-orbs-v3"], shell.Stage.Backdrop!.Animated!);
        });

        [AvaloniaFact]
        public async Task InTodaysLook_RecentHasNoAnimation_AndAConsoleKeepsItsList()
        {
            IReadOnlyList<string> animated = ["ps2-towers", "ps2-orbs-v3"];
            var library = new FakeLibrary();
            library.Platforms.Add(new HandheldPlatform("playstation2", "PlayStation 2", "PS2", "#3D6DB5", 2, Core.Systems.SystemEra.Disc, animated));
            library.Games["playstation2"] = [Entry(1, "playstation2"), Entry(2, "playstation2")];
            var shell = await CreateAsync(library);
            Assert.Same(animated, shell.Stage.Backdrop!.Animated);

            Press(shell, GamepadAction.NavigateRight);
            Assert.Equal("Recent", shell.Stage.Title);
            Assert.Null(shell.Stage.Backdrop?.Animated);
        }
    }
}
