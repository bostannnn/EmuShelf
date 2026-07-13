using EmuShelf.Core.Library;
using EmuShelf.Infrastructure.Library;
using EmuShelf.Infrastructure.Persistence;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.Tests.Library;

public class GameLibraryTests : TempAppDirectoryTestBase
{
    private readonly GameLibrary _library;

    public GameLibraryTests()
    {
        AppPaths.EnsureDirectoriesExist();
        var database = new LibraryDatabase(AppPaths);
        database.Initialize();
        _library = new GameLibrary(database, new RelativePathResolver(AppPaths));
    }

    private Game NewGame(string system, string path, string title) => new()
    {
        SystemId = system,
        Path = path,
        Title = title,
        DateAdded = DateTimeOffset.Now,
    };

    [Fact]
    public void AddGames_InsertsNewAndReportsCount()
    {
        var added = _library.AddGames([
            NewGame("playstation", "/games/ps1/a.cue", "A"),
            NewGame("playstation", "/games/ps1/b.chd", "B"),
        ]);

        Assert.Equal(2, added);
        Assert.Equal(2, _library.GetGames("playstation").Count);
    }

    [Fact]
    public void AddGames_IsIdempotentByPath()
    {
        _library.AddGames([NewGame("playstation", "/games/ps1/a.cue", "A")]);
        var addedAgain = _library.AddGames([NewGame("playstation", "/games/ps1/a.cue", "A")]);

        Assert.Equal(0, addedAgain);
        Assert.Single(_library.GetGames("playstation"));
    }

    [Fact]
    public void GetGames_FiltersBySystem_AndOrdersByTitle()
    {
        _library.AddGames([
            NewGame("playstation", "/g/z.cue", "Zelda-ish"),
            NewGame("playstation", "/g/a.cue", "Ace"),
            NewGame("wii", "/g/w.iso", "WiiGame"),
        ]);

        var ps = _library.GetGames("playstation");
        Assert.Equal(["Ace", "Zelda-ish"], ps.Select(g => g.Title));
        Assert.Single(_library.GetGames("wii"));
        Assert.Equal(3, _library.GetGames().Count);
    }

    [Fact]
    public void SetAvailability_Persists()
    {
        _library.AddGames([NewGame("playstation", "/g/a.cue", "A")]);
        var game = _library.GetGames("playstation").Single();
        Assert.True(game.IsAvailable);

        _library.SetAvailability(game.Id, false);

        Assert.False(_library.GetGames("playstation").Single().IsAvailable);
    }

    [Fact]
    public void AddGames_StoresPathRelativeToAppDirectory()
    {
        var absolute = Path.Combine(BaseDirectory, "Games", "ps1", "a.cue");
        _library.AddGames([NewGame("playstation", absolute, "A")]);

        // Round-trips back to the absolute path...
        Assert.Equal(absolute, _library.GetGames("playstation").Single().Path);

        // ...but is persisted relative (portable) under the app directory.
        using var connection = new LibraryDatabase(AppPaths).CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Path FROM Games LIMIT 1;";
        var stored = (string)command.ExecuteScalar()!;
        Assert.False(Path.IsPathRooted(stored));
        Assert.Equal("Games/ps1/a.cue", stored);
    }

    [Fact]
    public void LibraryFolders_AddAndFilterBySystem_WithDedup()
    {
        _library.AddLibraryFolder("playstation", "/roms/ps1");
        _library.AddLibraryFolder("playstation", "/roms/ps1"); // duplicate ignored
        _library.AddLibraryFolder("wii", "/roms/wii");

        Assert.Single(_library.GetLibraryFolders("playstation"));
        Assert.Equal(2, _library.GetLibraryFolders().Count);
    }
}
