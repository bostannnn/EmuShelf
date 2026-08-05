using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
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
}
