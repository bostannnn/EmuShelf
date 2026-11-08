using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Rendering;
using EmuShelf.Rendering.Models;
using EmuShelf.Rendering.Stage;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// Feeds the native Stage (M45 H4, option 5) from the Avalonia Stage, which stays laid out but invisible and
/// is only pictured: each piece (the centre art, the plate, the corner pills, the backdrop's layers) is drawn
/// once when it changes and posted to the native surface, which does all the motion on its own thread. The
/// game's medium is not pictured at all: the native Stage renders it, given the shell, the cover and the pose.
/// The Title page and the launch flight still draw on the Avalonia Stage: the feed hands the screen back while
/// a Title page is open.
/// </summary>
internal sealed class HandheldNativeStageFeed
{
    private const float CentreAreaHeight = 920;
    private const float WideMediumWidth = 1400;
    private const float MediumHeight = 820;

    private readonly Control _view;
    private readonly HandheldStageMotion _motion;
    private readonly IHandheldNativeStage _host;
    private readonly Control _canvas;
    private readonly Control _centre;
    private readonly Control _plate;
    private readonly HandheldFolderArt _folder;
    private readonly Control _heroBackdrop;
    private readonly Control _heroContent;
    private HandheldStageKind _lastKind;
    private bool _capturing;
    private FolderArt? _folderArt;
    private string? _folderArtKey;
    private FolderArtState? _folderStateSent;
    private readonly HandheldEraBackdrop _backdrop;
    private readonly (string Key, Control Piece, Func<HandheldShellViewModel, string> Content)[] _chrome;
    private readonly Dictionary<string, string> _chromeSent = new(StringComparer.Ordinal);
    private string? _plateSent;
    private StageImage? _plateImage;
    private readonly DispatcherTimer _backdropSettle;

    private HandheldShellViewModel? _shell;
    private HandheldStageViewModel? _stage;
    private bool _active;
    private bool _failed;
    private bool _centrePending;
    private bool _popPending;
    private bool _chromePending;
    private Bitmap? _sentCover;
    private int _coverVersion;

    public HandheldNativeStageFeed(Control view, HandheldStageMotion motion, IHandheldNativeStage host, Func<bool> isNarrow)
    {
        _isNarrow = isNarrow;
        _view = view;
        _motion = motion;
        _host = host;
        _canvas = Find("StageCanvas");
        _centre = Find("CentreShot");
        _plate = Find("PlateShot");
        _folder = (HandheldFolderArt)Find("StageFolder");
        _heroBackdrop = Find("HeroBackdrop");
        _heroContent = Find("HeroContent");
        _backdrop = (HandheldEraBackdrop)Find("EraBackdrop");
        // Each corner piece is pictured again only when what it says changes (a picture is a GPU round trip).
        _chrome =
        [
            ("status", Find("StatusShot"), s => $"{s.TimeText}|{s.BatteryText}|{s.IsCharging}|{s.WifiOpacity}|{s.HasWifiState}"),
            ("hints", Find("HintsShot"), s => string.Join("|", s.Hints.Select(h => $"{h.Button}:{h.Label}:{h.Icon}"))),
            ("achievements", Find("AchievementShot"), s => $"{s.Stage.ShowAchievementPill}|{s.Stage.AchievementText}"),
            ("message", Find("MessageShot"), s => $"{s.HasStatusMessage}|{s.StatusMessage}|{s.StatusGlyph}"),
        ];

        // The Avalonia Stage only lends its pieces now: its own backdrop never draws, and its medium neither
        // draws nor follows the pose (each pose would still ask Avalonia for a frame).
        var medium = Find("Medium");
        medium.IsVisible = false;
        medium.DataContext = null;
        _backdrop.IsVisible = false;

        _backdropSettle = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(HandheldMotion.BackdropSettleMilliseconds),
        };
        _backdropSettle.Tick += (_, _) =>
        {
            _backdropSettle.Stop();
            SendBackdrop();
        };
        // The hidden folder's flap never animates (an invisible transition still costs Avalonia frames): its
        // state changes are passed on, and the native Stage glides its pictured parts there.
        _folder.Classes.CollectionChanged += (_, _) => SendFolderState();
        view.AttachedToVisualTree += (_, _) => OnAttached();
        view.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty)
                OnViewVisibility();
        };
        host.Failed += () =>
        {
            _failed = true;
            // The Avalonia medium draws again (its binding was replaced while the native Stage drew it).
            var medium = Find("Medium");
            medium.ClearValue(StyledElement.DataContextProperty);
            medium.Bind(Visual.IsVisibleProperty, new Avalonia.Data.Binding("Stage.HasMedium"));
            UpdateActive();
        };
    }

    private Control Find(string name) =>
        _view.FindControl<Control>(name) ?? throw new InvalidOperationException($"The Stage has no {name}.");

    // 1920 on the 16:9 screen, 1240 on the narrow one (M45 H5).
    private float CanvasWidth => (float)(_isNarrow() ? HandheldShellViewModel.NarrowCanvasWidth : HandheldShellViewModel.WideCanvasWidth);

    private readonly Func<bool> _isNarrow;

    // A Stage kept for the other display (M45 H5) is hidden until a swap brings it in: it pictures nothing meanwhile,
    // and catches up as soon as it is shown again.
    private bool IsPaused => !_view.IsVisible;

    private bool _catchUpPending;
    private object? _backdropSent;
    private bool _backdropSentOnce;

    private void OnViewVisibility()
    {
        if (IsPaused || !_active)
            return;
        _catchUpPending = true;
        if (!_backdropSentOnce || !ReferenceEquals(_backdropSent, _stage?.Backdrop))
            SendBackdrop();
        if (_stage is { HasMedium: true } stage)
            _host.Post(new StageMediumPoseCommand((float)stage.MediumYaw, (float)stage.MediumPitch));
        _chromeSent.Clear();
        Schedule(centre: true, pop: false, chrome: true);
    }

    public void Attach(HandheldShellViewModel? shell)
    {
        if (ReferenceEquals(shell, _shell))
            return;
        if (_shell is { } old)
        {
            old.StageChanged -= OnStageChanged;
            old.PropertyChanged -= OnShellChanged;
            old.LaunchStarting -= OnLaunchStarting;
        }
        if (_stage is { } oldStage)
            oldStage.PropertyChanged -= OnStagePropertyChanged;
        _shell = shell;
        _stage = shell?.Stage;
        if (shell is null)
            return;
        shell.StageChanged += OnStageChanged;
        shell.PropertyChanged += OnShellChanged;
        shell.LaunchStarting += OnLaunchStarting;
        shell.Stage.PropertyChanged += OnStagePropertyChanged;
        UpdateActive();
    }

    // ---- Which Stage draws ----

    private void UpdateActive()
    {
        var active = !_failed && _shell is not null;
        if (active == _active)
            return;
        _active = active;
        _motion.IsOnScreen = !active;
        _view.Opacity = active ? 0 : 1;
        _folder.Classes.Set("frozen", active);
        _backdrop.IsVisible = !active;
        _host.SetActive(active);
        if (active)
        {
            SendBackdrop();
            Resend();
        }
    }

    /// <summary>Everything again from scratch: on activation, and after a swap moved the Stage to the other canvas.</summary>
    private void Resend()
    {
        // The canvas goes over with the pictures, in the flush, so the native Stage never maps old pictures on it.
        _resendPending = true;
        _sentCover = null;
        _chromeSent.Clear();
        _plateSent = null;
        _folderArtKey = null;
        // The backdrop is not resent: its pictures are 1920 wide on either screen (the narrow one shows their middle),
        // and the native side keeps them.
        Schedule(centre: true, pop: true, chrome: true);
    }

    // ---- Changes ----

    private void OnStageChanged() => Schedule(centre: true, pop: true, chrome: true);

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HandheldShellViewModel.TitlePage) when _shell?.TitlePage is null:
                // The page closed: a landed launch cover goes with it.
                _host.Post(new StageLaunchCommand(null));
                break;
            case nameof(HandheldShellViewModel.TimeText):
            case nameof(HandheldShellViewModel.BatteryText):
            case nameof(HandheldShellViewModel.IsCharging):
            case nameof(HandheldShellViewModel.WifiOpacity):
            case nameof(HandheldShellViewModel.HasWifiState):
            case nameof(HandheldShellViewModel.HasBattery):
            case nameof(HandheldShellViewModel.Hints):
            case nameof(HandheldShellViewModel.StatusMessage):
            case nameof(HandheldShellViewModel.HasStatusMessage):
            case nameof(HandheldShellViewModel.StatusGlyph):
                Schedule(centre: false, pop: false, chrome: true);
                break;
            case nameof(HandheldShellViewModel.IsSwapped) when _active && !IsPaused:
                // A Stage that follows the swap moved to the other canvas: every piece sits somewhere new.
                if (_view is HandheldStageView { PinnedNarrow: null })
                    Resend();
                break;
        }
    }

    private void OnStagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_active || IsPaused || _stage is not { } stage)
            return;
        switch (e.PropertyName)
        {
            case nameof(HandheldStageViewModel.Backdrop):
                _backdropSettle.Stop();
                _backdropSettle.Start();
                break;
            case nameof(HandheldStageViewModel.MediumPoseVersion):
                if (stage.HasMedium)
                    _host.Post(new StageMediumPoseCommand((float)stage.MediumYaw, (float)stage.MediumPitch, stage.MediumPoseIsSway));
                break;
            case nameof(HandheldStageViewModel.Art):
                if (stage.HasMedium)
                    SendCover(stage.Art, then: null);
                else
                    Schedule(centre: true, pop: false);
                break;
            case nameof(HandheldStageViewModel.PeekA):
            case nameof(HandheldStageViewModel.PeekB):
            case nameof(HandheldStageViewModel.PeekC):
            case nameof(HandheldStageViewModel.Fanart):
            case nameof(HandheldStageViewModel.Logo):
                Schedule(centre: true, pop: false, chrome: true);
                break;
            case nameof(HandheldStageViewModel.AchievementText):
                Schedule(centre: false, pop: false, chrome: true);
                break;
            case nameof(HandheldStageViewModel.PanelCard):
                // A panel's card changed in place (a switch flipped, a key typed, a badge or a search's covers arriving): pictured
                // again without the pop (a new subject pops through its title), with the hints, which change with it.
                Schedule(centre: true, pop: false, chrome: true);
                break;
            case nameof(HandheldStageViewModel.SettingVersion):
                // Settings' card changed in place (a switch flipped, a chooser opened on the same row, a key typed):
                // pictured again without the pop, with the hints, which change with it (only a new subject re-checks them).
                Schedule(centre: true, pop: false, chrome: true);
                break;
        }
    }

    /// <summary>Pictures are taken once layout has caught up with the change (the Loaded priority runs after layout).</summary>
    private void Schedule(bool centre, bool pop, bool chrome = false)
    {
        if (!_active || IsPaused)
            return;
        var first = !_centrePending && !_chromePending;
        _centrePending |= centre;
        _popPending |= pop;
        _chromePending |= chrome;
        if (first)
            Dispatcher.UIThread.Post(Flush, DispatcherPriority.Loaded);
    }

    private void Flush()
    {
        // Never picture a view that is between windows (a swap detaches it before attaching it to the other display):
        // its pieces would come from stale or stand-alone layout. What is pending waits for the attach (OnAttached).
        if (_active && (!_view.IsAttachedToVisualTree() || IsPaused))
            return;
        var centre = _centrePending;
        var pop = _popPending;
        var chrome = _chromePending;
        var resent = _resendPending;
        _centrePending = _popPending = _chromePending = _resendPending = false;
        if (!_active || _stage is not { } stage)
            return;
        if (resent)
            _host.Post(new StageCanvasCommand(CanvasWidth));
        if (centre)
            SendCentre(stage, pop);
        if (chrome && _shell is { } shell)
        {
            foreach (var (key, piece, content) in _chrome)
            {
                var now = content(shell);
                if (_chromeSent.TryGetValue(key, out var sent) && sent == now)
                    continue;
                _chromeSent[key] = now;
                _host.Post(new StageChromeCommand(key, HandheldStageShots.Shot(piece, _canvas)));
            }
        }
        if (resent || _catchUpPending)
        {
            _catchUpPending = false;
            CaughtUp?.Invoke();
        }
    }

    // A swap's pictures waited for the view to arrive in its new window; it has, and layout runs before Loaded.
    private void OnAttached()
    {
        if (_centrePending || _chromePending || _resendPending)
            Dispatcher.UIThread.Post(Flush, DispatcherPriority.Loaded);
    }

    // Set by Resend; the flush that pictures everything for the new canvas raises CaughtUp.
    private bool _resendPending;

    /// <summary>
    /// Raised after the first flush since this Stage was shown again or moved to another canvas: its native screen has
    /// every piece (a swap waits for it before sliding that screen in).
    /// </summary>
    public event Action? CaughtUp;

    // ---- Pieces ----

    private void SendCentre(HandheldStageViewModel stage, bool pop)
    {
        // A Title page crossfades in and out rather than popping from the centre, as on the Avalonia Stage.
        var fade = pop && (stage.Kind == HandheldStageKind.Hero || _lastKind == HandheldStageKind.Hero);
        if (pop)
            _lastKind = stage.Kind;
        if (fade)
            pop = false;
        if (stage.IsHeroArt)
        {
            var hero = new[] { HandheldStageShots.Shot(_heroBackdrop, _canvas), HandheldStageShots.Shot(_heroContent, _canvas) }
                .Where(image => image is not null).Cast<StageImage>().ToArray();
            _host.Post(new StageCentreCommand(null, null, null, pop, Hero: hero, FadeIn: fade));
            return;
        }
        // The same plate is the same picture (and the same texture on the other side).
        var plateKey = $"{stage.HasPlate}|{stage.Title}|{stage.Subtitle}";
        if (plateKey != _plateSent)
        {
            _plateSent = plateKey;
            _plateImage = HandheldStageShots.Shot(_plate, _canvas);
        }
        var plate = _plateImage;
        if (stage.HasMedium && stage.Medium is { } shell)
        {
            var accent = MediaShellRenderer.ToLinear(stage.MediumAccent.R / 255f, stage.MediumAccent.G / 255f, stage.MediumAccent.B / 255f);
            // The medium's box as the XAML lays it out: 1400 wide, or the narrow canvas's 1240 (M45 H5).
            var mediumWidth = Math.Min(WideMediumWidth, CanvasWidth);
            var medium = new StageMedium(shell, accent, (CanvasWidth - mediumWidth) / 2, (CentreAreaHeight - MediumHeight) / 2,
                mediumWidth, MediumHeight);
            _host.Post(new StageMediumPoseCommand((float)stage.MediumYaw, (float)stage.MediumPitch));
            // The new medium pops in only once its cover is ready, so it never shows the last game's label.
            SendCover(stage.Art, then: new StageCentreCommand(null, medium, plate, pop, FadeIn: fade));
            return;
        }
        if (stage.IsFolder && FolderArt() is { } folder)
        {
            var state = HandheldFolderArtShots.StateOf(_folder);
            _folderStateSent = state;
            _host.Post(new StageCentreCommand(null, null, plate, pop, folder, state, FadeIn: fade));
            return;
        }
        _host.Post(new StageCentreCommand(HandheldStageShots.Shot(_centre, _canvas), null, plate, pop, FadeIn: fade));
    }

    /// <summary>The big folder as glidable parts, captured again only when what it shows changes.</summary>
    private FolderArt? FolderArt()
    {
        if (_stage is not { } stage)
            return null;
        var key = $"{(stage.AccentBrush as Avalonia.Media.ISolidColorBrush)?.Color}|{stage.Monogram}|{Ref(stage.PeekA)}|{Ref(stage.PeekB)}|{Ref(stage.PeekC)}";
        if (key == _folderArtKey && _folderArt is not null)
            return _folderArt;
        _capturing = true;
        try
        {
            _folderArt = HandheldFolderArtShots.Capture(_folder, _canvas);
        }
        finally
        {
            _capturing = false;
        }
        _folderArtKey = _folderArt is null ? null : key;
        return _folderArt;
    }

    private void SendFolderState()
    {
        if (_capturing || !_active || IsPaused || _stage is not { IsFolder: true })
            return;
        var state = HandheldFolderArtShots.StateOf(_folder);
        if (state == _folderStateSent)
            return;
        _folderStateSent = state;
        _host.Post(new StageFolderStateCommand(state, Animate: !_folder.Classes.Contains("instant")));
    }

    private static string Ref(object? value) => value is null ? "-" : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The launch: the cover card lands on the top screen, then zooms into the emulator (native).</summary>
    private void OnLaunchStarting(HandheldTitlePageViewModel page)
    {
        if (!_active || IsPaused || page.Cover is not { } cover)
            return;
        var card = new Border
        {
            Width = 448,
            Height = 632,
            CornerRadius = new CornerRadius(28),
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.Parse("#D5D9E4")),
            BorderBrush = new SolidColorBrush(Color.Parse("#D9FFFFFF")),
            BorderThickness = new Thickness(8),
            Child = new Image { Source = cover, Stretch = Stretch.UniformToFill },
        };
        _host.Post(new StageLaunchCommand(HandheldStageShots.Picture(card, (CanvasWidth - 448) / 2f, (1080 - 632) / 2f, 448, 632),
            FromAbove: _isNarrow()));
    }

    private void SendBackdrop()
    {
        if (!_active)
            return;
        _backdropSent = _stage?.Backdrop;
        _backdropSentOnce = true;
        _host.Post(new StageBackdropCommand(HandheldEraBackdrop.RenderLayers(_stage?.Backdrop), _stage?.Backdrop?.Animated));
    }

    /// <summary>
    /// The medium's cover, in the straight-alpha RGBA the renderer uploads: the pixels are copied here (the
    /// bitmap may be released right after) and converted on the thread pool.
    /// </summary>
    private void SendCover(Bitmap? cover, StageCommand? then)
    {
        if (ReferenceEquals(cover, _sentCover))
        {
            if (then is not null)
                _host.Post(then);
            return;
        }
        _sentCover = cover;
        var version = ++_coverVersion;
        var size = cover?.PixelSize ?? default;
        if (cover is null || size.Width <= 0 || size.Height <= 0)
        {
            _host.Post(new StageMediumCoverCommand(null));
            if (then is not null)
                _host.Post(then);
            return;
        }
        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            cover.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }
        var swapRedAndBlue = cover.Format != PixelFormat.Rgba8888;
        var premultiplied = cover.AlphaFormat != AlphaFormat.Unpremul;
        Task.Run(() =>
        {
            for (var i = 0; i < pixels.Length; i += 4)
            {
                if (swapRedAndBlue)
                    (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
                var alpha = pixels[i + 3];
                if (premultiplied && alpha is > 0 and < 255)
                {
                    pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / alpha);
                    pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / alpha);
                    pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / alpha);
                }
            }
            if (version != _coverVersion)
                return; // a newer cover (or game) has been sent since
            _host.Post(new StageMediumCoverCommand(new TextureImage { Width = size.Width, Height = size.Height, Rgba = pixels }));
            if (then is not null)
                _host.Post(then);
        });
    }
}
