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
            SELECT Id, SystemId, Path, Title, CoverPath, IsAvailable, DateAdded
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

    public int AddGames(IEnumerable<Game> games)
    {
        using var connection = _database.CreateConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT OR IGNORE INTO Games (SystemId, Path, Title, CoverPath, IsAvailable, DateAdded)
            VALUES ($systemId, $path, $title, $coverPath, $isAvailable, $dateAdded);
            """;
        var systemId = command.Parameters.Add("$systemId", SqliteType.Text);
        var path = command.Parameters.Add("$path", SqliteType.Text);
        var title = command.Parameters.Add("$title", SqliteType.Text);
        var coverPath = command.Parameters.Add("$coverPath", SqliteType.Text);
        var isAvailable = command.Parameters.Add("$isAvailable", SqliteType.Integer);
        var dateAdded = command.Parameters.Add("$dateAdded", SqliteType.Text);

        var added = 0;
        foreach (var game in games)
        {
            systemId.Value = game.SystemId;
            path.Value = _pathResolver.ToStorablePath(game.Path);
            title.Value = game.Title;
            coverPath.Value = (object?)game.CoverPath ?? DBNull.Value;
            isAvailable.Value = game.IsAvailable ? 1 : 0;
            dateAdded.Value = game.DateAdded.ToString("O");
            added += command.ExecuteNonQuery();
        }

        transaction.Commit();
        return added;
    }

    public void SetAvailability(long gameId, bool isAvailable)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Games SET IsAvailable = $isAvailable WHERE Id = $id;";
        command.Parameters.AddWithValue("$isAvailable", isAvailable ? 1 : 0);
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
        CoverPath = reader.IsDBNull(4) ? null : reader.GetString(4),
        IsAvailable = reader.GetInt64(5) != 0,
        // Written with the invariant round-trip ("O") format; parse the same way so a
        // non-Gregorian current culture can't shift the year or throw.
        DateAdded = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
    };
}
