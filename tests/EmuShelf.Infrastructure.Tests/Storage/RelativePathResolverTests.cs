using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.Tests.Storage;

public class RelativePathResolverTests
{
    private static IAppPaths MakeAppPaths(string baseDirectory) => new AppPaths(baseDirectory);

    [Fact]
    public void ToStorablePath_PathUnderAppDirectory_ReturnsRelativeWithForwardSlashes()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "EmuShelfTests", "app");
        var resolver = new RelativePathResolver(MakeAppPaths(baseDir));
        var absolute = Path.Combine(baseDir, "Games", "PS1", "game.cue");

        var stored = resolver.ToStorablePath(absolute);

        Assert.False(Path.IsPathRooted(stored));
        // Canonical '/' separators so the stored library survives moving between OSes.
        Assert.Equal("Games/PS1/game.cue", stored);
        Assert.DoesNotContain('\\', stored);
    }

    [Fact]
    public void ToStorablePath_PathOutsideAppDirectoryButSameVolume_ReturnsRelativeWithParentSegments()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "EmuShelfTests", "app");
        var sibling = Path.Combine(Path.GetTempPath(), "EmuShelfTests", "games", "game.iso");
        var resolver = new RelativePathResolver(MakeAppPaths(baseDir));

        var stored = resolver.ToStorablePath(sibling);

        Assert.False(Path.IsPathRooted(stored));
        Assert.Contains("..", stored);
    }

    [Fact]
    public void ToAbsolutePath_RelativeStoredPath_ResolvesUnderAppDirectory()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "EmuShelfTests", "app");
        var resolver = new RelativePathResolver(MakeAppPaths(baseDir));

        var absolute = resolver.ToAbsolutePath(Path.Combine("Games", "game.cue"));

        Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "Games", "game.cue")), absolute);
    }

    [Fact]
    public void ToAbsolutePath_AlreadyAbsoluteStoredPath_ReturnsUnchanged()
    {
        var resolver = new RelativePathResolver(MakeAppPaths(Path.GetTempPath()));
        var absolute = Path.Combine(Path.GetTempPath(), "elsewhere", "game.iso");

        Assert.Equal(absolute, resolver.ToAbsolutePath(absolute));
    }

    [Fact]
    public void RoundTrip_PathUnderAppDirectory_ResolvesBackToOriginal()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "EmuShelfTests", "app");
        var resolver = new RelativePathResolver(MakeAppPaths(baseDir));
        var absolute = Path.GetFullPath(Path.Combine(baseDir, "Games", "game.cue"));

        var roundTripped = resolver.ToAbsolutePath(resolver.ToStorablePath(absolute));

        Assert.Equal(absolute, roundTripped);
    }

    [Fact]
    public void ToAbsolutePath_ForwardSlashStoredPath_ResolvesOnThisOs()
    {
        // A path stored on another OS always uses '/'; it must resolve on this one.
        var baseDir = Path.Combine(Path.GetTempPath(), "EmuShelfTests", "app");
        var resolver = new RelativePathResolver(MakeAppPaths(baseDir));

        var absolute = resolver.ToAbsolutePath("Games/PS1/game.cue");

        Assert.Equal(
            Path.GetFullPath(Path.Combine(baseDir, "Games", "PS1", "game.cue")),
            absolute);
    }

    [Fact]
    public void ToStorablePath_NonPortableStorage_ReturnsAbsoluteEvenOnSameRoot()
    {
        // Android case: app base is app-private, the game is on shared storage; both root at '/', but
        // relativizing would emit a fragile '../../../storage/…' path. Non-portable storage stores absolute.
        var resolver = new RelativePathResolver(
            new AppPaths("/data/data/com.emushelf.app/files", usesPortableStorage: false));

        var stored = resolver.ToStorablePath("/storage/AE6A-1092/roms/psx/game.m3u");

        Assert.Equal("/storage/AE6A-1092/roms/psx/game.m3u", stored);
    }

    [Fact]
    public void ToStorablePath_DifferentWindowsDrive_ReturnsAbsolute()
    {
        if (!OperatingSystem.IsWindows())
            return; // drive-letter volumes only exist on Windows; exercised by the windows-latest CI runner.

        var resolver = new RelativePathResolver(MakeAppPaths(@"C:\EmuShelf"));

        var stored = resolver.ToStorablePath(@"D:\Games\game.iso");

        Assert.Equal(@"D:\Games\game.iso", stored);
    }
}
