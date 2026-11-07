namespace EmuShelf.Core.Settings;

/// <summary>Library interaction layout; controller input itself is supplied by Steam Input.</summary>
public enum InterfaceMode
{
    Desktop,
    Gamepad,

    /// <summary>
    /// The dual-screen home menu (M45): a launcher on the bottom screen and the selection shown large
    /// on the top. Offered only where <see cref="IInterfaceModeService.SupportsHandheldMode"/> is true
    /// (an Android handheld with a presentation display); a desktop head never runs it.
    /// </summary>
    Handheld,
}
