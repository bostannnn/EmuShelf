using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using EmuShelf.App.ViewModels;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// The photo viewer as the Reel (M47 B15, proposals-screens.html option 9, the owner's pick of 16 November 2026): the game's pictures side by
/// side, the one shown a fifth smaller than the screen (1000 × 562) with the next one already showing at either edge, smaller and faint.
/// Left and right slide the row (going round at the ends, as today); a video shows its poster under a play mark, and while it plays the
/// native video sits over the picture shown. Opening, the picture grows out of its small picture on the Photos tab; closing, it goes back
/// into it. Touch is today's: a sideways swipe pages, a downward swipe closes, a tap on the play mark plays, a tap on the picture hides the
/// type. Only the picture shown and one either side are decoded (the view model's). Numbers: tokens.js <c>screens.Reel…</c>, timing
/// <see cref="BigTypeScreensMotion"/>.
/// </summary>
public sealed class HandheldPhotoReel : Control
{
    private HandheldCompanionViewModel? _companion;
    private readonly BigTypeGlide _position = new(0f, BigTypeScreensMotion.ReelSlideMilliseconds, BigTypeScreensMotion.Soft);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private float _flipStart = float.NegativeInfinity;
    private bool _opening = true;
    private int _flipIndex;
    private int _flipThumb;
    private bool _ticking;

    public HandheldPhotoReel()
    {
        ClipToBounds = false;
        Focusable = false;
    }

    private float Now => (float)_clock.Elapsed.TotalMilliseconds;

    private bool Still => _companion?.HoldsStill == true || !HandheldRollingText.GetRolls(this);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_companion is { } old)
        {
            old.Model.PropertyChanged -= OnModelChanged;
            old.ReelPictures.CollectionChanged -= OnPicturesChanged;
        }
        _companion = DataContext as HandheldCompanionViewModel;
        if (_companion is { } companion)
        {
            companion.Model.PropertyChanged += OnModelChanged;
            companion.ReelPictures.CollectionChanged += OnPicturesChanged;
        }
        Request();
    }

    private void OnPicturesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is { } added)
            foreach (HandheldCompanionShotViewModel picture in added)
                picture.PropertyChanged += (_, _) => Request();
        Request();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_companion is not { IsPhotosLayer: true } companion || !_opening)
            return;
        if (e.PropertyName == nameof(SecondScreenViewModel.MediaIndex))
            _position.To(companion.Model.MediaIndex, Now, Still);
        if (e.PropertyName is nameof(SecondScreenViewModel.MediaIndex) or nameof(SecondScreenViewModel.IsVideoPlaying) or nameof(SecondScreenViewModel.ShowMediaPlay))
            Request();
    }

    /// <summary>The viewer opened: the picture shown grows out of its small picture on the Photos tab; the others are simply there.</summary>
    public void Open()
    {
        if (_companion is not { } companion)
            return;
        _opening = true;
        _position.Jump(companion.Model.MediaIndex);
        _flipIndex = companion.Model.MediaIndex;
        _flipThumb = companion.ReelThumbIndex;
        _flipStart = Still || !companion.IsGameRunning ? float.NegativeInfinity : Now;
        Request();
    }

    /// <summary>The viewer closed: the picture shown goes back into its small picture on the Photos tab.</summary>
    public void Close()
    {
        if (_companion is not { } companion)
            return;
        _opening = false;
        _flipIndex = (int)MathF.Round(_position.Target);
        _flipThumb = _flipIndex < companion.Shots.Count ? _flipIndex : 0;
        _flipStart = Still || !companion.IsGameRunning ? float.NegativeInfinity : Now;
        Request();
    }

    /// <summary>How long the closing takes.</summary>
    public static float CloseMilliseconds(bool still) => still ? 0f : BigTypeScreensMotion.PhotoCloseMilliseconds;

    /// <summary>The picture shown's box at rest on this control (the native video sits there while it plays).</summary>
    public Rect Shown => new((Bounds.Width - HandheldTokens.ReelWidth) / 2, HandheldTokens.ReelTop, HandheldTokens.ReelWidth, HandheldTokens.ReelHeight);

    // The Photos tab's small picture on the canvas (the tab's two rows of three), which the picture shown grows out of and goes back into.
    private static Rect Thumb(int index) => new(
        HandheldTokens.InGameShotLeft + ((index % HandheldTokens.InGameShotColumns) * HandheldTokens.InGameShotPitchX),
        HandheldTokens.InGameShotTop + ((index / HandheldTokens.InGameShotColumns) * HandheldTokens.InGameShotPitchY),
        HandheldTokens.InGameShotWidth, HandheldTokens.InGameShotHeight);

    public override void Render(DrawingContext context)
    {
        if (_companion is not { } companion)
            return;
        var pictures = companion.ReelPictures;
        if (pictures.Count == 0)
            return;
        var now = Now;
        var still = Still;
        var position = _position.At(now);
        var moving = !_position.Settled(now);
        var shown = Shown;
        var flip = BigTypeScreensMotion.PhotoFlip(_opening, now - _flipStart, still);
        moving |= _opening ? flip < 1f : flip > 0f;
        // Further ones first, the one shown last (on top).
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var picture in pictures)
            {
                var offset = picture.Index - position;
                var near = Math.Abs(offset) < 0.5f;
                if ((pass == 0) == near)
                    continue;
                var (x, scale, opacity) = BigTypeScreensMotion.ReelPose(offset);
                Rect box;
                if (picture.Index == _flipIndex && (flip < 1f || !_opening))
                {
                    // Growing out of (or going back into) its small picture on the Photos tab.
                    var thumb = Thumb(_flipThumb);
                    box = new Rect(thumb.X + ((shown.X - thumb.X) * flip), thumb.Y + ((shown.Y - thumb.Y) * flip),
                        thumb.Width + ((shown.Width - thumb.Width) * flip), thumb.Height + ((shown.Height - thumb.Height) * flip));
                    opacity = 1f;
                }
                else
                {
                    if (opacity <= 0f || !_opening)
                        continue;
                    var centre = new Point(shown.Center.X + x, shown.Center.Y);
                    box = new Rect(centre.X - (shown.Width * scale / 2), centre.Y - (shown.Height * scale / 2), shown.Width * scale, shown.Height * scale);
                }
                using (context.PushOpacity(opacity))
                    DrawPicture(context, picture, box, box.Width / shown.Width, playing: near && companion.Model.IsVideoPlaying);
            }
        }
        if (moving)
            Request();
    }

    private static void DrawPicture(DrawingContext context, HandheldCompanionShotViewModel picture, Rect box, double scale, bool playing)
    {
        var shape = new RoundedRect(box, HandheldTokens.ReelRadius * scale);
        using (context.PushClip(shape))
        {
            if (picture.Image is { } image)
                context.DrawImage(image, Cover(image, box), box);
            else
                context.FillRectangle(HandheldTokens.TileRest, box);
            if (!picture.IsVideo || playing)
                return;
            // A video: its poster darkened under a play mark (the native video covers it while it plays).
            context.FillRectangle(HandheldTokens.ReelVideoShadeBrush, box);
            var mark = HandheldTokens.ReelPlayMark(box.Width);
            var left = box.Center.X - (mark * 0.42);
            var top = box.Center.Y - (mark / 2);
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(left + (mark * 0.12), top + (mark * 0.04)), true);
                g.LineTo(new Point(left + (mark * 0.96), top + (mark * 0.5)));
                g.LineTo(new Point(left + (mark * 0.12), top + (mark * 0.96)));
                g.EndFigure(true);
            }
            context.DrawGeometry(HandheldTokens.OnColour, null, geometry);
        }
    }

    // The part of a picture that fills its box (UniformToFill, centred).
    private static Rect Cover(Bitmap image, Rect box)
    {
        var size = image.Size;
        var k = Math.Max(box.Width / size.Width, box.Height / size.Height);
        var w = box.Width / k;
        var h = box.Height / k;
        return new Rect((size.Width - w) / 2, (size.Height - h) / 2, w, h);
    }

    private void Request()
    {
        if (_ticking || TopLevel.GetTopLevel(this) is not { } top)
        {
            if (!_ticking)
                InvalidateVisual();
            return;
        }
        _ticking = true;
        top.RequestAnimationFrame(_ =>
        {
            _ticking = false;
            InvalidateVisual();
        });
    }

    // ---- Touch: today's (a sideways swipe pages, a downward swipe closes, a tap on the mark plays, a tap on the picture hides the type) ----

    private Point? _press;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _press = _companion?.Model.IsVideoPlaying == true ? null : e.GetPosition(this);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_press is not { } start || _companion?.Model is not { } model)
            return;
        _press = null;
        var end = e.GetPosition(this);
        var delta = end - start;
        if (Math.Abs(delta.X) >= 8 || Math.Abs(delta.Y) >= 8)
        {
            model.SwipeMedia(delta.X, delta.Y);
            return;
        }
        var shown = Shown;
        var mark = HandheldTokens.ReelPlayMark(shown.Width);
        if (model.ShowMediaPlay && Math.Abs(end.X - shown.Center.X) <= mark && Math.Abs(end.Y - shown.Center.Y) <= mark)
        {
            model.PlayMediaCommand.Execute(null);
            return;
        }
        model.TapMedia();
    }
}
