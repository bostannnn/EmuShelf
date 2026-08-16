using EmuShelf.Core.Metadata;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace EmuShelf.Infrastructure.Metadata;

public sealed class SqliteGameDetailsStore : IGameDetailsStore
{
    private readonly LibraryDatabase _database;
    private readonly IRelativePathResolver _pathResolver;

    public SqliteGameDetailsStore(LibraryDatabase database, IRelativePathResolver pathResolver)
    {
        _database = database;
        _pathResolver = pathResolver;
    }

    public GameDetails GetDetails(long gameId)
    {
        using var connection = _database.CreateConnection();
        return new GameDetails(
            gameId,
            ReadMetadata(connection, gameId),
            ReadMedia(connection, gameId),
            ReadProviderMatches(connection, gameId));
    }

    public IReadOnlyDictionary<long, GameDetailsProjection> GetAllDetailsProjections()
    {
        using var connection = _database.CreateConnection();
        var accumulators = new Dictionary<long, ProjectionAccumulator>();

        ProjectionAccumulator For(long gameId)
        {
            if (!accumulators.TryGetValue(gameId, out var accumulator))
            {
                accumulator = new ProjectionAccumulator();
                accumulators[gameId] = accumulator;
            }

            return accumulator;
        }

        // Media presence: a game has a kind if any asset row of that kind exists for it.
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT GameId, Kind FROM GameMediaAssets;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var accumulator = For(reader.GetInt64(0));
                switch ((GameMediaKind)reader.GetInt32(1))
                {
                    case GameMediaKind.BoxFront:
                        accumulator.HasBoxFront = true;
                        break;
                    case GameMediaKind.Screenshot:
                        accumulator.HasScreenshot = true;
                        break;
                    case GameMediaKind.Wheel:
                        accumulator.HasWheel = true;
                        break;
                    case GameMediaKind.Fanart:
                        accumulator.HasFanart = true;
                        break;
                    case GameMediaKind.TitleScreen:
                        accumulator.HasTitleScreen = true;
                        break;
                    case GameMediaKind.BoxBack:
                        accumulator.HasBoxBack = true;
                        break;
                    case GameMediaKind.BoxSpine:
                        accumulator.HasBoxSpine = true;
                        break;
                    case GameMediaKind.PhysicalMedia:
                        accumulator.HasPhysicalMedia = true;
                        break;
                    case GameMediaKind.PhysicalMediaTexture:
                        accumulator.HasPhysicalMediaTexture = true;
                        break;
                }
            }
        }

        // Scalar metadata: the first value per field per game, using the same deterministic order as
        // GetDetails (Field, Locale) so a column shows what GetDetails' FirstOrDefault would pick.
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT GameId, Field, Value
                FROM GameMetadataValues
                ORDER BY GameId, Field, Locale;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var accumulator = For(reader.GetInt64(0));
                var value = reader.GetString(2);
                switch ((GameMetadataField)reader.GetInt32(1))
                {
                    case GameMetadataField.Description:
                        accumulator.HasDescription = true;
                        break;
                    case GameMetadataField.Rating:
                        accumulator.Rating ??= value;
                        break;
                    case GameMetadataField.Genre:
                        accumulator.Genre ??= value;
                        break;
                    case GameMetadataField.ReleaseDate:
                        accumulator.ReleaseDate ??= value;
                        break;
                    case GameMetadataField.Players:
                        accumulator.Players ??= value;
                        break;
                    case GameMetadataField.Developer:
                        accumulator.Developer ??= value;
                        break;
                    case GameMetadataField.Publisher:
                        accumulator.Publisher ??= value;
                        break;
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT DISTINCT GameId FROM GameProviderMatches;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                For(reader.GetInt64(0)).HasProviderMatch = true;
        }

        return accumulators.ToDictionary(pair => pair.Key, pair => pair.Value.ToProjection());
    }

    public IReadOnlyDictionary<long, string> GetSelectedMediaPaths(GameMediaKind kind)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT GameId, LocalPath
            FROM GameMediaAssets
            WHERE Kind = $kind AND IsSelected = 1
            ORDER BY GameId, Id DESC;
            """;
        command.Parameters.AddWithValue("$kind", (int)kind);

        var paths = new Dictionary<long, string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            // The schema/store enforce one selected asset per game+kind. Keeping the newest row if
            // an externally edited database violates that invariant makes this projection stable.
            paths.TryAdd(reader.GetInt64(0), _pathResolver.ToAbsolutePath(reader.GetString(1)));
        }

        return paths;
    }

    public bool TryApplyMetadata(GameMetadataValue value, GameMetadataApplyMode mode)
    {
        ValidateMetadata(value, mode);
        var locale = NormalizeCode(value.Locale);
        using var connection = _database.CreateConnection();
        using var transaction = connection.BeginTransaction();

        GameMetadataValueOrigin? existingOrigin = null;
        string? existingProviderId = null;
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText =
                """
                SELECT Origin, ProviderId
                FROM GameMetadataValues
                WHERE GameId = $gameId AND Field = $field AND Locale = $locale;
                """;
            read.Parameters.AddWithValue("$gameId", value.GameId);
            read.Parameters.AddWithValue("$field", (int)value.Field);
            read.Parameters.AddWithValue("$locale", locale);
            using var reader = read.ExecuteReader();
            if (reader.Read())
            {
                existingOrigin = (GameMetadataValueOrigin)reader.GetInt32(0);
                existingProviderId = reader.IsDBNull(1) ? null : reader.GetString(1);
            }
        }

        var mayWrite = existingOrigin is null || mode switch
        {
            GameMetadataApplyMode.FillMissing => false,
            GameMetadataApplyMode.RefreshProviderOwned =>
                existingOrigin == GameMetadataValueOrigin.Provider &&
                string.Equals(existingProviderId, value.ProviderId, StringComparison.OrdinalIgnoreCase),
            GameMetadataApplyMode.UserEdit => true,
            _ => false,
        };
        if (!mayWrite)
            return false;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO GameMetadataValues (
                GameId, Field, Locale, Value, Origin, ProviderId, ProviderItemId,
                SourceUri, UpdatedUnixMilliseconds)
            VALUES (
                $gameId, $field, $locale, $value, $origin, $providerId, $providerItemId,
                $sourceUri, $updated)
            ON CONFLICT (GameId, Field, Locale) DO UPDATE SET
                Value = excluded.Value,
                Origin = excluded.Origin,
                ProviderId = excluded.ProviderId,
                ProviderItemId = excluded.ProviderItemId,
                SourceUri = excluded.SourceUri,
                UpdatedUnixMilliseconds = excluded.UpdatedUnixMilliseconds;
            """;
        command.Parameters.AddWithValue("$gameId", value.GameId);
        command.Parameters.AddWithValue("$field", (int)value.Field);
        command.Parameters.AddWithValue("$locale", locale);
        command.Parameters.AddWithValue("$value", value.Value.Trim());
        command.Parameters.AddWithValue("$origin", (int)value.Origin);
        command.Parameters.AddWithValue("$providerId", DbValue(value.ProviderId));
        command.Parameters.AddWithValue("$providerItemId", DbValue(value.ProviderItemId));
        command.Parameters.AddWithValue("$sourceUri", DbValue(value.SourceUri));
        command.Parameters.AddWithValue("$updated", value.UpdatedAt.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }

    public GameMediaAsset SaveMedia(GameMediaAsset media, bool overrideUserSelection = false)
    {
        ValidateMedia(media);
        var storedPath = _pathResolver.ToStorablePath(media.LocalPath);
        using var connection = _database.CreateConnection();
        using var transaction = connection.BeginTransaction();

        var existing = FindExistingMedia(connection, transaction, media, storedPath);
        if (media.Id > 0 && existing is null)
            throw new InvalidOperationException("The media asset does not belong to this game and media kind.");
        if (existing is not null &&
            media.Origin == GameMediaOrigin.Provider &&
            (existing.Value.Origin == GameMediaOrigin.User ||
             !string.Equals(existing.Value.ProviderId, media.ProviderId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Provider media cannot overwrite user-owned or another provider's media.");
        }

        var effectiveMedia = ApplySelectionProtection(connection, transaction, media, existing, overrideUserSelection);
        if (effectiveMedia.IsSelected)
            ClearSelection(connection, transaction, effectiveMedia.GameId, effectiveMedia.Kind);

        long id;
        if (existing is not null)
        {
            id = existing.Value.Id;
            using var update = CreateMediaWriteCommand(connection, transaction, effectiveMedia, storedPath,
                """
                UPDATE GameMediaAssets SET
                    LocalPath = $localPath,
                    IsSelected = $isSelected,
                    SelectionOrigin = $selectionOrigin,
                    Origin = $origin,
                    ProviderId = $providerId,
                    ProviderItemId = $providerItemId,
                    SourceUri = $sourceUri,
                    Region = $region,
                    Language = $language,
                    FileExtension = $fileExtension,
                    Width = $width,
                    Height = $height,
                    Crc32 = $crc32,
                    Md5 = $md5,
                    Sha1 = $sha1,
                    UpdatedUnixMilliseconds = $updated
                WHERE Id = $id;
                """);
            update.Parameters.AddWithValue("$id", id);
            update.ExecuteNonQuery();
        }
        else
        {
            using var insert = CreateMediaWriteCommand(connection, transaction, effectiveMedia, storedPath,
                """
                INSERT INTO GameMediaAssets (
                    GameId, Kind, LocalPath, IsSelected, SelectionOrigin, Origin, ProviderId, ProviderItemId,
                    SourceUri, Region, Language, FileExtension, Width, Height, Crc32, Md5, Sha1,
                    UpdatedUnixMilliseconds)
                VALUES (
                    $gameId, $kind, $localPath, $isSelected, $selectionOrigin, $origin, $providerId, $providerItemId,
                    $sourceUri, $region, $language, $fileExtension, $width, $height, $crc32, $md5,
                    $sha1, $updated)
                RETURNING Id;
                """);
            id = (long)insert.ExecuteScalar()!;
        }

        transaction.Commit();
        return effectiveMedia with { Id = id, LocalPath = _pathResolver.ToAbsolutePath(storedPath) };
    }

    public bool SelectMedia(long gameId, GameMediaKind kind, long mediaId)
    {
        using var connection = _database.CreateConnection();
        using var transaction = connection.BeginTransaction();
        using (var exists = connection.CreateCommand())
        {
            exists.Transaction = transaction;
            exists.CommandText =
                "SELECT COUNT(*) FROM GameMediaAssets WHERE Id = $id AND GameId = $gameId AND Kind = $kind;";
            exists.Parameters.AddWithValue("$id", mediaId);
            exists.Parameters.AddWithValue("$gameId", gameId);
            exists.Parameters.AddWithValue("$kind", (int)kind);
            if ((long)exists.ExecuteScalar()! == 0)
                return false;
        }

        ClearSelection(connection, transaction, gameId, kind);
        using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText =
            "UPDATE GameMediaAssets SET IsSelected = 1, SelectionOrigin = $selectionOrigin WHERE Id = $id;";
        select.Parameters.AddWithValue("$id", mediaId);
        select.Parameters.AddWithValue("$selectionOrigin", (int)GameMediaSelectionOrigin.User);
        select.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }

    public void UpsertProviderMatch(GameProviderMatch match)
    {
        if (match.GameId <= 0)
            throw new ArgumentOutOfRangeException(nameof(match), "Game ID must be positive.");
        if (string.IsNullOrWhiteSpace(match.ProviderId))
            throw new ArgumentException("Provider ID cannot be empty.", nameof(match));

        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO GameProviderMatches (
                GameId, ProviderId, ProviderSystemId, SystemMappingVersion, ProviderGameId, ProviderRomId,
                MatchMethod, EvidenceValue, Status, LastAttemptUnixMilliseconds, LastError, CoverageComplete)
            VALUES (
                $gameId, $providerId, $providerSystemId, $systemMappingVersion, $providerGameId, $providerRomId,
                $matchMethod, $evidenceValue, $status, $lastAttempt, $lastError, $coverageComplete)
            ON CONFLICT (GameId, ProviderId) DO UPDATE SET
                ProviderSystemId = excluded.ProviderSystemId,
                SystemMappingVersion = excluded.SystemMappingVersion,
                ProviderGameId = excluded.ProviderGameId,
                ProviderRomId = excluded.ProviderRomId,
                MatchMethod = excluded.MatchMethod,
                EvidenceValue = excluded.EvidenceValue,
                Status = excluded.Status,
                LastAttemptUnixMilliseconds = excluded.LastAttemptUnixMilliseconds,
                LastError = excluded.LastError,
                CoverageComplete = excluded.CoverageComplete;
            """;
        command.Parameters.AddWithValue("$gameId", match.GameId);
        command.Parameters.AddWithValue("$providerId", match.ProviderId.Trim());
        command.Parameters.AddWithValue("$providerSystemId", DbValue(match.ProviderSystemId));
        command.Parameters.AddWithValue(
            "$systemMappingVersion",
            match.SystemMappingVersion is null ? DBNull.Value : match.SystemMappingVersion.Value);
        command.Parameters.AddWithValue("$providerGameId", DbValue(match.ProviderGameId));
        command.Parameters.AddWithValue("$providerRomId", DbValue(match.ProviderRomId));
        command.Parameters.AddWithValue("$matchMethod", (int)match.MatchMethod);
        command.Parameters.AddWithValue("$evidenceValue", DbValue(match.EvidenceValue));
        command.Parameters.AddWithValue("$status", (int)match.Status);
        command.Parameters.AddWithValue("$lastAttempt", match.LastAttemptedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$lastError", DbValue(match.LastError));
        command.Parameters.AddWithValue("$coverageComplete", match.CoverageComplete ? 1 : 0);
        command.ExecuteNonQuery();
    }

    private IReadOnlyList<GameMetadataValue> ReadMetadata(SqliteConnection connection, long gameId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Field, Value, Locale, Origin, ProviderId, ProviderItemId, SourceUri,
                   UpdatedUnixMilliseconds
            FROM GameMetadataValues
            WHERE GameId = $gameId
            ORDER BY Field, Locale;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        var values = new List<GameMetadataValue>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            values.Add(new GameMetadataValue(
                gameId,
                (GameMetadataField)reader.GetInt32(0),
                reader.GetString(1),
                NullIfEmpty(reader.GetString(2)),
                (GameMetadataValueOrigin)reader.GetInt32(3),
                ReadNullableString(reader, 4),
                ReadNullableString(reader, 5),
                ReadNullableString(reader, 6),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7))));
        }
        return values;
    }

    private IReadOnlyList<GameMediaAsset> ReadMedia(SqliteConnection connection, long gameId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Kind, LocalPath, IsSelected, SelectionOrigin, Origin, ProviderId, ProviderItemId, SourceUri,
                   Region, Language, FileExtension, Width, Height, Crc32, Md5, Sha1,
                   UpdatedUnixMilliseconds
            FROM GameMediaAssets
            WHERE GameId = $gameId
            ORDER BY Kind, IsSelected DESC, Id;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        var media = new List<GameMediaAsset>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            media.Add(new GameMediaAsset(
                reader.GetInt64(0),
                gameId,
                (GameMediaKind)reader.GetInt32(1),
                _pathResolver.ToAbsolutePath(reader.GetString(2)),
                reader.GetInt64(3) != 0,
                reader.IsDBNull(4) ? null : (GameMediaSelectionOrigin)reader.GetInt32(4),
                (GameMediaOrigin)reader.GetInt32(5),
                ReadNullableString(reader, 6),
                ReadNullableString(reader, 7),
                ReadNullableString(reader, 8),
                ReadNullableString(reader, 9),
                ReadNullableString(reader, 10),
                reader.GetString(11),
                ReadNullableInt(reader, 12),
                ReadNullableInt(reader, 13),
                ReadNullableString(reader, 14),
                ReadNullableString(reader, 15),
                ReadNullableString(reader, 16),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(17))));
        }
        return media;
    }

    private static IReadOnlyList<GameProviderMatch> ReadProviderMatches(
        SqliteConnection connection,
        long gameId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ProviderId, ProviderSystemId, SystemMappingVersion, ProviderGameId, ProviderRomId, MatchMethod,
                   EvidenceValue, Status, LastAttemptUnixMilliseconds, LastError, CoverageComplete
            FROM GameProviderMatches
            WHERE GameId = $gameId
            ORDER BY ProviderId;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        var matches = new List<GameProviderMatch>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            matches.Add(new GameProviderMatch(
                gameId,
                reader.GetString(0),
                ReadNullableString(reader, 1),
                ReadNullableInt(reader, 2),
                ReadNullableString(reader, 3),
                ReadNullableString(reader, 4),
                (GameProviderMatchMethod)reader.GetInt32(5),
                ReadNullableString(reader, 6),
                (GameMetadataStatus)reader.GetInt32(7),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(8)),
                ReadNullableString(reader, 9),
                reader.GetInt64(10) != 0));
        }
        return matches;
    }

    private static SqliteCommand CreateMediaWriteCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        GameMediaAsset media,
        string storedPath,
        string commandText)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        command.Parameters.AddWithValue("$gameId", media.GameId);
        command.Parameters.AddWithValue("$kind", (int)media.Kind);
        command.Parameters.AddWithValue("$localPath", storedPath);
        command.Parameters.AddWithValue("$isSelected", media.IsSelected ? 1 : 0);
        command.Parameters.AddWithValue(
            "$selectionOrigin",
            media.SelectionOrigin is null ? DBNull.Value : (int)media.SelectionOrigin.Value);
        command.Parameters.AddWithValue("$origin", (int)media.Origin);
        command.Parameters.AddWithValue("$providerId", DbValue(media.ProviderId));
        command.Parameters.AddWithValue("$providerItemId", DbValue(media.ProviderItemId));
        command.Parameters.AddWithValue("$sourceUri", DbValue(media.SourceUri));
        command.Parameters.AddWithValue("$region", DbValue(NormalizeCode(media.Region)));
        command.Parameters.AddWithValue("$language", DbValue(NormalizeCode(media.Language)));
        command.Parameters.AddWithValue("$fileExtension", media.FileExtension.Trim());
        command.Parameters.AddWithValue("$width", media.Width is null ? DBNull.Value : media.Width.Value);
        command.Parameters.AddWithValue("$height", media.Height is null ? DBNull.Value : media.Height.Value);
        command.Parameters.AddWithValue("$crc32", DbValue(media.Crc32));
        command.Parameters.AddWithValue("$md5", DbValue(media.Md5));
        command.Parameters.AddWithValue("$sha1", DbValue(media.Sha1));
        command.Parameters.AddWithValue("$updated", media.UpdatedAt.ToUnixTimeMilliseconds());
        return command;
    }

    private static void ClearSelection(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long gameId,
        GameMediaKind kind)
    {
        using var clear = connection.CreateCommand();
        clear.Transaction = transaction;
        clear.CommandText =
            """
            UPDATE GameMediaAssets
            SET IsSelected = 0, SelectionOrigin = NULL
            WHERE GameId = $gameId AND Kind = $kind;
            """;
        clear.Parameters.AddWithValue("$gameId", gameId);
        clear.Parameters.AddWithValue("$kind", (int)kind);
        clear.ExecuteNonQuery();
    }

    private static void ValidateMetadata(GameMetadataValue value, GameMetadataApplyMode mode)
    {
        if (value.GameId <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Game ID must be positive.");
        if (string.IsNullOrWhiteSpace(value.Value))
            throw new ArgumentException("Metadata value cannot be empty.", nameof(value));
        if (value.Origin == GameMetadataValueOrigin.Provider && string.IsNullOrWhiteSpace(value.ProviderId))
            throw new ArgumentException("Provider metadata requires a provider ID.", nameof(value));
        if (mode == GameMetadataApplyMode.UserEdit && value.Origin != GameMetadataValueOrigin.User)
            throw new ArgumentException("User-edit mode requires user-owned metadata.", nameof(value));
        if (mode != GameMetadataApplyMode.UserEdit && value.Origin != GameMetadataValueOrigin.Provider)
            throw new ArgumentException("Provider write modes require provider-owned metadata.", nameof(value));
    }

    private static void ValidateMedia(GameMediaAsset media)
    {
        if (media.GameId <= 0)
            throw new ArgumentOutOfRangeException(nameof(media), "Game ID must be positive.");
        if (string.IsNullOrWhiteSpace(media.LocalPath))
            throw new ArgumentException("Media path cannot be empty.", nameof(media));
        if (string.IsNullOrWhiteSpace(media.FileExtension))
            throw new ArgumentException("Media file extension cannot be empty.", nameof(media));
        if (media.Origin == GameMediaOrigin.Provider && string.IsNullOrWhiteSpace(media.ProviderId))
            throw new ArgumentException("Provider media requires a provider ID.", nameof(media));
        if (media.IsSelected != (media.SelectionOrigin is not null))
            throw new ArgumentException("Selected media must record who selected it.", nameof(media));
    }

    private static (long Id, GameMediaOrigin Origin, string? ProviderId, bool IsSelected,
        GameMediaSelectionOrigin? SelectionOrigin)? FindExistingMedia(
        SqliteConnection connection,
        SqliteTransaction transaction,
        GameMediaAsset media,
        string storedPath)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = media.Id > 0
            ? """
              SELECT Id, Origin, ProviderId, IsSelected, SelectionOrigin
              FROM GameMediaAssets
              WHERE Id = $id AND GameId = $gameId AND Kind = $kind;
              """
            : """
              SELECT Id, Origin, ProviderId, IsSelected, SelectionOrigin
              FROM GameMediaAssets
              WHERE GameId = $gameId AND Kind = $kind AND LocalPath = $localPath;
              """;
        if (media.Id > 0)
            command.Parameters.AddWithValue("$id", media.Id);
        command.Parameters.AddWithValue("$gameId", media.GameId);
        command.Parameters.AddWithValue("$kind", (int)media.Kind);
        if (media.Id <= 0)
            command.Parameters.AddWithValue("$localPath", storedPath);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;
        return (
            reader.GetInt64(0),
            (GameMediaOrigin)reader.GetInt32(1),
            ReadNullableString(reader, 2),
            reader.GetInt64(3) != 0,
            reader.IsDBNull(4) ? null : (GameMediaSelectionOrigin)reader.GetInt32(4));
    }

    private static GameMediaAsset ApplySelectionProtection(
        SqliteConnection connection,
        SqliteTransaction transaction,
        GameMediaAsset media,
        (long Id, GameMediaOrigin Origin, string? ProviderId, bool IsSelected,
            GameMediaSelectionOrigin? SelectionOrigin)? existing,
        bool overrideUserSelection)
    {
        // An explicit override (the single-game scraper's ticked row) makes the new art the selected
        // one as requested, bypassing the user-selection guard below.
        if (overrideUserSelection)
            return media;

        if (media.Origin != GameMediaOrigin.Provider)
            return media;

        if (existing is { IsSelected: true, SelectionOrigin: GameMediaSelectionOrigin.User })
            return media with { IsSelected = true, SelectionOrigin = GameMediaSelectionOrigin.User };

        if (!media.IsSelected)
            return media;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT Id
            FROM GameMediaAssets
            WHERE GameId = $gameId AND Kind = $kind AND IsSelected = 1
              AND SelectionOrigin = $userOrigin;
            """;
        command.Parameters.AddWithValue("$gameId", media.GameId);
        command.Parameters.AddWithValue("$kind", (int)media.Kind);
        command.Parameters.AddWithValue("$userOrigin", (int)GameMediaSelectionOrigin.User);
        var userSelectedId = command.ExecuteScalar();
        return userSelectedId is null
            ? media
            : media with { IsSelected = false, SelectionOrigin = null };
    }

    private static string NormalizeCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static object DbValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static int? ReadNullableInt(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    /// <summary>Mutable per-game bucket that the three grouped projection queries fill in place.</summary>
    private sealed class ProjectionAccumulator
    {
        public bool HasBoxFront;
        public bool HasScreenshot;
        public bool HasWheel;
        public bool HasFanart;
        public bool HasTitleScreen;
        public bool HasBoxBack;
        public bool HasBoxSpine;
        public bool HasPhysicalMedia;
        public bool HasPhysicalMediaTexture;
        public bool HasDescription;
        public bool HasProviderMatch;
        public string? Rating;
        public string? Genre;
        public string? ReleaseDate;
        public string? Players;
        public string? Developer;
        public string? Publisher;

        public GameDetailsProjection ToProjection() => new(
            HasBoxFront,
            HasScreenshot,
            HasWheel,
            HasFanart,
            HasDescription,
            HasProviderMatch,
            Rating,
            Genre,
            ReleaseDate,
            Players,
            Developer,
            Publisher,
            HasTitleScreen,
            HasBoxBack,
            HasBoxSpine,
            HasPhysicalMedia,
            HasPhysicalMediaTexture);
    }
}
