using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using EmuShelf.App.Controls;

namespace EmuShelf.App.Tests;

public sealed class MarqueeTextBlockTests
{
    [AvaloniaFact]
    public void MarksOverflow_OnlyWhenTheTextIsWiderThanItsSlot()
    {
        var marquee = new MarqueeTextBlock { FontSize = 42, FontWeight = FontWeight.Bold, Text = "X-Men" };

        // A short title with plenty of room stays static.
        marquee.Measure(new Size(800, 120));
        marquee.Arrange(new Rect(0, 0, 800, 120));
        Assert.False(marquee.IsOverflowing);

        // A long title in a narrow slot overflows, so it will scroll.
        marquee.Text = "Teenage Mutant Ninja Turtles: Turtles in Time (World, Deluxe Edition)";
        marquee.Measure(new Size(240, 120));
        marquee.Arrange(new Rect(0, 0, 240, 120));
        Assert.True(marquee.IsOverflowing);
    }

    [AvaloniaFact]
    public void TextAlignment_DefaultsToStart_AndForwardsToInnerText()
    {
        var marquee = new MarqueeTextBlock { Text = "Okami" };
        var inner = Assert.IsType<TextBlock>(marquee.Child);

        // Control-neutral default (left/start), so reuse outside the hero is not surprised.
        Assert.Equal(TextAlignment.Start, marquee.TextAlignment);

        // The spotlight hero sets Center; it forwards to the inner text so a fitting title centres.
        marquee.TextAlignment = TextAlignment.Center;
        Assert.Equal(TextAlignment.Center, inner.TextAlignment);
    }

    [AvaloniaFact]
    public async Task Scrolling_AnAttachedOverflowingTitle_DoesNotThrow()
    {
        // A long title in a shown window is attached, visible and overflowing, so it starts its
        // scroll during layout. Regression: driving that scroll via Animation.RunAsync against a bare
        // TranslateTransform threw (InvalidCastException) here and crashed the render pass.
        var marquee = new MarqueeTextBlock
        {
            FontSize = 42,
            FontWeight = FontWeight.Bold,
            Text = "Teenage Mutant Ninja Turtles: Turtles in Time (World, Deluxe Edition)",
            Width = 240,
            Height = 72,
        };
        var window = new Window { Width = 320, Height = 220, Content = marquee };
        window.Show();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            Assert.True(marquee.IsOverflowing);
        }
        finally
        {
            window.Close();
        }
    }
}
