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
            """
            SELECT CASE
                       WHEN configurations.EmulatorInstallationId IS NULL OR
                            trim(configurations.EmulatorInstallationId) = ''
                           THEN configurations.ExecutablePath
                       ELSE installations.ExecutablePath
                   END,
                   configurations.LaunchArguments,
                   configurations.EmulatorId,
                   configurations.EmulatorInstallationId,
                   configurations.CorePath
            FROM EmulatorConfigs AS configurations
            LEFT JOIN EmulatorInstallations AS installations
                ON installations.InstallationId = configurations.EmulatorInstallationId
            WHERE configurations.SystemId = $systemId;
            """;
        command.Parameters.AddWithValue("$systemId", systemId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        var storedExecutable = reader.IsDBNull(0) ? null : reader.GetString(0);
        return new EmulatorConfiguration(
            systemId,
            storedExecutable is null ? null : _pathResolver.ToAbsolutePath(storedExecutable),
            reader.IsDBNull(1) ? null : reader.GetString(1))
        {
            EmulatorId = reader.IsDBNull(2) ? null : reader.GetString(2),
            EmulatorInstallationId = reader.IsDBNull(3) ? null : reader.GetString(3),
            CorePath = reader.IsDBNull(4)
                ? null
                : _pathResolver.ToAbsolutePath(reader.GetString(4)),
        };
    }

    public void Save(EmulatorConfiguration configuration) => SaveAll([configuration]);

    public void SaveAll(IReadOnlyList<EmulatorConfiguration> configurations)
    {
        var normalized = configurations
            .Select(Normalize)
            .ToArray();
        var installations = normalized
            .GroupBy(configuration => configuration.EmulatorInstallationId!, StringComparer.Ordinal)
            .Select(CreateInstallation)
            .ToArray();

        using var connection = _database.CreateConnection();
        using var transaction = connection.BeginTransaction();
        using (var installationCommand = connection.CreateCommand())
        {
            installationCommand.Transaction = transaction;
            installationCommand.CommandText =
                """
                INSERT INTO EmulatorInstallations (InstallationId, EmulatorId, ExecutablePath)
                VALUES ($installationId, $emulatorId, $executablePath)
                ON CONFLICT(InstallationId) DO UPDATE SET
                    EmulatorId = excluded.EmulatorId,
                    ExecutablePath = excluded.ExecutablePath;
                """;
            var installationId = installationCommand.Parameters.Add("$installationId", SqliteType.Text);
            var emulatorId = installationCommand.Parameters.Add("$emulatorId", SqliteType.Text);
            var installationPath = installationCommand.Parameters.Add("$executablePath", SqliteType.Text);

            foreach (var installation in installations)
            {
                installationId.Value = installation.Id;
                emulatorId.Value = installation.EmulatorId;
                installationPath.Value = installation.ExecutablePath is null
                    ? DBNull.Value
                    : _pathResolver.ToStorablePath(installation.ExecutablePath);
                installationCommand.ExecuteNonQuery();
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO EmulatorConfigs (
                    SystemId, ExecutablePath, LaunchArguments, EmulatorId,
                    EmulatorInstallationId, CorePath)
                VALUES (
                    $systemId, $executablePath, $launchArguments, $emulatorId,
                    $installationId, $corePath)
                ON CONFLICT(SystemId) DO UPDATE SET
                    ExecutablePath = excluded.ExecutablePath,
                    LaunchArguments = excluded.LaunchArguments,
                    EmulatorId = excluded.EmulatorId,
                    EmulatorInstallationId = excluded.EmulatorInstallationId,
                    CorePath = excluded.CorePath;
                """;
            var systemId = command.Parameters.Add("$systemId", SqliteType.Text);
            var executablePath = command.Parameters.Add("$executablePath", SqliteType.Text);
            var launchArguments = command.Parameters.Add("$launchArguments", SqliteType.Text);
            var emulatorId = command.Parameters.Add("$emulatorId", SqliteType.Text);
            var installationId = command.Parameters.Add("$installationId", SqliteType.Text);
            var corePath = command.Parameters.Add("$corePath", SqliteType.Text);

            foreach (var configuration in normalized)
            {
                systemId.Value = configuration.SystemId;
                executablePath.Value = string.IsNullOrWhiteSpace(configuration.ExecutablePath)
                    ? DBNull.Value
                    : _pathResolver.ToStorablePath(configuration.ExecutablePath);
                launchArguments.Value = configuration.LaunchArguments is null
                    ? DBNull.Value
                    : configuration.LaunchArguments;
                emulatorId.Value = configuration.EmulatorId!;
                installationId.Value = configuration.EmulatorInstallationId!;
                corePath.Value = string.IsNullOrWhiteSpace(configuration.CorePath)
                    ? DBNull.Value
                    : _pathResolver.ToStorablePath(configuration.CorePath);
                command.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    private static EmulatorConfiguration Normalize(EmulatorConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.SystemId))
            throw new ArgumentException("A system id is required.", nameof(configuration));

        var emulatorId = string.IsNullOrWhiteSpace(configuration.EmulatorId)
            ? configuration.SystemId
            : configuration.EmulatorId.Trim();
        var installationId = string.IsNullOrWhiteSpace(configuration.EmulatorInstallationId)
            ? emulatorId + "-" + configuration.SystemId
            : configuration.EmulatorInstallationId.Trim();
        return configuration with
        {
            EmulatorId = emulatorId,
            EmulatorInstallationId = installationId,
        };
    }

    private static EmulatorInstallation CreateInstallation(
        IGrouping<string, EmulatorConfiguration> configurations)
    {
        var emulatorIds = configurations
            .Select(configuration => configuration.EmulatorId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (emulatorIds.Length != 1)
        {
            throw new ArgumentException(
                $"The shared installation '{configurations.Key}' maps to more than one emulator.");
        }

        var executablePaths = configurations
            .Select(configuration => configuration.ExecutablePath?.Trim())
            .Where(path => !string.IsNullOrEmpty(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (executablePaths.Length > 1)
        {
            throw new ArgumentException(
                $"The shared installation '{configurations.Key}' has more than one executable path.");
        }

        return new EmulatorInstallation(
            configurations.Key,
            emulatorIds[0],
            executablePaths.SingleOrDefault());
    }

    private sealed record EmulatorInstallation(
        string Id,
        string EmulatorId,
        string? ExecutablePath);
}
