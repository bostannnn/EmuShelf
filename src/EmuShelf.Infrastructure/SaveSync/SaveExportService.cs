using System.IO.Compression;
using System.Text;
using System.Text.Json;
using EmuShelf.Core.SaveSync;

namespace EmuShelf.Infrastructure.SaveSync;

/// <summary>
/// Copies save data out into a portable, browsable archive via an <see cref="ISaveExportSink"/>. It
/// is the read-only counterpart to <see cref="SaveSyncService"/>: it never writes to a save, a game
/// file, or emulator configuration. Providers and endpoints do the resolving and reading; a
/// transport, when supplied, folds in saves that live only in the cloud.
///
/// The class is network- and filesystem-agnostic on purpose — everything comes through the injected
/// targets, transport, and sink — so it is exercised entirely with in-memory fakes.
/// </summary>
public sealed class SaveExportService
{
    private readonly Func<DateTimeOffset> _clock;

    public SaveExportService(Func<DateTimeOffset>? clock = null) =>
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

    /// <summary>
    /// Exports every device save from <paramref name="targets"/> and, when <paramref name="cloud"/>
    /// is supplied, every cloud save that is not already present on the device. The device copy wins
    /// for a save that exists on both sides. Returns without writing a manifest when nothing was
    /// found, so an empty export leaves no archive.
    /// </summary>
    public async Task<SaveExportResult> ExportAsync(
        IReadOnlyList<SaveExportTarget> targets,
        ICloudSyncTransport? cloud,
        ISaveExportSink sink,
        IProgress<SaveTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(sink);

        var usedEntryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exportedUnitIds = new HashSet<string>(StringComparer.Ordinal);
        var manifest = new List<ManifestEntry>();
        var skipped = new List<string>();
        var totalBytes = 0L;
        var fromCloud = 0;

        try
        {
            // Device pass first, so a unit present both locally and in the cloud is exported from the
            // device and the cloud pass skips it.
            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IReadOnlyList<SaveUnit> units;
                try
                {
                    units = await target.Provider.GetSaveUnitsAsync(cancellationToken);
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException or InvalidDataException or
                        SaveProviderConfigurationException)
                {
                    // One platform's saves being unreadable (a locked or removed folder) must not fail
                    // the whole export; note it and carry on with the other platforms.
                    skipped.Add($"{target.PlatformName}: saves could not be listed ({ex.Message}).");
                    continue;
                }

                foreach (var unit in units)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var basePath = BasePath(target.PlatformName, unit.UnitId, target.Provider.UnitIdPrefix);
                    try
                    {
                        await using var content = await target.Endpoint.ReadAsync(unit.UnitId, cancellationToken);
                        totalBytes += await EmitAsync(sink, usedEntryPaths, basePath, unit.Kind, content, cancellationToken);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                    {
                        // A save that vanished or got locked between listing and reading must not sink
                        // the whole export; note it and move on.
                        skipped.Add($"{unit.UnitId}: could not be read on this machine ({ex.Message}).");
                        continue;
                    }

                    exportedUnitIds.Add(unit.UnitId);
                    manifest.Add(new ManifestEntry(unit.UnitId, target.PlatformName, "device", unit.Kind.ToString(), unit.DisplayName));
                    progress?.Report(new SaveTransferProgress(manifest.Count, manifest.Count, 100));
                }
            }

            if (cloud is not null)
            {
                var (cloudCount, cloudBytes) = await ExportCloudOnlyAsync(
                    targets, cloud, sink, usedEntryPaths, exportedUnitIds, manifest, skipped, progress, cancellationToken);
                fromCloud = cloudCount;
                totalBytes += cloudBytes;
            }

            if (manifest.Count == 0)
                return SaveExportResult.NothingToExport(skipped);

            await WriteReadmeAsync(sink, manifest, fromCloud, cancellationToken);
            await WriteManifestAsync(sink, manifest, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or InvalidDataException or HttpRequestException or InvalidOperationException or
                UnauthorizedAccessException or JsonException)
        {
            return SaveExportResult.Failed(ex.Message);
        }

        // DestinationPath is filled in by the caller that owns the sink; the service does not know it.
        return SaveExportResult.Completed(
            destinationPath: string.Empty,
            savesExported: manifest.Count,
            fromCloud: fromCloud,
            totalBytes: totalBytes,
            skipped: skipped);
    }

    private async Task<(int FromCloud, long Bytes)> ExportCloudOnlyAsync(
        IReadOnlyList<SaveExportTarget> targets,
        ICloudSyncTransport cloud,
        ISaveExportSink sink,
        HashSet<string> usedEntryPaths,
        HashSet<string> exportedUnitIds,
        List<ManifestEntry> manifest,
        List<string> skipped,
        IProgress<SaveTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        var remote = await cloud.ListAsync(cancellationToken);
        var pending = remote
            .Where(snapshot => !exportedUnitIds.Contains(snapshot.UnitId))
            .OrderBy(snapshot => snapshot.UnitId, StringComparer.Ordinal)
            .ToList();
        if (pending.Count == 0)
            return (0, 0);

        cloud.ExpectDownloads(pending.Select(snapshot => snapshot.UnitId));

        var fromCloud = 0;
        var bytes = 0L;
        foreach (var snapshot in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var owner = targets.FirstOrDefault(target => target.Provider.OwnsUnit(snapshot.UnitId));
            if (owner is null)
            {
                // A unit sitting under a known platform's prefix that the platform does not own is an
                // excluded namespace — cheats/patches an older build uploaded, which are not user save
                // data (see DECISIONS 2026-07-24). Ignore those silently rather than reporting each as
                // a skip; a remote can hold thousands. Only a genuinely foreign unit (no platform's
                // prefix matches) is worth noting.
                var underKnownPlatform = targets.Any(target =>
                    snapshot.UnitId.StartsWith(target.Provider.UnitIdPrefix, StringComparison.Ordinal));
                if (!underKnownPlatform)
                    skipped.Add($"{snapshot.UnitId}: no matching platform is configured on this machine.");
                continue;
            }

            var location = owner.Provider.ResolveUnit(snapshot.UnitId);
            if (location is null)
            {
                skipped.Add($"{snapshot.UnitId}: the cloud save cannot be placed on this machine.");
                continue;
            }

            var basePath = BasePath(owner.PlatformName, snapshot.UnitId, owner.Provider.UnitIdPrefix);
            try
            {
                await using var content = await cloud.DownloadAsync(snapshot.UnitId, cancellationToken);
                bytes += await EmitAsync(sink, usedEntryPaths, basePath, location.Kind, content, cancellationToken);
            }
            catch (CloudPayloadMissingException)
            {
                skipped.Add($"{snapshot.UnitId}: the cloud copy is no longer available.");
                continue;
            }

            exportedUnitIds.Add(snapshot.UnitId);
            manifest.Add(new ManifestEntry(snapshot.UnitId, owner.PlatformName, "cloud", location.Kind.ToString(), snapshot.UnitId));
            fromCloud++;
            progress?.Report(new SaveTransferProgress(manifest.Count, manifest.Count, 100));
        }

        return (fromCloud, bytes);
    }

    // Writes one save unit's bytes into the sink and returns how many bytes were written. A file unit
    // is one entry; a folder unit's payload is a zip that is expanded into one entry per contained
    // file, so the archive is browsable rather than holding a zip inside a zip.
    private static async Task<long> EmitAsync(
        ISaveExportSink sink,
        HashSet<string> usedEntryPaths,
        string basePath,
        SaveUnitKind kind,
        Stream content,
        CancellationToken cancellationToken)
    {
        if (kind == SaveUnitKind.File)
        {
            var counting = new CountingStream(content);
            await sink.AddFileAsync(Dedupe(usedEntryPaths, basePath), counting, cancellationToken);
            return counting.BytesRead;
        }

        var total = 0L;
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Directory markers carry no bytes and no name; skip them, the folders are implied by paths.
            if (string.IsNullOrEmpty(entry.Name))
                continue;
            await using var entryStream = entry.Open();
            var counting = new CountingStream(entryStream);
            await sink.AddFileAsync(Dedupe(usedEntryPaths, $"{basePath}/{entry.FullName}"), counting, cancellationToken);
            total += counting.BytesRead;
        }

        return total;
    }

    // The archive folder for one platform + the portable tail of the unit id (the part after the
    // provider's namespace prefix), e.g. "PlayStation 2/Mcd001.ps2" or "Game Boy Color/states/x.state".
    private static string BasePath(string platformName, string unitId, string unitIdPrefix)
    {
        var tail = unitId.StartsWith(unitIdPrefix, StringComparison.Ordinal)
            ? unitId[unitIdPrefix.Length..]
            : unitId;
        if (string.IsNullOrWhiteSpace(tail))
            tail = "save";
        return $"{SanitizeSegment(platformName)}/{tail.TrimStart('/')}";
    }

    // A platform display name is free text and can contain a slash ("Mega Drive / Genesis"), which
    // would otherwise fork into sub-folders. Keep the tail (unit id) untouched — it is already a
    // validated, portable key whose slashes are intentional sub-paths.
    private static string SanitizeSegment(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(character is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '-' : character);
        var sanitized = builder.ToString().Trim().Trim('.');
        return sanitized.Length == 0 ? "saves" : sanitized;
    }

    private static string Dedupe(HashSet<string> used, string entryPath)
    {
        if (used.Add(entryPath))
            return entryPath;

        var directory = Path.GetDirectoryName(entryPath.Replace('/', Path.DirectorySeparatorChar));
        var prefix = string.IsNullOrEmpty(directory)
            ? string.Empty
            : directory.Replace(Path.DirectorySeparatorChar, '/') + "/";
        var stem = Path.GetFileNameWithoutExtension(entryPath);
        var extension = Path.GetExtension(entryPath);
        for (var index = 2; ; index++)
        {
            var candidate = $"{prefix}{stem} ({index}){extension}";
            if (used.Add(candidate))
                return candidate;
        }
    }

    private async Task WriteReadmeAsync(
        ISaveExportSink sink,
        IReadOnlyList<ManifestEntry> manifest,
        int fromCloud,
        CancellationToken cancellationToken)
    {
        var platforms = manifest
            .GroupBy(entry => entry.Platform, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);
        var builder = new StringBuilder();
        builder.AppendLine("EmuShelf save export");
        builder.AppendLine("====================");
        builder.AppendLine();
        builder.AppendLine($"Exported: {_clock():u}");
        builder.AppendLine($"Saves: {manifest.Count} ({fromCloud} pulled from the cloud).");
        builder.AppendLine();
        builder.AppendLine("These are copies of your emulator saves, grouped by platform. Each save keeps");
        builder.AppendLine("the name the emulator uses, so you can drop the files into the matching folder");
        builder.AppendLine("of another emulator. Save states live under a 'states' sub-folder and only");
        builder.AppendLine("reload on a compatible emulator build.");
        builder.AppendLine();
        builder.AppendLine("Contents:");
        foreach (var platform in platforms)
            builder.AppendLine($"  {platform.Key}: {platform.Count()} save(s)");
        builder.AppendLine();
        builder.AppendLine("EmuShelf did not modify any of your saves, game files, or emulator settings.");
        builder.AppendLine("See manifest.json for the full list.");

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        using var stream = new MemoryStream(bytes, writable: false);
        await sink.AddFileAsync("EXPORT-README.txt", stream, cancellationToken);
    }

    private async Task WriteManifestAsync(
        ISaveExportSink sink,
        IReadOnlyList<ManifestEntry> manifest,
        CancellationToken cancellationToken)
    {
        var document = new ManifestDocument(
            "EmuShelf",
            _clock(),
            manifest.OrderBy(entry => entry.Platform, StringComparer.Ordinal)
                .ThenBy(entry => entry.UnitId, StringComparer.Ordinal)
                .ToList());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, ManifestJsonOptions);
        using var stream = new MemoryStream(bytes, writable: false);
        await sink.AddFileAsync("manifest.json", stream, cancellationToken);
    }

    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { WriteIndented = true };

    private sealed record ManifestDocument(string GeneratedBy, DateTimeOffset ExportedUtc, IReadOnlyList<ManifestEntry> Saves);

    private sealed record ManifestEntry(string UnitId, string Platform, string Source, string Kind, string DisplayName);

    // Counts bytes as the sink reads them, without buffering: some units are hundreds of megabytes.
    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
