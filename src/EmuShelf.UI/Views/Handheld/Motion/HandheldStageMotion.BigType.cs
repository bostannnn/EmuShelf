using System;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using EmuShelf.App.ViewModels;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

// The Big Type Stage's launch and messages on the Avalonia Stage (M47 B8), from the maths the native Stage shares (BigTypeLaunchMotion):
// the console's colour taking the Stage over through a circle from the case, the case bumping as the tile arrives, the takeover fading
// when the game ends, and an error shaking the line. Each frame on this view's clock while one runs; held still, each is simply there.
internal sealed partial class HandheldStageMotion
{
    private bool _btTicking;
    private long? _shakeStart;
    private HandheldPlayViewModel? _watchedPlay;

    private bool BigTypeStill => _shell?.HoldsStill == true || !_onScreen;

    private void OnBigTypeShellChanged(string? property)
    {
        if (!HandheldTokens.IsBigType)
            return;
        switch (property)
        {
            case nameof(HandheldShellViewModel.Playing):
                WatchPlay(_shell?.Playing);
                RequestBigTypeFrame();
                break;
            case nameof(HandheldShellViewModel.LineShakes):
                _shakeStart = Stopwatch.GetTimestamp();
                RequestBigTypeFrame();
                break;
            case nameof(HandheldShellViewModel.HoldsStill):
                // The model view (M47 B12) stands at rest, or gets going again.
                RequestModelFrame();
                break;
        }
    }

    private void WatchPlay(HandheldPlayViewModel? play)
    {
        if (_watchedPlay is { } old)
            old.PropertyChanged -= OnPlayChanged;
        _watchedPlay = play;
        if (play is not null)
            play.PropertyChanged += OnPlayChanged;
    }

    private void OnPlayChanged(object? sender, PropertyChangedEventArgs e) => RequestBigTypeFrame();

    private void RequestBigTypeFrame()
    {
        if (_btTicking || TopLevel.GetTopLevel(_view) is not { } top)
        {
            if (!_btTicking)
                StepBigType();
            return;
        }
        _btTicking = true;
        top.RequestAnimationFrame(_ =>
        {
            _btTicking = false;
            if (StepBigType())
                RequestBigTypeFrame();
        });
    }

    private static float Since(long start) => (float)Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    /// <summary>Poses the launch layer, the case and the line for now; true while any of them still moves.</summary>
    private bool StepBigType()
    {
        var moving = false;
        var still = BigTypeStill;
        var launch = _view.FindControl<Control>("BtLaunch");
        var medium = _view.FindControl<Control>("BtMedium");
        var cover = _view.FindControl<Control>("BtCoverShot");
        if (_shell?.Playing is { } play && launch is not null)
        {
            var since = Since(play.Started);
            var radius = BigTypeLaunchMotion.TakeoverRadiusAt(since, still);
            // M47 B12: from the model view's medium, or from the name's middle (there is no case beside it any more); M47 B11's narrow
            // case corner otherwise.
            var origin = TakeoverOrigin();
            launch.Clip = radius >= BigTypeLaunchMotion.TakeoverRadius ? null
                : new EllipseGeometry(new Rect(origin.X - radius, origin.Y - radius, radius * 2, radius * 2));
            var fade = play.IsEnding ? BigTypeLaunchMotion.LaunchFade(Since(play.EndingSince), still) : 1f;
            launch.Opacity = Math.Round(fade, 3);
            var (scale, rise) = BigTypeLaunchMotion.CaseBump(since, still);
            Bump(medium, scale, rise);
            Bump(cover, scale, rise);
            moving |= since < BigTypeLaunchMotion.StageTotal(play.Title.Length, still) || (play.IsEnding && fade > 0f);
        }
        else
        {
            if (launch is not null)
            {
                launch.Clip = null;
                launch.Opacity = 1;
            }
            Bump(medium, 1, 0);
            Bump(cover, 1, 0);
        }
        if (_shakeStart is { } shook && _view.FindControl<Control>("BtLineShot") is { } line)
        {
            var since = Since(shook);
            var x = BigTypeLaunchMotion.ShakeAt(since, still);
            line.RenderTransform = x == 0f ? null : new TranslateTransform(x, 0);
            if (since >= BigTypeLaunchMotion.ShakeMilliseconds || still)
                _shakeStart = null;
            else
                moving = true;
        }
        return moving;
    }

    // The case bumps about its middle and rises (the mock's scale(1.16) translateY(-10px)).
    private static void Bump(Control? control, float scale, float rise)
    {
        if (control is null)
            return;
        control.RenderTransform = scale == 1f && rise == 0f ? null
            : new MatrixTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(0, -rise));
    }
}
