namespace EmuShelf.App.ViewModels;

/// <summary>
/// Which region of the Gamepad Start menu owns the focus ring. The two selector rows sit above the
/// option list, so the values are ordered top→bottom (<see cref="ViewMode"/> highest); Up/Down walk
/// between them. Replaced the single "view-mode row focused" bool when the sort row was added.
/// </summary>
public enum GamepadMenuFocusRegion
{
    Options,
    Sort,
    ViewMode,
}
