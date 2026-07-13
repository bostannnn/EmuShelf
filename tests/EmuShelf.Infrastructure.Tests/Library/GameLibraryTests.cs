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
    public void ReconcileImport_AtomicallyAddsEntryAndSuppressesOnlyTargetSystemRows()
    {
        var disc = "/games/ps1/disc.chd";
        var unrelatedWiiGame = "/games/wii/game.iso";
        _library.AddGames([
            NewGame("playstation", disc, "Disc"),
            NewGame("wii", unrelatedWiiGame, "Wii Game"),
        ]);

        var added = _library.ReconcileImport(
            "playstation",
            [NewGame("playstation", "/games/ps1/collection.m3u", "Collection")],
            [disc, unrelatedWiiGame]);

        Assert.Equal(1, added.AddedCount);
        Assert.Equal(["Collection"], _library.GetGames("playstation").Select(game => game.Title));
        Assert.Equal(["Wii Game"], _library.GetGames("wii").Select(game => game.Title));
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
    public void GetRecentlyAddedGames_LimitsAndOrdersInTheDatabase()
    {
        var oldest = DateTimeOffset.Parse("2026-07-13T10:00:00+03:00");
        var middle = DateTimeOffset.Parse("2026-07-13T08:30:00+00:00");
        var newest = DateTimeOffset.Parse("2026-07-13T12:00:00+03:00");
        _library.AddGames([
            NewGame("playstation", "/g/old.cue", "Old") with { DateAdded = oldest },
            NewGame("wii", "/g/middle.iso", "Middle") with { DateAdded = middle },
            NewGame("gamecube", "/g/new.iso", "New") with { DateAdded = newest },
        ]);

        var recent = _library.GetRecentlyAddedGames(2);

        Assert.Equal(["New", "Middle"], recent.Select(game => game.Title));
        Assert.Empty(_library.GetRecentlyAddedGames(0));
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
    public void SetAvailabilities_PersistsBatch()
    {
        _library.AddGames([
            NewGame("playstation", "/g/a.cue", "A"),
            NewGame("playstation", "/g/b.cue", "B"),
        ]);
        var games = _library.GetGames("playstation");

        _library.SetAvailabilities(games
            .Select(game => new GameAvailabilityUpdate(game.Id, false))
            .ToArray());

        Assert.All(_library.GetGames("playstation"), game => Assert.False(game.IsAvailable));
    }

    [Fact]
    public void UpdateTitle_PersistsAndChangesLibraryOrdering()
    {
        _library.AddGames([
            NewGame("playstation", "/g/a.cue", "Alpha"),
            NewGame("playstation", "/g/z.cue", "Zulu"),
        ]);
        var zulu = _library.GetGames("playstation").Single(game => game.Title == "Zulu");

        _library.UpdateTitle(zulu.Id, "Aardvark");

        Assert.Equal(["Aardvark", "Alpha"],
            _library.GetGames("playstation").Select(game => game.Title));
        Assert.Equal(
            GameTitleOrigin.User,
            _library.GetGames("playstation").Single(game => game.Title == "Aardvark").TitleOrigin);
    }

    [Fact]
    public void UpdateCoverPath_RoundTripsPortablePath()
    {
        _library.AddGames([NewGame("playstation", "/g/a.cue", "A")]);
        var game = _library.GetGames("playstation").Single();
        var coverPath = Path.Combine(AppPaths.CoversDirectory, "1.png");

        _library.UpdateCoverPath(game.Id, coverPath);

        Assert.Equal(coverPath, _library.GetGames("playstation").Single().CoverPath);
        Assert.Equal(GameCoverOrigin.User, _library.GetGames("playstation").Single().CoverOrigin);
        using var connection = new LibraryDatabase(AppPaths).CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CoverPath FROM Games WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", game.Id);
        Assert.Equal("Covers/1.png", command.ExecuteScalar());
    }

    [Fact]
    public void RemoveGame_DeletesOnlyDatabaseRow()
    {
        var gamePath = Path.Combine(BaseDirectory, "Games", "A.cue");
        var coverPath = Path.Combine(AppPaths.CoversDirectory, "1.png");
        Directory.CreateDirectory(Path.GetDirectoryName(gamePath)!);
        File.WriteAllText(gamePath, "game");
        File.WriteAllText(coverPath, "cover");
        _library.AddGames([NewGame("playstation", gamePath, "A") with { CoverPath = coverPath }]);
        var game = _library.GetGames("playstation").Single();

        _library.RemoveGame(game.Id);

        Assert.Empty(_library.GetGames("playstation"));
        Assert.True(File.Exists(gamePath));
        Assert.True(File.Exists(coverPath));
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
    public void AddGames_WithExistingCover_ClassifiesItAsUserOwned()
    {
        var coverPath = Path.Combine(AppPaths.CoversDirectory, "existing.png");

        _library.AddGames([
            NewGame("playstation", "/g/a.cue", "A") with { CoverPath = coverPath },
        ]);

        Assert.Equal(GameCoverOrigin.User, _library.GetGames().Single().CoverOrigin);
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
