using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using EmuShelf.App.ViewModels;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// The companion's Big Type layers coming and going (M47 B15, proposals-screens.html): a layer that opens fades in as a whole while its
/// grid, wall or reel does its own arrival and its name lands; one that closes does its own leaving while it fades, and is hidden once both
/// are done. The hints at the foot rise in whenever they change and, with the viewer's type, fade on a tap that hides it. Held still (the
/// device, or the app's Reduce motion) everything is simply there or gone. The view model never knows about any of it.
/// </summary>
internal sealed class HandheldScreensMotion
{
    private readonly Control _view;
    private readonly Panel _shelf;
    private readonly Panel _wall;
    private readonly Panel _reel;
    private readonly Control _reelChrome;
    private readonly ItemsControl _hints;
    private readonly HandheldShelfGrid _shelfGrid;
    private readonly HandheldBadgeWall _badgeWall;
    private readonly HandheldPhotoReel _photoReel;
    private readonly HandheldScreenHeading _shelfName;
    private readonly HandheldScreenHeading _wallName;
    private HandheldCompanionViewModel? _companion;
    private HandheldCompanionLayer _shown;
    private (Panel Panel, long Since, float Milliseconds)? _leaving;
    private long _shownSince;
    private long _hintsSince;
    private float _chromeFrom = 1f;
    private float _chromeNow = 1f;
    private long _chromeSince;
    private bool _ticking;

    public HandheldScreensMotion(Control view)
    {
        _view = view;
        _shelf = Find<Panel>("Shelf");
        _wall = Find<Panel>("Wall");
        _reel = Find<Panel>("Reel");
        _reelChrome = Find<Control>("ReelChrome");
        _hints = Find<ItemsControl>("ScreenHints");
        _shelfGrid = Find<HandheldShelfGrid>("ShelfGrid");
        _badgeWall = Find<HandheldBadgeWall>("BadgeWall");
        _photoReel = Find<HandheldPhotoReel>("PhotoReel");
        _shelfName = Find<HandheldScreenHeading>("ShelfName");
        _wallName = Find<HandheldScreenHeading>("WallName");
        _hints.ContainerPrepared += (_, _) => Request();
    }

    private T Find<T>(string name) where T : Control =>
        _view.FindControl<T>(name) ?? throw new InvalidOperationException($"The companion's layers have no {name}.");

    private bool Still => _companion?.HoldsStill == true;

    public void Attach(HandheldCompanionViewModel? companion)
    {
        if (_companion is { } old)
        {
            old.PropertyChanged -= OnChanged;
            old.Model.PropertyChanged -= OnModelChanged;
        }
        _companion = companion;
        if (companion is null)
            return;
        companion.PropertyChanged += OnChanged;
        companion.Model.PropertyChanged += OnModelChanged;
        HandheldRollingText.SetRolls(_view, !companion.HoldsStill);
        // The look is read now, not when the view was built (on the Thor the companion's views are built before the look is set).
        Follow(companion.Layer, first: true);
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_companion is not { } companion || !HandheldTokens.IsBigType)
            return;
        switch (e.PropertyName)
        {
            case nameof(HandheldCompanionViewModel.Layer):
                Follow(companion.Layer, first: false);
                break;
            case nameof(HandheldCompanionViewModel.HoldsStill):
                HandheldRollingText.SetRolls(_view, !companion.HoldsStill);
                break;
            case nameof(HandheldCompanionViewModel.ScreenHints):
                WatchHints();
                break;
        }
    }

    private INotifyCollectionChanged? _watchedHints;

    // The hints change (a hidden badge focused, a video playing): the row rises in again.
    private void WatchHints()
    {
        var hints = _companion?.ScreenHints;
        if (!ReferenceEquals(hints, _watchedHints))
        {
            if (_watchedHints is { } old)
                old.CollectionChanged -= OnHintsChanged;
            _watchedHints = hints;
            if (hints is not null)
                hints.CollectionChanged += OnHintsChanged;
        }
        _hintsSince = Stopwatch.GetTimestamp();
        Request();
    }

    private void OnHintsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _hintsSince = Stopwatch.GetTimestamp();
        Request();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SecondScreenViewModel.IsMediaChromeVisible) || _companion is not { } companion)
            return;
        _chromeFrom = _chromeNow;
        _chromeSince = Stopwatch.GetTimestamp();
        Request();
    }

    private Panel? PanelOf(HandheldCompanionLayer layer) => layer switch
    {
        HandheldCompanionLayer.Drawer => _shelf,
        HandheldCompanionLayer.Achievements => _wall,
        HandheldCompanionLayer.Photos => _reel,
        _ => null,
    };

    private void Follow(HandheldCompanionLayer layer, bool first)
    {
        _view.IsVisible = HandheldTokens.IsBigType;
        if (!HandheldTokens.IsBigType || layer == _shown && !first)
            return;
        var still = Still || first;
        var old = PanelOf(_shown);
        // The layer that was in front does its own leaving while it fades.
        if (old is not null && !first)
        {
            var milliseconds = _shown switch
            {
                HandheldCompanionLayer.Drawer => Close(_shelfGrid.Close, HandheldShelfGrid.CloseMilliseconds(Still)),
                HandheldCompanionLayer.Achievements => Close(_badgeWall.Close, _badgeWall.CloseMilliseconds(Still)),
                HandheldCompanionLayer.Photos => Close(_photoReel.Close, HandheldPhotoReel.CloseMilliseconds(Still)),
                _ => 0f,
            };
            if (_leaving is { } unfinished && !ReferenceEquals(unfinished.Panel, old))
                unfinished.Panel.IsVisible = false;
            _leaving = Still ? null : (old, Stopwatch.GetTimestamp(), Math.Max(milliseconds, BigTypeScreensMotion.LayerFadeMilliseconds));
            if (Still)
                old.IsVisible = false;
        }
        _shown = layer;
        // From the native Launcher the companion replaces the entire display. Paint the Shelf's
        // ground immediately, even if the newly exposed surface is still waiting for a frame.
        // Its icons and name keep their entrance. Over a running game the whole layer fades in.
        var fadeIn = !still && _companion?.IsGameRunning == true;
        _shownSince = fadeIn ? Stopwatch.GetTimestamp() : 0;
        if (PanelOf(layer) is { } panel)
        {
            if (_leaving is { } leaving && ReferenceEquals(leaving.Panel, panel))
                _leaving = null;
            panel.IsVisible = true;
            panel.Opacity = fadeIn ? 0 : 1;
            switch (layer)
            {
                case HandheldCompanionLayer.Drawer:
                    _shelfGrid.Open();
                    _shelfName.Arrive();
                    break;
                case HandheldCompanionLayer.Achievements:
                    _badgeWall.Open();
                    _wallName.Arrive();
                    break;
                case HandheldCompanionLayer.Photos:
                    _photoReel.Open();
                    _chromeFrom = _chromeNow = 1f;
                    _chromeSince = 0;
                    break;
            }
        }
        WatchHints();
        Request();
    }

    private static float Close(Action close, float milliseconds)
    {
        close();
        return milliseconds;
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

    /// <summary>Poses the layers and the hints for now; true while any of them still moves.</summary>
    private bool Step()
    {
        if (_companion is not { } companion || !HandheldTokens.IsBigType)
            return false;
        var still = Still;
        var moving = false;
        if (PanelOf(_shown) is { } panel)
        {
            var since = Since(_shownSince);
            panel.Opacity = Math.Round(BigTypeScreensMotion.LayerOpacity(true, since, still), 3);
            moving |= panel.Opacity < 1;
        }
        if (_leaving is { } leaving)
        {
            var since = Since(leaving.Since);
            leaving.Panel.Opacity = Math.Round(BigTypeScreensMotion.LayerOpacity(false, since, still), 3);
            if (still || since >= leaving.Milliseconds)
            {
                leaving.Panel.IsVisible = false;
                _leaving = null;
            }
            else
            {
                moving = true;
            }
        }
        // The viewer's type and the hints fade on a tap that hides them (the page's .bare).
        var chromeShown = _shown != HandheldCompanionLayer.Photos || companion.Model.IsMediaChromeVisible;
        _chromeNow = BigTypeScreensMotion.Chrome(_chromeFrom, chromeShown, Since(_chromeSince), still);
        _reelChrome.Opacity = Math.Round(_chromeNow, 3);
        _hints.Opacity = _reelChrome.Opacity;
        moving |= _chromeNow != (chromeShown ? 1f : 0f);
        // Each hint rises in, 50 ms after the one before.
        var hintsSince = Since(_hintsSince);
        var index = 0;
        foreach (var container in _hints.GetRealizedContainers().OrderBy(c => _hints.IndexFromContainer(c)))
        {
            var (rise, opacity) = BigTypeScreensMotion.HintIn(index++, hintsSince, still);
            container.RenderTransform = rise == 0f ? null : new TranslateTransform(0, Math.Round(rise, 2));
            container.Opacity = Math.Round(opacity, 3);
            moving |= opacity < 1f;
        }
        return moving;
    }
}
