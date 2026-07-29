namespace EmuShelf.Core.SaveSync;

/// <summary>
/// The logical identity of one independently syncable save, decoupled from where its bytes live
/// on a particular machine. A PCSX2 file memory card is one unit; each per-game subfolder of a
/// PCSX2 folder memory card is its own unit keyed by the game serial.
/// </summary>
/// <param name="UnitId">
/// Stable identifier shared across machines (e.g. <c>pcsx2/Mcd001.ps2</c> or
/// <c>pcsx2/folder/SLUS-20552</c>). It is a portable key, never an absolute path, so the same
/// save resolves to the same unit on Windows and on a Steam Deck Flatpak layout.
/// </param>
/// <param name="DisplayName">Human-readable label for status and conflict messages.</param>
/// <param name="Kind">Whether the unit is a single file, directory, or allow-listed file set.</param>
public sealed record SaveUnit(string UnitId, string DisplayName, SaveUnitKind Kind);
