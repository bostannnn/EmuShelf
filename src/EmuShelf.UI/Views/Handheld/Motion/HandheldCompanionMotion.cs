using System;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using EmuShelf.App.ViewModels;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// The Big Type companion's motion (M47 B8, the mock's launch, playTab, playWake and endPlay), from <see cref="BigTypeLaunchMotion"/>: its
/// parts (Now playing, the title, the tabs, the tab's body, the bar) rise in one after another when the game starts and drop out when it
/// ends; a tab's body slides in from the side moved to; the bar stretches to the achievements' share; the whole rests to
/// <c>InGameRestOpacity</c> and wakes. On this view's frame clock while any of it moves; held still (the device), each is simply there.
/// The view model never knows about any of it.
/// </summary>
internal sealed class HandheldCompanionMotion
{
    private readonly Control _view;
    private readonly Control _body;
    private readonly Control[] _parts;
    private readonly Control _session;
    private readonly Control _achievements;
    private readonly Control _photos;
    private readonly Control _bar;
    private readonly Control _barFill;
    private HandheldCompanionViewModel? _companion;
    private bool _ticking;

    private float _barFrom;
    private float _barTo;
    private float _barNow;
    private long _barSince;
    private float _restFrom = 1f;
    private float _restNow = 1f;
    private bool _resting;
    private long _restSince;

    // The mock's playEl children, in order: the index each part's stagger counts from.
    private const int KickerIndex = 0;
    private const int TitleIndex = 1;
    private const int TabsIndex = 2;
    private const int SessionIndex = 3;
    private const int AchievementsIndex = 4;
    private const int PhotosIndex = 5;
    private const int BarIndex = 6;

    public HandheldCompanionMotion(Control view)
    {
        _view = view;
        _body = Find("BtBody");
        _session = Find("BtSession");
        _achievements = Find("BtAchievements");
        _photos = Find("BtPhotos");
        _bar = Find("BtBar");
        _barFill = Find("BtBarFill");
        _parts = [Find("BtKicker"), Find("BtTitle"), Find("BtTabs")];
        _bar.SizeChanged += (_, _) => Request();
    }

    private Control Find(string name) =>
        _view.FindControl<Control>(name) ?? throw new InvalidOperationException($"The companion has no {name}.");

    public void Attach(HandheldCompanionViewModel? companion)
    {
        if (_companion is { } old)
            old.PropertyChanged -= OnChanged;
        _companion = companion;
        if (companion is null)
            return;
        companion.PropertyChanged += OnChanged;
        _barNow = _barFrom = _barTo = (float)companion.ProgressRatio;
        _resting = companion.IsResting;
        _restNow = _restFrom = _resting ? (float)HandheldTokens.InGameRestOpacity : 1f;
        Request();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_companion is not { } companion || !HandheldTokens.IsBigType)
            return;
        switch (e.PropertyName)
        {
            case nameof(HandheldCompanionViewModel.ProgressRatio):
                _barFrom = _barNow;
                _barTo = (float)companion.ProgressRatio;
                _barSince = Stopwatch.GetTimestamp();
                break;
            case nameof(HandheldCompanionViewModel.IsResting):
                _restFrom = _restNow;
                _resting = companion.IsResting;
                _restSince = Stopwatch.GetTimestamp();
                break;
            case nameof(HandheldCompanionViewModel.ArrivedSince):
            case nameof(HandheldCompanionViewModel.LayerChangedSince):
            case nameof(HandheldCompanionViewModel.IsLeaving):
            case nameof(HandheldCompanionViewModel.TabChangedSince):
            case nameof(HandheldCompanionViewModel.ShowsBigTypeBody):
                break;
            default:
                return;
        }
        Request();
    }

    private void Request()
    {
        if (_ticking || TopLevel.GetTopLevel(_view) is not { } top)
        {
            if (!_ticking)
                Step();
            return;
        }
        _ticking = true;
        top.RequestAnimationFrame(_ =>
        {
            _ticking = false;
            if (Step())
                Request();
        });
    }

    private static float Since(long start) => start == 0 ? float.PositiveInfinity : (float)Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    /// <summary>Poses every part for now; true while any of them still moves.</summary>
    private bool Step()
    {
        if (_companion is not { } companion || !HandheldTokens.IsBigType)
            return false;
        var still = companion.HoldsStill;
        var moving = false;
        var leaving = companion.IsLeaving;
        var arrived = Since(companion.ArrivedSince);
        var left = Since(companion.LeavingSince);

        // M47 B15: a layer over the running game (the Shelf, the Wall, the Reel): the parts drop out under it (the page's C.hide()).
        var covered = companion.HasLayer;
        var coveredFor = Since(companion.LayerChangedSince);

        (float Offset, float Opacity) PartPose(int index)
        {
            if (leaving)
                return BigTypeLaunchMotion.PartOut(index, left, still);
            if (covered)
                return BigTypeLaunchMotion.PartOut(index, coveredFor, still);
            return BigTypeLaunchMotion.PartIn(index, arrived, still);
        }

        for (var i = 0; i < _parts.Length; i++)
            Pose(_parts[i], 0, PartPose(i).Offset, PartPose(i).Opacity);
        var tab = BigTypeLaunchMotion.TabIn(companion.TabDirection, Since(companion.TabChangedSince), still);
        foreach (var (body, index) in new[] { (_session, SessionIndex), (_achievements, AchievementsIndex), (_photos, PhotosIndex) })
        {
            var part = PartPose(index);
            Pose(body, tab.Offset, part.Offset, part.Opacity * tab.Opacity);
        }
        var bar = PartPose(BarIndex);
        Pose(_bar, 0, bar.Offset, bar.Opacity);
        moving |= leaving || covered ? (leaving ? left : coveredFor) < BigTypeLaunchMotion.PartOutMilliseconds + (BarIndex * BigTypeLaunchMotion.PartOutStagger)
            : arrived < BigTypeLaunchMotion.PartsInTotal(BarIndex + 1);
        moving |= tab.Offset != 0f || tab.Opacity < 1f;

        // The bar stretches to the share unlocked (tokens.js SessionAchievementBar).
        _barNow = BigTypeLaunchMotion.Bar(_barFrom, _barTo, Since(_barSince), still);
        var width = Math.Max(0, _bar.Bounds.Width);
        _barFill.Width = Math.Round(Math.Clamp(_barNow, 0f, 1.2f) * width, 1);
        moving |= _barNow != _barTo;

        // Resting: the whole fades to InGameRestOpacity (tokens.js InGameRest), and back on a press.
        _restNow = BigTypeLaunchMotion.RestOpacity(_restFrom, _resting, Since(_restSince), (float)HandheldTokens.InGameRestOpacity, still);
        _body.Opacity = Math.Round(_restNow, 3);
        moving |= _restNow != (_resting ? (float)HandheldTokens.InGameRestOpacity : 1f);
        return moving;
    }

    private static void Pose(Control control, double x, double y, double opacity)
    {
        x = Math.Round(x, 2);
        y = Math.Round(y, 2);
        control.RenderTransform = x == 0 && y == 0 ? null : new TranslateTransform(x, y);
        control.Opacity = Math.Round(Math.Clamp(opacity, 0, 1), 3);
    }
}
