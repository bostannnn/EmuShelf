using System.Collections.Specialized;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Logging;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmuShelf.App.Rendering;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Rendering;
using EmuShelf.Rendering.Gl;
using EmuShelf.Rendering.Models;
using EmuShelf.Rendering.Shells;
using Silk.NET.Core.Contexts;
using GL = Silk.NET.OpenGL.GL;

namespace EmuShelf.App.Controls;

/// <summary>
/// One OpenGL scene containing the focused physical medium and its visible neighbours.
/// </summary>
public sealed class MediaShelf3DControl : OpenGlControlBase
{
    /// <summary>Avalonia log area for this scene's own GL diagnostics; captured by AvaloniaFileLogSink.</summary>
    internal const string ShelfLogArea = "EmuShelf.Shelf3D";

    private const int NeighbourRadius = 3;
    // Tightened alongside the camera's closer framing: at the old gap the neighbouring media fell
    // entirely outside a filled frame, which turns a shelf back into a single-hero view.
    private const float ItemGap = 0.14f;
    private const float NeighbourYaw = -0.18f;
    /// <summary>
    /// Uploaded face textures kept on the GPU, across all games.
    /// </summary>
    /// <remarks>
    /// Counted in textures rather than games because a keep case uploads three — front, back and
    /// spine — where a cartridge uploads one. The old game-based limit of 21 silently became a
    /// ceiling of 63 textures the moment faces went independent, which at a 1024px decode is a
    /// quarter of a gigabyte of GPU memory for something the design documents as a 21-entry cache.
    /// The budget still comfortably exceeds the visible window, which is what keeps reversing
    /// direction from repeating upload and mipmap work.
    /// </remarks>
    private const int CoverTextureBudget = 24;
    private const int PhysicalArtworkCacheCapacity = 21;
    private const int PhysicalArtworkDecodeSize = 1024;
    private const int MaximumConcurrentPhysicalArtworkDecodes = 2;

    public static readonly StyledProperty<IReadOnlyList<GameViewModel>?> ItemsProperty =
        AvaloniaProperty.Register<MediaShelf3DControl, IReadOnlyList<GameViewModel>?>(nameof(Items));

    public static readonly StyledProperty<GameViewModel?> FocusedItemProperty =
        AvaloniaProperty.Register<MediaShelf3DControl, GameViewModel?>(nameof(FocusedItem));

    public static readonly StyledProperty<double> ShelfPositionProperty =
        AvaloniaProperty.Register<MediaShelf3DControl, double>(nameof(ShelfPosition));

    public static readonly StyledProperty<double> YawProperty =
        AvaloniaProperty.Register<MediaShelf3DControl, double>(nameof(Yaw));

    public static readonly StyledProperty<double> PitchProperty =
        AvaloniaProperty.Register<MediaShelf3DControl, double>(nameof(Pitch));

    public static readonly StyledProperty<PhysicalShelfDeparturePose?> DeparturePoseProperty =
        AvaloniaProperty.Register<MediaShelf3DControl, PhysicalShelfDeparturePose?>(nameof(DeparturePose));

    public static readonly StyledProperty<PhysicalShelfLaunchPose?> LaunchPoseProperty =
        AvaloniaProperty.Register<MediaShelf3DControl, PhysicalShelfLaunchPose?>(nameof(LaunchPose));

    public static readonly StyledProperty<CrtPresentation> CrtProperty =
        AvaloniaProperty.Register<MediaShelf3DControl, CrtPresentation>(
            nameof(Crt), CrtPresentation.Off);

    public static readonly StyledProperty<bool> TintBackdropWithAccentProperty =
        AvaloniaProperty.Register<MediaShelf3DControl, bool>(
            nameof(TintBackdropWithAccent), true);

    /// <summary>
    /// The couch UI to draw inside the tube, rather than on top of it.
    /// </summary>
    /// <remarks>
    /// Must be a visual that does NOT contain this control, or the capture would be trying to
    /// photograph itself. That is why the scene sits beside <c>GamepadRoot</c> in the window rather
    /// than inside it.
    /// </remarks>
    public static readonly StyledProperty<Visual?> ChromeSourceProperty =
        AvaloniaProperty.Register<MediaShelf3DControl, Visual?>(nameof(ChromeSource));

    /// <summary>
    /// Multiplies the desktop-tuned framing fill so the media reads larger on a handheld. 1.0 is the
    /// desktop composition; the Android head raises it. See <see cref="MediaShellRenderer.ShelfCamera"/>.
    /// </summary>
    public static readonly StyledProperty<double> FrameFillScaleProperty =
        AvaloniaProperty.Register<MediaShelf3DControl, double>(nameof(FrameFillScale), 1.0);

    private readonly List<LayoutEntry> _layout = [];
    private readonly Dictionary<long, GameViewModel> _gamesByKey = [];
    private readonly HashSet<GameViewModel> _observedGames = [];
    private readonly Dictionary<long, UploadedCover> _uploadedCovers = [];
    private readonly LinkedList<long> _coverLru = [];
    private readonly Dictionary<ArtworkKey, DecodedArtwork> _decodedPhysicalArtwork = [];
    private readonly LinkedList<ArtworkKey> _physicalArtworkLru = [];
    private readonly Dictionary<ArtworkKey, PhysicalArtworkLoad> _physicalArtworkLoads = [];
    private readonly Queue<PhysicalArtworkLoad> _physicalArtworkQueue = [];
    private readonly Dictionary<long, PhysicalShelfDeparturePose> _departurePoses = [];
    private readonly System.Diagnostics.Stopwatch _crtClock = System.Diagnostics.Stopwatch.StartNew();
    private ChromeSnapshot? _chromeSnapshot;
    private Window? _observedWindow;
    private INotifyCollectionChanged? _observedCollection;
    private int _observedStart = -1;
    private int _observedEnd = -1;
    private int _activePhysicalArtworkDecodes;
    private int _focusedIndex = -1;
    private float _sceneMediaHeight = 1f;
    private float _sceneMediaWidth = 1f;
    private int _preparationGeneration;
    private GL? _gl;
    private MediaShellRenderer? _renderer;
    private bool _failed;
    private bool _isAttached;
    private Color _uploadedAccent;

    /// <summary>
    /// The snapshot the supersampled scene buffer was last fully drawn from, and the surface size it
    /// was drawn at. When the next frame is the very same snapshot at the same size — a redraw driven
    /// purely by the tube's own animation — the 3D scene is identical and only the post pass needs to
    /// run again. Render-thread only; reset to null whenever the GL context or renderer changes.
    /// </summary>
    private FrameSnapshot? _lastRenderedSnapshot;
    private uint _lastRenderedWidth;
    private uint _lastRenderedHeight;
    // One line per context: proves frames actually reach the surface and at what pixel size, so a
    // scene that inits cleanly but shows nothing (a degenerate viewport, or output that never
    // composites) is told apart from one that renders wrong. See DECISIONS 2026-08-16.
    private bool _firstFrameLogged;
    // The surface size the last diagnostic line reported. The tube must cover the whole window, so a
    // surface that stays smaller than the window after a mode switch (the desktop→gamepad full-screen
    // resize not reaching the GL surface) is exactly the "doubled platform row" signature: log every
    // change so a stuck surface is visible in Logs/, not just the first frame.
    private uint _lastLoggedSurfaceWidth;
    private uint _lastLoggedSurfaceHeight;

    /// <summary>
    /// The frozen description of the next frame, built on the UI thread and read by the render
    /// thread.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the shelf survives fast navigation. <see cref="OnOpenGlRender"/>
    /// runs on Avalonia's render thread while every list this control keeps — the layout, the games
    /// by key, the decoded artwork and its LRU — is rebuilt on the UI thread as the platform cycles.
    /// Reading those live from the render frame meant a scope switch that landed mid-frame threw a
    /// collection-modified exception out of the draw, and the blanket handler below read any such
    /// exception as "this GPU cannot do it" and dropped the shelf to flat covers for the session.
    /// Publishing an immutable snapshot the render thread never has to reach back into removes the
    /// shared state entirely; the field is a reference swap, which is atomic, and marked volatile so
    /// the render thread sees the newest one.
    /// </remarks>
    private volatile FrameSnapshot? _frameSnapshot;

    /// <summary>
    /// The last resolved face artwork, reused across publishes that changed only the position.
    /// </summary>
    /// <remarks>
    /// A glide republishes the frame every 16ms as the motion timer eases the position, but the
    /// artwork a face wears changes far more rarely — a decode completing, a scope switch, a cover
    /// arriving. Re-resolving it and allocating a fresh map on every tick was pure churn, and worse,
    /// walked the decoded-artwork LRU each time. This map is rebuilt only when the visible keys or
    /// <see cref="_artworkGeneration"/> move; a position-only publish hands the render thread the
    /// exact same immutable map it had last frame. It is never mutated after it is put in a snapshot,
    /// so a rebuild allocates a new one and leaves any in-flight frame's copy untouched.
    /// </remarks>
    private Dictionary<long, IImage?[]>? _artworkCache;
    private long[] _artworkCacheKeys = [];
    private int _artworkCacheGeneration = -1;

    /// <summary>
    /// Bumped whenever something that changes a face's artwork happens, to invalidate
    /// <see cref="_artworkCache"/> without inspecting what actually moved.
    /// </summary>
    private int _artworkGeneration;

    /// <summary>
    /// How many draws in a row have thrown, so a genuine failure still gives up while a one-off does
    /// not.
    /// </summary>
    private int _consecutiveRenderFailures;

    /// <summary>
    /// The run of failed draws that turns a transient render error into the flat-cover fallback.
    /// </summary>
    /// <remarks>
    /// A render exception used to be fatal on the first occurrence, which is wrong for anything
    /// transient — a one-frame race against a bitmap being disposed, a momentary driver hiccup. Now
    /// the renderer is kept and the frame retried; only a fault that repeats every frame this many
    /// times, i.e. an actually-broken context, is treated as unsupported. The count resets on the
    /// first clean frame, so occasional blips never accumulate to the threshold.
    /// </remarks>
    private const int MaxConsecutiveRenderFailures = 8;

    public event EventHandler<Exception>? InitializationFailed;

    public IReadOnlyList<GameViewModel>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public GameViewModel? FocusedItem
    {
        get => GetValue(FocusedItemProperty);
        set => SetValue(FocusedItemProperty, value);
    }

    /// <summary>Continuous selection coordinate: integer values rest exactly on a game.</summary>
    public double ShelfPosition
    {
        get => GetValue(ShelfPositionProperty);
        set => SetValue(ShelfPositionProperty, value);
    }

    public double Yaw
    {
        get => GetValue(YawProperty);
        set => SetValue(YawProperty, value);
    }

    public double Pitch
    {
        get => GetValue(PitchProperty);
        set => SetValue(PitchProperty, value);
    }

    public PhysicalShelfDeparturePose? DeparturePose
    {
        get => GetValue(DeparturePoseProperty);
        set => SetValue(DeparturePoseProperty, value);
    }

    public PhysicalShelfLaunchPose? LaunchPose
    {
        get => GetValue(LaunchPoseProperty);
        set => SetValue(LaunchPoseProperty, value);
    }

    /// <summary>How hard the scene is pushed through a CRT tube on the way to the screen.</summary>
    public CrtPresentation Crt
    {
        get => GetValue(CrtProperty);
        set => SetValue(CrtProperty, value);
    }

    /// <summary>
    /// Whether the resolved backdrop carries the focused system's accent wash.
    /// </summary>
    /// <remarks>
    /// True for the window-covering tube, whose backdrop is the whole couch screen and follows the
    /// artwork. False for the in-place shelf, which is opaque inside its own slot with the couch
    /// root's plain library fill in the bands around it — a washed backdrop there would print a
    /// tinted rectangle against that plain fill, and the wash it would use (always the system accent)
    /// would not even match the theme-accent wash the couch shows when artwork matching is off.
    /// </remarks>
    public bool TintBackdropWithAccent
    {
        get => GetValue(TintBackdropWithAccentProperty);
        set => SetValue(TintBackdropWithAccentProperty, value);
    }

    /// <inheritdoc cref="ChromeSourceProperty"/>
    public Visual? ChromeSource
    {
        get => GetValue(ChromeSourceProperty);
        set => SetValue(ChromeSourceProperty, value);
    }

    /// <inheritdoc cref="FrameFillScaleProperty"/>
    public double FrameFillScale
    {
        get => GetValue(FrameFillScaleProperty);
        set => SetValue(FrameFillScaleProperty, value);
    }

    /// <summary>
    /// Hands the newest couch-UI capture to the renderer, if one has arrived since the last frame.
    /// </summary>
    /// <remarks>
    /// Runs on the render thread, which is why it only ever touches the byte buffer the snapshot
    /// timer left behind and never the visual tree itself. A chrome refresh rate below the frame
    /// rate therefore costs nothing per frame beyond the check.
    /// </remarks>
    private void UploadChrome(MediaShellRenderer renderer) =>
        _chromeSnapshot?.TryTake((pixels, size) =>
            renderer.SetCrtChrome(pixels, size.Width, size.Height));

    /// <summary>
    /// The colour the tube shows where the shelf does not cover it.
    /// </summary>
    /// <remarks>
    /// The CRT pass composites and writes opaque, so it — not the Border stack behind this control —
    /// paints the couch backdrop wherever the pass is active. Resolving the same theme brush and
    /// applying the same accent wash the XAML would have keeps the two paths matching, which is what
    /// stops the scene's rectangle from showing as a seam against the screen around it.
    /// </remarks>
    // The last backdrop-resolution diagnostic emitted, so the trace lands once per distinct state
    // instead of every published frame.
    private string? _lastBackdropDiagnostic;

    private Vector3 ResolveBackdrop(Color accent)
    {
        // The couch shelf is hosted in a Window on desktop but in a plain view on the Android
        // single-view head, where this control's ActualThemeVariant can settle at Default. A theme
        // brush lives in a ThemeDictionary keyed Light/Dark only, so an imperative TryFindResource
        // against a Default variant matches nothing and drops to the dark fallback below — the
        // "dark grey shelf" bug, visible only here because the flat presentations paint an opaque
        // chrome capture over the backdrop while the 3D shelf clears to it. Resolve against a
        // concrete variant instead (the control's, else the application's, else Light), and consult
        // the application resources when the local walk comes up short — which is what the flat
        // views get from {DynamicResource} for free.
        // Candidate variants in preference order: the control's own, then the application's, then an
        // explicit Light backstop. During the first frames on the Android head the control's
        // ActualThemeVariant is briefly an uninitialised value that is neither Default nor a real
        // variant and so matches no ThemeDictionary; falling through to the application's concrete
        // variant (and Light as a last resort) is what stops the backdrop flashing the dark fallback.
        var candidates = new ThemeVariant?[]
        {
            ActualThemeVariant,
            Application.Current?.ActualThemeVariant,
            ThemeVariant.Light,
        };

        var library = Color.FromRgb(22, 23, 27);
        var resolved = false;
        ThemeVariant? usedVariant = null;
        foreach (var candidate in candidates)
        {
            if (candidate is null || candidate == ThemeVariant.Default)
            {
                continue;
            }

            ISolidColorBrush? found = null;
            if (this.TryFindResource("EmuLibraryBrush", candidate, out var local)
                && local is ISolidColorBrush localBrush)
            {
                found = localBrush;
            }
            else if (Application.Current is { } app
                && app.TryFindResource("EmuLibraryBrush", candidate, out var global)
                && global is ISolidColorBrush globalBrush)
            {
                found = globalBrush;
            }

            if (found is not null)
            {
                library = found.Color;
                resolved = true;
                usedVariant = candidate;
                break;
            }
        }

        var diagnostic =
            $"used={usedVariant} control={ActualThemeVariant} app={Application.Current?.ActualThemeVariant} "
            + $"resolved={resolved} library=#{library.R:X2}{library.G:X2}{library.B:X2}";
        if (diagnostic != _lastBackdropDiagnostic)
        {
            _lastBackdropDiagnostic = diagnostic;
            Logger.TryGet(LogEventLevel.Information, ShelfLogArea)?.Log(
                this, "Shelf backdrop resolve: {Diagnostic}", diagnostic);
        }

        // The in-place shelf wants exactly the couch root's own fill, with no wash to seam against it.
        if (!TintBackdropWithAccent)
        {
            return new Vector3(library.R / 255f, library.G / 255f, library.B / 255f);
        }

        // Matches the wash Borders in MainWindow.axaml: the accent at 0.16 over the library colour.
        const float WashOpacity = 0.16f;
        return new Vector3(
            ((library.R / 255f) * (1f - WashOpacity)) + ((accent.R / 255f) * WashOpacity),
            ((library.G / 255f) * (1f - WashOpacity)) + ((accent.G / 255f) * WashOpacity),
            ((library.B / 255f) * (1f - WashOpacity)) + ((accent.B / 255f) * WashOpacity));
    }

    public event EventHandler? InitializationSucceeded;

    public event EventHandler? ContextLost;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsProperty)
        {
            ObserveCollection();
            RebuildLayout();
            UpdateVisibleSubscriptions(force: true);
            PrepareShells();
        }
        else if (change.Property == FocusedItemProperty)
        {
            _focusedIndex = FocusedItem is null || Items is null ? -1 : IndexOf(Items, FocusedItem);
        }
        else if (change.Property == ShelfPositionProperty)
        {
            UpdateVisibleSubscriptions();
        }
        else if (change.Property == DeparturePoseProperty && DeparturePose is { } pose)
        {
            RememberDeparturePose(pose);
        }

        if (change.Property == ItemsProperty
            || change.Property == FocusedItemProperty
            || change.Property == ShelfPositionProperty
            || change.Property == YawProperty
            || change.Property == PitchProperty
            || change.Property == DeparturePoseProperty
            || change.Property == LaunchPoseProperty
            || change.Property == FrameFillScaleProperty
            || change.Property == BoundsProperty)
        {
            PublishFrame();
        }

        if (change.Property == CrtProperty)
        {
            // Switching the effect on or off has to start or stop the capture timer, not just change
            // what the shader does with its output.
            StartChromeCapture();
            PublishFrame();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        ObserveCollection();
        UpdateVisibleSubscriptions(force: true);
        PrepareShells();
        ObserveWindowState();
        StartChromeCapture();
        PublishFrame();
    }

    /// <summary>
    /// Watches the window so the capture can stop while nobody can see it.
    /// </summary>
    /// <remarks>
    /// Minimised is the state that matters, and it is how EmuShelf spends a play session: the app
    /// minimises while an emulator runs (see DECISIONS 2026-07-12). The GPU side already stops on its
    /// own, because a minimised window stops being rendered and the frame-request loop is driven from
    /// inside that render. The capture does not — it is a dispatcher timer, and it would go on doing
    /// a full-window offscreen render 30 times a second while the emulator wants that CPU.
    ///
    /// Keyed on window state rather than on launching a game, so it also covers the user simply
    /// minimising the window, which costs exactly as much and is just as invisible.
    /// </remarks>
    private void ObserveWindowState()
    {
        if (_observedWindow is not null || TopLevel.GetTopLevel(this) is not Window window)
        {
            return;
        }

        _observedWindow = window;
        window.PropertyChanged += OnWindowPropertyChanged;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == Window.WindowStateProperty)
        {
            StartChromeCapture();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        StopObserving();
        ClearDecodedPhysicalArtwork();
        _preparationGeneration++;
        _chromeSnapshot?.Dispose();
        _chromeSnapshot = null;
        if (_observedWindow is { } observed)
        {
            observed.PropertyChanged -= OnWindowPropertyChanged;
            _observedWindow = null;
        }
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Begins capturing the couch UI, if there is one to capture and a tube to show it in.
    /// </summary>
    /// <remarks>
    /// The interval is the chrome's own frame rate, and it is deliberately not the display's. The
    /// rail's indicator slide and the focus transitions are the only things in there that move, and
    /// they read fine at 30Hz once the tube's scanlines and softening are over them — while halving
    /// the number of full-window offscreen renders the UI thread has to absorb.
    /// </remarks>
    private void StartChromeCapture()
    {
        if (!_isAttached || !Crt.IsActive || ChromeSource is null
            || _observedWindow?.WindowState == WindowState.Minimized)
        {
            // A non-active presentation stops the timer, not merely stops using its output: the
            // capture is a full-window offscreen render on the UI thread. This fires on the grid and
            // spotlight with the effect off, where the one renderer idles entirely, and while the
            // window is minimised. On the effect-off shelf the presentation is Flat (active), so the
            // capture keeps running — the price of one renderer compositing the couch chrome (rail,
            // title, overlays, toasts) over the flat media there. A scene with no chrome source never
            // starts a capture at all.
            _chromeSnapshot?.Dispose();
            _chromeSnapshot = null;
            return;
        }

        if (_chromeSnapshot is not null)
        {
            return;
        }

        _chromeSnapshot = new ChromeSnapshot(
            () => ChromeSource, TimeSpan.FromMilliseconds(33), OnChromeCaptured);
        _chromeSnapshot.Start();
    }

    /// <summary>
    /// Runs on the UI thread after each chrome capture; keeps the couch chrome live behind a still
    /// scene.
    /// </summary>
    /// <remarks>
    /// The animated tube already requests a frame every render, so its captures upload on their own
    /// and this does nothing for it. A Flat presentation redraws only on demand, so nothing would
    /// upload a capture taken while the shelf sits still — and the chrome it carries is the whole
    /// couch UI, including overlays and toasts that appear without moving the shelf. Requesting a
    /// frame per capture is what keeps those visible, at the capture's own 30Hz rather than the
    /// tube's 60Hz. When the capture stops — off the shelf, or minimised — so do these redraws.
    /// </remarks>
    private void OnChromeCaptured()
    {
        if (Crt.IsActive && !Crt.IsAnimated)
        {
            RequestNextFrameRendering();
        }
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        // Instrumentation for the Steam Deck "no 3D/CRT" diagnosis. This entry line proves the
        // framework actually handed the control a context — its absence in the log means the silent
        // no-context path (an unsupported GL surface), which no init timeout can cure. The elapsed
        // time to build the renderer then separates a slow Mesa cold start from an unsupported
        // platform, since Create links five shader programs and bakes an environment cubemap here,
        // synchronously, before success is reported. Routed through Avalonia's log, which now lands in
        // Logs/ (AvaloniaFileLogSink). See DECISIONS 2026-08-16.
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        Logger.TryGet(LogEventLevel.Information, ShelfLogArea)
            ?.Log(this, "Shelf GL context acquired; building renderer.");
        try
        {
            _gl = GL.GetApi(new LamdaNativeContext(name => gl.GetProcAddress(name)));
            var version = GlVersion;
            var dialect = version.Type == GlProfileType.OpenGLES
                ? GlslDialect.Es300
                : GlslDialect.Desktop;
            var accent = FocusedItem?.ShelfAccent ?? Colors.Gray;
            _renderer = MediaShellRenderer.Create(
                _gl, dialect, version.Major, version.Minor, ToLinear(accent));
            _uploadedAccent = accent;
            // A fresh renderer has an empty scene buffer, so the first frame must render in full.
            _lastRenderedSnapshot = null;
            _firstFrameLogged = false;
            Logger.TryGet(LogEventLevel.Information, ShelfLogArea)?.Log(
                this,
                "Shelf GL init succeeded in {Elapsed} ms using {Dialect}; {GlIdentity}",
                (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                dialect,
                DescribeGl());
            InitializationSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _renderer = null;
            _gl = null;
            Logger.TryGet(LogEventLevel.Error, ShelfLogArea)?.Log(
                this,
                "Shelf GL init failed after {Elapsed} ms: {Error}",
                (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                exception.Message);
            Fail(exception);
        }
    }

    /// <summary>The GPU identity behind the context, for the log — tells a Mesa/RADV Deck from Windows/ANGLE.</summary>
    private string DescribeGl()
    {
        try
        {
            return $"vendor={_gl!.GetStringS(Silk.NET.OpenGL.StringName.Vendor)}; "
                + $"renderer={_gl.GetStringS(Silk.NET.OpenGL.StringName.Renderer)}; "
                + $"version={_gl.GetStringS(Silk.NET.OpenGL.StringName.Version)}; "
                + $"glsl={_gl.GetStringS(Silk.NET.OpenGL.StringName.ShadingLanguageVersion)}";
        }
        catch (Exception exception)
        {
            return $"(GL identity unavailable: {exception.Message})";
        }
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_renderer is null)
        {
            return;
        }

        // Everything the frame draws is read from the immutable snapshot the UI thread last
        // published, never from this control's live lists — see <see cref="_frameSnapshot"/>. A null
        // snapshot means nothing has been published yet; an empty one is normal on the grid and
        // spotlight, where there is no physical media, only the captured UI to warp.
        var frame = _frameSnapshot;

        // The tube's configuration, its backdrop and the display scaling all ride in on the snapshot
        // too, resolved on the UI thread — the backdrop's theme-brush lookup and the scaling's
        // top-level walk both traverse the visual tree, which the render thread must not do. Only the
        // framebuffer size is still read live, straight from the GL viewport below, because it has to
        // match the framebuffer Avalonia sized for this exact frame; the snapshot's RenderScaling is
        // just the fallback for the first frame before a viewport exists.
        var crt = frame?.Crt ?? Crt;

        // An empty item list is normal, not a reason to skip the frame: the tube runs over every
        // couch layout, and on the grid and spotlight there is no physical media to draw — only the
        // captured UI to warp. Returning early here left those layouts with a stale surface.
        if ((frame is null || frame.Items.Count == 0) && !crt.IsActive)
        {
            return;
        }

        // Avalonia sizes its own framebuffer and leaves the viewport set to match before handing the
        // frame over, so the viewport it left is the true pixel size of the surface — which is not
        // always Bounds * RenderScaling. On a HiDPI panel where RenderScaling under-reports, computing
        // the size from it renders the tube into a fraction of the screen (see the couch shelf notes),
        // so query the surface and only fall back to the computed size before the first viewport is
        // reported. This read is safe on the render thread: nothing has touched the viewport yet.
        var surfaceSizeReported = _renderer.TryGetSurfaceSize(out var width, out var height);
        if (!surfaceSizeReported)
        {
            var scaling = frame?.RenderScaling ?? 1.0;
            width = (uint)Math.Max(1, Math.Round(Bounds.Width * scaling));
            height = (uint)Math.Max(1, Math.Round(Bounds.Height * scaling));
        }

        // The 3D scene in the supersampled buffer only changes when a new snapshot is published; a
        // redraw driven purely by the animated tube reuses the very same snapshot at the same size. In
        // that case the whole scene render — every cartridge, shadow and reflection — would draw an
        // identical picture, so skip it and re-run only the post pass over the buffer already there.
        var sceneUnchanged = crt.IsActive
            && frame is not null
            && ReferenceEquals(frame, _lastRenderedSnapshot)
            && width == _lastRenderedWidth
            && height == _lastRenderedHeight;

        try
        {
            _renderer.Crt = crt.IsActive && frame is not null
                ? crt with { Backdrop = frame.Backdrop }
                : crt;
            _renderer.CrtElapsedSeconds = (float)_crtClock.Elapsed.TotalSeconds;
            if (crt.IsActive)
            {
                UploadChrome(_renderer);
            }
            else
            {
                // Dropped here rather than when the setting changed, because releasing a texture
                // needs the GL context current and the setting changes on the UI thread. Without it,
                // switching the effect off and on again showed one frame of the couch UI as it
                // looked when it was last switched off.
                _renderer.ClearCrtChrome();
            }

            if (sceneUnchanged)
            {
                // Same scene, moving tube: re-run the CRT pass over the cached scene and the freshly
                // uploaded chrome, at the new time, without re-rendering anything in 3D.
                _renderer.RepresentShelf((uint)fb, width, height);
            }
            else
            {
                var accent = frame?.FocusedAccent ?? Colors.Gray;
                if (accent != _uploadedAccent)
                {
                    _renderer.SetAccent(ToLinear(accent));
                    _uploadedAccent = accent;
                }

                if (frame is not null)
                {
                    SynchronizeArtworkTextures(frame);
                }

                // main's camera now frames both axes, so the row's width travels with its height.
                _renderer.RenderShelf(
                    frame?.Items ?? [],
                    frame?.SceneMediaHeight ?? 1f,
                    frame?.SceneMediaWidth ?? 1f,
                    (uint)fb,
                    width,
                    height,
                    (float)(frame?.FillScale ?? 1.0));

                _lastRenderedSnapshot = frame;
                _lastRenderedWidth = width;
                _lastRenderedHeight = height;
            }

            // A clean frame clears the transient-failure count that keeps a one-off from ever
            // reaching the give-up threshold.
            _consecutiveRenderFailures = 0;

            if (!_firstFrameLogged)
            {
                _firstFrameLogged = true;
                Logger.TryGet(LogEventLevel.Information, ShelfLogArea)?.Log(
                    this,
                    "Shelf first frame drawn at {Width}x{Height} px (viewportReported={Reported}, crtActive={Crt}).",
                    width,
                    height,
                    surfaceSizeReported,
                    crt.IsActive);
                _lastLoggedSurfaceWidth = width;
                _lastLoggedSurfaceHeight = height;
            }
            else if (width != _lastLoggedSurfaceWidth || height != _lastLoggedSurfaceHeight)
            {
                // The surface resized after the first frame — normally the window going full screen on
                // entry. Logged so a surface that never grows to the window (the tube not covering the
                // live rail) is distinguishable from one that tracks the resize as it should.
                Logger.TryGet(LogEventLevel.Information, ShelfLogArea)?.Log(
                    this,
                    "Shelf surface resized {Old} -> {New} px (boundsDip={Bounds}, scaling={Scaling}).",
                    $"{_lastLoggedSurfaceWidth}x{_lastLoggedSurfaceHeight}",
                    $"{width}x{height}",
                    $"{Bounds.Width:0}x{Bounds.Height:0}",
                    frame?.RenderScaling ?? 1.0);
                _lastLoggedSurfaceWidth = width;
                _lastLoggedSurfaceHeight = height;
            }

            // A tube whose roll, hum and jitter are moving has to be redrawn even when nothing in
            // the library changed, so the scene cannot go back to drawing only on demand. This is
            // the whole cost of the animated effects — it holds the couch screen at the compositor's
            // frame rate for as long as couch mode is open, which on a handheld is a battery
            // decision as much as a visual one. Turning the animation knobs off returns the scene to
            // its old on-demand behaviour rather than merely freezing a still tube.
            if (crt.IsAnimated)
            {
                RequestNextFrameRendering();
            }
        }
        catch (Exception exception)
        {
            // A half-written scene buffer must not be re-presented as if it were whole, so the next
            // frame is forced back onto the full render path rather than the animation shortcut.
            _lastRenderedSnapshot = null;

            // A render exception is no longer treated as an unsupported GPU on sight. The snapshot
            // above already removed the cross-thread races that used to throw here, so a stray
            // exception is far more likely to be transient — a bitmap disposed a frame before its
            // upload, a momentary driver fault — and dropping the whole shelf to flat covers for the
            // session over one of those is the bug this pairs with. Keep the renderer and try the
            // next frame; only a fault that repeats every frame is taken as a real failure.
            if (++_consecutiveRenderFailures >= MaxConsecutiveRenderFailures)
            {
                _renderer.Dispose();
                _renderer = null;
                Fail(exception);
                return;
            }

            RequestNextFrameRendering();
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _renderer?.Dispose();
        _renderer = null;
        _gl = null;
        _lastRenderedSnapshot = null;
        ClearUploadedCoverState();
        ContextLost?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnOpenGlLost()
    {
        _renderer = null;
        _gl = null;
        _lastRenderedSnapshot = null;
        ClearUploadedCoverState();
        ContextLost?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Freezes the current library state into the description the render thread draws from.
    /// </summary>
    /// <remarks>
    /// Runs on the UI thread — every caller is a property change, a collection change, or a decode
    /// that completed back on it — which is exactly what makes reading <see cref="_layout"/>,
    /// <see cref="_gamesByKey"/> and the artwork caches here safe when it was not in the frame. The
    /// window it resolves is small (at most the neighbour radius each side of focus), so rebuilding
    /// it per selection step or per animated position is cheap; the render thread then never reaches
    /// back into any of those lists.
    /// </remarks>
    private void PublishFrame()
    {
        var items = BuildRenderItems();
        var accent = FocusedItem?.ShelfAccent ?? Colors.Gray;
        var crt = Crt;
        _frameSnapshot = new FrameSnapshot(
            items,
            _sceneMediaHeight,
            _sceneMediaWidth,
            accent,
            ResolveArtworkMap(items),
            crt,
            // Only the tube consumes the backdrop, and resolving it means a theme-brush lookup up the
            // visual tree; a shelf with the effect off has no use for it.
            crt.IsActive ? ResolveBackdrop(accent) : default,
            TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0,
            FrameFillScale);
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Returns the resolved face artwork for the visible items, rebuilding it only when it moved.
    /// </summary>
    /// <remarks>
    /// The cache is reused across a glide's position-only publishes and rebuilt from scratch — into
    /// a fresh map, so any published copy stays immutable — the moment the visible keys change or
    /// <see cref="_artworkGeneration"/> is bumped by a decode, an eviction, a cover, or a warmed
    /// placeholder.
    /// </remarks>
    private IReadOnlyDictionary<long, IImage?[]> ResolveArtworkMap(IReadOnlyList<MediaShelfRenderItem> items)
    {
        var keys = new long[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            keys[index] = items[index].Key;
        }

        if (_artworkCache is not null &&
            _artworkCacheGeneration == _artworkGeneration &&
            _artworkCacheKeys.AsSpan().SequenceEqual(keys))
        {
            return _artworkCache;
        }

        var artwork = new Dictionary<long, IImage?[]>(items.Count);
        foreach (var item in items)
        {
            if (artwork.ContainsKey(item.Key) || !_gamesByKey.TryGetValue(item.Key, out var game))
            {
                continue;
            }

            var faces = new IImage?[MediaShellRenderer.MaxArtworkFaces];
            foreach (var face in Faces)
            {
                faces[(int)face] = ResolveArtwork(game, face);
            }

            artwork[item.Key] = faces;
        }

        _artworkCache = artwork;
        _artworkCacheKeys = keys;
        _artworkCacheGeneration = _artworkGeneration;
        return artwork;
    }

    /// <summary>Marks the resolved-artwork cache stale, so the next publish rebuilds it.</summary>
    private void InvalidateArtwork() => _artworkGeneration++;

    private IReadOnlyList<MediaShelfRenderItem> BuildRenderItems()
    {
        if (_layout.Count == 0)
        {
            return [];
        }

        var position = Math.Clamp(ShelfPosition, 0d, _layout.Count - 1d);
        var anchor = CentreAt(position);
        var centreIndex = (int)Math.Round(position);
        var start = Math.Max(0, centreIndex - NeighbourRadius);
        var end = Math.Min(_layout.Count - 1, centreIndex + NeighbourRadius);
        var renderItems = new List<MediaShelfRenderItem>((NeighbourRadius * 2) + 1);

        for (var index = start; index <= end; index++)
        {
            var entry = _layout[index];
            var distance = Math.Abs(index - position);
            var focus = (float)Math.Clamp(1d - distance, 0d, 1d);
            var isFocused = index == _focusedIndex;
            var hasDeparture = _departurePoses.TryGetValue(entry.Game.Id, out var departure);
            var pose = ResolvePose(
                focus,
                isFocused,
                (float)Yaw,
                (float)Pitch,
                hasDeparture ? departure : null);
            PhysicalShelfLaunchPose? launch = LaunchPose is { } candidate && candidate.GameId == entry.Game.Id
                ? candidate
                : null;
            renderItems.Add(new MediaShelfRenderItem(
                entry.Game.Id,
                entry.Game.ShelfMediaProfile,
                entry.Centre - anchor,
                focus,
                launch?.Yaw ?? pose.Yaw,
                launch?.Pitch ?? pose.Pitch,
                ToLinear(entry.Game.ShelfAccent),
                launch?.VerticalOffset ?? 0f,
                launch?.DepthOffset ?? 0f,
                launch?.Scale ?? 1f,
                ToRenderDiscPose(launch?.Disc)));

            if (!isFocused && hasDeparture && focus <= 0.001f)
            {
                _departurePoses.Remove(entry.Game.Id);
            }
        }

        return renderItems;
    }

    /// <summary>
    /// Carries the choreography's disc across into the renderer's own vocabulary.
    /// </summary>
    /// <remarks>
    /// A restatement rather than a shared type, for the same reason the launch offsets beside it
    /// are: EmuShelf.Rendering knows about media and nothing about the app's services, and the
    /// alternative to copying six floats here is a reference the wrong way down the stack.
    /// </remarks>
    private static MediaShelfDiscPose? ToRenderDiscPose(PhysicalShelfDiscPose? disc) =>
        disc is { } pose
            ? new MediaShelfDiscPose(
                pose.HorizontalOffset,
                pose.VerticalOffset,
                pose.DepthOffset,
                pose.Spin,
                pose.Tilt,
                pose.Flip,
                pose.Scale)
            : null;

    /// <summary>
    /// The angle one shelf item is turned to, blended by how close it is to the centre.
    /// </summary>
    /// <remarks>
    /// Arrival and departure use the same blend on purpose. The focused item used to take the
    /// focused angle the instant selection changed, while the outgoing one eased back to the
    /// neighbour angle — so every step turned one cartridge smoothly and snapped the other through
    /// the ~14 degrees between the two rest poses, before it had travelled anywhere.
    /// </remarks>
    internal static (float Yaw, float Pitch) ResolvePose(
        float focus,
        bool isFocused,
        float focusedYaw,
        float focusedPitch,
        PhysicalShelfDeparturePose? departure)
    {
        var amount = Math.Clamp(focus, 0f, 1f);

        if (isFocused)
        {
            return (
                float.Lerp(NeighbourYaw, focusedYaw, amount),
                focusedPitch * amount);
        }

        if (departure is not { } outgoing)
        {
            return (NeighbourYaw, 0f);
        }

        return (
            float.Lerp(NeighbourYaw, outgoing.Yaw, amount),
            outgoing.Pitch * amount);
    }

    private void RememberDeparturePose(PhysicalShelfDeparturePose pose)
    {
        if (!_departurePoses.ContainsKey(pose.GameKey) && _departurePoses.Count >= (NeighbourRadius * 2) + 1)
        {
            _departurePoses.Remove(_departurePoses.Keys.First());
        }

        _departurePoses[pose.GameKey] = pose;
    }

    /// <summary>
    /// Brings the GPU's per-face textures in line with the artwork the snapshot resolved.
    /// </summary>
    /// <remarks>
    /// Runs on the render thread, and deliberately reads only the frozen snapshot and the upload
    /// bookkeeping that is this thread's alone (<see cref="_uploadedCovers"/>,
    /// <see cref="_coverLru"/>). The artwork itself was resolved on the UI thread in
    /// <see cref="PublishFrame"/>, so nothing here reaches into the decoded-artwork caches the UI
    /// thread is free to rebuild underneath it.
    /// </remarks>
    private void SynchronizeArtworkTextures(FrameSnapshot frame)
    {
        if (_renderer is null)
        {
            return;
        }

        foreach (var item in frame.Items)
        {
            if (!_uploadedCovers.TryGetValue(item.Key, out var uploaded))
            {
                uploaded = new UploadedCover(_coverLru.AddFirst(item.Key));
                _uploadedCovers[item.Key] = uploaded;
            }
            else
            {
                TouchCover(uploaded);
            }

            frame.Artwork.TryGetValue(item.Key, out var faces);
            foreach (var face in Faces)
            {
                var artwork = faces?[(int)face];
                if (ReferenceEquals(uploaded.Faces[(int)face], artwork))
                {
                    continue;
                }

                if (!TryBuildFaceTexture(artwork, out var texture))
                {
                    // The artwork raced disposal — see TryBuildFaceTexture. Keep whatever is already
                    // on the GPU for this face and do not record the upload, so a later clean frame
                    // retries. A per-face skip, never the whole draw.
                    continue;
                }

                _renderer.SetPanelArt(item.Key, (int)face, texture);
                uploaded.Faces[(int)face] = artwork;
            }
        }

        // Evict whole games, but measure the budget in textures: one game can be holding three.
        var uploadedTextures = _uploadedCovers.Values.Sum(cover => cover.UploadedFaceCount);
        while (uploadedTextures > CoverTextureBudget && _coverLru.Last is { } oldest)
        {
            if (_uploadedCovers.Remove(oldest.Value, out var evicted))
            {
                uploadedTextures -= evicted.UploadedFaceCount;
            }

            _renderer.RemoveCoverArt(oldest.Value);
            _coverLru.RemoveLast();
        }
    }

    private static readonly ShelfArtworkFace[] Faces =
    [
        ShelfArtworkFace.Front,
        ShelfArtworkFace.Back,
        ShelfArtworkFace.Spine,
        ShelfArtworkFace.DiscLabel,
    ];

    private IImage? ResolveArtwork(GameViewModel game, ShelfArtworkFace face)
    {
        var path = game.ShelfArtworkPath(face);
        DecodedArtwork? decoded = null;
        var hasDecoded =
            !string.IsNullOrWhiteSpace(path) &&
            _decodedPhysicalArtwork.TryGetValue(new ArtworkKey(game.Id, face), out decoded) &&
            string.Equals(decoded.Path, path, StringComparison.Ordinal);

        var kind = ArtworkKindFor(game.ShelfMediaProfile, face, hasDecoded);
        switch (kind)
        {
            case ShelfArtworkKind.Cover:
                return game.CoverImage;

            case ShelfArtworkKind.PhysicalMediaTexture when decoded is not null:
                TouchPhysicalArtwork(decoded);
                return decoded.Image;

            case ShelfArtworkKind.PlaceholderLabel:
                // A cartridge with no selected/decoded support texture wears the blank-label
                // placeholder: platform medallion and "artwork missing", the same vocabulary the
                // 2D grid uses. Portrait box art is packaging and is still never cropped onto a
                // cartridge label.
                return CartridgeLabelPlaceholder.TryGet(game.SystemId);

            default:
                // An unscraped back or spine keeps the platform tint the shader already paints
                // there. A case with a front and no back is the common state, not a failure.
                return null;
        }
    }

    /// <summary>
    /// Draws the blank labels for the systems on this shelf, on the UI thread, so the GL frame can
    /// take them straight from the cache.
    /// </summary>
    private void WarmLabelPlaceholders()
    {
        if (Items is null)
        {
            return;
        }

        foreach (var game in Items)
        {
            var slots = game.ShelfMediaProfile.ArtworkSlots;
            if ((slots & (PhysicalArtworkSlots.CartridgeSupport | PhysicalArtworkSlots.DiscLabel)) == 0)
            {
                continue;
            }

            // The disc's label belongs to the disc, so it is drawn at the disc's proportions rather
            // than the case's. Taking the case's would letterbox a square label into a portrait
            // sleeve's shape and then print it on a circle, which is two wrongs and no right.
            var labelShell = (slots & PhysicalArtworkSlots.DiscLabel) != 0
                ? MediaShell.GameDisc
                : game.ShelfMediaProfile.Shell;

            // Needs the shell's own label proportions, which are only known once its asset has
            // finished decoding. Warming is retried on every list change and every prepared-shell
            // callback, so a label missed here is drawn moments later rather than lost.
            if (MediaShellCatalog.TryGetPanelAspect(labelShell) is { } aspect)
            {
                CartridgeLabelPlaceholder.Warm(
                    game.SystemId, game.SystemName, game.ShelfAccent, game.PlatformArtwork, aspect);
            }
        }
    }

    /// <summary>What a given face of a given medium should be painted with.</summary>
    internal static ShelfArtworkKind ArtworkKindFor(
        PhysicalMediaProfile profile,
        ShelfArtworkFace face,
        bool hasDecodedArtwork)
    {
        var slots = profile.ArtworkSlots;
        if (face == ShelfArtworkFace.Front)
        {
            // A cartridge label is the one face that refuses the scraped cover: box art is
            // packaging, and a portrait scan cropped to a landscape label is not a cartridge.
            if ((slots & PhysicalArtworkSlots.CartridgeSupport) == 0)
            {
                return ShelfArtworkKind.Cover;
            }

            return hasDecodedArtwork
                ? ShelfArtworkKind.PhysicalMediaTexture
                : ShelfArtworkKind.PlaceholderLabel;
        }

        var wanted = face switch
        {
            ShelfArtworkFace.Back => PhysicalArtworkSlots.Back,
            ShelfArtworkFace.DiscLabel => PhysicalArtworkSlots.DiscLabel,
            _ => PhysicalArtworkSlots.Spine,
        };
        if ((slots & wanted) == 0)
        {
            return ShelfArtworkKind.None;
        }

        // A disc's face behaves like a cartridge's, not like a case's back. Both are the printed
        // surface of the medium itself, so an unscraped one says so with the same blank label the
        // grid and the cartridges use — where an unscraped back or spine has nothing to say and
        // keeps the platform tint. A disc left bare reads as a finish rather than as missing art.
        if (face == ShelfArtworkFace.DiscLabel)
        {
            return hasDecodedArtwork
                ? ShelfArtworkKind.PhysicalMediaTexture
                : ShelfArtworkKind.PlaceholderLabel;
        }

        return hasDecodedArtwork ? ShelfArtworkKind.PhysicalMediaTexture : ShelfArtworkKind.None;
    }

    private void TouchCover(UploadedCover cover)
    {
        _coverLru.Remove(cover.Node);
        _coverLru.AddFirst(cover.Node);
    }

    private void ClearUploadedCoverState()
    {
        _uploadedCovers.Clear();
        _coverLru.Clear();
    }

    private float CentreAt(double position)
    {
        var lower = Math.Clamp((int)Math.Floor(position), 0, _layout.Count - 1);
        var upper = Math.Min(_layout.Count - 1, lower + 1);
        var fraction = (float)(position - lower);
        return float.Lerp(_layout[lower].Centre, _layout[upper].Centre, fraction);
    }

    private static int IndexOf(IReadOnlyList<GameViewModel> items, GameViewModel game)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], game))
            {
                return index;
            }
        }

        return -1;
    }

    private void RebuildLayout()
    {
        _layout.Clear();
        _gamesByKey.Clear();
        if (Items is not { Count: > 0 })
        {
            _focusedIndex = -1;
            ClearDecodedPhysicalArtwork();
            return;
        }

        var cursor = 0f;
        var tallest = 0f;
        var widest = 0f;
        foreach (var game in Items)
        {
            var profile = game.ShelfMediaProfile;
            // The turning circle, not the face width: every medium on this shelf is turned, and a
            // medium deeper than it is wide occupies far more of the row than its front suggests.
            var width = profile.TurningWidthInShelfUnits;
            var centre = cursor + (width * 0.5f);
            _layout.Add(new LayoutEntry(game, centre));
            _gamesByKey[game.Id] = game;
            cursor += width + ItemGap;
            // The camera frames the tallest and widest media in the whole view, not the visible
            // window, so scrolling a mixed row past a keep case cannot make the world zoom.
            tallest = MathF.Max(
                tallest, profile.HeightInShelfUnits + profile.FloorClearanceInShelfUnits);
            widest = MathF.Max(widest, width);
        }

        _sceneMediaHeight = tallest;
        _sceneMediaWidth = widest;
        WarmLabelPlaceholders();

        _focusedIndex = FocusedItem is null ? -1 : IndexOf(Items, FocusedItem);
        PruneDecodedPhysicalArtwork();
        // A structural rebuild can warm a placeholder or reorder the row without touching the decoded
        // caches, and it can produce the same visible keys as before (a refilter that lands on the
        // same window), so the artwork cache cannot be trusted to notice on its own.
        InvalidateArtwork();
    }

    private void ObserveCollection()
    {
        if (_observedCollection is not null)
        {
            _observedCollection.CollectionChanged -= OnItemsCollectionChanged;
        }

        _observedCollection = _isAttached ? Items as INotifyCollectionChanged : null;
        if (_observedCollection is not null)
        {
            _observedCollection.CollectionChanged += OnItemsCollectionChanged;
        }
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildLayout();
        UpdateVisibleSubscriptions(force: true);
        PrepareShells();
        PublishFrame();
    }

    private void PrepareShells()
    {
        if (!_isAttached)
        {
            return;
        }

        var generation = ++_preparationGeneration;
        var profiles = Items?.Select(game => game.ShelfMediaProfile).ToArray() ?? [];
        var shells = profiles
            .Select(profile => profile.Shell)
            // The disc is not any game's own shell, so nothing else would ask for it — and the
            // first frame that needs one is the launch, where a shell still decoding draws nothing
            // at all. Preparing it with the row costs a generated mesh and no file.
            .Concat(profiles.Any(profile => profile.HasDisc) ? [MediaShell.GameDisc] : [])
            .Distinct()
            .ToArray();
        if (shells.Length == 0)
        {
            return;
        }

        _ = AwaitPreparedShellsAsync(shells, generation);
    }

    private async Task AwaitPreparedShellsAsync(MediaShell[] shells, int generation)
    {
        try
        {
            await Task.WhenAll(shells.Select(MediaShellCatalog.PrepareAsync)).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation == _preparationGeneration)
                {
                    // The blank label can only be drawn at the shell's panel proportions, and those
                    // are unknown until the asset has decoded — which is after the list arrives. Warm
                    // again here or the first frames of a session show the bare accent tint, and the
                    // label only appears once something else happens to rebuild the layout. That was
                    // visible as a cartridge whose placeholder arrived only after changing platform.
                    WarmLabelPlaceholders();
                    // A placeholder that was null when last resolved now draws, so the cached
                    // artwork for the unchanged visible keys has to be rebuilt.
                    InvalidateArtwork();
                    PublishFrame();
                }
            });
        }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation == _preparationGeneration)
                {
                    Fail(exception);
                }
            });
        }
    }

    private void StopObserving()
    {
        if (_observedCollection is not null)
        {
            _observedCollection.CollectionChanged -= OnItemsCollectionChanged;
            _observedCollection = null;
        }

        foreach (var game in _observedGames)
        {
            game.PropertyChanged -= OnVisibleGamePropertyChanged;
        }

        _observedGames.Clear();
        _observedStart = -1;
        _observedEnd = -1;
    }

    private void UpdateVisibleSubscriptions(bool force = false)
    {
        if (!_isAttached)
        {
            return;
        }

        var start = -1;
        var end = -1;
        var centre = -1;
        if (Items is { Count: > 0 })
        {
            centre = Math.Clamp((int)Math.Round(ShelfPosition), 0, Items.Count - 1);
            start = Math.Max(0, centre - NeighbourRadius);
            end = Math.Min(Items.Count - 1, centre + NeighbourRadius);
        }

        if (!force && start == _observedStart && end == _observedEnd)
        {
            return;
        }

        _observedStart = start;
        _observedEnd = end;

        foreach (var game in _observedGames.ToArray())
        {
            var wanted = false;
            if (Items is not null)
            {
                for (var index = start; index <= end; index++)
                {
                    if (index >= 0 && ReferenceEquals(Items[index], game))
                    {
                        wanted = true;
                        break;
                    }
                }
            }

            if (!wanted)
            {
                game.PropertyChanged -= OnVisibleGamePropertyChanged;
                _observedGames.Remove(game);
            }
        }

        if (Items is null)
        {
            return;
        }

        for (var index = start; index <= end; index++)
        {
            if (index < 0)
            {
                continue;
            }

            var game = Items[index];
            if (_observedGames.Contains(game))
            {
                continue;
            }

            game.PropertyChanged += OnVisibleGamePropertyChanged;
            _observedGames.Add(game);
        }

        CancelPhysicalArtworkLoadsOutsideVisibleWindow();

        for (var index = start; index <= end; index++)
        {
            if (index >= 0)
            {
                QueuePhysicalArtworkLoad(Items[index], pumpImmediately: false);
            }
        }

        PrioritizePhysicalArtworkQueue(centre);
        PumpPhysicalArtworkQueue();
    }

    private void OnVisibleGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is GameViewModel game && FaceForPathProperty(e.PropertyName) is { } face)
        {
            RemoveDecodedPhysicalArtwork(new ArtworkKey(game.Id, face));
            QueuePhysicalArtworkLoad(game);
            PublishFrame();
            return;
        }

        if (e.PropertyName is nameof(GameViewModel.CoverImage))
        {
            // The cover is the front face of any medium without its own support texture, so a cover
            // arriving changes what a visible face wears.
            InvalidateArtwork();
            PublishFrame();
        }
    }

    /// <summary>Which face a changed path property belongs to, or null for anything else.</summary>
    private static ShelfArtworkFace? FaceForPathProperty(string? propertyName) => propertyName switch
    {
        nameof(GameViewModel.PhysicalMediaTexturePath) => ShelfArtworkFace.Front,
        nameof(GameViewModel.BoxBackPath) => ShelfArtworkFace.Back,
        nameof(GameViewModel.BoxSpinePath) => ShelfArtworkFace.Spine,
        _ => null,
    };

    private void QueuePhysicalArtworkLoad(GameViewModel game, bool pumpImmediately = true)
    {
        foreach (var face in Faces)
        {
            QueueFaceLoad(game, face);
        }

        if (pumpImmediately)
        {
            PumpPhysicalArtworkQueue();
        }
    }

    private void QueueFaceLoad(GameViewModel game, ShelfArtworkFace face)
    {
        if (game.ShelfArtworkPath(face) is not { Length: > 0 } path)
        {
            return;
        }

        var key = new ArtworkKey(game.Id, face);
        if (_decodedPhysicalArtwork.TryGetValue(key, out var decoded))
        {
            if (string.Equals(decoded.Path, path, StringComparison.Ordinal))
            {
                TouchPhysicalArtwork(decoded);
                return;
            }

            RemoveDecodedPhysicalArtwork(key);
        }

        if (_physicalArtworkLoads.TryGetValue(key, out var existingLoad))
        {
            if (string.Equals(existingLoad.Path, path, StringComparison.Ordinal))
            {
                return;
            }

            CancelPhysicalArtworkLoad(existingLoad);
        }

        var load = new PhysicalArtworkLoad(key, path);
        _physicalArtworkLoads[key] = load;
        _physicalArtworkQueue.Enqueue(load);
    }

    private void PumpPhysicalArtworkQueue()
    {
        while (_activePhysicalArtworkDecodes < MaximumConcurrentPhysicalArtworkDecodes &&
               _physicalArtworkQueue.TryDequeue(out var load))
        {
            if (load.IsCancelled ||
                !_physicalArtworkLoads.TryGetValue(load.Key, out var currentLoad) ||
                !ReferenceEquals(currentLoad, load) ||
                !IsPhysicalArtworkVisible(load.Key.GameId))
            {
                continue;
            }

            _activePhysicalArtworkDecodes++;
            _ = DecodePhysicalArtworkAsync(load);
        }
    }

    private async Task DecodePhysicalArtworkAsync(PhysicalArtworkLoad load)
    {
        Bitmap? bitmap = null;
        try
        {
            bitmap = await Task.Run(() => SafeImageDecoder.DecodeToFit(
                load.Path, PhysicalArtworkDecodeSize, PhysicalArtworkDecodeSize)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Scraped media can be removed while the app is open, fail header validation, or be
            // rejected by the platform image codec. The authored blank label is the complete
            // fallback; a bad optional asset must not disable GL or fault a fire-and-forget task.
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _activePhysicalArtworkDecodes--;
                var isCurrentLoad = _physicalArtworkLoads.TryGetValue(load.Key, out var currentLoad) &&
                                    ReferenceEquals(currentLoad, load);
                if (isCurrentLoad)
                {
                    _physicalArtworkLoads.Remove(load.Key);
                }

                if (bitmap is null || load.IsCancelled || !isCurrentLoad || !_isAttached ||
                    !IsPhysicalArtworkVisible(load.Key.GameId) ||
                    !_gamesByKey.TryGetValue(load.Key.GameId, out var currentGame) ||
                    !string.Equals(currentGame.ShelfArtworkPath(load.Key.Face), load.Path, StringComparison.Ordinal))
                {
                    bitmap?.Dispose();
                }
                else
                {
                    AddDecodedPhysicalArtwork(load.Key, load.Path, bitmap);
                    PublishFrame();
                }

                PumpPhysicalArtworkQueue();
            });
            bitmap = null;
        }
        catch (Exception)
        {
            // The dispatcher can be shutting down after the visual tree detached. In that case the
            // decoded bitmap never changed ownership and must be released here.
            bitmap?.Dispose();
        }
    }

    private bool IsPhysicalArtworkVisible(long gameId) =>
        _observedGames.Any(game => game.Id == gameId);

    private void CancelPhysicalArtworkLoadsOutsideVisibleWindow()
    {
        foreach (var load in _physicalArtworkLoads.Values.ToArray())
        {
            if (!IsPhysicalArtworkVisible(load.Key.GameId))
            {
                CancelPhysicalArtworkLoad(load);
            }
        }

        if (_physicalArtworkQueue.Count == 0)
        {
            return;
        }

        var retained = _physicalArtworkQueue.Where(load => !load.IsCancelled).ToArray();
        _physicalArtworkQueue.Clear();
        foreach (var load in retained)
        {
            _physicalArtworkQueue.Enqueue(load);
        }
    }

    private void PrioritizePhysicalArtworkQueue(int centre)
    {
        if (_physicalArtworkQueue.Count < 2 || Items is null || centre < 0)
        {
            return;
        }

        var pending = _physicalArtworkQueue
            .Where(load => !load.IsCancelled)
            .OrderBy(load => DistanceFromFocusedGame(load.Key.GameId, centre))
            .ToArray();
        _physicalArtworkQueue.Clear();
        foreach (var load in pending)
        {
            _physicalArtworkQueue.Enqueue(load);
        }
    }

    private int DistanceFromFocusedGame(long gameId, int centre)
    {
        if (Items is null)
        {
            return int.MaxValue;
        }

        for (var index = Math.Max(0, centre - NeighbourRadius);
             index <= Math.Min(Items.Count - 1, centre + NeighbourRadius);
             index++)
        {
            if (Items[index].Id == gameId)
            {
                return Math.Abs(index - centre);
            }
        }

        return int.MaxValue;
    }

    private void CancelPhysicalArtworkLoad(PhysicalArtworkLoad load)
    {
        load.IsCancelled = true;
        if (_physicalArtworkLoads.TryGetValue(load.Key, out var currentLoad) &&
            ReferenceEquals(currentLoad, load))
        {
            _physicalArtworkLoads.Remove(load.Key);
        }
    }

    private void AddDecodedPhysicalArtwork(ArtworkKey key, string path, Bitmap bitmap)
    {
        RemoveDecodedPhysicalArtwork(key);
        var node = _physicalArtworkLru.AddFirst(key);
        _decodedPhysicalArtwork[key] = new DecodedArtwork(path, bitmap, node);

        while (_decodedPhysicalArtwork.Count > PhysicalArtworkCacheCapacity &&
               _physicalArtworkLru.Last is { } oldest)
        {
            RemoveDecodedPhysicalArtwork(oldest.Value);
        }

        InvalidateArtwork();
    }

    private void TouchPhysicalArtwork(DecodedArtwork artwork)
    {
        _physicalArtworkLru.Remove(artwork.Node);
        _physicalArtworkLru.AddFirst(artwork.Node);
    }

    private void RemoveDecodedPhysicalArtwork(ArtworkKey key)
    {
        if (!_decodedPhysicalArtwork.Remove(key, out var artwork))
        {
            return;
        }

        _physicalArtworkLru.Remove(artwork.Node);
        artwork.Image.Dispose();
        InvalidateArtwork();
    }

    private void PruneDecodedPhysicalArtwork()
    {
        foreach (var key in _decodedPhysicalArtwork.Keys.ToArray())
        {
            if (!_gamesByKey.ContainsKey(key.GameId))
            {
                RemoveDecodedPhysicalArtwork(key);
            }
        }
    }

    private void ClearDecodedPhysicalArtwork()
    {
        foreach (var artwork in _decodedPhysicalArtwork.Values)
        {
            artwork.Image.Dispose();
        }

        _decodedPhysicalArtwork.Clear();
        _physicalArtworkLru.Clear();
        foreach (var load in _physicalArtworkLoads.Values)
        {
            load.IsCancelled = true;
        }

        _physicalArtworkLoads.Clear();
        _physicalArtworkQueue.Clear();
        InvalidateArtwork();
    }

    private void Fail(Exception exception)
    {
        if (_failed)
        {
            return;
        }

        _failed = true;
        InitializationFailed?.Invoke(this, exception);
    }

    private static Vector3 ToLinear(Color colour) =>
        MediaShellRenderer.ToLinear(colour.R / 255f, colour.G / 255f, colour.B / 255f);

    /// <summary>
    /// Builds one shell face's GPU texture from its artwork, or reports that the artwork raced
    /// disposal and the face should be skipped.
    /// </summary>
    /// <remarks>
    /// The frame snapshot holds a live reference to the bitmap, but the UI thread can still dispose it
    /// — an eviction, a path change, a detach, or (the reported case) a scrape swapping the cover —
    /// between the frame being published and this upload reading its pixels. Reading a disposed bitmap
    /// throws, and this contains that throw to the single face: it returns <c>false</c> so the caller
    /// keeps whatever is on the GPU and retries on a later clean frame, rather than letting the
    /// exception escape into <see cref="OnOpenGlRender"/> and march the whole shelf toward the
    /// flat-cover fallback via the consecutive-failure counter — which is exactly how a scrape could
    /// blank the CRT and every model. Extracted so this invariant can be tested without a GL context;
    /// a returned <c>true</c> with a null texture is the normal no-artwork face.
    /// </remarks>
    internal static bool TryBuildFaceTexture(object? artwork, out TextureImage? texture)
    {
        try
        {
            texture = artwork is Bitmap bitmap ? ToTextureImage(bitmap) : null;
            return true;
        }
        catch (Exception)
        {
            texture = null;
            return false;
        }
    }

    private static TextureImage? ToTextureImage(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return null;
        }

        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(
                new PixelRect(0, 0, size.Width, size.Height),
                handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        var swapRedAndBlue = bitmap.Format != PixelFormat.Rgba8888;
        var premultiplied = bitmap.AlphaFormat != AlphaFormat.Unpremul;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            if (swapRedAndBlue)
            {
                (pixels[index], pixels[index + 2]) = (pixels[index + 2], pixels[index]);
            }

            var alpha = pixels[index + 3];
            if (premultiplied && alpha is > 0 and < 255)
            {
                pixels[index] = (byte)Math.Min(255, pixels[index] * 255 / alpha);
                pixels[index + 1] = (byte)Math.Min(255, pixels[index + 1] * 255 / alpha);
                pixels[index + 2] = (byte)Math.Min(255, pixels[index + 2] * 255 / alpha);
            }
        }

        return new TextureImage { Width = size.Width, Height = size.Height, Rgba = pixels };
    }

    private sealed record LayoutEntry(GameViewModel Game, float Centre);

    /// <summary>
    /// One frame's worth of drawing, frozen on the UI thread for the render thread to consume.
    /// </summary>
    /// <remarks>
    /// Every field is either an immutable value or a collection that is built once and never
    /// mutated after publication, so the render thread can read it with no lock while the UI thread
    /// goes on rebuilding this control's live lists for the next one. <see cref="Artwork"/> maps a
    /// game's key to its resolved face images, indexed by <see cref="ShelfArtworkFace"/>, so the
    /// render thread decides what to upload without touching the decoded-artwork caches.
    /// <see cref="Crt"/>, <see cref="Backdrop"/> and <see cref="RenderScaling"/> are captured here as
    /// well because their live reads walked the visual tree — the backdrop resolves a theme brush and
    /// the scaling reaches the top level — which is the UI thread's alone to do.
    /// </remarks>
    private sealed record FrameSnapshot(
        IReadOnlyList<MediaShelfRenderItem> Items,
        float SceneMediaHeight,
        float SceneMediaWidth,
        Color FocusedAccent,
        IReadOnlyDictionary<long, IImage?[]> Artwork,
        CrtPresentation Crt,
        Vector3 Backdrop,
        double RenderScaling,
        double FillScale);

    private sealed class UploadedCover(LinkedListNode<long> node)
    {
        /// <summary>What is currently on the GPU for each face, so unchanged faces are not re-uploaded.</summary>
        /// <remarks>
        /// Sized from the renderer's own panel count rather than a second literal three, so the two
        /// cannot drift apart across the assembly boundary.
        /// </remarks>
        public IImage?[] Faces { get; } = new IImage?[MediaShellRenderer.MaxArtworkFaces];

        /// <summary>How many of this game's faces are actually uploaded, for the texture budget.</summary>
        public int UploadedFaceCount => Faces.Count(face => face is not null);

        public LinkedListNode<long> Node { get; } = node;
    }

    /// <summary>One decodable face of one game — the unit the artwork caches are keyed by.</summary>
    private readonly record struct ArtworkKey(long GameId, ShelfArtworkFace Face);

    private sealed record DecodedArtwork(
        string Path,
        Bitmap Image,
        LinkedListNode<ArtworkKey> Node);

    private sealed class PhysicalArtworkLoad(ArtworkKey key, string path)
    {
        public ArtworkKey Key { get; } = key;

        public string Path { get; } = path;

        public bool IsCancelled { get; set; }
    }
}

internal enum ShelfArtworkKind
{
    /// <summary>Leave the shader's platform tint on this face.</summary>
    None,

    /// <summary>The scraped cover the library already has decoded for the grid.</summary>
    Cover,

    /// <summary>A separately scraped face, decoded off the UI thread for the visible window.</summary>
    PhysicalMediaTexture,

    /// <summary>The drawn "artwork missing" label, for a cartridge with no support texture.</summary>
    PlaceholderLabel,
}
