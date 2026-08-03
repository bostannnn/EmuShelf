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
            "Follows the operating system preference, resolving to Dark or Light.",
            IsDark: true,
            PreviewBackground: "#1E1E2A",
            PreviewSurface: "#2A2A38",
            PreviewAccent: "#5B58D9",
            PreviewText: "#E8E8F0"),
        new(
            ThemePreference.Dark,
            "Dark",
            "Neutral default dark theme. Low contrast surfaces, indigo accent, easy on the eyes for long sessions.",
            IsDark: true,
            PreviewBackground: "#1E1E2A",
            PreviewSurface: "#2A2A38",
            PreviewAccent: "#5B58D9",
            PreviewText: "#E8E8F0"),
        new(
            ThemePreference.Light,
            "Light",
            "Clean and airy with lavender-tinted whites and a soft violet accent.",
            IsDark: false,
            PreviewBackground: "#FFFFFF",
            PreviewSurface: "#F4F1FB",
            PreviewAccent: "#7C6CE0",
            PreviewText: "#1E1B2E"),
        new(
            ThemePreference.Oled,
            "OLED",
            "True black for OLED panels. Maximum contrast and reduced power draw.",
            IsDark: true,
            PreviewBackground: "#000000",
            PreviewSurface: "#0D0D0D",
            PreviewAccent: "#5B58D9",
            PreviewText: "#F0F0F5"),
        new(
            ThemePreference.Valentine,
            "Valentine",
            "Bright and romantic. Blush backgrounds with a saturated hot pink accent and lilac support.",
            IsDark: false,
            PreviewBackground: "#FFF5F9",
            PreviewSurface: "#FFE4EF",
            PreviewAccent: "#F0509A",
            PreviewText: "#3D1229"),
        new(
            ThemePreference.Dracula,
            "Dracula",
            "The classic developer palette. Muted slate-purple base with vivid pink, purple and green highlights.",
            IsDark: true,
            PreviewBackground: "#282A36",
            PreviewSurface: "#44475A",
            PreviewAccent: "#BD93F9",
            PreviewText: "#F8F8F2"),
        new(
            ThemePreference.Nord,
            "Nord",
            "Arctic and desaturated. Cool grey-blues with frost cyan, calm and low-saturation throughout.",
            IsDark: false,
            PreviewBackground: "#ECEFF4",
            PreviewSurface: "#D8DEE9",
            PreviewAccent: "#81A1C1",
            PreviewText: "#2E3440"),
        new(
            ThemePreference.Coffee,
            "Coffee",
            "Warm and cozy. Roasted brown surfaces with caramel and amber highlights.",
            IsDark: true,
            PreviewBackground: "#241A14",
            PreviewSurface: "#33251C",
            PreviewAccent: "#C8802D",
            PreviewText: "#F2E6DA"),
        new(
            ThemePreference.TokyoNight,
            "Tokyo Night",
            "Deep navy with soft neon blue. Moody but highly readable.",
            IsDark: true,
            PreviewBackground: "#1A1B26",
            PreviewSurface: "#24283B",
            PreviewAccent: "#7AA2F7",
            PreviewText: "#C0CAF5"),
        new(
            ThemePreference.Retro,
            "Retro",
            "Faded paper and 70s print. Warm cream base with muted coral and sage.",
            IsDark: false,
            PreviewBackground: "#F3E7D3",
            PreviewSurface: "#E8DCC6",
            PreviewAccent: "#E08A7D",
            PreviewText: "#3B2F22"),
        new(
            ThemePreference.Abyss,
            "Abyss",
            "Near-black teal with a single acid-green accent. Terminal-like and high focus.",
            IsDark: true,
            PreviewBackground: "#0A1512",
            PreviewSurface: "#16342C",
            PreviewAccent: "#9ECF00",
            PreviewText: "#DFF2E6"),
        new(
            ThemePreference.Cyberpunk,
            "Cyberpunk",
            "Loud and high-energy. Saturated yellow field with coral red and mint. Needs dark text everywhere.",
            IsDark: false,
            PreviewBackground: "#F7E733",
            PreviewSurface: "#FFEF5C",
            PreviewAccent: "#F4675F",
            PreviewText: "#1A1600"),
        new(
            ThemePreference.Aqua,
            "Aqua",
            "Saturated deep blue with a bright cyan accent. Glossy and underwater.",
            IsDark: true,
            PreviewBackground: "#0C2C8F",
            PreviewSurface: "#1A3FB0",
            PreviewAccent: "#16D3F0",
            PreviewText: "#E6F7FF"),
        new(
            ThemePreference.Palenight,
            "Palenight",
            "A softer, hazier Material-style cousin of Dracula. Muted indigo with pastel blue.",
            IsDark: true,
            PreviewBackground: "#292D3E",
            PreviewSurface: "#3A3F58",
            PreviewAccent: "#82AAFF",
            PreviewText: "#EEFFFF"),
        new(
            ThemePreference.Horizon,
            "Horizon",
            "Warm dark plum with a dusty rose-red accent. Softer than a pure neutral dark.",
            IsDark: true,
            PreviewBackground: "#1C1E26",
            PreviewSurface: "#2E303E",
            PreviewAccent: "#E95678",
            PreviewText: "#E0DEF4"),
        new(
            ThemePreference.Matrix,
            "Matrix",
            "A phosphor-green terminal: green text on black.",
            IsDark: true,
            PreviewBackground: "#020604",
            PreviewSurface: "#06180E",
            PreviewAccent: "#00FF66",
            PreviewText: "#7CFF9E"),
        new(
            ThemePreference.Synthwave,
            "Synthwave",
            "Retro-outrun purple night with hot magenta and cyan.",
            IsDark: true,
            PreviewBackground: "#1B0E33",
            PreviewSurface: "#2C1857",
            PreviewAccent: "#FF3CC8",
            PreviewText: "#F7E6FF"),
        new(
            ThemePreference.Sunset,
            "Sunset",
            "Warm ember dusk with a bright tangerine accent.",
            IsDark: true,
            PreviewBackground: "#2A1109",
            PreviewSurface: "#43231A",
            PreviewAccent: "#FF7A2E",
            PreviewText: "#FDEAE0"),
        new(
            ThemePreference.Everforest,
            "Everforest",
            "Soft, low-contrast woodland greens over a warm dark base. Cozy and gentle on the eyes.",
            IsDark: true,
            PreviewBackground: "#2D353B",
            PreviewSurface: "#343F44",
            PreviewAccent: "#A7C080",
            PreviewText: "#D3C6AA"),
        new(
            ThemePreference.Gruvbox,
            "Gruvbox",
            "Warm earthy retro-terminal palette with a bright orange accent and yellow, green and aqua highlights.",
            IsDark: true,
            PreviewBackground: "#282828",
            PreviewSurface: "#3C3836",
            PreviewAccent: "#FE8019",
            PreviewText: "#EBDBB2"),
        new(
            ThemePreference.CatppuccinMocha,
            "Catppuccin Mocha",
            "Soft pastel lavender, pink and blue on a muted purple-grey base. Gentle and low-contrast.",
            IsDark: true,
            PreviewBackground: "#1E1E2E",
            PreviewSurface: "#313244",
            PreviewAccent: "#CBA6F7",
            PreviewText: "#CDD6F4"),
        new(
            ThemePreference.Kanagawa,
            "Kanagawa",
            "Hokusai-inspired ink-blue surfaces with warm parchment text and a crystal-blue wave accent.",
            IsDark: true,
            PreviewBackground: "#1F1F28",
            PreviewSurface: "#2A2A37",
            PreviewAccent: "#7E9CD8",
            PreviewText: "#DCD7BA"),
    ];

    public static AppTheme Get(ThemePreference id) =>
        All.FirstOrDefault(theme => theme.Id == id) ?? All[0];
}
