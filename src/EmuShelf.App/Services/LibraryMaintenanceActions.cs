namespace EmuShelf.App.Services;

/// <summary>
/// Operations surfaced from the main library inside Settings. Returning the final
/// message lets the modal report the result without coupling it to MainViewModel.
/// </summary>
public sealed record LibraryMaintenanceActions(
    Func<string, Task<string>> RescanSystem,
    Func<Task<string>> RescanAll);
