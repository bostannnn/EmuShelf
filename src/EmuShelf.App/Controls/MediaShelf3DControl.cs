using System.Collections.Specialized;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Platform;
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
    private const int NeighbourRadius = 3;
    // Tightened alongside the camera's closer framing: at the old gap the neighbouring media fell
    // entirely outside a filled frame, which turns a shelf back into a single-hero view.
    private const float ItemGap = 0.14f;
    private const float NeighbourYaw = -0.18f;
    private const int CoverTextureCacheCapacity = 21;
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

    private readonly List<LayoutEntry> _layout = [];
    private readonly List<MediaShelfRenderItem> _renderItems = new((NeighbourRadius * 2) + 1);
    private readonly Dictionary<long, GameViewModel> _gamesByKey = [];
    private readonly HashSet<GameViewModel> _observedGames = [];
    private readonly Dictionary<long, UploadedCover> _uploadedCovers = [];
    private readonly LinkedList<long> _coverLru = [];
    private readonly Dictionary<ArtworkKey, DecodedArtwork> _decodedPhysicalArtwork = [];
    private readonly LinkedList<ArtworkKey> _physicalArtworkLru = [];
    private readonly Dictionary<ArtworkKey, PhysicalArtworkLoad> _physicalArtworkLoads = [];
    private readonly Queue<PhysicalArtworkLoad> _physicalArtworkQueue = [];
    private readonly Dictionary<long, PhysicalShelfDeparturePose> _departurePoses = [];
    private INotifyCollectionChanged? _observedCollection;
    private int _observedStart = -1;
    private int _observedEnd = -1;
    private int _activePhysicalArtworkDecodes;
    private int _focusedIndex = -1;
    private float _sceneMediaHeight = 1f;
    private int _preparationGeneration;
    private GL? _gl;
    private MediaShellRenderer? _renderer;
    private bool _failed;
    private bool _isAttached;
    private Color _uploadedAccent;

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
            || change.Property == BoundsProperty)
        {
            RequestNextFrameRendering();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        ObserveCollection();
        UpdateVisibleSubscriptions(force: true);
        PrepareShells();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        StopObserving();
        ClearDecodedPhysicalArtwork();
        _preparationGeneration++;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
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
            InitializationSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _renderer = null;
            _gl = null;
            Fail(exception);
        }
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_renderer is null || Items is not { Count: > 0 })
        {
            return;
        }

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var width = (uint)Math.Max(1, Math.Round(Bounds.Width * scaling));
        var height = (uint)Math.Max(1, Math.Round(Bounds.Height * scaling));

        try
        {
            var accent = FocusedItem?.ShelfAccent ?? Colors.Gray;
            if (accent != _uploadedAccent)
            {
                _renderer.SetAccent(ToLinear(accent));
                _uploadedAccent = accent;
            }

            var renderItems = BuildRenderItems();
            SynchronizeArtworkTextures(renderItems);
            _renderer.RenderShelf(renderItems, _sceneMediaHeight, (uint)fb, width, height);
        }
        catch (Exception exception)
        {
            _renderer.Dispose();
            _renderer = null;
            Fail(exception);
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _renderer?.Dispose();
        _renderer = null;
        _gl = null;
        ClearUploadedCoverState();
        ContextLost?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnOpenGlLost()
    {
        _renderer = null;
        _gl = null;
        ClearUploadedCoverState();
        ContextLost?.Invoke(this, EventArgs.Empty);
    }

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
        _renderItems.Clear();

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
            _renderItems.Add(new MediaShelfRenderItem(
                entry.Game.Id,
                entry.Game.ShelfMediaProfile,
                entry.Centre - anchor,
                focus,
                launch?.Yaw ?? pose.Yaw,
                launch?.Pitch ?? pose.Pitch,
                ToLinear(entry.Game.ShelfAccent),
                launch?.VerticalOffset ?? 0f,
                launch?.DepthOffset ?? 0f,
                launch?.Scale ?? 1f));

            if (!isFocused && hasDeparture && focus <= 0.001f)
            {
                _departurePoses.Remove(entry.Game.Id);
            }
        }

        return _renderItems;
    }

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

    private void SynchronizeArtworkTextures(IReadOnlyList<MediaShelfRenderItem> renderItems)
    {
        if (_renderer is null || Items is null)
        {
            return;
        }

        foreach (var item in renderItems)
        {
            _gamesByKey.TryGetValue(item.Key, out var game);
            if (!_uploadedCovers.TryGetValue(item.Key, out var uploaded))
            {
                uploaded = new UploadedCover(_coverLru.AddFirst(item.Key));
                _uploadedCovers[item.Key] = uploaded;
            }
            else
            {
                TouchCover(uploaded);
            }

            foreach (var face in Faces)
            {
                var artwork = game is null ? null : ResolveArtwork(game, face);
                if (ReferenceEquals(uploaded.Faces[(int)face], artwork))
                {
                    continue;
                }

                _renderer.SetPanelArt(
                    item.Key, (int)face, artwork is Bitmap bitmap ? ToTextureImage(bitmap) : null);
                uploaded.Faces[(int)face] = artwork;
            }
        }

        while (_uploadedCovers.Count > CoverTextureCacheCapacity && _coverLru.Last is { } oldest)
        {
            _renderer.RemoveCoverArt(oldest.Value);
            _uploadedCovers.Remove(oldest.Value);
            _coverLru.RemoveLast();
        }
    }

    private static readonly ShelfArtworkFace[] Faces =
        [ShelfArtworkFace.Front, ShelfArtworkFace.Back, ShelfArtworkFace.Spine];

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
            if ((game.ShelfMediaProfile.ArtworkSlots & PhysicalArtworkSlots.CartridgeSupport) == 0)
            {
                continue;
            }

            // Needs the shell's own label proportions, which are only known once its asset has
            // finished decoding. Warming is retried on every list change and every prepared-shell
            // callback, so a label missed here is drawn moments later rather than lost.
            if (MediaShellCatalog.TryGetPanelAspect(game.ShelfMediaProfile.Shell) is { } aspect)
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

        var wanted = face == ShelfArtworkFace.Back
            ? PhysicalArtworkSlots.Back
            : PhysicalArtworkSlots.Spine;
        if ((slots & wanted) == 0)
        {
            return ShelfArtworkKind.None;
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
        foreach (var game in Items)
        {
            var profile = game.ShelfMediaProfile;
            var width = profile.WidthInShelfUnits;
            var centre = cursor + (width * 0.5f);
            _layout.Add(new LayoutEntry(game, centre));
            _gamesByKey[game.Id] = game;
            cursor += width + ItemGap;
            // The camera frames the tallest medium in the whole view, not the visible window, so
            // scrolling a mixed row past a keep case cannot make the world zoom.
            tallest = MathF.Max(
                tallest, profile.HeightInShelfUnits + profile.FloorClearanceInShelfUnits);
        }

        _sceneMediaHeight = tallest;
        WarmLabelPlaceholders();

        _focusedIndex = FocusedItem is null ? -1 : IndexOf(Items, FocusedItem);
        PruneDecodedPhysicalArtwork();
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
        RequestNextFrameRendering();
    }

    private void PrepareShells()
    {
        if (!_isAttached)
        {
            return;
        }

        var generation = ++_preparationGeneration;
        var shells = Items?
            .Select(game => game.ShelfMediaProfile.Shell)
            .Distinct()
            .ToArray() ?? [];
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
                    RequestNextFrameRendering();
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
            RequestNextFrameRendering();
            return;
        }

        if (e.PropertyName is nameof(GameViewModel.CoverImage))
        {
            RequestNextFrameRendering();
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
                    RequestNextFrameRendering();
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

    private sealed class UploadedCover(LinkedListNode<long> node)
    {
        /// <summary>What is currently on the GPU for each face, so unchanged faces are not re-uploaded.</summary>
        public IImage?[] Faces { get; } = new IImage?[3];

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
