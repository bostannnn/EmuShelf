namespace EmuShelf.App.Services;

/// <summary>
/// Operations surfaced from the main library inside Settings. Returning the final
/// message lets the modal report the result without coupling it to MainViewModel.
/// </summary>
public sealed record LibraryMaintenanceActions(
    Func<string, Task<string>> RescanSystem,
    Func<Task<string>> RescanAll,
    Func<string, Task<string>>? FetchMetadataForSystem = null,
    Func<IProgress<MetadataEnrichmentProgress>, Task<string>>? FetchAllMetadata = null,
    Func<Task<string>>? SyncRpcs3Library = null,
    Func<bool>? GetShowEmptyPlatforms = null,
    Func<bool, Task>? SetShowEmptyPlatforms = null);
