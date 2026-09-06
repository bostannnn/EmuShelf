namespace EmuShelf.App.ViewModels;

/// <summary>
/// Whatever lists <see cref="GamepadSettingsRowViewModel"/>s and receives their taps: the couch Settings
/// projection, and the pre-boot setup wizard that has no settings model behind it. Rows only need the
/// host to move focus to them and run their action.
/// </summary>
internal interface IGamepadSettingsRowHost
{
    Task FocusAndActivateAsync(GamepadSettingsRowViewModel row);
}
