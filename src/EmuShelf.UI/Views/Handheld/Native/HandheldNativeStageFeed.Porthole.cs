using Avalonia.Controls;
using Avalonia.Media;
using EmuShelf.App.ViewModels;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

// M47 B15 (proposals-screens.html option 12, the Porthole): the idle drift's artwork goes to the native Stage as its round window at rest,
// pictured once per game (the artwork cropped to fill the circle); the native Stage rises it, floats it, wipes in the next one and drops
// it from the shared maths.
internal sealed partial class HandheldNativeStageFeed
{
    private Avalonia.Media.Imaging.Bitmap? _portSent;
    private bool _portSentNarrow;

    /// <summary>
    /// The drift changing to another game's console: its backdrop's circle grows from behind the window (the page's <c>T.backdrop(..., 670)</c>),
    /// not from the Launcher's focus. Null for anything else.
    /// </summary>
    private float? DriftWipeOriginX() => HandheldTokens.IsBigType && _stage is { IsDrift: true }
        ? IsBigTypeNarrow ? (float)HandheldTokens.PortRect(narrow: true).Center.X : (float)HandheldTokens.PortWipeX
        : null;

    private bool OnPortholeProperty(HandheldStageViewModel stage, string? property)
    {
        if (property != nameof(HandheldStageViewModel.PortholeArt))
            return false;
        SendPorthole(stage, animate: true);
        return false;
    }

    /// <summary>Sends the window again (a resend after a swap puts it where the other canvas has it).</summary>
    private void ResendPorthole()
    {
        if (_stage is { } stage && (_portSent is not null || stage.PortholeArt is not null))
        {
            _portSent = null;
            SendPorthole(stage, animate: false);
        }
    }

    private void SendPorthole(HandheldStageViewModel stage, bool animate)
    {
        if (!_active || IsPaused || !HandheldTokens.IsBigType)
            return;
        var art = stage.PortholeArt;
        var narrow = IsBigTypeNarrow;
        if (ReferenceEquals(art, _portSent) && narrow == _portSentNarrow)
            return;
        _portSent = art;
        _portSentNarrow = narrow;
        if (art is null)
        {
            _host.Post(new StagePortholeCommand(null, animate));
            return;
        }
        var window = HandheldTokens.PortRect(narrow);
        var round = new Border
        {
            Width = window.Width,
            Height = window.Height,
            CornerRadius = new Avalonia.CornerRadius(window.Width / 2),
            ClipToBounds = true,
            Child = new Image { Source = art, Stretch = Stretch.UniformToFill },
        };
        _host.Post(new StagePortholeCommand(HandheldStageShots.Picture(round, (float)window.X, (float)window.Y, (float)window.Width, (float)window.Height), animate));
    }
}
