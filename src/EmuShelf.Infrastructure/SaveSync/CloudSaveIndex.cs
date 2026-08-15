using System.Text.Json;
using EmuShelf.Core.SaveSync;

namespace EmuShelf.Infrastructure.SaveSync;

/// <summary>
/// The on-remote wire format shared by every cloud transport: one <c>index.json</c> describing every
/// unit, beside one <c>&lt;unitId&gt;.payload</c> blob per unit.
/// </summary>
/// <remarks>
/// Deliberately owned here rather than by a transport. The format — not the tool that moves it — is
/// the contract with the remote, so two transports that agree on it can address the same cloud folder
/// interchangeably: a user can switch between them, and a transport that misbehaves in the field can
/// be swapped out without the remote having to be rebuilt. Reading is strict for the same reason a
/// half-written index must never be treated as authoritative: a missing or malformed entry would read
/// as "this save is not on the remote", and the machine holding it would stop uploading it.
/// </remarks>
public static class CloudSaveIndex
{
    /// <summary>The index file's name inside the cloud folder.</summary>
    public const string FileName = "index.json";

    /// <summary>The suffix appended to a unit id to name its payload blob.</summary>
    public const string PayloadSuffix = ".payload";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>The remote-relative name of one unit's payload.</summary>
    public static string PayloadName(string unitId) => unitId + PayloadSuffix;

    /// <summary>
    /// Whether a unit id is safe to use as a remote-relative path. Rejects traversal, empty segments,
    /// and the separators that would change meaning on some backend or local staging filesystem.
    /// </summary>
    public static bool IsSafeUnitId(string unitId) =>
        !string.IsNullOrWhiteSpace(unitId) &&
        unitId.Split('/', StringSplitOptions.None).All(segment =>
            segment.Length > 0 && segment is not "." and not ".." &&
            !segment.Contains('\\') && !segment.Contains(':'));

    /// <summary>Throws when <paramref name="unitId"/> is not a safe remote-relative path.</summary>
    public static void ValidateUnitId(string unitId)
    {
        if (!IsSafeUnitId(unitId))
            throw new ArgumentException("The cloud save unit id is not a safe remote-relative path.", nameof(unitId));
    }

    /// <summary>Serializes an index to the exact bytes written to the remote.</summary>
    public static string Serialize(IEnumerable<SaveUnitSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var entries = snapshots
            .Select(snapshot => new RemoteUnitMetadata(
                snapshot.UnitId,
                snapshot.ContentHash,
                snapshot.ModifiedUtc,
                snapshot.Compatibility))
            .ToList();
        return JsonSerializer.Serialize(entries, SerializerOptions);
    }

    /// <summary>
    /// Parses index bytes read from the remote. Every malformed shape throws
    /// <see cref="InvalidDataException"/> rather than being skipped, so a corrupt index is a visible
    /// failure instead of a silently shorter save list.
    /// </summary>
    public static Dictionary<string, SaveUnitSnapshot> Parse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length == 0)
            throw new InvalidDataException("The cloud index is empty.");

        List<RemoteUnitMetadata>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<RemoteUnitMetadata>>(utf8Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The cloud index is not valid EmuShelf metadata.", ex);
        }

        if (entries is null)
            throw new InvalidDataException("The cloud index is not valid EmuShelf metadata.");

        var index = new Dictionary<string, SaveUnitSnapshot>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry is null || !IsSafeUnitId(entry.UnitId) ||
                string.IsNullOrWhiteSpace(entry.ContentHash) || entry.ModifiedUtc == default)
            {
                throw new InvalidDataException("The cloud index is not valid EmuShelf metadata.");
            }

            if (!index.TryAdd(
                    entry.UnitId,
                    new SaveUnitSnapshot(entry.UnitId, entry.ContentHash, entry.ModifiedUtc, entry.Compatibility)))
            {
                throw new InvalidDataException("The cloud index contains a duplicate save unit.");
            }
        }

        return index;
    }

    private sealed record RemoteUnitMetadata(
        string UnitId,
        string ContentHash,
        DateTimeOffset ModifiedUtc,
        string? Compatibility = null);
}
