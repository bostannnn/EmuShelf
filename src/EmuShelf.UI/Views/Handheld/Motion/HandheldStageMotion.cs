using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// The top screen's motion (HANDHELD-MODE.md Motion spec): every change of focus grows out of the centre
/// (the old art shrinks away as a picture while the new one springs up from 0.72×, the plate pops with it),
/// the big folder's flap tilts after a beat so its covers peek out and swings open with the folder, a Title
/// page crossfades in, and a launch lands the cover from below and zooms into the emulator. All compositor
/// animations on fixed-size wrappers, scale and opacity only.
/// </summary>
internal sealed class HandheldStageMotion
{
    private const double CanvasWidth = 1920;
    private const double CanvasHeight = 1080;

    private static readonly HandheldSpringEasing PopEasing =
        new(HandheldMotion.Pop, HandheldMotion.PopFromScale, 1);

    private readonly Control _view;
    private readonly Control _content;
    private readonly Control _centre;
    private readonly Control _plate;
    private readonly HandheldPicture _ghost;
    private readonly Control _heroBackdrop;
    private readonly Control _folder;
    private readonly Border _fly;
    private readonly Image _flyImage;

    private HandheldShellViewModel? _shell;
    private HandheldStageKind _outgoingKind;
    private int _change;
    private int _launch;
    private string? _closingFolder;

    public HandheldStageMotion(Control view)
    {
        _view = view;
        _content = Find<Control>("StageContent");
        _centre = Find<Control>("CentreWrap");
        _plate = Find<Control>("PlateWrap");
        _ghost = Find<HandheldPicture>("StageGhost");
        _heroBackdrop = Find<Control>("HeroBackdrop");
        _folder = Find<Control>("StageFolder");
        _fly = Find<Border>("LaunchFly");
        _flyImage = Find<Image>("LaunchFlyImage");
    }

    private T Find<T>(string name) where T : Control =>
        _view.FindControl<T>(name) ?? throw new InvalidOperationException($"The Stage has no {name}.");

    public void Attach(HandheldShellViewModel? shell)
    {
        if (ReferenceEquals(shell, _shell))
            return;
        if (_shell is { } old)
        {
            old.StageChanging -= OnStageChanging;
            old.StageChanged -= OnStageChanged;
            old.LaunchStarting -= OnLaunchStarting;
            old.PropertyChanged -= OnShellChanged;
            old.FolderClosing -= OnFolderClosing;
        }
        _shell = shell;
        if (shell is null)
            return;
        shell.StageChanging += OnStageChanging;
        shell.StageChanged += OnStageChanged;
        shell.LaunchStarting += OnLaunchStarting;
        shell.PropertyChanged += OnShellChanged;
        shell.FolderClosing += OnFolderClosing;
        UpdateFolderState(peekNow: true);
    }

    // ---- Focus change: from the centre, never from the side ----

    private void OnStageChanging()
    {
        _outgoingKind = _shell?.Stage.Kind ?? HandheldStageKind.None;
        // Picture what is on screen now; the picture shrinks away while the new art grows in its place.
        // The 3D medium is a GL surface a RenderTargetBitmap cannot capture; it simply gives way.
        var size = _centre.Bounds.Size;
        if (_outgoingKind == HandheldStageKind.None || _shell?.Stage.HasMedium == true ||
            !_view.IsEffectivelyVisible || size.Width <= 0 || size.Height <= 0)
        {
            return;
        }
        try
        {
            // At the canvas's own resolution: a lower-DPI render misplaces text.
            var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)));
            bitmap.Render(_centre);
            SetGhost(bitmap);
        }
        catch (Exception)
        {
            SetGhost(null); // no picture: the old art simply gives way
        }
    }

    private void OnStageChanged()
    {
        if (_shell is not { } shell)
            return;
        var change = ++_change;
        var kind = shell.Stage.Kind;
        var crossfade = kind == HandheldStageKind.Hero || _outgoingKind == HandheldStageKind.Hero;
        var centre = new Point(CanvasWidth / 2, (CanvasHeight - 160) / 2);

        if (_ghost.Source is not null)
        {
            _ghost.IsVisible = true;
            var outMs = crossfade ? 300 : HandheldMotion.OutgoingMilliseconds;
            if (!crossfade)
                HandheldComposition.ScaleAbout(_ghost, centre, outMs, new CubicEaseIn(), 0, (0f, 1), (1f, HandheldMotion.OutgoingScale));
            HandheldComposition.Opacity(_ghost, 1, 0, outMs, crossfade ? new CubicEaseInOut() : new CubicEaseIn());
            DispatcherTimer.RunOnce(() =>
            {
                if (change == _change)
                    SetGhost(null);
            }, TimeSpan.FromMilliseconds(outMs + 40));
        }

        if (crossfade)
        {
            HandheldComposition.Opacity(_content, 0, 1, 340, new CubicEaseInOut());
            HandheldComposition.Opacity(_heroBackdrop, 0, 1, 340, new CubicEaseInOut());
        }
        else
        {
            // The centre wrapper fills the canvas above the plate, so its middle is where the art sits.
            HandheldComposition.ScaleAbout(_centre, centre, PopEasing.Duration.TotalMilliseconds, new LinearEasingAdapter(PopEasing), 0,
                (0f, HandheldMotion.PopFromScale), (1f, 1));
            HandheldComposition.Opacity(_centre, 0, 1, HandheldMotion.PopFadeMilliseconds, new CubicEaseOut());
            var plateCentre = new Point(CanvasWidth / 2, CanvasHeight - 140);
            HandheldComposition.ScaleAbout(_plate, plateCentre, HandheldMotion.PlatePopMilliseconds, HandheldMotion.PopCurve(), 0,
                (0f, HandheldMotion.PlateFromScale), (1f, 1));
            HandheldComposition.Opacity(_plate, 0, 1, HandheldMotion.PlatePopMilliseconds, new CubicEaseOut());
            HandheldComposition.Opacity(_content, 1, 1, 1);
        }
        UpdateFolderState(peekNow: false);
    }

    private void SetGhost(RenderTargetBitmap? bitmap)
    {
        var previous = _ghost.Source as IDisposable;
        _ghost.Source = bitmap;
        _ghost.IsVisible = false;
        previous?.Dispose();
    }

    // ---- The big folder: the flap peeks after a beat, opens with the folder ----

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HandheldShellViewModel.OpenFolder):
                UpdateFolderState(peekNow: true);
                break;
            case nameof(HandheldShellViewModel.TitlePage) when _shell?.TitlePage is null:
                // The page's cover is disposed right after this: take it off the Stage first.
                EndLaunch();
                break;
        }
    }

    // A closing folder comes back to the Stage still open, from the centre, and closes a beat later.
    private void OnFolderClosing(HandheldFolderViewModel folder) => _closingFolder = folder.Name;

    private void UpdateFolderState(bool peekNow)
    {
        if (_shell is not { } shell)
            return;
        if (_closingFolder is { } closingName && !peekNow)
        {
            _closingFolder = null;
            if (shell.Stage.IsFolder && !shell.IsFolderOpen && shell.Stage.Title == closingName)
            {
                _folder.Classes.Set("instant", true);
                _folder.Classes.Set("open", true);
                _folder.Classes.Set("peek", true);
                var closing = _change;
                DispatcherTimer.RunOnce(() =>
                {
                    _folder.Classes.Set("instant", false);
                    if (closing == _change)
                        _folder.Classes.Set("open", false);
                }, TimeSpan.FromMilliseconds(HandheldMotion.StageFlapCloseDelayMilliseconds));
                return;
            }
        }
        var open = shell.IsFolderOpen && shell.Stage.IsFolder;
        _folder.Classes.Set("open", open);
        if (open || peekNow)
        {
            _folder.Classes.Set("peek", shell.Stage.IsFolder);
            return;
        }
        _folder.Classes.Set("peek", false);
        var change = _change;
        DispatcherTimer.RunOnce(() =>
        {
            if (change == _change && _shell?.Stage.IsFolder == true)
                _folder.Classes.Set("peek", true);
        }, TimeSpan.FromMilliseconds(HandheldMotion.FlapPeekDelayMilliseconds));
    }

    // ---- Launch: the cover lands from the Launcher's side and zooms into the emulator ----

    private void OnLaunchStarting(HandheldTitlePageViewModel page)
    {
        var launch = ++_launch;
        // No decoded cover yet: no flight (an empty card landing would read as a fault).
        if (page.Cover is null)
            return;
        _flyImage.Source = page.Cover;
        _fly.IsVisible = true;
        // The Launcher sits below the Stage (until H5's swap), so the cover arrives from the bottom edge.
        HandheldComposition.OffsetFrom(_fly, new Vector(0, 760), HandheldMotion.LaunchLandMilliseconds, HandheldMotion.PopCurve(),
            HandheldMotion.LaunchLandDelayMilliseconds);
        HandheldComposition.Scale(_fly, 0.45, 1, HandheldMotion.LaunchLandMilliseconds, HandheldMotion.PopCurve(), HandheldMotion.LaunchLandDelayMilliseconds);
        HandheldComposition.Opacity(_fly, HandheldMotion.LaunchLandMilliseconds, new LinearEasing(), HandheldMotion.LaunchLandDelayMilliseconds,
            (0f, 0), (0.25f, 1), (1f, 1));
        DispatcherTimer.RunOnce(() =>
        {
            if (launch != _launch || !_fly.IsVisible)
                return;
            HandheldComposition.Scale(_fly, 1, HandheldMotion.LaunchZoomScale, HandheldMotion.LaunchZoomMilliseconds, new CubicEaseIn());
            HandheldComposition.Opacity(_fly, 1, 0, HandheldMotion.LaunchZoomMilliseconds, new CubicEaseIn());
            DispatcherTimer.RunOnce(() =>
            {
                if (launch == _launch)
                    EndLaunch();
            }, TimeSpan.FromMilliseconds(HandheldMotion.LaunchZoomMilliseconds + 40));
        }, HandheldMotion.LaunchLead);
    }

    private void EndLaunch()
    {
        _launch++;
        _fly.IsVisible = false;
        _flyImage.Source = null;
    }

    /// <summary>Plays a baked spring curve on a key frame whose own progress runs linearly.</summary>
    private sealed class LinearEasingAdapter(Easing curve) : Easing
    {
        public override double Ease(double progress) => curve.Ease(progress);
    }
}
