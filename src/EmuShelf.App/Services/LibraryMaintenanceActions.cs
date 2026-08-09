using EmuShelf.Core.Library;

namespace EmuShelf.App.Services;

/// <summary>
/// Operations surfaced from the main library inside Settings. Returning the final
/// message lets the modal report the result without coupling it to MainViewModel.
/// </summary>
public sealed record LibraryMaintenanceActions(
    Func<string, IProgress<string>, Task<string>> RescanSystem,
    Func<IProgress<string>, Task<string>> RescanAll,
    Func<string, Task<string>>? FetchMetadataForSystem = null,
    Func<IProgress<MetadataEnrichmentProgress>, Task<string>>? FetchAllMetadata = null,
    Func<Task<string>>? SyncRpcs3Library = null,
    Func<bool>? GetShowEmptyPlatforms = null,
    Func<bool, Task>? SetShowEmptyPlatforms = null,
    LibraryFolderManagementActions? Folders = null,
    // EmuShelf's data root (database, covers, settings, saves), so Settings can reveal it in the
    // desktop file manager. Where this lives is platform-specific; see IAppPaths.
    string? DataDirectory = null);

/// <summary>Immediate database-only management of remembered recursive scan roots.</summary>
public sealed record LibraryFolderManagementActions(
    Func<string, IReadOnlyList<LibraryFolder>> Get,
    Func<string, string, Task<string>> Add,
    Func<string, long, string, Task<string>> Change,
    Func<string, long, Task<string>> Forget,
    // Reads every system's remembered folders in one database connection so opening Settings can seed
    // all rows off the UI thread, instead of opening one connection per system while it builds. Null
    // when a caller only supplies the per-system Get (tests), which keeps the old per-row read.
    Func<IReadOnlyList<LibraryFolder>>? GetAll = null);
