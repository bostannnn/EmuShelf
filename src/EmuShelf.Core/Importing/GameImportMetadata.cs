using EmuShelf.Core.Metadata;

namespace EmuShelf.Core.Importing;

/// <summary>
/// Read-only embedded evidence discovered while preparing a library entry. It is separate from
/// network metadata: callers may persist it immediately without contacting a catalogue.
/// </summary>
public sealed record GameImportMetadata(
    string? EmbeddedTitle,
    IReadOnlyList<GameIdentifier> Identifiers)
{
    public static GameImportMetadata Empty { get; } = new(null, []);
}
