using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using EmuShelf.App.Views.Handheld;
using EmuShelf.Rendering.Stage;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

/// <summary>The native screens glide a folder's parts to exactly where the XAML's states put them.</summary>
public class HandheldFolderArtShotsTests
{
    private sealed class ArtContext
    {
        public IBrush AccentBrush { get; } = Brushes.SlateBlue;
        public string Monogram { get; } = "DS";
        public object? PeekA => null;
        public object? PeekB => null;
        public object? PeekC => null;
    }

    [AvaloniaFact]
    public void TheFlap_HalvesAboutItsBottomEdge_WhenOpen_AndStillPartsStayPut()
    {
        var art = new HandheldFolderArt { Width = 416, Height = 416, DataContext = new ArtContext() };
        var canvas = new Canvas { Width = 800, Height = 600, Children = { art } };
        Canvas.SetLeft(art, 100);
        Canvas.SetTop(art, 50);
        var window = new Window { Content = canvas, Width = 800, Height = 600 };
        window.Show();

        var parts = HandheldFolderArtShots.Capture(art, canvas)!.Parts;

        // Shadow and back plate (still), then the flap (no covers decoded here).
        Assert.Equal(3, parts.Count);
        Assert.Equal(StageAffine.Identity, parts[0].Open);
        var flap = parts[^1];
        Assert.Equal(1f, flap.Closed.M22, 3);
        Assert.Equal(0.86f, flap.Peek.M22, 3);
        Assert.Equal(0.5f, flap.Open.M22, 3);
        // The bottom edge does not move: a point on it maps to itself in every state.
        var bottom = flap.Image.Y + flap.Image.Height;
        var moved = (bottom * flap.Open.M22) + flap.Open.M32;
        Assert.Equal(bottom, moved, 1);
        // The art was put back as it was.
        Assert.DoesNotContain("open", art.Classes);
        Assert.DoesNotContain("peek", art.Classes);
        Assert.DoesNotContain("frozen", art.Classes);
        window.Close();
    }
}
