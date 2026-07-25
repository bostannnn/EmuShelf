using EmuShelf.Core.Library;
using EmuShelf.Integrations.Launching;

namespace EmuShelf.Infrastructure.Tests.Launching;

public sealed class GameLaunchDependencyResolverTests : TempAppDirectoryTestBase
{
    [Fact]
    public void Resolve_RecursesM3uAndCueDependencies()
    {
        Directory.CreateDirectory(BaseDirectory);
        var playlist = Path.Combine(BaseDirectory, "collection.m3u");
        var cue = Path.Combine(BaseDirectory, "disc.cue");
        var track = Path.Combine(BaseDirectory, "track.bin");
        File.WriteAllText(playlist, "disc.cue\n");
        File.WriteAllText(cue, "FILE \"track.bin\" BINARY\n");
        File.WriteAllBytes(track, [1, 2, 3]);

        var result = new GameLaunchDependencyResolver().Resolve(GameFor(playlist));

        Assert.True(result.IsComplete);
        Assert.Equal(
            [Path.GetFullPath(playlist), Path.GetFullPath(cue), Path.GetFullPath(track)],
            result.Paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_CyclicDescriptors_FailsWithoutLooping()
    {
        Directory.CreateDirectory(BaseDirectory);
        var first = Path.Combine(BaseDirectory, "first.m3u");
        var second = Path.Combine(BaseDirectory, "second.m3u");
        File.WriteAllText(first, "second.m3u\n");
        File.WriteAllText(second, "first.m3u\n");

        var result = new GameLaunchDependencyResolver().Resolve(GameFor(first));

        Assert.False(result.IsComplete);
        Assert.Contains("cycle", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_MissingCueTrack_FailsAsIncomplete()
    {
        Directory.CreateDirectory(BaseDirectory);
        var cue = Path.Combine(BaseDirectory, "disc.cue");
        File.WriteAllText(cue, "FILE \"missing.bin\" BINARY\n");

        var result = new GameLaunchDependencyResolver().Resolve(GameFor(cue));

        Assert.False(result.IsComplete);
        Assert.Contains("missing.bin", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_GdiIncludesEveryReferencedTrack()
    {
        Directory.CreateDirectory(BaseDirectory);
        var gdi = Path.Combine(BaseDirectory, "disc.gdi");
        var tracks = new[] { "track01.bin", "track02.raw", "track03.bin" };
        File.WriteAllText(gdi, "3\n1 0 4 2352 track01.bin 0\n2 600 0 2352 track02.raw 0\n3 45000 4 2352 track03.bin 0\n");
        foreach (var track in tracks)
            File.WriteAllBytes(Path.Combine(BaseDirectory, track), new byte[2352]);

        var result = new GameLaunchDependencyResolver().Resolve(GameFor(gdi));

        Assert.True(result.IsComplete);
        Assert.Equal(
            tracks.Prepend(gdi).Select(track =>
                track == gdi ? track : Path.Combine(BaseDirectory, track))
                .Select(Path.GetFullPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
            result.Paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_MissingGdiTrack_FailsAsIncomplete()
    {
        Directory.CreateDirectory(BaseDirectory);
        var gdi = Path.Combine(BaseDirectory, "disc.gdi");
        File.WriteAllText(gdi, "3\n1 0 4 2352 track01.bin 0\n2 600 0 2352 track02.raw 0\n3 45000 4 2352 track03.bin 0\n");
        File.WriteAllBytes(Path.Combine(BaseDirectory, "track01.bin"), new byte[2352]);
        File.WriteAllBytes(Path.Combine(BaseDirectory, "track03.bin"), new byte[2352]);

        var result = new GameLaunchDependencyResolver().Resolve(GameFor(gdi));

        Assert.False(result.IsComplete);
        Assert.Contains("track02.raw", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static Game GameFor(string path) => new()
    {
        SystemId = "playstation",
        Path = path,
        Title = "Test game",
        DateAdded = DateTimeOffset.UtcNow,
    };
}
