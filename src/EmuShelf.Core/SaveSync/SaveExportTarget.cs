namespace EmuShelf.Core.SaveSync;

/// <summary>
/// One platform's export source: the emulator-aware provider that enumerates its save units, the
/// local endpoint that reads their bytes, and the human-readable platform name used as the export's
/// top-level folder (e.g. <c>PlayStation 2</c>).
/// </summary>
public sealed record SaveExportTarget(
    ISaveLocationProvider Provider,
    ILocalSaveEndpoint Endpoint,
    string PlatformName);
