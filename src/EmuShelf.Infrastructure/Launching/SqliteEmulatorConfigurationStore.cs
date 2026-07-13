using EmuShelf.Core.Launching;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace EmuShelf.Infrastructure.Launching;

public sealed class SqliteEmulatorConfigurationStore : IEmulatorConfigurationStore
{
    private readonly LibraryDatabase _database;
    private readonly IRelativePathResolver _pathResolver;

    public SqliteEmulatorConfigurationStore(
        LibraryDatabase database,
        IRelativePathResolver pathResolver)
    {
        _database = database;
        _pathResolver = pathResolver;
    }

    public EmulatorConfiguration? Get(string systemId)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT ExecutablePath, LaunchArguments FROM EmulatorConfigs WHERE SystemId = $systemId;";
        command.Parameters.AddWithValue("$systemId", systemId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        var storedExecutable = reader.IsDBNull(0) ? null : reader.GetString(0);
        return new EmulatorConfiguration(
            systemId,
            storedExecutable is null ? null : _pathResolver.ToAbsolutePath(storedExecutable),
            reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    public void Save(EmulatorConfiguration configuration) => SaveAll([configuration]);

    public void SaveAll(IReadOnlyList<EmulatorConfiguration> configurations)
    {
        using var connection = _database.CreateConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO EmulatorConfigs (SystemId, ExecutablePath, LaunchArguments)
            VALUES ($systemId, $executablePath, $launchArguments)
            ON CONFLICT(SystemId) DO UPDATE SET
                ExecutablePath = excluded.ExecutablePath,
                LaunchArguments = excluded.LaunchArguments;
            """;
        var systemId = command.Parameters.Add("$systemId", SqliteType.Text);
        var executablePath = command.Parameters.Add("$executablePath", SqliteType.Text);
        var launchArguments = command.Parameters.Add("$launchArguments", SqliteType.Text);

        foreach (var configuration in configurations)
        {
            systemId.Value = configuration.SystemId;
            executablePath.Value = string.IsNullOrWhiteSpace(configuration.ExecutablePath)
                ? DBNull.Value
                : _pathResolver.ToStorablePath(configuration.ExecutablePath);
            launchArguments.Value = configuration.LaunchArguments is null
                ? DBNull.Value
                : configuration.LaunchArguments;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }
}
