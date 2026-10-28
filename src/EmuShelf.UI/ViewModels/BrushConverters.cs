using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EmuShelf.App.ViewModels;

/// <summary>View-only brush shaping helpers for bindings.</summary>
public static class BrushConverters
{
    /// <summary>
    /// Turns a solid surface brush into a top-to-bottom fade from transparent to that colour: the soft edge
    /// where a scrolled list runs under the surface it sits on. Bound to the surface's own Background, so it
    /// follows every palette. A plain gradient on purpose — an OpacityMask would force an offscreen layer on
    /// every scrolled frame, which the Thor pays for (DECISIONS 2026-08-31).
    /// </summary>
    public static readonly IValueConverter FadeInFromTransparent =
        new FuncValueConverter<IBrush?, IBrush?>(static brush =>
        {
            if (brush is not ISolidColorBrush solid)
                return null;
            var color = solid.Color;
            var opaque = Color.FromArgb((byte)(color.A * solid.Opacity), color.R, color.G, color.B);
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0),
                    new GradientStop(opaque, 1),
                },
            };
        });
}
