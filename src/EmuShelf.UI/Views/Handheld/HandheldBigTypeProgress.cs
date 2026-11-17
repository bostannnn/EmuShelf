using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// A panel's progress in the Big Type look (M47 B7): a flat bar <see cref="HandheldTokens.ProgressHeight"/> high, its track a tile's
/// white (<see cref="HandheldTokens.TileRest"/>) and what is done in white, both fully rounded. <see cref="Value"/> is 0 to 1.
/// </summary>
public sealed class HandheldBigTypeProgress : Control
{
    public static readonly StyledProperty<double?> ValueProperty =
        AvaloniaProperty.Register<HandheldBigTypeProgress, double?>(nameof(Value));

    static HandheldBigTypeProgress() => AffectsRender<HandheldBigTypeProgress>(ValueProperty);

    public double? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        var radius = bounds.Height / 2;
        context.DrawRectangle(HandheldTokens.TileRest, null, bounds, radius, radius);
        var done = Math.Clamp(Value ?? 0, 0, 1) * bounds.Width;
        if (done > 0)
            context.DrawRectangle(HandheldTokens.OnColour, null, new Rect(0, 0, Math.Max(done, bounds.Height), bounds.Height), radius, radius);
    }
}
