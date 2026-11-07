using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// Draws a bitmap stretched over its bounds, whatever the bitmap's DPI. The Stage's outgoing-focus picture is
/// rendered at half resolution (a lower DPI), which <see cref="Image"/> would draw at the wrong scale.
/// </summary>
public sealed class HandheldPicture : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<HandheldPicture, Bitmap?>(nameof(Source));

    static HandheldPicture()
    {
        AffectsRender<HandheldPicture>(SourceProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<HandheldPicture>(false);
    }

    public Bitmap? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    public override void Render(DrawingContext context)
    {
        if (Source is not { } bitmap || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        // The source rectangle is in pixels.
        context.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), new Rect(Bounds.Size));
    }
}
