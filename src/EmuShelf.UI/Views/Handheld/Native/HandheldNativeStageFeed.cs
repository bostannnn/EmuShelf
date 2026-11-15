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
using EmuShelf.Rendering.Stage.Backdrops;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// Feeds the native Stage (M45 H4, option 5) from the Avalonia Stage, which stays laid out but invisible and
/// is only pictured: each piece (the centre art, the plate, the corner pills, the backdrop's layers) is drawn
/// once when it changes and posted to the native surface, which does all the motion on its own thread. The
/// game's medium is not pictured at all: the native Stage renders it, given the shell, the cover and the pose.
/// The Title page and the launch flight still draw on the Avalonia Stage: the feed hands the screen back while
/// a Title page is open.
/// </summary>
internal sealed partial class HandheldNativeStageFeed
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
    private readonly (string Key, Control Piece, Func<HandheldShellViewModel, string> Content, StageInkZone Zone)[] _chrome;
    private readonly Control _bigTypeCover;

    // Big Type's name (M47 B6): sent as one atlas of its letters, again only when it changes.
    private readonly HandheldBigTypeName _bigTypeName;
    private string? _nameSent;
    private string? _veilsSent;
    private Avalonia.Media.Color? _inkSent;
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
        _bigTypeCover = Find("BtCoverShot");
        _bigTypeName = (HandheldBigTypeName)Find("BtName");
        // Each corner piece is pictured again only when what it says changes (a picture is a GPU round trip). Under Big Type
        // (M47 B4) the pills are gone: the label, the name, the two lines, the status and the hints are each pictured in white
        // and drawn by the native Stage in their zone's ink; the app's messages are the line's (M47 B10), and the toast is left only
        // where the Stage shows no name to say them under.
        var message = ("message", Find("MessageShot"), (Func<HandheldShellViewModel, string>)(s => $"{s.HasStatusMessage}|{s.StatusMessage}|{s.StatusGlyph}|{s.Stage.HasBigTypeName}"),
            StageInkZone.None);
        _chrome = HandheldTokens.IsBigType
            ?
            [
                ("bt-label", Find("BtLabelShot"), s => $"{s.Stage.HasBigTypeName}|{s.Stage.Label}", StageInkZone.Left),
                ("bt-line", Find("BtLineShot"), s => $"{s.Stage.HasBigTypeName}|{s.HasLineMessage}|{s.Stage.Line}", StageInkZone.Left),
                ("bt-facts", Find("BtFactsShot"), s => $"{s.Stage.HasBigTypeName}|{s.Stage.Facts}|{(s.HasLineMessage ? s.Stage.Line : null)}", StageInkZone.Left),
                ("bt-status", Find("BtStatusShot"), s => $"{s.TimeText}|{s.BatteryText}|{s.HasBattery}", StageInkZone.TopRight),
                ("bt-hints", Find("BtHintsShot"), s => string.Join("|", s.Hints.Select(h => $"{h.Button}:{h.Label}:{h.Icon}")), StageInkZone.BottomRight),
                message,
            ]
            :
            [
                ("status", Find("StatusShot"), s => $"{s.TimeText}|{s.BatteryText}|{s.IsCharging}|{s.WifiOpacity}|{s.HasWifiState}", StageInkZone.None),
                ("hints", Find("HintsShot"), s => string.Join("|", s.Hints.Select(h => $"{h.Button}:{h.Label}:{h.Icon}")), StageInkZone.None),
                ("achievements", Find("AchievementShot"), s => $"{s.Stage.ShowAchievementPill}|{s.Stage.AchievementText}", StageInkZone.None),
                message,
            ];

        // The Avalonia Stage only lends its pieces now: its own backdrop never draws, and its medium neither
        // draws nor follows the pose (each pose would still ask Avalonia for a frame).
        foreach (var name in new[] { "Medium", "BtMedium" })
        {
            var medium = Find(name);
            medium.IsVisible = false;
            medium.DataContext = null;
        }
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
            var bigTypeMedium = Find("BtMedium");
            bigTypeMedium.ClearValue(StyledElement.DataContextProperty);
            bigTypeMedium.Bind(Visual.IsVisibleProperty, new Avalonia.Data.Binding("Stage.HasBigTypeMedium"));
            UpdateActive();
        };
    }

    private Control Find(string name) =>
        _view.FindControl<Control>(name) ?? throw new InvalidOperationException($"The Stage has no {name}.");

    // 1920 on the 16:9 screen, 1240 on the narrow one (M45 H5), in either look: Big Type lays its Stage out again for the narrow
    // screen (M47 B11; B4 had shown the 16:9 one whole, letterboxed).
    private float CanvasWidth => (float)(_isNarrow() ? HandheldShellViewModel.NarrowCanvasWidth : HandheldShellViewModel.WideCanvasWidth);

    // The Big Type Stage on the narrow screen: the name's box and the case have narrow places of their own.
    private bool IsBigTypeNarrow => HandheldTokens.IsBigType && _isNarrow();

    private double NameBoxWidth(HandheldStageViewModel stage) => IsBigTypeNarrow ? stage.NarrowNameBoxWidth : stage.NameBoxWidth;

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
            old.InputArrived -= OnInput;
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
        shell.InputArrived += OnInput;
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
            SendMotion();
            SendBackdrop();
            Resend();
            SendInk();
        }
    }

    // Battery saver or reduced motion (M45 H7c): the native Stage holds its backdrop still. Retained, so a rebuilt context knows.
    // Big Type's motion (M47 B6) goes with it: the wipe, the letters.
    private void SendMotion()
    {
        if (_active && _shell is { } shell)
            _host.Post(new StageMotionCommand(shell.HoldsStill, HandheldTokens.IsBigType));
    }

    // A press (M47 B6): the name's letters hop on the Launcher's schedule.
    private void OnInput()
    {
        if (_active && HandheldTokens.IsBigType)
            _host.Post(new StageInputCommand());
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
        _veilsSent = null;
        _inkSent = null;
        _nameSent = null;
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
            case nameof(HandheldShellViewModel.HoldsStill):
                SendMotion();
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
            case nameof(HandheldShellViewModel.HasLineMessage):
                Schedule(centre: false, pop: false, chrome: true);
                break;
            case nameof(HandheldShellViewModel.LauncherGround):
                SendInk();
                break;
            // Big Type (M47 B8): the launch's takeover, and an error shaking the line.
            case nameof(HandheldShellViewModel.Playing):
                OnPlayingChanged();
                break;
            case nameof(HandheldShellViewModel.LineShakes) when _active && !IsPaused:
                _host.Post(new StageShakeCommand("bt-line"));
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
                // Big Type's wipe (M47 B6) follows the Launcher's flood by its own delay, so the backdrop goes at once; the native Stage
                // gets it ready over the frames before its circle grows.
                if (HandheldTokens.IsBigType)
                    SendBackdrop();
                else
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
            case nameof(HandheldStageViewModel.ListCover):
            case nameof(HandheldStageViewModel.IsListTyping):
            case nameof(HandheldStageViewModel.AppIcon):
                // A dock app's large icon arrived after its slot took the Stage (M45 H6d).
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
            // Big Type's texts (M47 B4): the emulator's name and a folder's last played game arrive after the move.
            case nameof(HandheldStageViewModel.Label):
            case nameof(HandheldStageViewModel.Name):
            case nameof(HandheldStageViewModel.Line):
            case nameof(HandheldStageViewModel.Facts):
            case nameof(HandheldStageViewModel.IsMissing):
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
        if (resent)
            SendInk();
        if (centre)
            SendCentre(stage, pop);
        if (chrome && _shell is { } shell)
        {
            SendVeils(stage);
            SendName(stage, shell, animate: !resent);
            foreach (var (key, piece, content, zone) in _chrome)
            {
                var now = content(shell);
                if (_chromeSent.TryGetValue(key, out var sent) && sent == now)
                    continue;
                _chromeSent[key] = now;
                // The clock and the line's counts roll (M47 B5): their pictures carry where each character sits.
                var text = key is "bt-status" or "bt-line" ? HandheldTextCells.Of(piece, _canvas) : null;
                _host.Post(new StageChromeCommand(key, HandheldStageShots.Shot(piece, _canvas), zone, Text: text) { Quick = shell.QuickChange });
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

    // ---- Big Type's name (M47 B6) ----

    /// <summary>
    /// The name as letters: the control's fit set as one atlas of its distinct letters with every letter's place on the canvas, sent
    /// only when the name, its box or its dimming changes. The old name's letters leave and the new one's arrive from the move that
    /// brought it; after a resend (a swap, the Stage shown again) it is simply there.
    /// </summary>
    private void SendName(HandheldStageViewModel stage, HandheldShellViewModel shell, bool animate)
    {
        if (!HandheldTokens.IsBigType)
            return;
        var key = $"{stage.HasBigTypeName}|{stage.Name}|{NameBoxWidth(stage)}|{stage.NameOpacity}|{stage.IsTyped}|{CanvasWidth}";
        if (key == _nameSent)
            return;
        var first = _nameSent is null;
        _nameSent = key;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var letters = stage.HasBigTypeName && HandheldStageShots.ToCanvas(_bigTypeName, default(Point), _canvas) is { } at
            ? _bigTypeName.Letters(at)
            : null;
        // The frame probe (R1's measurement) ties the Stage's frames to the name that arrived: its lines, letters and atlas.
        if (HandheldProbeLog.Write is { } log && letters is not null)
        {
            log(FormattableString.Invariant(
                $"name '{letters.Text}' lines {_bigTypeName.Fitted?.Lines} letters {letters.Letters.Count} size {letters.Size:0} atlas {letters.Atlas.Width}x{letters.Atlas.Height} in {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.0} ms (UI thread)"));
        }
        // A word being typed (M47 B9) says how it changed, its anchor on the canvas.
        var typed = stage.IsTyped && _bigTypeName.TypedChange is { } change && HandheldStageShots.ToCanvas(_bigTypeName, default(Point), _canvas) is { } origin
            ? change with { AnchorX = (float)(origin.X + change.AnchorX), AnchorY = (float)(origin.Y + change.AnchorY) }
            : null;
        _host.Post(new StageNameCommand(letters, HandheldStageEntrance.Of(shell), Animate: animate && !first) { Typed = typed, Quick = shell.QuickChange });
    }

    // ---- Big Type's ink and veils (M47 B4) ----

    /// <summary>The ground the native Stage clears to and the dark ink it draws with, from the Launcher's colour. Big Type only.</summary>
    private void SendInk()
    {
        if (!_active || !HandheldTokens.IsBigType || _shell is not { } shell || _inkSent == shell.LauncherGround)
            return;
        _inkSent = shell.LauncherGround;
        var ground = shell.LauncherGround;
        var deep = shell.StageDeep;
        _host.Post(new StageInkCommand(new System.Numerics.Vector3(ground.R, ground.G, ground.B) / 255f,
            new System.Numerics.Vector3(deep.R, deep.G, deep.B) / 255f));
    }

    /// <summary>The three veils as white masks, sent again only when the name's box (and so the left veil) changes. Big Type only.</summary>
    private void SendVeils(HandheldStageViewModel stage)
    {
        if (!HandheldTokens.IsBigType)
            return;
        var flatEnd = BigTypeVeil.LeftFlatEnd((float)HandheldTokens.StageNameLeft, (float)NameBoxWidth(stage));
        var key = FormattableString.Invariant($"{flatEnd}|{CanvasWidth}");
        if (key == _veilsSent)
            return;
        _veilsSent = key;
        foreach (var (name, zone) in new[] { ("veil-left", StageInkZone.Left), ("veil-top-right", StageInkZone.TopRight), ("veil-bottom-right", StageInkZone.BottomRight) })
            _host.Post(new StageChromeCommand(name, BigTypeVeil.Mask(zone, flatEnd, CanvasWidth, StageCompositor.CanvasHeight), zone, Veil: true));
    }

    // ---- Pieces ----

    private void SendCentre(HandheldStageViewModel stage, bool pop)
    {
        // A Title page crossfades in and out rather than popping from the centre, as on the Avalonia Stage.
        var fade = pop && (stage.Kind == HandheldStageKind.Hero || _lastKind == HandheldStageKind.Hero);
        if (pop)
            _lastKind = stage.Kind;
        if (fade)
            pop = false;
        // The idle drift (M45 H6d) crossfades slowly from one recent game to the next, and is not navigation.
        var drift = stage.IsDrift;
        var fadeMilliseconds = drift ? HandheldMotion.IdleDriftCrossfadeMilliseconds : StageCentreCommand.HeroFadeMilliseconds;
        if (stage.IsHeroArt)
        {
            var hero = new[] { HandheldStageShots.Shot(_heroBackdrop, _canvas), HandheldStageShots.Shot(_heroContent, _canvas) }
                .Where(image => image is not null).Cast<StageImage>().ToArray();
            _host.Post(new StageCentreCommand(null, null, null, pop, Hero: hero, FadeIn: fade, FadeMilliseconds: fadeMilliseconds,
                Drift: drift));
            return;
        }
        if (HandheldTokens.IsBigType)
        {
            SendBigTypeCentre(stage, pop, fade);
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
        _host.Post(new StageCentreCommand(HandheldStageShots.Shot(_centre, _canvas), null, plate, pop, FadeIn: fade,
            FadeMilliseconds: fadeMilliseconds, Drift: drift));
    }

    /// <summary>
    /// The centre under Big Type (M47 B4): no plate and no big folder; a game's medium (or its flat cover) in its corner;
    /// Settings' and a panel's cards as today until B7; nothing else (the name is the picture).
    /// </summary>
    private void SendBigTypeCentre(HandheldStageViewModel stage, bool pop, bool fade)
    {
        if (stage.HasMedium && stage.Medium is { } shell)
        {
            var accent = MediaShellRenderer.ToLinear(stage.MediumAccent.R / 255f, stage.MediumAccent.G / 255f, stage.MediumAccent.B / 255f);
            var frame = HandheldTokens.StageMediumFrame(CanvasWidth);
            var medium = new StageMedium(shell, accent, (float)frame.X, (float)frame.Y, (float)frame.Width, (float)frame.Height);
            _host.Post(new StageMediumPoseCommand((float)stage.MediumYaw, (float)stage.MediumPitch));
            SendCover(stage.Art, then: new StageCentreCommand(null, medium, null, pop, FadeIn: fade));
            return;
        }
        // M47 B7: Settings and a panel are type (their focused row is the name; M47 B9: while they type, the word typed); a panel's
        // cover (cover search's candidate) sits where a game's flat cover does.
        var art = stage.HasBigTypeCover ? HandheldStageShots.Shot(_bigTypeCover, _canvas) : null;
        _host.Post(new StageCentreCommand(art, null, null, pop, FadeIn: fade));
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
        // Big Type (M47 B8): the takeover is sent when the shell's play begins (OnPlayingChanged).
        if (HandheldTokens.IsBigType)
            return;
        if (!_active || IsPaused || page.Cover is not { } cover)
            return;
        var width = (float)HandheldTokens.LaunchCardWidth;
        var height = (float)HandheldTokens.LaunchCardHeight;
        var card = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = HandheldTokens.LaunchCardRadius,
            ClipToBounds = true,
            Background = HandheldTokens.CoverPlaceholder,
            BorderBrush = HandheldTokens.ClassicLaunchCardEdge,
            BorderThickness = HandheldTokens.ClassicLaunchCardEdgeThickness,
            Child = new Image { Source = cover, Stretch = Stretch.UniformToFill },
        };
        _host.Post(new StageLaunchCommand(HandheldStageShots.Picture(card, (CanvasWidth - width) / 2f, (1080 - height) / 2f, width, height),
            FromAbove: _isNarrow()));
    }

    private void SendBackdrop()
    {
        if (!_active)
            return;
        // Big Type (M47 B11): no backdrop yet (a start, before the first focus has its own) is not the plain grey: the native Stage
        // keeps the one it has, or at a start shows its ground, the Launcher's colour, with white type, until the first one comes.
        if (HandheldTokens.IsBigType && _stage?.Backdrop is null)
            return;
        _backdropSent = _stage?.Backdrop;
        _backdropSentOnce = true;
        _host.Post(new StageBackdropCommand(HandheldEraBackdrop.RenderLayers(_stage?.Backdrop), _stage?.Backdrop?.Animated,
            Drift: _stage?.IsDrift == true, WipeOriginX: WipeOriginX(), Ground: BigTypeGround(_stage?.Backdrop)));
    }

    /// <summary>
    /// The colour Big Type recolours the backdrop's animations toward (M47 B13): the backdrop's own colour, which under Big Type is the
    /// console's ground (or Recent's); null in today's look.
    /// </summary>
    private static System.Numerics.Vector3? BigTypeGround(HandheldBackdrop? backdrop) =>
        HandheldTokens.IsBigType && backdrop is { } shown
            ? new System.Numerics.Vector3(shown.Color.R / 255f, shown.Color.G / 255f, shown.Color.B / 255f)
            : null;

    /// <summary>
    /// Where a Big Type wipe grows from across the Stage canvas (M47 B6): the Launcher's focused tile, its canvas centred under the
    /// Stage's as on the Thor (the mock's <c>cx + 170</c>); null (the middle) when no native Launcher is drawing or nothing is focused.
    /// </summary>
    private float? WipeOriginX()
    {
        if (!HandheldTokens.IsBigType || _shell is not { } shell || HandheldNativeLauncherFeed.FocusX(shell) is not { } focus)
            return null;
        return BigTypeMotion.WipeOrigin(focus.X, focus.CanvasWidth, CanvasWidth, StageCompositor.CanvasHeight).X;
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
