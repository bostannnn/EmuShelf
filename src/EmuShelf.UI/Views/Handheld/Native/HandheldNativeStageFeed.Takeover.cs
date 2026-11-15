using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using Avalonia.Controls;
using Avalonia.Threading;
using EmuShelf.App.ViewModels;
using EmuShelf.Rendering.Stage;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

// The launch's takeover on the native Stage (M47 B8): the console's colour and the title's letters go over once the Avalonia launch
// layer has laid the title out, timed from the press; the status line is pictured each time it changes (and rolls there); the takeover
// fades when the game ends and goes when the shelf is back.
internal sealed partial class HandheldNativeStageFeed
{
    private HandheldPlayViewModel? _play;
    private BigTypeLetters? _takeoverTitle;
    private string? _statusSent;

    private void OnPlayingChanged()
    {
        if (!HandheldTokens.IsBigType || _shell is not { } shell)
            return;
        var play = shell.Playing;
        if (ReferenceEquals(play, _play))
            return;
        if (_play is { } old)
            old.PropertyChanged -= OnPlayChanged;
        _play = play;
        _takeoverTitle = null;
        _statusSent = null;
        if (play is null)
        {
            if (_active)
            {
                _host.Post(new StageTakeoverStatusCommand(null, null));
                _host.Post(new StageTakeoverCommand(null, null, 0f));
            }
            return;
        }
        play.PropertyChanged += OnPlayChanged;
        // The launch layer lays its title out on the next layout pass; the takeover goes over once it has.
        Dispatcher.UIThread.Post(() => SendTakeover(play), DispatcherPriority.Loaded);
    }

    private void OnPlayChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not HandheldPlayViewModel play || !ReferenceEquals(play, _play))
            return;
        switch (e.PropertyName)
        {
            case nameof(HandheldPlayViewModel.Status):
                Dispatcher.UIThread.Post(() => SendTakeoverStatus(play), DispatcherPriority.Loaded);
                break;
            case nameof(HandheldPlayViewModel.IsEnding) when play.IsEnding && _active && !IsPaused:
                _host.Post(new StageTakeoverCommand(Colour(play), _takeoverTitle, 0f, Ending: true, Animate: _shell?.HoldsStill != true,
                    Origin: TakeoverOrigin));
                break;
        }
    }

    // The narrow Stage's takeover grows from its own case (M47 B11); null is the wide Stage's.
    private Vector2? TakeoverOrigin => IsBigTypeNarrow && HandheldTokens.StageTakeoverCentre(narrow: true) is var at
        ? new Vector2((float)at.X, (float)at.Y)
        : null;

    private static Vector3 Colour(HandheldPlayViewModel play) => new(play.Colour.R / 255f, play.Colour.G / 255f, play.Colour.B / 255f);

    private void SendTakeover(HandheldPlayViewModel play)
    {
        if (!_active || IsPaused || !ReferenceEquals(play, _play))
            return;
        if (_view.FindControl<HandheldBigTypeLaunchTitle>("BtLaunchTitle") is { } title &&
            HandheldStageShots.ToCanvas(title, default, _canvas) is { } at)
            _takeoverTitle = title.Letters(at);
        var elapsed = (float)Stopwatch.GetElapsedTime(play.Started).TotalMilliseconds;
        _host.Post(new StageTakeoverCommand(Colour(play), _takeoverTitle, elapsed, Ending: play.IsEnding, Animate: _shell?.HoldsStill != true,
            Origin: TakeoverOrigin));
        SendTakeoverStatus(play);
    }

    private void SendTakeoverStatus(HandheldPlayViewModel play)
    {
        if (!_active || IsPaused || !ReferenceEquals(play, _play) || play.Status == _statusSent ||
            _view.FindControl<Avalonia.Controls.Control>("BtLaunchStatusShot") is not { } shot ||
            _view.FindControl<Avalonia.Controls.Control>("BtLaunchStatus") is not { } status)
            return;
        _statusSent = play.Status;
        _host.Post(new StageTakeoverStatusCommand(HandheldStageShots.Shot(shot, _canvas), HandheldTextCells.Of(status, _canvas)));
    }
}
