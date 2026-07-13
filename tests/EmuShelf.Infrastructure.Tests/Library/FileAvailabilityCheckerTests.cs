using EmuShelf.Core.Library;
using EmuShelf.Infrastructure.Library;

namespace EmuShelf.Infrastructure.Tests.Library;

public class FileAvailabilityCheckerTests : TempAppDirectoryTestBase
{
    private readonly FileAvailabilityChecker _checker = new();

    private static Game GameAt(string path) => new()
    {
        SystemId = "playstation",
        Path = path,
        Title = "x",
        DateAdded = DateTimeOffset.Now,
    };

    [Fact]
    public void IsAvailable_ExistingFile_True()
    {
        Directory.CreateDirectory(BaseDirectory);
        var file = Path.Combine(BaseDirectory, "game.cue");
        File.WriteAllText(file, "x");

        Assert.True(_checker.IsAvailable(GameAt(file)));
    }

    [Fact]
    public void IsAvailable_MissingPath_False()
    {
        Assert.False(_checker.IsAvailable(GameAt(Path.Combine(BaseDirectory, "nope.cue"))));
    }

    [Fact]
    public void IsAvailable_Directory_True()
    {
        // Directory-based games (PS3, M5) point at a folder.
        Directory.CreateDirectory(BaseDirectory);
        Assert.True(_checker.IsAvailable(GameAt(BaseDirectory)));
    }
}
