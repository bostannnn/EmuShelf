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

    // Shared projection for both Get and GetAll. SystemId is column 0 so the same reader layout
    // materializes a single row or the whole table.
    private const string SelectColumns =
        """
        SELECT configurations.SystemId,
               CASE
                   WHEN configurations.EmulatorInstallationId IS NULL OR
                        trim(configurations.EmulatorInstallationId) = ''
                       THEN 'direct'
                   WHEN installations.TargetKind IS NULL OR trim(installations.TargetKind) = ''
                       THEN 'direct'
                   ELSE installations.TargetKind
               END,
               CASE
                   WHEN configurations.EmulatorInstallationId IS NULL OR
                        trim(configurations.EmulatorInstallationId) = ''
                       THEN configurations.ExecutablePath
                   WHEN installations.TargetValue IS NULL OR trim(installations.TargetValue) = ''
                       THEN installations.ExecutablePath
                   ELSE installations.TargetValue
               END,
               configurations.LaunchArguments,
               configurations.EmulatorId,
               configurations.EmulatorInstallationId,
               configurations.CorePath
        FROM EmulatorConfigs AS configurations
        LEFT JOIN EmulatorInstallations AS installations
            ON installations.InstallationId = configurations.EmulatorInstallationId
        """;

    // Selects only the row whose emulator matches the system's active selection. When no selection
    // exists the COALESCE falls back to the row's own emulator (always true), so the ORDER BY + LIMIT
    // still yields one deterministic row rather than throwing on an unconfigured legacy database.
    private const string ActiveEmulatorPredicate =
        """
        configurations.EmulatorId = COALESCE(
            (SELECT EmulatorId FROM SystemEmulatorSelection WHERE SystemId = configurations.SystemId),
            configurations.EmulatorId)
        """;

    public EmulatorConfiguration? Get(string systemId)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SelectColumns +
            $"\nWHERE configurations.SystemId = $systemId AND {ActiveEmulatorPredicate}" +
            "\nORDER BY configurations.EmulatorId\nLIMIT 1;";
        command.Parameters.AddWithValue("$systemId", systemId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Materialize(reader) : null;
    }

    public EmulatorConfiguration? GetForEmulator(string systemId, string emulatorId)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SelectColumns +
            "\nWHERE configurations.SystemId = $systemId AND configurations.EmulatorId = $emulatorId;";
        command.Parameters.AddWithValue("$systemId", systemId);
        command.Parameters.AddWithValue("$emulatorId", emulatorId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Materialize(reader) : null;
    }

    public IReadOnlyDictionary<string, EmulatorConfiguration?> GetAll(IEnumerable<string> systemIds)
    {
        // Every requested id gets an entry (null until a row fills it in), preserving the shape the
        // old per-system Get loop produced while touching the database exactly once.
        var result = new Dictionary<string, EmulatorConfiguration?>(StringComparer.Ordinal);
        foreach (var systemId in systemIds)
            result[systemId] = null;

        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SelectColumns +
            $"\nWHERE {ActiveEmulatorPredicate}" +
            "\nORDER BY configurations.SystemId, configurations.EmulatorId;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var configuration = Materialize(reader);
            // Ignore stray rows for systems the caller didn't ask about, and keep the first active
            // row per system (a degenerate no-selection database could return more than one).
            if (result.TryGetValue(configuration.SystemId, out var existing) && existing is null)
                result[configuration.SystemId] = configuration;
        }

        return result;
    }

    public string? GetActiveEmulatorId(string systemId)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EmulatorId FROM SystemEmulatorSelection WHERE SystemId = $systemId;";
        command.Parameters.AddWithValue("$systemId", systemId);
        return command.ExecuteScalar() as string;
    }

    public SystemEmulatorProfiles GetProfiles(string systemId) =>
        GetAllProfiles([systemId])[systemId];

    public IReadOnlyDictionary<string, SystemEmulatorProfiles> GetAllProfiles(IEnumerable<string> systemIds)
    {
        var requested = systemIds.ToArray();
        var configurations = new Dictionary<string, List<EmulatorConfiguration>>(StringComparer.Ordinal);
        var active = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var systemId in requested)
            configurations[systemId] = [];

        using var connection = _database.CreateConnection();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = SelectColumns +
                "\nORDER BY configurations.SystemId, configurations.EmulatorId;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var configuration = Materialize(reader);
                if (configurations.TryGetValue(configuration.SystemId, out var list))
                    list.Add(configuration);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT SystemId, EmulatorId FROM SystemEmulatorSelection;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var systemId = reader.GetString(0);
                if (configurations.ContainsKey(systemId))
                    active[systemId] = reader.GetString(1);
            }
        }

        return requested.ToDictionary(
            systemId => systemId,
            systemId => new SystemEmulatorProfiles(
                systemId,
                active.GetValueOrDefault(systemId),
                configurations[systemId]),
            StringComparer.Ordinal);
    }

    public void SetActiveEmulator(string systemId, string emulatorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(emulatorId);
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO SystemEmulatorSelection (SystemId, EmulatorId)
            VALUES ($systemId, $emulatorId)
            ON CONFLICT(SystemId) DO UPDATE SET EmulatorId = excluded.EmulatorId;
            """;
        command.Parameters.AddWithValue("$systemId", systemId);
        command.Parameters.AddWithValue("$emulatorId", emulatorId.Trim());
        command.ExecuteNonQuery();
    }

    private EmulatorConfiguration Materialize(SqliteDataReader reader)
    {
        var systemId = reader.GetString(0);
        var targetKind = reader.IsDBNull(1) ? null : reader.GetString(1);
        var targetValue = reader.IsDBNull(2) ? null : reader.GetString(2);
        var target = CreateTarget(targetKind, targetValue);
        var executablePath = target is DirectExecutableTarget direct ? direct.Path : null;
        return new EmulatorConfiguration(
            systemId,
            executablePath,
            reader.IsDBNull(3) ? null : reader.GetString(3))
        {
            LaunchTarget = target,
            EmulatorId = reader.IsDBNull(4) ? null : reader.GetString(4),
            EmulatorInstallationId = reader.IsDBNull(5) ? null : reader.GetString(5),
            CorePath = reader.IsDBNull(6)
                ? null
                : _pathResolver.ToAbsolutePath(reader.GetString(6)),
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
                INSERT INTO EmulatorInstallations (InstallationId, EmulatorId, ExecutablePath, TargetKind, TargetValue)
                VALUES ($installationId, $emulatorId, $executablePath, $targetKind, $targetValue)
                ON CONFLICT(InstallationId) DO UPDATE SET
                    EmulatorId = excluded.EmulatorId,
                    ExecutablePath = excluded.ExecutablePath,
                    TargetKind = excluded.TargetKind,
                    TargetValue = excluded.TargetValue;
                """;
            var installationId = installationCommand.Parameters.Add("$installationId", SqliteType.Text);
            var emulatorId = installationCommand.Parameters.Add("$emulatorId", SqliteType.Text);
            var installationPath = installationCommand.Parameters.Add("$executablePath", SqliteType.Text);
            var targetKind = installationCommand.Parameters.Add("$targetKind", SqliteType.Text);
            var targetValue = installationCommand.Parameters.Add("$targetValue", SqliteType.Text);

            foreach (var installation in installations)
            {
                installationId.Value = installation.Id;
                emulatorId.Value = installation.EmulatorId;
                installationPath.Value = installation.Target is DirectExecutableTarget direct
                    ? _pathResolver.ToStorablePath(direct.Path)
                    : DBNull.Value;
                targetKind.Value = ToKind(installation.Target);
                targetValue.Value = ToStorableTargetValue(installation.Target);
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
                ON CONFLICT(SystemId, EmulatorId) DO UPDATE SET
                    ExecutablePath = excluded.ExecutablePath,
                    LaunchArguments = excluded.LaunchArguments,
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
                // Target ownership lives exclusively in EmulatorInstallations. This legacy
                // field remains readable for an interrupted pre-v11 migration only.
                executablePath.Value = DBNull.Value;
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

        // Saving a profile makes it the active one for its system: a single-emulator system stays
        // exactly as before, and configuring a system's alternative emulator switches launch/saves to
        // it. When several profiles for one system are saved together, the last one wins here; the
        // caller can pin a different active profile afterwards with SetActiveEmulator.
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO SystemEmulatorSelection (SystemId, EmulatorId)
                VALUES ($systemId, $emulatorId)
                ON CONFLICT(SystemId) DO UPDATE SET EmulatorId = excluded.EmulatorId;
                """;
            var systemId = command.Parameters.Add("$systemId", SqliteType.Text);
            var emulatorId = command.Parameters.Add("$emulatorId", SqliteType.Text);
            foreach (var configuration in normalized)
            {
                systemId.Value = configuration.SystemId;
                emulatorId.Value = configuration.EmulatorId!;
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
        var target = configuration.LaunchTarget ??
            (string.IsNullOrWhiteSpace(configuration.ExecutablePath)
                ? null
                : new DirectExecutableTarget(configuration.ExecutablePath));
        if (target is FlatpakApplicationTarget flatpak)
        {
            if (!IsValidFlatpakApplicationId(flatpak.AppId))
            {
                throw new ArgumentException(
                    "A Flatpak application id must contain at least three dot-separated identifier segments.",
                    nameof(configuration));
            }

            if (flatpak.Branch is not null && !IsValidFlatpakBranch(flatpak.Branch))
            {
                throw new ArgumentException(
                    "A Flatpak branch may only contain letters, digits, '.', '_' or '-'.",
                    nameof(configuration));
            }
        }

        return configuration with
        {
            EmulatorId = emulatorId,
            EmulatorInstallationId = installationId,
            LaunchTarget = target,
        };
    }

    private static bool IsValidFlatpakApplicationId(string? appId) =>
        !string.IsNullOrWhiteSpace(appId) &&
        appId.Split('.').Length >= 3 &&
        appId.Split('.').All(segment => segment.Length > 0 &&
            segment.All(character => char.IsLetterOrDigit(character) || character is '_' or '-'));

    private static bool IsValidFlatpakBranch(string branch) =>
        branch.Length > 0 &&
        branch.All(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-');

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

        var targets = configurations
            .Select(configuration => configuration.LaunchTarget)
            .Where(target => target is not null)
            .Distinct(EmulatorLaunchTargetComparer.Instance)
            .ToArray();
        if (targets.Length > 1)
        {
            throw new ArgumentException(
                $"The shared installation '{configurations.Key}' has more than one launcher target.");
        }

        return new EmulatorInstallation(
            configurations.Key,
            emulatorIds[0],
            targets.SingleOrDefault());
    }

    private EmulatorLaunchTarget? CreateTarget(string? kind, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return kind?.Trim().ToLowerInvariant() switch
        {
            "flatpak" => FlatpakApplicationTarget.Parse(value.Trim()),
            _ => new DirectExecutableTarget(_pathResolver.ToAbsolutePath(value)),
        };
    }

    private static string ToKind(EmulatorLaunchTarget? target) => target switch
    {
        FlatpakApplicationTarget => "flatpak",
        _ => "direct",
    };

    private object ToStorableTargetValue(EmulatorLaunchTarget? target) => target switch
    {
        DirectExecutableTarget direct => _pathResolver.ToStorablePath(direct.Path),
        // The ref carries the pinned branch (appId//branch) when one is set, so it round-trips.
        FlatpakApplicationTarget flatpak => flatpak.Ref,
        _ => DBNull.Value,
    };

    private sealed record EmulatorInstallation(
        string Id,
        string EmulatorId,
        EmulatorLaunchTarget? Target);

    private sealed class EmulatorLaunchTargetComparer : IEqualityComparer<EmulatorLaunchTarget?>
    {
        public static EmulatorLaunchTargetComparer Instance { get; } = new();

        public bool Equals(EmulatorLaunchTarget? left, EmulatorLaunchTarget? right) =>
            (left, right) switch
            {
                (null, null) => true,
                (DirectExecutableTarget a, DirectExecutableTarget b) =>
                    string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase),
                // A pinned branch is part of the identity: stable and beta are different installs.
                (FlatpakApplicationTarget a, FlatpakApplicationTarget b) =>
                    string.Equals(a.Ref, b.Ref, StringComparison.Ordinal),
                _ => false,
            };

        public int GetHashCode(EmulatorLaunchTarget? target) => target switch
        {
            null => 0,
            DirectExecutableTarget direct => StringComparer.OrdinalIgnoreCase.GetHashCode(direct.Path),
            FlatpakApplicationTarget flatpak => StringComparer.Ordinal.GetHashCode(flatpak.Ref),
            _ => 0,
        };
    }
}
