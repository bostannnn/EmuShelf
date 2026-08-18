namespace EmuShelf.Core.SaveSync;

/// <summary>
/// The destination a save export writes into: one flat sequence of named entries. The zip
/// implementation packs them into a single archive; a test sink can capture them in memory. The
/// sink only receives already-expanded file entries — a folder unit's archive is unpacked into
/// individual <see cref="AddFileAsync"/> calls by the exporter — so it never needs to know a
/// save's shape.
/// </summary>
public interface ISaveExportSink
{
    /// <summary>
    /// Writes one file into the export at <paramref name="entryPath"/> (a forward-slash relative
    /// path, e.g. <c>PlayStation 2/Mcd001.ps2</c>). Overwriting is never expected; the exporter
    /// de-duplicates names before calling this.
    /// </summary>
    Task AddFileAsync(string entryPath, Stream content, CancellationToken cancellationToken = default);
}
