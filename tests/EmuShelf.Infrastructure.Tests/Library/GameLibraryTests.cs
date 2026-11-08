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
        _library = new GameLibrary(
            database,
            new RelativePathResolver(AppPaths),
            new FixedDeviceIdentity("test-device"));
    }

    // One finished sitting: opened at launch, closed at the tracked exit.
    private long Play(long gameId, TimeSpan duration, DateTimeOffset? startedAt = null)
    {
        var sessionId = _library.RecordLaunchStarted(gameId, startedAt ?? DateTimeOffset.UtcNow)!.Value;
        Assert.True(_library.CompletePlaySession(sessionId, duration, PlaySessionMeasurement.TrackedProcess));
        return sessionId;
    }

    private Game NewGame(string system, string path, string title) => new()
    {
        SystemId = system,
        Path = path,
        Title = title,
        DateAdded = DateTimeOffset.Now,
    };

    [Fact]
    public void SteamExportRenamePreservesIdentityTitleAndPlayHistory()
    {
        var firstPath = Path.Combine(BaseDirectory, "Old.steam");
        var secondPath = Path.Combine(BaseDirectory, "New.steam");
        File.WriteAllText(firstPath, "381780");
        var game = NewGame("steam", firstPath, "Old") with
        { ExternalSourceId = "gamenative-steam", ExternalSourceEntryId = "381780" };
        _library.AddGames([game]);
        var original = Assert.Single(_library.GetGames());
        _library.UpdateTitle(original.Id, "My title");
        Play(original.Id, TimeSpan.FromMinutes(10));
        File.Move(firstPath, secondPath);
        Assert.Equal(0, _library.AddGames([game with { Path = secondPath, Title = "New" }]));
        var relocated = Assert.Single(_library.GetGames());
        Assert.Equal(original.Id, relocated.Id);
        Assert.Equal("My title", relocated.Title);
        Assert.Equal(TimeSpan.FromMinutes(10), relocated.Playtime);
        Assert.Equal(secondPath, relocated.Path);
        Assert.Equal("381780", File.ReadAllText(secondPath));
    }

    [Fact]
    public void SteamDuplicateExportsShareOneRecord_ChangedIdentityRollsBack()
    {
        var path = Path.Combine(BaseDirectory, "First.steam");
        var duplicate = Path.Combine(BaseDirectory, "Duplicate.steam");
        File.WriteAllText(path, "381780"); File.WriteAllText(duplicate, "381780");
        var game = NewGame("steam", path, "First") with
        { ExternalSourceId = "gamenative-steam", ExternalSourceEntryId = "381780" };
        Assert.Equal(1, _library.AddGames([game, game with { Path = duplicate }]));
        var first = Assert.Single(_library.GetGames());
        Assert.Throws<ExternalLibrarySourceConflictException>(() => _library.AddGames([
            game with { Path = Path.Combine(BaseDirectory, "Other.steam"), ExternalSourceEntryId = "123" },
            game with { ExternalSourceEntryId = "456" }]));
        Assert.Equal(first, Assert.Single(_library.GetGames()));
    }

    [Fact]
    public void SteamRelocationOntoLegacyRowReportsConflictAndPreservesBothHistories()
    {
        var old = NewGame("steam", Path.Combine(BaseDirectory, "Missing.steam"), "Tracked") with
        { ExternalSourceId = "gamenative-steam", ExternalSourceEntryId = "381780" };
        var legacy = NewGame("steam", Path.Combine(BaseDirectory, "Legacy.steam"), "Legacy");
        _library.AddGames([old, legacy]);
        var before = _library.GetGames().ToArray();
        Assert.Throws<ExternalLibrarySourceConflictException>(() => _library.AddGames([old with { Path = legacy.Path }]));
        Assert.Equal(before, _library.GetGames());
    }

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
    public void ReconcileImport_ReplacesEmbeddedTitleWithFilenameButPreservesCatalogAndUserTitles()
    {
        var embeddedPath = "/games/gba/translated-game.gba";
        _library.AddGames([
            NewGame("gba", embeddedPath, "INTERNAL ID") with { TitleOrigin = GameTitleOrigin.Embedded },
            NewGame("gba", "/games/gba/catalog-game.gba", "Catalog title") with { TitleOrigin = GameTitleOrigin.Catalog },
            NewGame("gba", "/games/gba/user-game.gba", "User title") with { TitleOrigin = GameTitleOrigin.User },
        ]);

        _library.ReconcileImport("gba",
        [
            NewGame("gba", embeddedPath, "Translated Game") with { TitleOrigin = GameTitleOrigin.Filename },
            NewGame("gba", "/games/gba/catalog-game.gba", "Catalog fallback") with { TitleOrigin = GameTitleOrigin.Filename },
            NewGame("gba", "/games/gba/user-game.gba", "User fallback") with { TitleOrigin = GameTitleOrigin.Filename },
        ], []);

        var games = _library.GetGames("gba");
        var translated = games.Single(game => game.Path == embeddedPath);
        Assert.Equal("Translated Game", translated.Title);
        Assert.Equal(GameTitleOrigin.Filename, translated.TitleOrigin);
        Assert.Equal("Catalog title", games.Single(game => game.Path.EndsWith("catalog-game.gba")).Title);
        Assert.Equal("User title", games.Single(game => game.Path.EndsWith("user-game.gba")).Title);
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
    public void GetPopulatedSystemIds_ReturnsDistinctSystemsIncludingUnavailableGames()
    {
        _library.AddGames([
            NewGame("playstation", "/g/a.cue", "A"),
            NewGame("playstation", "/g/b.chd", "B"),
            NewGame("gamecube", "/g/missing.iso", "Missing") with { IsAvailable = false },
        ]);

        var populated = _library.GetPopulatedSystemIds();

        Assert.Equal(2, populated.Count);
        Assert.Contains("playstation", populated);
        Assert.Contains("gamecube", populated);
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
    public void LastPlayed_DefaultsToNull_AndRecordLaunchStartedRoundTrips()
    {
        _library.AddGames([NewGame("playstation", "/g/a.cue", "A")]);
        var game = _library.GetGames("playstation").Single();
        Assert.Null(game.LastPlayedAt);
        Assert.Equal(0, game.PlayCount);

        var playedAt = DateTimeOffset.FromUnixTimeMilliseconds(
            DateTimeOffset.Parse("2026-08-04T09:15:00+00:00").ToUnixTimeMilliseconds());
        _library.RecordLaunchStarted(game.Id, playedAt);

        var reloaded = _library.GetGames("playstation").Single();
        Assert.Equal(playedAt, reloaded.LastPlayedAt);
        Assert.Equal(1, reloaded.PlayCount);
    }

    [Fact]
    public void RecordLaunchStarted_OverwritesTimestamp_AndCountsEveryLaunch()
    {
        _library.AddGames([NewGame("playstation", "/g/a.cue", "A")]);
        var game = _library.GetGames("playstation").Single();
        var first = DateTimeOffset.Parse("2026-08-01T00:00:00+00:00");
        var later = DateTimeOffset.Parse("2026-08-04T00:00:00+00:00");

        _library.RecordLaunchStarted(game.Id, first);
        _library.RecordLaunchStarted(game.Id, later);

        var reloaded = _library.GetGames("playstation").Single();
        Assert.Equal(
            later.ToUnixTimeMilliseconds(),
            reloaded.LastPlayedAt!.Value.ToUnixTimeMilliseconds());
        Assert.Equal(2, reloaded.PlayCount);
    }

    [Fact]
    public void Playtime_DefaultsToZero_AndCompletedSessionsAccumulate()
    {
        _library.AddGames([NewGame("playstation", "/g/a.cue", "A")]);
        var game = _library.GetGames("playstation").Single();
        Assert.Equal(TimeSpan.Zero, game.Playtime);

        Play(game.Id, TimeSpan.FromMinutes(20));
        Play(game.Id, TimeSpan.FromMinutes(25));

        Assert.Equal(TimeSpan.FromMinutes(45), _library.GetGames("playstation").Single().Playtime);
    }

    [Fact]
    public void CompletePlaySession_ClosesZeroAndNegativeSpansAtZero_WithoutAddingTime()
    {
        _library.AddGames([NewGame("playstation", "/g/a.cue", "A")]);
        var game = _library.GetGames("playstation").Single();

        Play(game.Id, TimeSpan.Zero);
        Play(game.Id, TimeSpan.FromSeconds(-30));

        Assert.Equal(TimeSpan.Zero, _library.GetGames("playstation").Single().Playtime);
        Assert.All(_library.GetPlaySessions(game.Id), session => Assert.Equal(TimeSpan.Zero, session.Duration));
    }

    [Fact]
    public void RecordLaunchStarted_OpensASession_WithTheGameSnapshotAndDevice()
    {
        _library.AddGames([NewGame("playstation", "/g/a.cue", "Alpha")]);
        var game = _library.GetGames("playstation").Single();
        var startedAt = DateTimeOffset.FromUnixTimeMilliseconds(1786000000000);

        var sessionId = _library.RecordLaunchStarted(game.Id, startedAt);

        var session = Assert.Single(_library.GetPlaySessions(game.Id));
        Assert.Equal(sessionId, session.Id);
        Assert.Equal(game.Id, session.GameId);
        Assert.Equal("Alpha", session.GameTitle);
        Assert.Equal("playstation", session.SystemId);
        Assert.Equal(startedAt, session.StartedAt);
        Assert.Null(session.Duration);
        Assert.Null(session.Measurement);
        Assert.Equal("test-device", session.DeviceId);
        Assert.Equal(sessionId, _library.FindOpenPlaySession(game.Id));
    }

    [Fact]
    public void RecordLaunchStarted_ForAGameNotInTheLibrary_ReturnsNull_AndWritesNothing()
    {
        Assert.Null(_library.RecordLaunchStarted(424242, DateTimeOffset.UtcNow));
        Assert.Empty(_library.GetPlaySessions(424242));
    }

    [Fact]
    public void CompletePlaySession_ClosesOnce_SoARetriedCompletionNeverCountsTwice()
    {
        _library.AddGames([NewGame("playstation", "/g/a.cue", "A")]);
        var game = _library.GetGames("playstation").Single();
        var sessionId = _library.RecordLaunchStarted(game.Id, DateTimeOffset.UtcNow)!.Value;

        Assert.True(_library.CompletePlaySession(
            sessionId, TimeSpan.FromMinutes(30), PlaySessionMeasurement.WallClockEstimate));
        Assert.False(_library.CompletePlaySession(
            sessionId, TimeSpan.FromMinutes(30), PlaySessionMeasurement.WallClockEstimate));
        Assert.False(_library.CompletePlaySession(999999, TimeSpan.FromMinutes(5), PlaySessionMeasurement.TrackedProcess));

        Assert.Equal(TimeSpan.FromMinutes(30), _library.GetGames("playstation").Single().Playtime);
        var session = Assert.Single(_library.GetPlaySessions(game.Id));
        Assert.Equal(TimeSpan.FromMinutes(30), session.Duration);
        Assert.Equal(PlaySessionMeasurement.WallClockEstimate, session.Measurement);
        Assert.Null(_library.FindOpenPlaySession(game.Id));
    }

    [Fact]
    public void AnUnclosedSession_AfterAKill_CountsAsAPlayOfUnknownLength()
    {
        _library.AddGames([NewGame("playstation", "/g/a.cue", "A")]);
        var game = _library.GetGames("playstation").Single();
        Play(game.Id, TimeSpan.FromMinutes(40), DateTimeOffset.UtcNow.AddDays(-1));
        // EmuShelf was killed mid-session: the row was opened at launch and never closed.
        var killed = _library.RecordLaunchStarted(game.Id, DateTimeOffset.UtcNow)!.Value;

        var reloaded = _library.GetGames("playstation").Single();
        Assert.Equal(2, reloaded.PlayCount);
        Assert.Equal(TimeSpan.FromMinutes(40), reloaded.Playtime);
        var sessions = _library.GetPlaySessions(game.Id);
        Assert.Equal(killed, sessions[0].Id); // newest first
        Assert.Null(sessions[0].Duration);
        Assert.Equal(killed, _library.FindOpenPlaySession(game.Id));
    }

    [Fact]
    public void Totals_MatchTheSumOfSessions_PlusEarlierPlay()
    {
        _library.AddGames([NewGame("playstation", "/g/a.cue", "A")]);
        var game = _library.GetGames("playstation").Single();
        SeedEarlierPlay(game.Id, TimeSpan.FromHours(2), plays: 3);
        Play(game.Id, TimeSpan.FromMinutes(15));
        Play(game.Id, TimeSpan.FromMinutes(45));
        _library.RecordLaunchStarted(game.Id, DateTimeOffset.UtcNow);

        var reloaded = _library.GetGames("playstation").Single();
        var sessions = _library.GetPlaySessions(game.Id);
        var history = GamePlayHistory.From(reloaded, sessions);

        Assert.Equal(TimeSpan.FromHours(2), history.EarlierPlaytime);
        Assert.Equal(3, history.EarlierPlayCount);
        Assert.Null(history.FirstPlayedAt); // the true first play predates the log
        Assert.Equal(
            reloaded.Playtime,
            history.EarlierPlaytime + TimeSpan.FromSeconds(sessions.Sum(s => s.Duration?.TotalSeconds ?? 0)));
        Assert.Equal(reloaded.PlayCount, history.EarlierPlayCount + sessions.Count);
    }

    [Fact]
    public void FirstPlayed_IsTheEarliestSession_WhenEveryPlayIsLogged()
    {
        _library.AddGames([NewGame("playstation", "/g/a.cue", "A")]);
        var game = _library.GetGames("playstation").Single();
        var first = DateTimeOffset.FromUnixTimeMilliseconds(1786000000000);
        Play(game.Id, TimeSpan.FromMinutes(10), first);
        Play(game.Id, TimeSpan.FromMinutes(10), first.AddDays(2));

        var history = GamePlayHistory.From(_library.GetGames("playstation").Single(), _library.GetPlaySessions(game.Id));

        Assert.Equal(first, history.FirstPlayedAt);
        Assert.Equal(TimeSpan.Zero, history.EarlierPlaytime);
        Assert.Equal(0, history.EarlierPlayCount);
    }

    [Fact]
    public void RemovingAGame_KeepsItsSessions_AndReAddingTheSameFileRelinksThem()
    {
        _library.AddGames([NewGame("playstation", "/g/a.cue", "Alpha")]);
        var game = _library.GetGames("playstation").Single();
        var lastStart = DateTimeOffset.FromUnixTimeMilliseconds(1786000000000);
        Play(game.Id, TimeSpan.FromMinutes(20), lastStart.AddDays(-1));
        Play(game.Id, TimeSpan.FromMinutes(30), lastStart);

        _library.RemoveGame(game.Id);
        Assert.Empty(_library.GetGames());
        Assert.Equal(2L, CountSessions());

        _library.AddGames([NewGame("playstation", "/G/A.CUE", "Alpha again")]);
        var readded = _library.GetGames("playstation").Single();

        Assert.NotEqual(game.Id, readded.Id);
        Assert.Equal(2, readded.PlayCount);
        Assert.Equal(TimeSpan.FromMinutes(50), readded.Playtime);
        Assert.Equal(lastStart, readded.LastPlayedAt);
        var sessions = _library.GetPlaySessions(readded.Id);
        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, session => Assert.Equal(readded.Id, session.GameId));
        Assert.Empty(_library.GetPlaySessions(game.Id));
    }

    [Fact]
    public void ASessionClosedAfterItsGameWasRemoved_JoinsTheTotalsOnlyWhenReAdded()
    {
        _library.AddGames([NewGame("playstation", "/g/a.cue", "A"), NewGame("playstation", "/g/b.cue", "B")]);
        var game = _library.GetGames("playstation").Single(g => g.Title == "A");
        var other = _library.GetGames("playstation").Single(g => g.Title == "B");
        var sessionId = _library.RecordLaunchStarted(game.Id, DateTimeOffset.UtcNow)!.Value;

        _library.RemoveGame(game.Id);
        Assert.True(_library.CompletePlaySession(sessionId, TimeSpan.FromMinutes(12), PlaySessionMeasurement.TrackedProcess));
        Assert.Equal(TimeSpan.Zero, _library.GetGames("playstation").Single().Playtime);

        _library.AddGames([NewGame("playstation", "/g/a.cue", "A")]);
        var readded = _library.GetGames("playstation").Single(g => g.Title == "A");
        Assert.Equal(TimeSpan.FromMinutes(12), readded.Playtime);
        Assert.Equal(1, readded.PlayCount);
        Assert.Equal(TimeSpan.Zero, _library.GetGames("playstation").Single(g => g.Id == other.Id).Playtime);
    }

    [Fact]
    public void AGameAbsorbedByAnImport_KeepsItsSessionsUnderItsLastTitle()
    {
        _library.AddGames([NewGame("playstation", "/g/disc1.cue", "Disc 1")]);
        var disc = _library.GetGames("playstation").Single();
        Play(disc.Id, TimeSpan.FromMinutes(5));
        _library.UpdateTitle(disc.Id, "Renamed disc");

        // A playlist now owns the disc: the import suppresses its row.
        _library.ReconcileImport("playstation", [NewGame("playstation", "/g/game.m3u", "Game")], ["/g/disc1.cue"]);

        Assert.Equal("Game", _library.GetGames("playstation").Single().Title);
        Assert.Equal(1L, CountSessions());
        _library.AddGames([NewGame("playstation", "/g/disc1.cue", "Disc 1")]);
        var back = _library.GetGames("playstation").Single(g => g.Path.EndsWith("disc1.cue", StringComparison.Ordinal));
        var session = Assert.Single(_library.GetPlaySessions(back.Id));
        Assert.Equal("Renamed disc", session.GameTitle);
        Assert.Equal(TimeSpan.FromMinutes(5), back.Playtime);
    }

    [Fact]
    public void RemovingAGame_ThatIsAlreadyGone_ChangesNoSession()
    {
        _library.AddGames([NewGame("playstation", "/g/a.cue", "A")]);
        var game = _library.GetGames("playstation").Single();
        Play(game.Id, TimeSpan.FromMinutes(5));
        _library.RemoveGame(game.Id);

        _library.RemoveGame(game.Id);
        _library.RemoveGame(987654);

        Assert.Equal(1L, CountSessions());
    }

    private void SeedEarlierPlay(long gameId, TimeSpan playtime, int plays)
    {
        // Play recorded before schema v21: it lives only in the totals.
        using var connection = new LibraryDatabase(AppPaths).CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Games SET PlaytimeSeconds = $seconds, PlayCount = $plays WHERE Id = $id;";
        command.Parameters.AddWithValue("$seconds", (long)playtime.TotalSeconds);
        command.Parameters.AddWithValue("$plays", plays);
        command.Parameters.AddWithValue("$id", gameId);
        command.ExecuteNonQuery();
    }

    private long CountSessions()
    {
        using var connection = new LibraryDatabase(AppPaths).CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM PlaySessions;";
        return (long)command.ExecuteScalar()!;
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
    public void DiscSelections_PersistAndAreRemovedWithTheirSelectedGame()
    {
        _library.AddGames([
            NewGame("playstation", "/g/Chrono Cross (Disc 1).cue", "Chrono Cross (Disc 1)"),
            NewGame("playstation", "/g/Chrono Cross (Disc 2).cue", "Chrono Cross (Disc 2)"),
        ]);
        var disc2 = _library.GetGames("playstation").Single(game => game.Title.Contains("Disc 2"));

        _library.SetDiscSelection("playstation\u001FCHRONO CROSS", disc2.Id);

        Assert.Equal(disc2.Id, _library.GetDiscSelections()["playstation\u001FCHRONO CROSS"]);

        _library.RemoveGame(disc2.Id);

        Assert.Empty(_library.GetDiscSelections());
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
    public void AddGames_WithExistingCover_KeepsItReplaceable_NotUserOwned()
    {
        // Art discovered during scanning/import keeps origin None so a later scrape may replace it.
        // Only a cover the user hand-picks (UpdateCoverPath) is stamped User and protected.
        var coverPath = Path.Combine(AppPaths.CoversDirectory, "existing.png");

        _library.AddGames([
            NewGame("playstation", "/g/a.cue", "A") with { CoverPath = coverPath },
        ]);

        Assert.Equal(GameCoverOrigin.None, _library.GetGames().Single().CoverOrigin);
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
    public void ReplaceLibraryFolder_RebasesRecognizedRelativePathsAndPreservesGameIdentity()
    {
        var oldRoot = Path.Combine(BaseDirectory, "Old", "PS1");
        var newRoot = Path.Combine(BaseDirectory, "New", "PS1");
        var oldGame = Path.Combine(oldRoot, "RPGs", "Alpha.cue");
        var oldUnmatched = Path.Combine(oldRoot, "Beta.cue");
        var newGame = Path.Combine(newRoot, "RPGs", "Alpha.cue");
        var cover = Path.Combine(AppPaths.CoversDirectory, "alpha.png");
        _library.AddLibraryFolder("playstation", oldRoot);
        _library.AddGames([
            NewGame("playstation", oldGame, "Custom Alpha") with { CoverPath = cover },
            NewGame("playstation", oldUnmatched, "Beta"),
        ]);
        var original = _library.GetGames("playstation").Single(game => game.Title == "Custom Alpha");
        var folder = _library.GetLibraryFolders("playstation").Single();

        var result = _library.ReplaceLibraryFolder(
            folder.Id,
            "playstation",
            newRoot,
            new Dictionary<long, string> { [original.Id] = newGame });

        var updated = _library.GetGames("playstation").Single(game => game.Title == "Custom Alpha");
        Assert.Equal(1, result.RebasedGameCount);
        Assert.Equal(original.Id, updated.Id);
        Assert.Equal(newGame, updated.Path);
        Assert.Equal(cover, updated.CoverPath);
        Assert.Contains(_library.GetGames("playstation"), game => game.Path == oldUnmatched);
        Assert.Equal(newRoot, _library.GetLibraryFolders("playstation").Single().Path);
    }

    [Fact]
    public void ReplaceLibraryFolder_PathConflictLeavesFolderAndGamesUnchanged()
    {
        var oldRoot = Path.Combine(BaseDirectory, "Old");
        var newRoot = Path.Combine(BaseDirectory, "New");
        var oldGame = Path.Combine(oldRoot, "Alpha.cue");
        var target = Path.Combine(newRoot, "Alpha.cue");
        _library.AddLibraryFolder("playstation", oldRoot);
        _library.AddGames([
            NewGame("playstation", oldGame, "Old Alpha"),
            NewGame("playstation", target, "Existing Alpha"),
        ]);
        var folder = _library.GetLibraryFolders("playstation").Single();

        var error = Assert.Throws<InvalidOperationException>(() => _library.ReplaceLibraryFolder(
            folder.Id,
            "playstation",
            newRoot,
            new Dictionary<long, string>
            {
                [_library.GetGames("playstation").Single(game => game.Path == oldGame).Id] = target,
            }));

        Assert.Contains("already owned", error.Message);
        Assert.Equal(oldRoot, _library.GetLibraryFolders("playstation").Single().Path);
        Assert.Contains(_library.GetGames("playstation"), game => game.Path == oldGame);
    }

    [Fact]
    public void ReplaceLibraryFolder_DoesNotRebaseSameRelativePathWithoutVerifiedIdentity()
    {
        var oldRoot = Path.Combine(BaseDirectory, "Old");
        var newRoot = Path.Combine(BaseDirectory, "New");
        var oldGame = Path.Combine(oldRoot, "Alpha.cue");
        _library.AddLibraryFolder("playstation", oldRoot);
        _library.AddGames([NewGame("playstation", oldGame, "Curated Alpha")]);
        var folder = _library.GetLibraryFolders("playstation").Single();

        var result = _library.ReplaceLibraryFolder(
            folder.Id,
            "playstation",
            newRoot,
            new Dictionary<long, string>());

        Assert.Equal(0, result.RebasedGameCount);
        Assert.Equal(oldGame, _library.GetGames("playstation").Single().Path);
        Assert.Equal(newRoot, _library.GetLibraryFolders("playstation").Single().Path);
    }

    [Fact]
    public void RemoveLibraryFolder_ForgetsOnlyTheRoot()
    {
        var root = Path.Combine(BaseDirectory, "Roms");
        var gamePath = Path.Combine(root, "Alpha.cue");
        _library.AddLibraryFolder("playstation", root);
        _library.AddGames([NewGame("playstation", gamePath, "Alpha")]);
        var folder = _library.GetLibraryFolders("playstation").Single();

        _library.RemoveLibraryFolder(folder.Id, "playstation");

        Assert.Empty(_library.GetLibraryFolders("playstation"));
        Assert.Equal(gamePath, _library.GetGames("playstation").Single().Path);
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
