using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia.Media;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using EmuShelf.Core.Systems;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.Tests.Handheld;

/// <summary>
/// M47 B2: the Big Type look's ground colours. The approved table (tokens.js <c>colour.consoles</c>, authored on
/// <see cref="GameSystem.GroundColor"/>) holds its floors: white type at 4.5 to 1 on each ground and 3 to 1 on a tile over
/// it, and no two grounds closer than 18 (CIE76). A colour derived for a system the table does not cover holds the first
/// two. Today's look keeps each console's accent.
/// </summary>
public class HandheldGroundColourTests
{
    private const uint White = 0xFFFFFFFF;

    public static TheoryData<string> Systems() => [.. KnownSystems.All.Select(system => system.Id)];

    private static uint Ground(GameSystem system)
    {
        Assert.True(HandheldGround.TryParse(system.GroundColor, out var ground), $"{system.Id} has no ground colour");
        return ground;
    }

    private static HandheldPlatform Platform(GameSystem system, bool withGround = true) =>
        new(system.Id, system.Name, system.ShortName, system.AccentColor, 1, system.Era, system.Backdrops,
            withGround ? system.GroundColor : null);

    [Fact]
    public void EveryKnownSystem_HasTheApprovedGround_FromTokensJs()
    {
        var tokens = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "prototypes", "handheld-big-type", "tokens.js"));
        var block = Regex.Match(tokens, @"consoles:\s*\{(?<body>[^}]*)\}").Groups["body"].Value;
        var approved = Regex.Matches(block, @"'?(?<id>[a-z0-9]+)'?\s*:\s*'(?<colour>#[0-9A-Fa-f]{6})'")
            .ToDictionary(match => match.Groups["id"].Value, match => match.Groups["colour"].Value.ToUpperInvariant());

        Assert.Equal(16, approved.Count);
        // Every key names a system the app knows, and every system the app knows has its approved colour.
        Assert.Equal(approved.Keys.Order(), KnownSystems.All.Select(system => system.Id).Order());
        Assert.All(KnownSystems.All, system => Assert.Equal(approved[system.Id], system.GroundColor));
    }

    [Theory]
    [MemberData(nameof(Systems))]
    public void EveryGround_HoldsTheContrastFloors(string id)
    {
        var ground = Ground(KnownSystems.All.Single(system => system.Id == id));

        Assert.True(HandheldGround.Contrast(White, ground) >= HandheldGround.TextFloor,
            $"{id}: white on {HandheldGround.ToHex(ground)} is {HandheldGround.Contrast(White, ground):0.00} to 1");
        var tile = HandheldGround.Tile(ground);
        Assert.True(HandheldGround.Contrast(White, tile) >= HandheldGround.TileFloor,
            $"{id}: white on its tile {HandheldGround.ToHex(tile)} is {HandheldGround.Contrast(White, tile):0.00} to 1");
    }

    [Fact]
    public void NoTwoGrounds_AreCloserThanTheFloor()
    {
        var grounds = KnownSystems.All.Select(system => (system.Id, Colour: Ground(system))).ToList();
        var closest = grounds.SelectMany((a, i) => grounds.Skip(i + 1), (a, b) => (a, b, Distance: HandheldGround.Distance(a.Colour, b.Colour)))
            .MinBy(pair => pair.Distance);

        Assert.True(closest.Distance >= HandheldGround.MinimumDistance,
            $"{closest.a.Id} and {closest.b.Id} are {closest.Distance:0.0} apart (CIE76)");
    }

    [Theory]
    [MemberData(nameof(Systems))]
    public void ADerivedGround_HoldsTheContrastFloors(string id)
    {
        // As if the table did not cover this system: its accent, darkened.
        var system = KnownSystems.All.Single(candidate => candidate.Id == id);
        var derived = HandheldTokens.ConsoleColour(Platform(system, withGround: false), HandheldLook.BigType);

        Assert.NotNull(derived);
        Assert.True(HandheldGround.HoldsFloors(derived.Value.ToUInt32()), $"{id}: derived {derived} misses a floor");
        Assert.True(HandheldGround.TryParse(system.AccentColor, out var accent));
        Assert.True(HandheldGround.Luminance(derived.Value.ToUInt32()) <= HandheldGround.Luminance(accent));
    }

    [Fact]
    public void Derive_KeepsAGroundThatAlreadyHolds_AndDarkensOneThatDoesNot()
    {
        Assert.Equal(0xFF262E7Au, HandheldGround.Derive(0xFF262E7A));
        var steam = HandheldGround.Derive(0xFF66C0F4); // Steam's accent, far too light for white type
        Assert.NotEqual(0xFF66C0F4u, steam);
        Assert.True(HandheldGround.HoldsFloors(steam));
        Assert.False(HandheldGround.HoldsFloors(0xFF66C0F4));
    }

    [Theory]
    [MemberData(nameof(Systems))]
    public void ConsoleColour_IsTheAccentInTodaysLook_AndTheGroundInBigType(string id)
    {
        var system = KnownSystems.All.Single(candidate => candidate.Id == id);
        var platform = Platform(system);

        Assert.Equal(Color.Parse(system.AccentColor), HandheldTokens.ConsoleColour(platform, HandheldLook.Classic));
        Assert.Equal(Color.Parse(system.GroundColor!), HandheldTokens.ConsoleColour(platform, HandheldLook.BigType));
    }

    [Fact]
    public void ConsoleColour_HasNoColour_ForNoPlatformOrAnAccentThatDoesNotParse()
    {
        Assert.Null(HandheldTokens.ConsoleColour(null, HandheldLook.Classic));
        Assert.Null(HandheldTokens.ConsoleColour(null, HandheldLook.BigType));
        var broken = new HandheldPlatform("x", "X", "X", "not a colour", 1);
        Assert.Null(HandheldTokens.ConsoleColour(broken, HandheldLook.Classic));
        Assert.Null(HandheldTokens.ConsoleColour(broken, HandheldLook.BigType));
        Assert.Same(Brushes.Gray, HandheldTokens.BrushOf("not a colour"));
    }

    [Fact]
    public void TheFace_IsAppAxamlsHandheldFamily_AndTodaysLookSetsNone()
    {
        var appXaml = Path.Combine(RepositoryRoot(), "src", "EmuShelf.UI", "App.axaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var declared = XDocument.Load(appXaml).Descendants()
            .Single(element => (string?)element.Attribute(x + "Key") == "EmuHandheldFontFamily");
        Assert.Equal(HandheldTokens.FaceSource, declared.Value.Trim());

        // The tests run in today's look (only a design render switches, and puts it back).
        Assert.Equal(HandheldLook.Classic, HandheldTokens.Look);
        Assert.Null(HandheldTokens.Face);
    }

    [Theory]
    [InlineData("bigtype", HandheldLook.BigType)]
    [InlineData(" BigType\n", HandheldLook.BigType)]
    [InlineData("classic", HandheldLook.Classic)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("on", null)]
    public void TheLookOverride_ReadsOnlyALooksName(string? text, HandheldLook? expected) =>
        Assert.Equal(expected, HandheldLookChoice.Parse(text));

    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EmuShelf.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
