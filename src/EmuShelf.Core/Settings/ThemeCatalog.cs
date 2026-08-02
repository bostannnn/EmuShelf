namespace EmuShelf.Core.Settings;

/// <summary>
/// Presentation metadata for a selectable <see cref="ThemePreference"/>: its display name, whether it
/// reads as a dark palette, and four representative swatch colors for the theme gallery. Colors are
/// plain hex strings so Core stays free of any UI-framework dependency; they mirror the corresponding
/// palette dictionary in <c>Styles/Palettes</c>.
/// </summary>
public sealed record AppTheme(
    ThemePreference Id,
    string Name,
    string Description,
    bool IsDark,
    string PreviewBackground,
    string PreviewSurface,
    string PreviewAccent,
    string PreviewText);

/// <summary>
/// The ordered set of built-in themes offered in both Desktop Settings and the controller
/// theme gallery. A single catalog keeps the two surfaces in lock-step (see the Desktop/Gamepad
/// parity rule): a theme added here appears in both modes without further wiring.
/// </summary>
public static class ThemeCatalog
{
    public static IReadOnlyList<AppTheme> All { get; } =
    [
        new(
            ThemePreference.System,
            "System",
            "Follow the operating system's light or dark setting.",
            IsDark: true,
            PreviewBackground: "#1D1D24",
            PreviewSurface: "#F2F1F4",
            PreviewAccent: "#F15C93",
            PreviewText: "#F2F0F3"),
        new(
            ThemePreference.Light,
            "Light",
            "A bright, low-contrast daytime palette.",
            IsDark: false,
            PreviewBackground: "#EEEDEF",
            PreviewSurface: "#FFFFFF",
            PreviewAccent: "#D23A76",
            PreviewText: "#252328"),
        new(
            ThemePreference.Dark,
            "Dark",
            "The default calm dark palette.",
            IsDark: true,
            PreviewBackground: "#19191B",
            PreviewSurface: "#2C2C2F",
            PreviewAccent: "#F15C93",
            PreviewText: "#F2F0F3"),
        new(
            ThemePreference.Nord,
            "Nord",
            "Muted arctic blues with a frost accent.",
            IsDark: true,
            PreviewBackground: "#2E3440",
            PreviewSurface: "#3B4252",
            PreviewAccent: "#88C0D0",
            PreviewText: "#ECEFF4"),
        new(
            ThemePreference.Oled,
            "OLED",
            "True black for OLED panels, with an electric-blue accent.",
            IsDark: true,
            PreviewBackground: "#000000",
            PreviewSurface: "#121215",
            PreviewAccent: "#5A8CFF",
            PreviewText: "#F4F5F7"),
        new(
            ThemePreference.Cyberpunk,
            "Cyberpunk",
            "Deep violet with neon magenta and cyan.",
            IsDark: true,
            PreviewBackground: "#0F0A1E",
            PreviewSurface: "#1E1338",
            PreviewAccent: "#FF3FA4",
            PreviewText: "#F6ECFF"),
    ];

    public static AppTheme Get(ThemePreference id) =>
        All.FirstOrDefault(theme => theme.Id == id) ?? All[0];
}
