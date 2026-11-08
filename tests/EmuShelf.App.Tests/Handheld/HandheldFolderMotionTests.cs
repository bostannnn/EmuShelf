using EmuShelf.Core.Handheld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views.Handheld;
using EmuShelf.Core.Systems;
using EmuShelf.Integrations.Systems;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

/// <summary>M45 H4: the folder's Zoom through and the Stage's era motif (picked in the folder lab).</summary>
public class HandheldFolderMotionTests
{
    [Fact]
    public void ZoomSheet_ArrivesWithASmallSpringyOvershoot_InAboutItsDuration()
    {
        var easing = new HandheldSpringEasing(HandheldMotion.ZoomSheet);
        var span = 1 - HandheldMotion.ZoomSheetFromScale;
        var peakScale = HandheldMotion.ZoomSheetFromScale + (span * easing.Peak);

        // "Springy, overshoot ≈ 2–5%": the page grows just past full size and settles.
        Assert.InRange(peakScale, 1.02, 1.05);
        Assert.InRange(easing.Duration.TotalMilliseconds, HandheldMotion.ZoomSheetInMilliseconds - 40, HandheldMotion.ZoomSheetInMilliseconds + 40);
    }

    [Fact]
    public void ZoomTileReturn_LandsOnItsSlot_WithOnlyAHairUnder()
    {
        // Played as a 0 → 1 curve from 2.6× to 1×: a peak above 1 is a dip below 1× on screen.
        var easing = new HandheldSpringEasing(HandheldMotion.ZoomTile);
        var lowestScale = HandheldMotion.ZoomTileScale + ((1 - HandheldMotion.ZoomTileScale) * easing.Peak);

        Assert.InRange(lowestScale, 0.97, 1.0);
        Assert.InRange(easing.Duration.TotalMilliseconds, HandheldMotion.ZoomTileInMilliseconds - 40, HandheldMotion.ZoomTileInMilliseconds + 40);
    }

    [Fact]
    public void ZoomNumbers_AreTheLabsZoomThrough()
    {
        Assert.Equal(2.6, HandheldMotion.ZoomTileScale);
        Assert.Equal(300, HandheldMotion.ZoomTileOutMilliseconds);
        Assert.Equal(0.55, HandheldMotion.ZoomSheetFromScale);
        Assert.Equal(380, HandheldMotion.ZoomSheetInMilliseconds);
        Assert.Equal(60, HandheldMotion.ZoomSheetInDelayMilliseconds);
        Assert.Equal(260, HandheldMotion.ZoomSheetOutMilliseconds);
        // The selector comes back once the sheet has landed, before the Stage's first game arrives.
        Assert.True(HandheldMotion.ZoomSelectorHiddenMilliseconds <= HandheldMotion.FolderOpenStageHold.TotalMilliseconds);
    }

    [Theory]
    [InlineData("nes")]
    [InlineData("snes")]
    [InlineData("gbc")]
    [InlineData("gba")]
    [InlineData("nds")]
    [InlineData("3ds")]
    [InlineData("megadrive")]
    [InlineData("arcade")]
    public void CartridgeSystems_HaveTheCartridgeEra(string id) =>
        Assert.Equal(SystemEra.Cartridge, KnownSystems.All.Single(system => system.Id == id).Era);

    [Theory]
    [InlineData("playstation")]
    [InlineData("playstation2")]
    [InlineData("playstation3")]
    [InlineData("psp")]
    [InlineData("gamecube")]
    [InlineData("wii")]
    [InlineData("dreamcast")]
    [InlineData("steam")]
    public void DiscSystems_HaveTheDiscEra(string id) =>
        Assert.Equal(SystemEra.Disc, KnownSystems.All.Single(system => system.Id == id).Era);

    [Fact]
    public void CartridgeGrid_IsTwoPixelLinesAtATenthOnA22PixelPitch()
    {
        var line = HandheldEraBackdrop.Hsl(210, 0.5, 0.6, 0.10);
        var pixels = HandheldEraBackdrop.TileSet.GridPixels(line);

        Assert.Equal(22 * 22, pixels.Length);
        Assert.Equal(line, pixels[(5 * 22) + 0]);   // left edge, 2 px wide
        Assert.Equal(line, pixels[(5 * 22) + 1]);
        Assert.Equal(default, pixels[(5 * 22) + 2]);
        Assert.Equal(line, pixels[(21 * 22) + 10]); // bottom edge, 2 px tall
        Assert.Equal(line, pixels[(20 * 22) + 10]);
        Assert.Equal(default, pixels[(19 * 22) + 10]);
        Assert.InRange(line.A / 255d, 0.09, 0.11);
    }

    [Fact]
    public void CartridgeBlocks_AreEight22PixelBlocksAtAFifthInThreeNeighbouringHues()
    {
        var pixels = HandheldEraBackdrop.TileSet.BlockPixels(200);

        Assert.Equal(176 * 132, pixels.Length);
        Assert.Equal(8 * 22 * 22, pixels.Count(pixel => pixel.A > 0));
        Assert.All(pixels.Where(pixel => pixel.A > 0), pixel => Assert.InRange(pixel.A / 255d, 0.21, 0.23));
        var hues = pixels.Where(pixel => pixel.A > 0).Select(pixel => Math.Round(pixel.ToHsl().H)).Distinct().Order().ToArray();
        Assert.Equal(3, hues.Length);
        Assert.InRange(hues[0], 178, 182);
        Assert.InRange(hues[2], 218, 222);
    }

    [Fact]
    public void DiscScanlines_AreTwoPixelLinesOnAFivePixelPitch()
    {
        var line = HandheldEraBackdrop.Hsl(30, 0.4, 0.3, 0.09);
        var pixels = HandheldEraBackdrop.TileSet.ScanlinePixels(line);

        Assert.Equal(5 * 5, pixels.Length);
        var rows = Enumerable.Range(0, 5).Select(row => pixels[row * 5].A > 0).ToArray();
        Assert.Equal([false, false, false, true, true], rows);
    }

    [Fact]
    public void BackdropColours_StayInTheFoldersHue_AndLight()
    {
        var field = HandheldEraBackdrop.Hsl(-20, 0.40, 0.92);
        var hsl = field.ToHsl();

        Assert.InRange(hsl.H, 335, 345); // 8-bit RGB this light holds the hue to a few degrees
        Assert.InRange(hsl.L, 0.91, 0.93);
        Assert.Equal(255, field.A);
    }
}

public partial class HandheldShellViewModelTests
{
    [Fact]
    public async Task StageBackdrop_FollowsTheFocusedSystem_ColourAndEra()
    {
        var library = new FakeLibrary();
        library.Platforms.Add(new HandheldPlatform("snes", "Super Nintendo", "SNES", "#8D66C4", 2, SystemEra.Cartridge));
        library.Games["snes"] = [Entry(1, "snes"), Entry(2, "snes")];
        library.Platforms.Add(new HandheldPlatform("ps2", "PlayStation 2", "PS2", "#3D6DB5", 1, SystemEra.Disc));
        library.Games["ps2"] = [Entry(3, "ps2")];
        var shell = await CreateAsync(library);

        Assert.Equal(new HandheldBackdrop(Color.Parse("#8D66C4"), SystemEra.Cartridge), shell.Stage.Backdrop);

        Press(shell, GamepadAction.NavigateRight);
        Assert.Equal(new HandheldBackdrop(Color.Parse("#3D6DB5"), SystemEra.Disc), shell.Stage.Backdrop);

        // Recent (and your folders) sit on the tile grid in their own colour.
        Press(shell, GamepadAction.NavigateRight);
        Assert.Equal(new HandheldBackdrop(Color.Parse(HandheldHomeLayout.RecentColor), SystemEra.Cartridge), shell.Stage.Backdrop);

        // An empty slot has no system: the plain light gradient.
        Press(shell, GamepadAction.NavigateRight);
        Assert.Null(shell.Stage.Backdrop);
    }

    [Fact]
    public async Task StageBackdrop_DoesNotChangeBetweenGamesOfOneSystem()
    {
        var library = new FakeLibrary();
        library.Platforms.Add(new HandheldPlatform("snes", "Super Nintendo", "SNES", "#8D66C4", 2, SystemEra.Cartridge));
        library.Games["snes"] = [Entry(1, "snes"), Entry(2, "snes")];
        var shell = await CreateAsync(library);
        Press(shell, GamepadAction.Confirm);
        var changes = 0;
        shell.Stage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HandheldStageViewModel.Backdrop))
                changes++;
        };

        Press(shell, GamepadAction.NavigateDown);

        Assert.Equal("snes game 2", shell.Stage.Title);
        Assert.Equal(0, changes);
        Assert.Equal(SystemEra.Cartridge, shell.Stage.Backdrop?.Era);
    }

    private static HandheldGameEntry Entry(long id, string system) =>
        new(id, $"{system} game {id}", system, system, null, 0, TimeSpan.Zero, true);
}

public class HandheldFolderWindowTests
{
    private static List<HandheldGameTileViewModel> Tiles(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new HandheldGameTileViewModel(i, new HandheldGameEntry(i, $"g{i}", "ds", "DS", null, 0, TimeSpan.Zero, true)))
            .ToList();

    [Fact]
    public void ScrollingAColumn_TouchesOnlyTheColumnsThatLeaveAndArrive()
    {
        var items = Tiles(60);
        var visible = new System.Collections.ObjectModel.ObservableCollection<HandheldGameTileViewModel>();
        HandheldShellViewModel.SlideWindow(visible, items, 0, 18);
        var changes = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        visible.CollectionChanged += (_, e) => changes.Add(e.Action);

        HandheldShellViewModel.SlideWindow(visible, items, 3, 21);

        Assert.Equal(Enumerable.Range(3, 18), visible.Select(tile => tile.Index));
        // Three removed at the front, three added at the back; no reset, so no tile control is rebuilt.
        Assert.Equal(6, changes.Count);
        Assert.DoesNotContain(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, changes);
        Assert.Same(items[3], visible[0]);

        changes.Clear();
        HandheldShellViewModel.SlideWindow(visible, items, 0, 18);
        Assert.Equal(Enumerable.Range(0, 18), visible.Select(tile => tile.Index));
        Assert.Equal(6, changes.Count);
    }

    [Fact]
    public void AJumpPastTheWindow_ReplacesIt()
    {
        var items = Tiles(60);
        var visible = new System.Collections.ObjectModel.ObservableCollection<HandheldGameTileViewModel>();
        HandheldShellViewModel.SlideWindow(visible, items, 0, 18);

        HandheldShellViewModel.SlideWindow(visible, items, 42, 60);

        Assert.Equal(Enumerable.Range(42, 18), visible.Select(tile => tile.Index));
    }

    [Fact]
    public void ATrackSpring_NeverLagsMoreThanItsLimit_AndKeepsItsSpeed()
    {
        var spring = new HandheldSpring(HandheldMotion.Track);
        spring.Snap(0);
        spring.Displace(264);
        spring.Step(1d / 120d);
        var speed = spring.Velocity;
        spring.Displace(264);
        spring.Limit(264);

        Assert.True(spring.Value <= 264);
        Assert.Equal(speed, spring.Velocity);
    }
}
