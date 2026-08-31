using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Controls;

/// <summary>
/// The couch grid's virtualizing surface: a fixed pool of permanently-attached
/// <see cref="GamepadGridTile"/> controls, repositioned and re-bound as the scroll offset moves
/// across the justified rows.
/// </summary>
/// <remarks>
/// This replaces the ListBox-of-rows the grid used before, for a measured reason: the
/// VirtualizingStackPanel clears a recycled row container's content, so every row-crossing of a
/// d-pad scroll rebuilt (or, pooled, re-ATTACHED) several deep tile trees on the UI thread. On the
/// Thor that cost ~80–170 ms per row — one quarter-second freeze per crossing, the whole "choppy
/// scroll" — and re-attaching pooled tiles was measured just as expensive as building them, because
/// attachment re-runs styling over ~40 controls per tile. Re-binding an ALREADY-ATTACHED tile is
/// ~1 ms. So the tiles here never leave the visual tree: entering a row costs one DataContext write
/// per tile plus an arrange.
///
/// Geometry is exact, not estimated. The packer stamps every game with its justified
/// <see cref="GameViewModel.CoverWidth"/>/<see cref="GameViewModel.CoverHeight"/> and every row's
/// covers share one height, so row tops, the scroll extent, and each tile's rect are plain
/// arithmetic over <see cref="Rows"/>. That also frees the reveal code from realized-container
/// measurement and the estimated-extent drift the old ListBox reveal had to work around
/// (DECISIONS 2026-08-05): any row's centre offset can be computed whether or not it is realized.
///
/// Layout constants mirror the old row template: rows carried Margin 0,10,0,18 and Padding 40,0,40,0
/// with 28 px between tiles, and the tile's label strip under the cover is 58 px
/// (its Grid's "Auto,58"). Pixel output is intended to be identical; the visual snapshot tests
/// assert it.
/// </remarks>
public sealed class GamepadGridPanel : Panel
{
    // Geometry the packer owns is READ from its owner, never copied: the packer budgets every
    // justified cover width against the gutter and inter-cover spacing, so an independent copy here
    // would silently desync arrange-time layout from pack-time widths the moment either is tuned.
    private const double SideGutter = MainViewModel.GamepadGridSideGutter;
    private const double TileSpacing = MainViewModel.CoverColumnSpacing;
    private const double TileLabelHeight = GamepadGridTile.LabelStripHeight;
    // The vertical row margins are owned here (the old row template that carried them is gone).
    private const double RowTopMargin = 10;
    private const double RowBottomMargin = 18;

    // Blank content above the first row and below the last, INSIDE the scrollable extent, so the
    // focused tile's shadow and light pool (which overflow the tile by ~20px once the 1.09 focus
    // scale is applied) are not hard-clipped by the scroll viewport on the edge rows. The host
    // ScrollViewer's top margin is reduced by EdgeInsetTop, so the resting pixel layout is unchanged.
    // The bottom inset additionally budgets for the overlay dock that floats over the grid's lower
    // edge: it is what lets the last row scroll clear of the dock rather than resting under it.
    //
    // Both are extent-only (see _extentHeight). They are deliberately NOT part of any row's band:
    // the reveal centres a row on rowTop + rowHeight / 2, so an inset folded into the last row's
    // height would push that one row off the line every other row rests on.
    private const double EdgeInsetTop = 24;
    private const double EdgeInsetBottom = 156;

    /// <summary>Rows realized beyond the viewport on each side, so a glide re-binds tiles just before they show.</summary>
    private const int OverscanRows = 1;

    /// <summary>
    /// Parked tiles kept alive (attached, invisible) for reuse. The pool may hold more transiently
    /// while a repack hands tiles from the released window to the re-realized one; ParkUnusedTiles
    /// trims back to this cap AFTER realization, so a same-sized window is always served from the
    /// pool and only a genuine surplus is removed from the tree and left for the GC. The cap is
    /// load-bearing: an unbounded pool once retained a startup-degenerate 968-tile pack (now also
    /// prevented at the source — RepackActiveGrid publishes no rows before the first real width) as
    /// permanently-attached children, and that ~40k-control live set made every minor GC on the
    /// Thor a ~700 ms stop-the-world freeze mid-scroll.
    /// </summary>
    private const int MaxPooledTiles = 48;

    public static readonly StyledProperty<IReadOnlyList<IReadOnlyList<GameViewModel>>?> RowsProperty =
        AvaloniaProperty.Register<GamepadGridPanel, IReadOnlyList<IReadOnlyList<GameViewModel>>?>(nameof(Rows));

    // Row tops in content coordinates; _rowTops[i] is where row i's outer (margin-inclusive) band
    // starts and _rowTops[^1] is where the LAST row's band ends. Rebuilt on any Rows change.
    private double[] _rowTops = [0];

    // The scrollable extent: the last row's band end plus EdgeInsetBottom. Kept separate from
    // _rowTops so the inset never leaks into a row's reported height — folding it into _rowTops[^1]
    // made TryGetRowBounds overstate the LAST row by EdgeInsetBottom, which skewed the reveal's
    // centring (rowTop + rowHeight / 2) by half the inset for that row alone.
    private double _extentHeight;

    private readonly Dictionary<int, List<GamepadGridTile>> _realizedRows = new();
    private readonly Stack<GamepadGridTile> _freeTiles = new();
    private ScrollViewer? _scroller;

    static GamepadGridPanel()
    {
        RowsProperty.Changed.AddClassHandler<GamepadGridPanel>((panel, e) => panel.OnRowsChanged(e));
        ClipToBoundsProperty.OverrideDefaultValue<GamepadGridPanel>(false);
    }

    public IReadOnlyList<IReadOnlyList<GameViewModel>>? Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <summary>
    /// The outer band of a row (margins included) in content coordinates — exact for every row,
    /// realized or not, so the reveal maths never depends on realization.
    /// </summary>
    public bool TryGetRowBounds(int rowIndex, out double top, out double height)
    {
        top = 0;
        height = 0;
        if (rowIndex < 0 || rowIndex >= _rowTops.Length - 1)
            return false;

        top = _rowTops[rowIndex];
        height = _rowTops[rowIndex + 1] - _rowTops[rowIndex];
        return true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scroller = this.FindAncestorOfType<ScrollViewer>();
        if (_scroller is not null)
            _scroller.PropertyChanged += OnScrollerPropertyChanged;
        HookRows(Rows);
        // A Reset raised while detached was deliberately missed (see OnDetachedFromVisualTree), so
        // geometry may be stale: rebuild rather than merely re-realize.
        RebuildGeometry();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_scroller is not null)
            _scroller.PropertyChanged -= OnScrollerPropertyChanged;
        _scroller = null;
        // GamepadRows outlives any one panel (it is view-model state), so a detached panel MUST let
        // go of its CollectionChanged subscription or the collection keeps the dead panel — and its
        // ~48 permanently-attached tile trees — alive across Android activity recreations, quietly
        // rebuilding the large-live-set GC cost this panel exists to avoid.
        HookRows(null);
    }

    // The collection currently subscribed for CollectionChanged; non-null only while attached.
    private INotifyCollectionChanged? _hookedRows;

    private void HookRows(IReadOnlyList<IReadOnlyList<GameViewModel>>? rows)
    {
        var incc = this.IsAttachedToVisualTree() ? rows as INotifyCollectionChanged : null;
        if (ReferenceEquals(_hookedRows, incc))
            return;

        if (_hookedRows is not null)
            _hookedRows.CollectionChanged -= OnRowsCollectionChanged;
        _hookedRows = incc;
        if (_hookedRows is not null)
            _hookedRows.CollectionChanged += OnRowsCollectionChanged;
    }

    private void OnScrollerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ScrollViewer.OffsetProperty || e.Property == ScrollViewer.ViewportProperty)
            UpdateRealization();
    }

    private void OnRowsChanged(AvaloniaPropertyChangedEventArgs e)
    {
        HookRows(e.NewValue as IReadOnlyList<IReadOnlyList<GameViewModel>>);
        RebuildGeometry();
    }

    private void OnRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RebuildGeometry();

    // Any rows change is a repack (the view model always ReplaceAll/Clears), so recompute the row
    // tops wholesale and re-derive which tiles show what — no incremental cases to get wrong.
    private void RebuildGeometry()
    {
        var rows = Rows;
        var count = rows?.Count ?? 0;
        if (_rowTops.Length != count + 1)
            _rowTops = new double[count + 1];

        double y = count > 0 ? EdgeInsetTop : 0;
        for (var index = 0; index < count; index++)
        {
            _rowTops[index] = y;
            var row = rows![index];
            var coverHeight = row.Count > 0 ? row[0].CoverHeight : 0;
            y += RowTopMargin + coverHeight + TileLabelHeight + RowBottomMargin;
        }
        _rowTops[count] = y;
        _extentHeight = count > 0 ? y + EdgeInsetBottom : 0;

        // Old assignments are meaningless against new geometry: release everything, then realize
        // the current window fresh.
        foreach (var tiles in _realizedRows.Values)
            ReleaseTiles(tiles);
        _realizedRows.Clear();

        InvalidateMeasure();
        UpdateRealization();
    }

    private void UpdateRealization()
    {
        var rows = Rows;
        if (rows is null || rows.Count == 0 || _scroller is null || _scroller.Viewport.Height <= 0)
        {
            foreach (var tiles in _realizedRows.Values)
                ReleaseTiles(tiles);
            _realizedRows.Clear();
            ParkUnusedTiles();
            return;
        }

        var viewTop = _scroller.Offset.Y;
        var viewBottom = viewTop + _scroller.Viewport.Height;

        var first = Math.Max(0, FindRowAt(viewTop) - OverscanRows);
        var last = Math.Min(rows.Count - 1, FindRowAt(viewBottom) + OverscanRows);

        var changed = false;

        // Release rows that left the window first, so their tiles are free for the entering rows.
        List<int>? stale = null;
        foreach (var rowIndex in _realizedRows.Keys)
        {
            if (rowIndex < first || rowIndex > last)
                (stale ??= []).Add(rowIndex);
        }
        if (stale is not null)
        {
            foreach (var rowIndex in stale)
            {
                ReleaseTiles(_realizedRows[rowIndex]);
                _realizedRows.Remove(rowIndex);
                changed = true;
            }
        }

        for (var rowIndex = first; rowIndex <= last; rowIndex++)
        {
            if (_realizedRows.ContainsKey(rowIndex))
                continue;

            var row = rows[rowIndex];
            var tiles = new List<GamepadGridTile>(row.Count);
            for (var column = 0; column < row.Count; column++)
            {
                var tile = _freeTiles.Count > 0 ? _freeTiles.Pop() : NewTile();
                // A pooled tile still carries its previous game (ReleaseTiles leaves DataContext in
                // place so a row-to-row transfer costs ONE binding pass, not a clear-then-set), so
                // only write when the game actually differs — on a same-shape repack (live resize)
                // most tiles get their own game back and skip the write entirely.
                if (!ReferenceEquals(tile.DataContext, row[column]))
                    tile.DataContext = row[column];
                tile.IsVisible = true;
                tiles.Add(tile);
            }
            _realizedRows[rowIndex] = tiles;
            changed = true;
        }

        if (changed)
        {
            ParkUnusedTiles();
            InvalidateMeasure();
            InvalidateArrange();
        }
    }

    private GamepadGridTile NewTile()
    {
        var tile = new GamepadGridTile();
        Children.Add(tile);
        return tile;
    }

    // Return a row's tiles to the pool. Deliberately does NOT clear DataContext or IsVisible yet:
    // a released tile is usually handed straight to a row entering in the same update, and clearing
    // first would double the binding work on the scroll hot path. ParkUnusedTiles, called once the
    // update settles, hides and unbinds whatever genuinely stayed in the pool.
    private void ReleaseTiles(List<GamepadGridTile> tiles)
    {
        foreach (var tile in tiles)
            _freeTiles.Push(tile);
    }

    // Finish a realization pass: trim the pool back to its cap (the surplus leaves the tree and
    // becomes garbage — keeping every excess tile attached-but-invisible is what turned minor GCs
    // into ~700 ms freezes), then hide the parked remainder and drop their game references so an
    // off-screen tile never retains a cover. Hidden, parked tiles are skipped by measure/arrange
    // and rendering; they stay attached, because re-attaching is the ~20 ms-per-tile styling cost
    // this panel exists to avoid.
    private void ParkUnusedTiles()
    {
        while (_freeTiles.Count > MaxPooledTiles)
            Children.Remove(_freeTiles.Pop());

        foreach (var tile in _freeTiles)
        {
            if (tile.DataContext is not null)
                tile.DataContext = null;
            if (tile.IsVisible)
                tile.IsVisible = false;
        }
    }

    // Index of the row whose band contains the content-space Y (clamped into range).
    private int FindRowAt(double y)
    {
        var tops = _rowTops;
        var lo = 0;
        var hi = tops.Length - 2;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (tops[mid] <= y)
                lo = mid;
            else
                hi = mid - 1;
        }
        return lo;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var rows = Rows;
        foreach (var (rowIndex, tiles) in _realizedRows)
        {
            if (rows is null || rowIndex >= rows.Count)
                continue;
            var row = rows[rowIndex];
            for (var column = 0; column < tiles.Count && column < row.Count; column++)
            {
                var game = row[column];
                tiles[column].Measure(new Size(game.CoverWidth, game.CoverHeight + TileLabelHeight));
            }
        }

        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        return new Size(width, _extentHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rows = Rows;
        foreach (var (rowIndex, tiles) in _realizedRows)
        {
            if (rows is null || rowIndex >= rows.Count)
                continue;
            var row = rows[rowIndex];
            var x = SideGutter;
            var y = _rowTops[rowIndex] + RowTopMargin;
            for (var column = 0; column < tiles.Count && column < row.Count; column++)
            {
                var game = row[column];
                tiles[column].Arrange(new Rect(x, y, game.CoverWidth, game.CoverHeight + TileLabelHeight));
                x += game.CoverWidth + TileSpacing;
            }
        }

        return new Size(finalSize.Width, _extentHeight);
    }
}
