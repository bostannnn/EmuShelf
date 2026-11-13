using EmuShelf.App.ViewModels;
using EmuShelf.App.Services;

namespace EmuShelf.App.Tests.Handheld;

// M47 B3: the Launcher's ground (drawn in the Big Type look) follows the focus: a folder's colour, the apps' indigo, and an
// empty slot keeps the colour it had. The dock's buttons carry it as their focused ink.
public partial class HandheldShellViewModelTests
{
    [Fact]
    public async Task LauncherGround_FollowsTheFocus_AndAnEmptySlotKeepsIt()
    {
        var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 3));
        var ps2 = shell.CurrentPage!.Slots[0];
        Assert.Equal(HandheldTokens.LauncherColour(ps2.Folder) ?? HandheldTokens.DefaultGround, shell.LauncherGround);

        // ps2, Recent, then Settings: the apps' ground.
        Press(shell, GamepadAction.NavigateRight, 2);
        Assert.True(shell.CurrentPage.Slots[2].IsSettings);
        Assert.Equal(HandheldTokens.AppGround, shell.LauncherGround);

        // Activity, then the first empty slot: it has no colour of its own and keeps the apps'.
        Press(shell, GamepadAction.NavigateRight, 2);
        Assert.True(shell.CurrentPage.Slots[4].IsEmpty);
        Assert.Equal(HandheldTokens.AppGround, shell.LauncherGround);
        Assert.All(shell.DockItems, item => Assert.Same(shell.LauncherGroundBrush, item.Ground));
    }

    [Fact]
    public async Task ABigTypeTile_TurnsWhite_WithItsColourAsInk_WhileFocused()
    {
        var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 3));
        var ps2 = shell.CurrentPage!.Slots[0];
        var recent = shell.CurrentPage.Slots[1];

        Assert.True(ps2.IsFocused);
        Assert.Same(HandheldTokens.TileFocus, ps2.FaceBrush);
        Assert.Same(ps2.GroundBrush, ps2.InkBrush);
        Assert.Same(HandheldTokens.TileRest, recent.FaceBrush);
        Assert.Same(HandheldTokens.OnColour, recent.InkBrush);
        Assert.True(ps2.HasFaceMonogram);
        Assert.True(recent.HasFaceGlyph);
        Assert.True(shell.CurrentPage.Slots[5].IsPlainEmpty);
    }
}
