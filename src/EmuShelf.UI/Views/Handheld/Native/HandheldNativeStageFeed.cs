using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
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
    private const float CanvasWidth = 1920;
    private const float CentreAreaHeight = 920;
    private const float MediumWidth = 1400;
    private const float MediumHeight = 820;

    private readonly Control _view;
    private readonly HandheldStageMotion _motion;
    private readonly IHandheldNativeStage _host;
    private readonly Control _canvas;
    private readonly Control _centre;
    private readonly Control _plate;
    private readonly Control _folder;
    private readonly HandheldEraBackdrop _backdrop;
    private readonly (string Key, Control Piece, Func<HandheldShellViewModel, string> Content)[] _chrome;
    private readonly Dictionary<string, string> _chromeSent = new(StringComparer.Ordinal);
    private string? _plateSent;
    private StageImage? _plateImage;
    private readonly DispatcherTimer _backdropSettle;
    private readonly DispatcherTimer _folderSettle;

    private HandheldShellViewModel? _shell;
    private HandheldStageViewModel? _stage;
    private bool _active;
    private bool _failed;
    private bool _centrePending;
    private bool _popPending;
    private bool _chromePending;
    private Bitmap? _sentCover;
    private int _coverVersion;

    public HandheldNativeStageFeed(Control view, HandheldStageMotion motion, IHandheldNativeStage host)
    {
        _view = view;
        _motion = motion;
        _host = host;
        _canvas = Find("StageCanvas");
        _centre = Find("CentreShot");
        _plate = Find("PlateShot");
        _folder = Find("StageFolder");
        _backdrop = (HandheldEraBackdrop)Find("EraBackdrop");
        // Each corner piece is pictured again only when what it says changes (a picture is a GPU round trip).
        _chrome =
        [
            ("status", Find("StatusShot"), s => $"{s.TimeText}|{s.BatteryText}|{s.IsCharging}|{s.WifiOpacity}|{s.HasWifiState}"),
            ("hints", Find("HintsShot"), s => string.Join("|", s.Hints.Select(h => $"{h.Button}:{h.Label}:{h.Icon}"))),
            ("achievements", Find("AchievementShot"), s => $"{s.Stage.ShowAchievementPill}|{s.Stage.AchievementText}"),
            ("message", Find("MessageShot"), s => $"{s.HasStatusMessage}|{s.StatusMessage}"),
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
        // The hidden folder's flap does not animate (an invisible transition still costs Avalonia frames); its
        // new state is pictured once layout has caught up.
        _folderSettle = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        _folderSettle.Tick += (_, _) =>
        {
            _folderSettle.Stop();
            Schedule(centre: true, pop: false);
        };
        _folder.Classes.CollectionChanged += (_, _) => _folderSettle.Start();
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

    public void Attach(HandheldShellViewModel? shell)
    {
        if (ReferenceEquals(shell, _shell))
            return;
        if (_shell is { } old)
        {
            old.StageChanged -= OnStageChanged;
            old.PropertyChanged -= OnShellChanged;
        }
        if (_stage is { } oldStage)
            oldStage.PropertyChanged -= OnStagePropertyChanged;
        _shell = shell;
        _stage = shell?.Stage;
        if (shell is null)
            return;
        shell.StageChanged += OnStageChanged;
        shell.PropertyChanged += OnShellChanged;
        shell.Stage.PropertyChanged += OnStagePropertyChanged;
        UpdateActive();
    }

    // ---- Which Stage draws ----

    private void UpdateActive()
    {
        var active = !_failed && _shell is { TitlePage: null };
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
            _sentCover = null;
            _chromeSent.Clear();
            _plateSent = null;
            SendBackdrop();
            Schedule(centre: true, pop: true, chrome: true);
        }
    }

    // ---- Changes ----

    private void OnStageChanged() => Schedule(centre: true, pop: true, chrome: true);

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HandheldShellViewModel.TitlePage):
                UpdateActive();
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
                Schedule(centre: false, pop: false, chrome: true);
                break;
        }
    }

    private void OnStagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_active || _stage is not { } stage)
            return;
        switch (e.PropertyName)
        {
            case nameof(HandheldStageViewModel.Backdrop):
                _backdropSettle.Stop();
                _backdropSettle.Start();
                break;
            case nameof(HandheldStageViewModel.MediumYaw):
            case nameof(HandheldStageViewModel.MediumPitch):
                if (stage.HasMedium)
                    _host.Post(new StageMediumPoseCommand((float)stage.MediumYaw, (float)stage.MediumPitch));
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
                Schedule(centre: true, pop: false);
                break;
            case nameof(HandheldStageViewModel.AchievementText):
                Schedule(centre: false, pop: false, chrome: true);
                break;
        }
    }

    /// <summary>Pictures are taken once layout has caught up with the change (the Loaded priority runs after layout).</summary>
    private void Schedule(bool centre, bool pop, bool chrome = false)
    {
        if (!_active)
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
        var centre = _centrePending;
        var pop = _popPending;
        var chrome = _chromePending;
        _centrePending = _popPending = _chromePending = false;
        if (!_active || _stage is not { } stage)
            return;
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
    }

    // ---- Pieces ----

    private void SendCentre(HandheldStageViewModel stage, bool pop)
    {
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
            var medium = new StageMedium(shell, accent, (CanvasWidth - MediumWidth) / 2, (CentreAreaHeight - MediumHeight) / 2,
                MediumWidth, MediumHeight);
            _host.Post(new StageMediumPoseCommand((float)stage.MediumYaw, (float)stage.MediumPitch));
            // The new medium pops in only once its cover is ready, so it never shows the last game's label.
            SendCover(stage.Art, then: new StageCentreCommand(null, medium, plate, pop));
            return;
        }
        _host.Post(new StageCentreCommand(HandheldStageShots.Shot(_centre, _canvas), null, plate, pop));
    }

    private void SendBackdrop()
    {
        if (!_active)
            return;
        _host.Post(new StageBackdropCommand(HandheldEraBackdrop.RenderLayers(_stage?.Backdrop)));
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
