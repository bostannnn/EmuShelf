using Avalonia.Media;

namespace EmuShelf.App.ViewModels;

/// <summary>Vector glyphs for the Handheld chrome (no emoji: device fonts cannot be relied on for them).</summary>
public static class HandheldIcons
{
    public static readonly Geometry Wifi = Geometry.Parse(
        "M12 21l3.6-4.8a6 6 0 0 0-7.2 0zM1 9l2 2c5-5 13-5 18 0l2-2C16.9 2.9 7.1 2.9 1 9zm4 4l2 2c2.8-2.8 7.2-2.8 10 0l2-2c-3.9-3.9-10.1-3.9-14 0z");

    public static readonly Geometry Charging = Geometry.Parse("M7 2v11h3v9l7-12h-4l4-8z");

    public static readonly Geometry Trophy = GamepadOptionIcons.Achievements;

    public static readonly Geometry AllApps = Geometry.Parse(
        "M4 4h4v4H4zm6 0h4v4h-4zm6 0h4v4h-4zM4 10h4v4H4zm6 0h4v4h-4zm6 0h4v4h-4zM4 16h4v4H4zm6 0h4v4h-4zm6 0h4v4h-4z");

    public static readonly Geometry Menu = Geometry.Parse("M3 6h18v2H3zm0 5h18v2H3zm0 5h18v2H3z");

    public static readonly Geometry Back = Geometry.Parse("M20 11H7.83l5.59-5.59L12 4l-8 8 8 8 1.41-1.41L7.83 13H20z");

    public static readonly Geometry Plus = Geometry.Parse("M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6z");

    public static readonly Geometry Sort = Geometry.Parse("M3 18h6v-2H3v2zM3 6v2h18V6H3zm0 7h12v-2H3v2z");

    public static readonly Geometry Play = GamepadOptionIcons.Launch;

    /// <summary>A disc: a ring with a hole (even-odd fill).</summary>
    public static readonly Geometry Disc = Geometry.Parse(
        "F0 M12 2a10 10 0 1 0 0 20a10 10 0 1 0 0-20zM12 9a3 3 0 1 1 0 6a3 3 0 1 1 0-6z");

    /// <summary>Your folders' flap glyph (decision 7: a glyph, not a monogram, so they read as yours).</summary>
    public static readonly Geometry Star = Geometry.Parse("M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z");

    /// <summary>The Recent folder's flap glyph.</summary>
    public static readonly Geometry Clock = Geometry.Parse(
        "F0 M12 2a10 10 0 1 1 0 20 10 10 0 0 1 0-20zm0 2a8 8 0 1 0 0 16 8 8 0 0 0 0-16zm1 3v4.59l3.2 3.2-1.41 1.42L11 12.41V7z");

    /// <summary>
    /// Settings (decision 18): three sliders, each with its knob at a different place. Non-zero fill (F1), every shape
    /// drawn clockwise, so a knob covers its bar instead of cutting a hole where they overlap.
    /// </summary>
    public static readonly Geometry Settings = Geometry.Parse(
        "F1 M3 5.2h18v1.6H3zM8 3a3 3 0 1 1 0 6a3 3 0 1 1 0-6zM3 11.2h18v1.6H3zM16 9a3 3 0 1 1 0 6a3 3 0 1 1 0-6z" +
        "M3 17.2h18v1.6H3zM10 15a3 3 0 1 1 0 6a3 3 0 1 1 0-6z");

    public static readonly Geometry ChevronLeft = Geometry.Parse("M15.41 7.41L14 6l-6 6 6 6 1.41-1.41L10.83 12z");

    public static readonly Geometry ChevronRight = Geometry.Parse("M8.59 16.59L10 18l6-6-6-6-1.41 1.41L13.17 12z");

    public static readonly Geometry Check = Geometry.Parse("M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z");

    /// <summary>The companion's Photos tab: a picture with a sun over two hills.</summary>
    public static readonly Geometry Photo = Geometry.Parse(
        "F0 M3 4h18a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1zm1 2v12h16V6zm2 10l4-5 3 3.5 2-2.5 4 4z" +
        "M16.5 7.5a1.5 1.5 0 1 1 0 3a1.5 1.5 0 1 1 0-3z");

    /// <summary>Two stacked screens with the top one filled: the Thor's top (main) display.</summary>
    public static readonly Geometry ScreenTop = Geometry.Parse("F0 M4 2h16v9H4zM4 13h16v9H4zM6 15h12v5H6z");

    /// <summary>Two stacked screens with the bottom one filled: the Thor's bottom (second) display.</summary>
    public static readonly Geometry ScreenBottom = Geometry.Parse("F0 M4 2h16v9H4zM6 4h12v5H6zM4 13h16v9H4z");
}

/// <summary>Small view-only mappings for the Handheld views.</summary>
public static class HandheldViewConverters
{
    private static readonly IBrush Selector = new SolidColorBrush(Color.Parse("#5B58D9"));
    private static readonly IBrush Dot = new SolidColorBrush(Color.Parse("#38465088"));
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#1E2235"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#5B6176"));

    /// <summary>A <see cref="HandheldGlyph"/> as the vector the views draw.</summary>
    public static readonly Avalonia.Data.Converters.IValueConverter GlyphGeometry =
        new Avalonia.Data.Converters.FuncValueConverter<HandheldGlyph, Geometry?>(glyph => glyph switch
        {
            HandheldGlyph.AllApps => HandheldIcons.AllApps,
            HandheldGlyph.Menu => HandheldIcons.Menu,
            HandheldGlyph.Plus => HandheldIcons.Plus,
            HandheldGlyph.Star => HandheldIcons.Star,
            HandheldGlyph.Clock => HandheldIcons.Clock,
            HandheldGlyph.Back => HandheldIcons.Back,
            HandheldGlyph.Sort => HandheldIcons.Sort,
            HandheldGlyph.Settings => HandheldIcons.Settings,
            _ => null,
        });

    /// <summary>A platform summary's chevron turns down while its rows are open beneath it.</summary>
    public static readonly Avalonia.Data.Converters.IValueConverter ExpandedTurn =
        new Avalonia.Data.Converters.FuncValueConverter<bool, ITransform>(expanded => new RotateTransform(expanded ? 90 : 0));

    public static readonly Avalonia.Data.Converters.IValueConverter FocusZIndex =
        new Avalonia.Data.Converters.FuncValueConverter<bool, int>(focused => focused ? 1 : 0);

    public static readonly Avalonia.Data.Converters.IValueConverter DotWidth =
        new Avalonia.Data.Converters.FuncValueConverter<bool, double>(current => current ? 52 : 18);

    public static readonly Avalonia.Data.Converters.IValueConverter DotBrush =
        new Avalonia.Data.Converters.FuncValueConverter<bool, IBrush>(current => current ? Selector : Dot);

    public static readonly Avalonia.Data.Converters.IValueConverter UnavailableOpacity =
        new Avalonia.Data.Converters.FuncValueConverter<bool, double>(unavailable => unavailable ? 0.4 : 1);

    public static readonly Avalonia.Data.Converters.IValueConverter MenuRowBrush =
        new Avalonia.Data.Converters.FuncValueConverter<bool, IBrush>(focused => focused ? Selector : Brushes.Transparent);

    public static readonly Avalonia.Data.Converters.IValueConverter MenuRowNote =
        new Avalonia.Data.Converters.FuncValueConverter<bool, IBrush>(focused => focused ? Brushes.White : Muted);

    public static readonly Avalonia.Data.Converters.IValueConverter MenuRowText =
        new Avalonia.Data.Converters.FuncValueConverter<bool, IBrush>(focused => focused ? Brushes.White : Ink);
}

/// <summary>The companion's own mappings (M45 H6b): white dots on its dark ground.</summary>
public static class HandheldCompanionConverters
{
    private static readonly IBrush CurrentDot = Brushes.White;
    private static readonly IBrush Dot = new SolidColorBrush(Color.Parse("#4DFFFFFF"));

    public static readonly Avalonia.Data.Converters.IValueConverter DotWidth =
        new Avalonia.Data.Converters.FuncValueConverter<bool, double>(current => current ? 40 : 14);

    public static readonly Avalonia.Data.Converters.IValueConverter DotBrush =
        new Avalonia.Data.Converters.FuncValueConverter<bool, IBrush>(current => current ? CurrentDot : Dot);
}
