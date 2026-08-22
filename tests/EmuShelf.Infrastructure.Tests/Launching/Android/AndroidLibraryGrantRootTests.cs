using EmuShelf.Core.Library;
using EmuShelf.Integrations.Emulators.Android;

namespace EmuShelf.Infrastructure.Tests.Launching.Android;

public class AndroidLibraryGrantRootTests
{
    private static LibraryFolder Folder(string path) =>
        new() { SystemId = "playstation", Path = path };

    [Fact]
    public void ForGame_ReturnsTheLibraryFolderContainingANestedMultiDiscGame()
    {
        // The import folder (roms/psx) is the emulator's grant root; the nested .m3u lives beneath it.
        var root = AndroidLibraryGrantRoot.ForGame(
            [Folder("/storage/AE6A-1092/roms/psx")],
            "/storage/AE6A-1092/roms/psx/Metal Gear Solid (USA) (Rev 1)/Metal Gear Solid (USA) (Rev 1).m3u");

        Assert.Equal("/storage/AE6A-1092/roms/psx", root);
    }

    [Fact]
    public void ForGame_ReturnsTheFolderForAGameSittingDirectlyInIt()
    {
        var root = AndroidLibraryGrantRoot.ForGame(
            [Folder("/storage/AE6A-1092/roms/psx")],
            "/storage/AE6A-1092/roms/psx/Crash Bandicoot (USA).chd");

        Assert.Equal("/storage/AE6A-1092/roms/psx", root);
    }

    [Fact]
    public void ForGame_PicksTheMostSpecificAncestorWhenFoldersNest()
    {
        var root = AndroidLibraryGrantRoot.ForGame(
            [Folder("/storage/AE6A-1092/roms"), Folder("/storage/AE6A-1092/roms/psx")],
            "/storage/AE6A-1092/roms/psx/game.chd");

        Assert.Equal("/storage/AE6A-1092/roms/psx", root);
    }

    [Fact]
    public void ForGame_ReturnsNullWhenNoFolderContainsTheGame()
    {
        var root = AndroidLibraryGrantRoot.ForGame(
            [Folder("/storage/AE6A-1092/roms/ps2")],
            "/storage/AE6A-1092/roms/psx/game.chd");

        Assert.Null(root);
    }

    [Fact]
    public void ForGame_DoesNotMatchASiblingWithASharedNamePrefix()
    {
        // roms/psx must not be treated as an ancestor of roms/psx-extra.
        var root = AndroidLibraryGrantRoot.ForGame(
            [Folder("/storage/AE6A-1092/roms/psx")],
            "/storage/AE6A-1092/roms/psx-extra/game.chd");

        Assert.Null(root);
    }

    [Fact]
    public void ForGame_ToleratesATrailingSlashOnTheFolder()
    {
        var root = AndroidLibraryGrantRoot.ForGame(
            [Folder("/storage/AE6A-1092/roms/psx/")],
            "/storage/AE6A-1092/roms/psx/game.chd");

        Assert.Equal("/storage/AE6A-1092/roms/psx/", root);
    }

    [Fact]
    public void ForGame_ReturnsNullForNoFolders()
    {
        Assert.Null(AndroidLibraryGrantRoot.ForGame([], "/storage/AE6A-1092/roms/psx/game.chd"));
    }
}
