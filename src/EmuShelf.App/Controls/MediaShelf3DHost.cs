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
    private static readonly TimeSpan InitializationTimeout = TimeSpan.FromSeconds(4);

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

    private readonly DispatcherTimer _initializationWatchdog;
    private MediaShelf3DControl? _scene;
    private bool _failedForActivation;

    public MediaShelf3DHost()
    {
        _initializationWatchdog = new DispatcherTimer { Interval = InitializationTimeout };
        _initializationWatchdog.Tick += OnInitializationTimedOut;
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
        Content = scene;
    }

    private void OnSceneAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) =>
        RestartWatchdog();

    private void OnSceneInitializationSucceeded(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(sender, _scene))
            {
                _initializationWatchdog.Stop();
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
                RestartWatchdog();
            }
        });
    }

    private void OnInitializationTimedOut(object? sender, EventArgs e) =>
        Fail(new TimeoutException(
            "The OpenGL shelf did not initialize. The platform may not support Avalonia's shared GL surface."));

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
