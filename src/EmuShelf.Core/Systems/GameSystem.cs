namespace EmuShelf.Core.Systems;

/// <summary>
/// A game system (console) known to EmuShelf. Instances are defined by the
/// Integrations project; the rest of the app treats them as opaque data.
/// </summary>
public sealed record GameSystem(
    string Id,
    string Name,
    string ShortName,
    string AccentColor);
