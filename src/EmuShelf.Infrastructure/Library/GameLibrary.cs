using System.Globalization;
using EmuShelf.Core.Library;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace EmuShelf.Infrastructure.Library;

/// <summary>
/// SQLite-backed <see cref="IGameLibrary"/>. Stores portable (relative-when-possible)
/// paths via <see cref="IRelativePathResolver"/> and hands back absolute paths.
/// </summary>
public sealed class GameLibrary : IGameLibrary
{
    private readonly LibraryDatabase _database;
    private readonly IRelativePathResolver _pathResolver;

    public GameLibrary(LibraryDatabase database, IRelativePathResolver pathResolver)
    {
        _database = database;
        _pathResolver = pathResolver;
    }

    public IReadOnlyList<Game> GetGames(string? systemId = null)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, SystemId, Path, Title, TitleOrigin, CoverPath, CoverOrigin,
                   IsAvailable, DateAdded
            FROM Games
            WHERE ($systemId IS NULL OR SystemId = $systemId)
            ORDER BY Title COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$systemId", (object?)systemId ?? DBNull.Value);

        var games = new List<Game>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            games.Add(ReadGame(reader));
        return games;
    }

    public IReadOnlyList<Game> GetRecentlyAddedGames(int limit)
    {
        if (limit <= 0)
            return [];

        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, SystemId, Path, Title, TitleOrigin, CoverPath, CoverOrigin,
                   IsAvailable, DateAdded
            FROM Games
            ORDER BY DateAddedUnixMilliseconds DESC, Id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var games = new List<Game>(limit);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            games.Add(ReadGame(reader));
        return games;
    }

    public int AddGames(IEnumerable<Game> games) =>
        WriteGames(games, systemId: null, suppressedPaths: []).AddedCount;

    public GameImportResult ReconcileImport(
        string systemId,
        IEnumerable<Game> entries,
        IReadOnlyList<string> suppressedPaths) =>
        WriteGames(entries, systemId, suppressedPaths);

    private GameImportResult WriteGames(
        IEnumerable<Game> games,
        string? systemId,
        IReadOnlyList<string> suppressedPaths)
    {
        using var connection = _database.CreateConnection();
        using var transaction = connection.BeginTransaction();

        if (systemId is not null && suppressedPaths.Count > 0)
        {
            using var deleteCommand = connection.CreateCommand();
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText =
                "DELETE FROM Games WHERE SystemId = $systemId AND Path = $path;";
            deleteCommand.Parameters.AddWithValue("$systemId", systemId);
            var suppressedPath = deleteCommand.Parameters.Add("$path", SqliteType.Text);

            foreach (var pathToSuppress in suppressedPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                suppressedPath.Value = _pathResolver.ToStorablePath(pathToSuppress);
                deleteCommand.ExecuteNonQuery();
            }
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT OR IGNORE INTO Games (
                SystemId, Path, Title, TitleOrigin, CoverPath, CoverOrigin,
                IsAvailable, DateAdded, DateAddedUnixMilliseconds)
            VALUES (
                $systemId, $path, $title, $titleOrigin, $coverPath, $coverOrigin,
                $isAvailable, $dateAdded, $dateAddedUnixMilliseconds);
            """;
        var systemIdParameter = command.Parameters.Add("$systemId", SqliteType.Text);
        var path = command.Parameters.Add("$path", SqliteType.Text);
        var title = command.Parameters.Add("$title", SqliteType.Text);
        var titleOrigin = command.Parameters.Add("$titleOrigin", SqliteType.Integer);
        var coverPath = command.Parameters.Add("$coverPath", SqliteType.Text);
        var coverOrigin = command.Parameters.Add("$coverOrigin", SqliteType.Integer);
        var isAvailable = command.Parameters.Add("$isAvailable", SqliteType.Integer);
        var dateAdded = command.Parameters.Add("$dateAdded", SqliteType.Text);
        var dateAddedUnixMilliseconds = command.Parameters.Add(
            "$dateAddedUnixMilliseconds",
            SqliteType.Integer);

        using var insertedIdCommand = connection.CreateCommand();
        insertedIdCommand.Transaction = transaction;
        insertedIdCommand.CommandText = "SELECT last_insert_rowid();";

        var addedIds = new List<long>();
        foreach (var game in games)
        {
            systemIdParameter.Value = game.SystemId;
            path.Value = _pathResolver.ToStorablePath(game.Path);
            title.Value = game.Title;
            titleOrigin.Value = (int)game.TitleOrigin;
            coverPath.Value = game.CoverPath is null
                ? DBNull.Value
                : _pathResolver.ToStorablePath(game.CoverPath);
            coverOrigin.Value = (int)(game.CoverPath is not null &&
                game.CoverOrigin == GameCoverOrigin.None
                    ? GameCoverOrigin.User
                    : game.CoverOrigin);
            isAvailable.Value = game.IsAvailable ? 1 : 0;
            dateAdded.Value = game.DateAdded.ToString("O", CultureInfo.InvariantCulture);
            dateAddedUnixMilliseconds.Value = game.DateAdded.ToUnixTimeMilliseconds();
            if (command.ExecuteNonQuery() > 0)
                addedIds.Add((long)insertedIdCommand.ExecuteScalar()!);
        }

        transaction.Commit();
        return new GameImportResult(addedIds);
    }

    public void SetAvailability(long gameId, bool isAvailable) =>
        SetAvailabilities([new GameAvailabilityUpdate(gameId, isAvailable)]);

    public void SetAvailabilities(IReadOnlyList<GameAvailabilityUpdate> updates)
    {
        if (updates.Count == 0)
            return;

        using var connection = _database.CreateConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE Games SET IsAvailable = $isAvailable WHERE Id = $id;";
        var availability = command.Parameters.Add("$isAvailable", SqliteType.Integer);
        var id = command.Parameters.Add("$id", SqliteType.Integer);
        foreach (var update in updates)
        {
            availability.Value = update.IsAvailable ? 1 : 0;
            id.Value = update.GameId;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void UpdateTitle(long gameId, string title)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE Games SET Title = $title, TitleOrigin = $origin WHERE Id = $id;";
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$origin", (int)GameTitleOrigin.User);
        command.Parameters.AddWithValue("$id", gameId);
        command.ExecuteNonQuery();
    }

    public void UpdateCoverPath(long gameId, string? coverPath)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE Games SET CoverPath = $coverPath, CoverOrigin = $origin WHERE Id = $id;";
        command.Parameters.AddWithValue(
            "$coverPath",
            coverPath is null ? DBNull.Value : _pathResolver.ToStorablePath(coverPath));
        command.Parameters.AddWithValue(
            "$origin",
            coverPath is null ? (int)GameCoverOrigin.None : (int)GameCoverOrigin.User);
        command.Parameters.AddWithValue("$id", gameId);
        command.ExecuteNonQuery();
    }

    public void RemoveGame(long gameId)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Games WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", gameId);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<LibraryFolder> GetLibraryFolders(string? systemId = null)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, SystemId, Path
            FROM LibraryFolders
            WHERE ($systemId IS NULL OR SystemId = $systemId)
            ORDER BY Id;
            """;
        command.Parameters.AddWithValue("$systemId", (object?)systemId ?? DBNull.Value);

        var folders = new List<LibraryFolder>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            folders.Add(new LibraryFolder
            {
                Id = reader.GetInt64(0),
                SystemId = reader.GetString(1),
                Path = _pathResolver.ToAbsolutePath(reader.GetString(2)),
            });
        }
        return folders;
    }

    public void AddLibraryFolder(string systemId, string folderPath)
    {
        var storable = _pathResolver.ToStorablePath(folderPath);

        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        // Skip if this system already tracks this exact folder.
        command.CommandText =
            """
            INSERT INTO LibraryFolders (SystemId, Path)
            SELECT $systemId, $path
            WHERE NOT EXISTS (
                SELECT 1 FROM LibraryFolders WHERE SystemId = $systemId AND Path = $path
            );
            """;
        command.Parameters.AddWithValue("$systemId", systemId);
        command.Parameters.AddWithValue("$path", storable);
        command.ExecuteNonQuery();
    }

    private Game ReadGame(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        SystemId = reader.GetString(1),
        Path = _pathResolver.ToAbsolutePath(reader.GetString(2)),
        Title = reader.GetString(3),
        TitleOrigin = (GameTitleOrigin)reader.GetInt32(4),
        CoverPath = reader.IsDBNull(5) ? null : _pathResolver.ToAbsolutePath(reader.GetString(5)),
        CoverOrigin = (GameCoverOrigin)reader.GetInt32(6),
        IsAvailable = reader.GetInt64(7) != 0,
        // Written with the invariant round-trip ("O") format; parse the same way so a
        // non-Gregorian current culture can't shift the year or throw.
        DateAdded = DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
    };
}
