using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Rendering;

namespace EmuShelf.App.Controls;

/// <summary>
/// Lazily attaches the expensive OpenGL shelf only while couch shelf mode is actually active and
/// converts framework-level GL initialization silence into the app's designed flat-cover fallback.
/// </summary>
public sealed class MediaShelf3DHost : ContentControl
{
    /// <summary>
    /// How long a scene is given to bring up a GL context before the flat-cover fallback is taken as
    /// the real answer.
    /// </summary>
    /// <remarks>
    /// Deliberately generous. Some drivers — the Steam Deck's Mesa stack among them — are slow to hand
    /// out a context the first time, and the renderer then links five shader programs and bakes an
    /// environment cubemap before it reports success, all synchronously inside <c>OnOpenGlInit</c>. A
    /// single long deadline covers that cold start.
    ///
    /// It deliberately does <em>not</em> retry by tearing the scene down and rebuilding it. Rebuilding
    /// restarts the same cold start from zero, so splitting the budget into short windows that must
    /// each independently beat the clock can only fail a slow-but-capable driver that one long wait
    /// would have rendered — the opposite of the intent. If the context genuinely never comes (an
    /// unsupported GL surface) the deadline still expires and the flat-cover fallback takes over, only
    /// later rather than wrongly. See DECISIONS 2026-08-16.
    /// </remarks>
    private static readonly TimeSpan InitializationTimeout = TimeSpan.FromSeconds(10);

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<MediaShelf3DHost, bool>(nameof(IsActive));

    public static readonly StyledProperty<bool> IsSceneSupportedProperty =
        AvaloniaProperty.Register<MediaShelf3DHost, bool>(nameof(IsSceneSupported), true);

    public static readonly StyledProperty<IReadOnlyList<GameViewModel>?> ItemsProperty =
        AvaloniaProperty.Register<MediaShelf3DHost, IReadOnlyList<GameViewModel>?>(nameof(Items));

    public static readonly StyledProperty<GameViewModel?> FocusedItemProperty =
        AvaloniaProperty.Register<MediaShelf3DHost, GameViewModel?>(nameof(FocusedItem));

    public static readonly StyledProperty<double> ShelfPositionProperty =
        AvaloniaProperty.Register<MediaShelf3DHost, double>(nameof(ShelfPosition));

    public static readonly StyledProperty<double> YawProperty =
        AvaloniaProperty.Register<MediaShelf3DHost, double>(nameof(Yaw));

    public static readonly StyledProperty<double> PitchProperty =
        AvaloniaProperty.Register<MediaShelf3DHost, double>(nameof(Pitch));

    public static readonly StyledProperty<PhysicalShelfDeparturePose?> DeparturePoseProperty =
        AvaloniaProperty.Register<MediaShelf3DHost, PhysicalShelfDeparturePose?>(nameof(DeparturePose));

    public static readonly StyledProperty<PhysicalShelfLaunchPose?> LaunchPoseProperty =
        AvaloniaProperty.Register<MediaShelf3DHost, PhysicalShelfLaunchPose?>(nameof(LaunchPose));

    public static readonly StyledProperty<Avalonia.Visual?> ChromeSourceProperty =
        AvaloniaProperty.Register<MediaShelf3DHost, Avalonia.Visual?>(nameof(ChromeSource));

    public static readonly StyledProperty<CrtPresentation> CrtProperty =
        AvaloniaProperty.Register<MediaShelf3DHost, CrtPresentation>(
            nameof(Crt), CrtPresentation.Off);

    public static readonly StyledProperty<bool> TintBackdropWithAccentProperty =
        AvaloniaProperty.Register<MediaShelf3DHost, bool>(
            nameof(TintBackdropWithAccent), true);

    /// <summary>
    /// How long the host waits for the surface size to stop changing before it rebuilds the scene at
    /// the settled size. Long enough to coalesce a burst of resize events (a window manager applying a
    /// full-screen grow over several frames) into a single rebuild, short enough that the correction is
    /// not perceptible as a separate step.
    /// </summary>
    private static readonly TimeSpan ResizeSettleDelay = TimeSpan.FromMilliseconds(150);

    private readonly DispatcherTimer _initializationWatchdog;
    private readonly DispatcherTimer _resizeSettleTimer;
    private MediaShelf3DControl? _scene;
    private bool _failedForActivation;
    // The host's own size when the current scene was built. The scene is an OpenGlControlBase whose GL
    // surface is fixed at the size it is first laid out at, so a scene stood up before the window
    // finished an asynchronous full-screen grow stays pinned at the pre-grow size and never re-covers
    // the window — the "doubled top row" / black-margin couch bug. Comparing against this tells the
    // settle handler whether a rebuild is actually needed.
    private Size _sceneBuiltAtSize;
    // Whether the current scene has reported a ready GL context. A resize-driven rebuild is held until
    // this is set: tearing the scene down mid-init would restart a cold start from zero, which the
    // initialization watchdog deliberately avoids (some drivers — the Steam Deck's Mesa stack — are
    // slow to hand out a first context). Init success re-checks the size, so a grow that landed during
    // the cold start is still corrected, just after the context is up rather than by interrupting it.
    private bool _sceneInitialized;

    public MediaShelf3DHost()
    {
        _initializationWatchdog = new DispatcherTimer { Interval = InitializationTimeout };
        _initializationWatchdog.Tick += OnInitializationTimedOut;
        _resizeSettleTimer = new DispatcherTimer { Interval = ResizeSettleDelay };
        _resizeSettleTimer.Tick += OnResizeSettled;
    }

    public event EventHandler<Exception>? InitializationFailed;

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public bool IsSceneSupported
    {
        get => GetValue(IsSceneSupportedProperty);
        set => SetValue(IsSceneSupportedProperty, value);
    }

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

    /// <inheritdoc cref="MediaShelf3DControl.ChromeSourceProperty"/>
    public Avalonia.Visual? ChromeSource
    {
        get => GetValue(ChromeSourceProperty);
        set => SetValue(ChromeSourceProperty, value);
    }

    /// <summary>How hard the scene is pushed through a CRT tube on the way to the screen.</summary>
    public CrtPresentation Crt
    {
        get => GetValue(CrtProperty);
        set => SetValue(CrtProperty, value);
    }

    /// <inheritdoc cref="MediaShelf3DControl.TintBackdropWithAccent"/>
    public bool TintBackdropWithAccent
    {
        get => GetValue(TintBackdropWithAccentProperty);
        set => SetValue(TintBackdropWithAccentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsActiveProperty || change.Property == IsSceneSupportedProperty)
        {
            UpdateSceneAttachment();
            return;
        }

        if (change.Property == BoundsProperty)
        {
            // The host fills the window, so its Bounds track the window's client size — including the
            // full-screen grow that trails a launch straight into Gamepad or a return from a launched
            // game, applied asynchronously (MacFullScreenController on macOS; a late gamescope resize
            // on the Steam Deck). A scene stood up before that grow lands is an OpenGlControlBase whose
            // GL surface is fixed at the pre-grow size: it renders into the lower-left of the now-larger
            // window and leaves the live GamepadRoot rail (or a black band) around it — the doubled top
            // row. Coalesce the resize burst and rebuild once it settles; OnResizeSettled checks the
            // size actually changed so a settled window never rebuilds.
            if (_scene is not null)
                RestartResizeSettle();
            return;
        }

        if (_scene is not { } scene)
        {
            return;
        }

        if (change.Property == ItemsProperty)
            scene.Items = Items;
        else if (change.Property == FocusedItemProperty)
            scene.FocusedItem = FocusedItem;
        else if (change.Property == ShelfPositionProperty)
            scene.ShelfPosition = ShelfPosition;
        else if (change.Property == YawProperty)
            scene.Yaw = Yaw;
        else if (change.Property == PitchProperty)
            scene.Pitch = Pitch;
        else if (change.Property == DeparturePoseProperty)
            scene.DeparturePose = DeparturePose;
        else if (change.Property == LaunchPoseProperty)
            scene.LaunchPose = LaunchPose;
        else if (change.Property == CrtProperty)
            scene.Crt = Crt;
        else if (change.Property == TintBackdropWithAccentProperty)
            scene.TintBackdropWithAccent = TintBackdropWithAccent;
        else if (change.Property == ChromeSourceProperty)
            scene.ChromeSource = ChromeSource;
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        RemoveScene();
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdateSceneAttachment()
    {
        if (!IsActive || !IsSceneSupported)
        {
            _failedForActivation = false;
            RemoveScene();
            return;
        }

        if (_scene is not null || _failedForActivation)
        {
            return;
        }

        var scene = new MediaShelf3DControl
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            Items = Items,
            FocusedItem = FocusedItem,
            ShelfPosition = ShelfPosition,
            Yaw = Yaw,
            Pitch = Pitch,
            DeparturePose = DeparturePose,
            LaunchPose = LaunchPose,
            Crt = Crt,
            TintBackdropWithAccent = TintBackdropWithAccent,
            ChromeSource = ChromeSource,
        };
        scene.InitializationSucceeded += OnSceneInitializationSucceeded;
        scene.InitializationFailed += OnSceneInitializationFailed;
        scene.ContextLost += OnSceneContextLost;
        scene.AttachedToVisualTree += OnSceneAttachedToVisualTree;
        _scene = scene;
        _sceneBuiltAtSize = Bounds.Size;
        _sceneInitialized = false;
        Content = scene;
    }

    private void RestartResizeSettle()
    {
        _resizeSettleTimer.Stop();
        _resizeSettleTimer.Start();
    }

    private void OnResizeSettled(object? sender, EventArgs e)
    {
        _resizeSettleTimer.Stop();

        // Only a live, healthy scene whose size has genuinely moved needs rebuilding. Requiring the
        // current bounds to be non-empty avoids ever rebuilding onto a zero-sized surface, and the
        // size comparison means a settled full-screen window (the common case, and every desktop
        // resize while the tube is inactive never reaches here) does no work.
        if (_scene is null
            || !IsActive
            || !IsSceneSupported
            || _failedForActivation
            || !_sceneInitialized
            || Bounds.Width <= 0
            || Bounds.Height <= 0
            || Bounds.Size == _sceneBuiltAtSize)
        {
            return;
        }

        // Rebuild the scene so its GL surface is created at the settled size — the same recovery the
        // CRT on/off toggle performs by flipping IsActive, applied automatically when the window
        // finishes resizing under a scene that was stood up too early.
        RemoveScene();
        UpdateSceneAttachment();
    }

    private void OnSceneAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) =>
        RestartWatchdog();

    private void OnSceneInitializationSucceeded(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(sender, _scene))
            {
                _initializationWatchdog.Stop();
                _sceneInitialized = true;
                // Re-check the size now the context is up: if the window grew during the cold start,
                // the rebuild was held off (see OnResizeSettled's guard) and this is where it lands.
                RestartResizeSettle();
            }
        });

    private void OnSceneInitializationFailed(object? sender, Exception exception) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(sender, _scene))
            {
                Fail(exception);
            }
        }, DispatcherPriority.Background);

    private void OnSceneContextLost(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(sender, _scene) && IsActive && IsSceneSupported)
            {
                // The context is being rebuilt, so the scene is no longer "ready": hold any
                // resize-driven rebuild until it reports success again.
                _sceneInitialized = false;
                RestartWatchdog();
            }
        });
    }

    private void OnInitializationTimedOut(object? sender, EventArgs e) =>
        Fail(new TimeoutException(
            $"The OpenGL shelf did not report a ready context within {InitializationTimeout.TotalSeconds:0}s. "
            + "If the log has no \"Shelf GL context acquired\" line the framework never handed the control a "
            + "GL context — the platform may not support Avalonia's shared GL surface; if it does, the "
            + "renderer build did not finish in time."));

    private void RestartWatchdog()
    {
        _initializationWatchdog.Stop();
        _initializationWatchdog.Start();
    }

    private void Fail(Exception exception)
    {
        if (_scene is null || _failedForActivation)
        {
            return;
        }

        _failedForActivation = true;
        RemoveScene();
        InitializationFailed?.Invoke(this, exception);
    }

    private void RemoveScene()
    {
        _initializationWatchdog.Stop();
        _resizeSettleTimer.Stop();
        _sceneBuiltAtSize = default;
        _sceneInitialized = false;
        if (_scene is not { } scene)
        {
            return;
        }

        scene.InitializationSucceeded -= OnSceneInitializationSucceeded;
        scene.InitializationFailed -= OnSceneInitializationFailed;
        scene.ContextLost -= OnSceneContextLost;
        scene.AttachedToVisualTree -= OnSceneAttachedToVisualTree;
        Content = null;
        _scene = null;
    }

    internal bool HasAttachedScene => _scene is not null;

    internal void ExpireInitializationForTests() => OnInitializationTimedOut(this, EventArgs.Empty);
}
