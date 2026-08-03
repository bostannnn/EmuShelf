namespace EmuShelf.Core.Settings;

/// <summary>
/// A selectable appearance. <see cref="System"/>, <see cref="Light"/>, and <see cref="Dark"/> are the
/// base variants; the remaining members are complete built-in palettes. Serialized as a string so the
/// portable settings file stays human-readable and older files (System/Light/Dark) keep parsing.
/// A user-importable <c>Themes/</c> format is a later phase; see ROADMAP M31 Phase 4.
/// </summary>
public enum ThemePreference
{
    System,
    Light,
    Dark,
    Oled,
    Cyberpunk,
    Nord,
    Valentine,
    Dracula,
    Coffee,
    TokyoNight,
    Retro,
    Abyss,
    Aqua,
    Palenight,
    Horizon,
    Matrix,
    Synthwave,
    Sunset,
}
