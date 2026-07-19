using EmuShelf.Core.Importing;
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

    [Fact]
    public void ReconcileExternalLibrary_RetainsProvenanceAndMarksAbsentSourceEntriesUnavailable()
    {
        var source = new ExternalLibrarySource(
            "rpcs3-test-library",
            "playstation3",
            "RPCS3 test library",
            Path.Combine(BaseDirectory, "RPCS3"));
        var entry = new ExternalLibraryGameEntry(
            "BLUS-12345",
            Path.Combine(BaseDirectory, "Games", "Example Game"),
            "Example Game");
        _library.AddGames([NewGame("playstation", "/manual/keep.cue", "Keep me")]);

        var first = _library.ReconcileExternalLibrary(source, [entry]);
        var imported = _library.GetGames("playstation3").Single();

        Assert.Equal(1, first.AddedCount);
        Assert.Equal("rpcs3-test-library", imported.ExternalSourceId);
        Assert.Equal("BLUS-12345", imported.ExternalSourceEntryId);
        Assert.Equal(GameTitleOrigin.Embedded, imported.TitleOrigin);
        Assert.True(imported.IsPresentInExternalSource);
        Assert.True(imported.IsAvailable);

        var refreshed = _library.ReconcileExternalLibrary(source, [entry]);

        Assert.Equal(0, refreshed.MarkedSourceMissingCount);
        Assert.True(_library.GetGames("playstation3").Single().IsAvailable);

        var second = _library.ReconcileExternalLibrary(source, []);

        Assert.Equal(1, second.MarkedSourceMissingCount);
        Assert.False(_library.GetGames("playstation3").Single().IsPresentInExternalSource);
        Assert.False(_library.GetGames("playstation3").Single().IsAvailable);
        Assert.Single(_library.GetGames("playstation")); // source reconciliation never deletes manual rows

        using var connection = new LibraryDatabase(AppPaths).CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Location FROM ExternalLibrarySources WHERE SourceId = $id;";
        command.Parameters.AddWithValue("$id", source.Id);
        Assert.Equal("RPCS3", command.ExecuteScalar());
    }

    [Fact]
    public void ReconcileExternalLibrary_PreservesManualTitleWhileRefreshingTheSourcePath()
    {
        var source = new ExternalLibrarySource(
            "rpcs3-test-library",
            "playstation3",
            "RPCS3 test library",
            Path.Combine(BaseDirectory, "RPCS3"));
        var initial = new ExternalLibraryGameEntry(
            "BLUS-12345",
            Path.Combine(BaseDirectory, "Games", "Old"),
            "Embedded title");
        _library.ReconcileExternalLibrary(source, [initial]);
        var imported = _library.GetGames("playstation3").Single();
        _library.UpdateTitle(imported.Id, "My title");
        var movedPath = Path.Combine(BaseDirectory, "Games", "Moved");

        var result = _library.ReconcileExternalLibrary(source,
        [
            new ExternalLibraryGameEntry("BLUS-12345", movedPath, "Different embedded title"),
        ]);

        var refreshed = _library.GetGames("playstation3").Single();
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(movedPath, refreshed.Path);
        Assert.Equal("My title", refreshed.Title);
        Assert.Equal(GameTitleOrigin.User, refreshed.TitleOrigin);
    }

    [Fact]
    public void ReconcileExternalLibrary_RejectsAPathAlreadyOwnedByAnotherEntry()
    {
        var source = new ExternalLibrarySource(
            "rpcs3-test-library",
            "playstation3",
            "RPCS3 test library",
            Path.Combine(BaseDirectory, "RPCS3"));
        var path = Path.Combine(BaseDirectory, "Games", "Shared path");
        _library.AddGames([NewGame("playstation", path, "Manual game")]);

        var exception = Assert.Throws<ExternalLibrarySourceConflictException>(() =>
            _library.ReconcileExternalLibrary(source,
            [
                new ExternalLibraryGameEntry("BLUS12345", path, "RPCS3 game"),
            ]));

        Assert.Contains("already owned by a different EmuShelf game", exception.Message);
        Assert.Single(_library.GetGames("playstation"));
        Assert.Empty(_library.GetGames("playstation3"));
    }

    [Fact]
    public void ReconcileExternalLibrary_RejectsAConflictingSourceMoveWithoutUpdatingTheOldEntry()
    {
        var source = new ExternalLibrarySource(
            "rpcs3-test-library",
            "playstation3",
            "RPCS3 test library",
            Path.Combine(BaseDirectory, "RPCS3"));
        var oldPath = Path.Combine(BaseDirectory, "Games", "Old path");
        var conflictingPath = Path.Combine(BaseDirectory, "Games", "Manual game");
        _library.ReconcileExternalLibrary(source,
        [
            new ExternalLibraryGameEntry("BLUS12345", oldPath, "RPCS3 game"),
        ]);
        _library.AddGames([NewGame("playstation", conflictingPath, "Manual game")]);

        Assert.Throws<ExternalLibrarySourceConflictException>(() =>
            _library.ReconcileExternalLibrary(source,
            [
                new ExternalLibraryGameEntry("BLUS12345", conflictingPath, "Moved RPCS3 game"),
            ]));

        var retained = Assert.Single(_library.GetGames("playstation3"));
        Assert.Equal(oldPath, retained.Path);
        Assert.True(retained.IsPresentInExternalSource);
        Assert.True(retained.IsAvailable);
    }

    [Fact]
    public void ReconcileExternalLibrary_PreservesManualCoverWhileRefreshingSourceMetadata()
    {
        var source = new ExternalLibrarySource(
            "rpcs3-test-library",
            "playstation3",
            "RPCS3 test library",
            Path.Combine(BaseDirectory, "RPCS3"));
        var initial = new ExternalLibraryGameEntry(
            "BLUS-12345",
            Path.Combine(BaseDirectory, "Games", "Example"),
            "Embedded title");
        _library.ReconcileExternalLibrary(source, [initial]);
        var imported = _library.GetGames("playstation3").Single();
        var cover = Path.Combine(AppPaths.CoversDirectory, "manual.png");
        _library.UpdateCoverPath(imported.Id, cover);

        _library.ReconcileExternalLibrary(source,
        [
            new ExternalLibraryGameEntry(
                "BLUS-12345",
                Path.Combine(BaseDirectory, "Games", "Moved"),
                "Updated embedded title"),
        ]);

        var refreshed = _library.GetGames("playstation3").Single();
        Assert.Equal(cover, refreshed.CoverPath);
        Assert.Equal(GameCoverOrigin.User, refreshed.CoverOrigin);
        Assert.Equal("Updated embedded title", refreshed.Title);
    }

    [Fact]
    public async Task ExternalLibrarySync_CancelledReadLeavesTheDatabaseUntouched()
    {
        var source = new ExternalLibrarySource(
            "rpcs3-test-library",
            "playstation3",
            "RPCS3 test library",
            Path.Combine(BaseDirectory, "RPCS3"));
        var synchronizer = new ExternalLibrarySyncService(_library);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => synchronizer.SyncAsync(
            new CancelledSource(source),
            cancellation.Token));

        Assert.Empty(_library.GetGames("playstation3"));
    }

    private sealed class CancelledSource(ExternalLibrarySource source) : IExternalLibrarySource
    {
        public ExternalLibrarySource Source { get; } = source;

        public Task<IReadOnlyList<ExternalLibraryGameEntry>> ReadGamesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromCanceled<IReadOnlyList<ExternalLibraryGameEntry>>(cancellationToken);
    }
}
