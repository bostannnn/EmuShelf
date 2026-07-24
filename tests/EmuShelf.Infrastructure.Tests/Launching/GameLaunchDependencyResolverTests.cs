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

    private static Game GameFor(string path) => new()
    {
        SystemId = "playstation",
        Path = path,
        Title = "Test game",
        DateAdded = DateTimeOffset.UtcNow,
    };
}
