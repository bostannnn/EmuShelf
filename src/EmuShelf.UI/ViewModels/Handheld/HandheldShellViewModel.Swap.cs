using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.ViewModels;

// Screen swap (M45 H5): X trades the Stage and the Launcher between the Thor's two displays. The shell owns the
// choice (remembered in AppSettings) and keeps home current in both shapes at all times: 5×4 pages for the
// 1240×1080 screen and 7×3 pages (seven folder columns) for the 16:9 one, at the same tile size. Each display keeps
// a Stage and a Launcher built for it (on Android), so a swap only changes which one is in front: nothing is
// rebuilt, and the platform head plays the slide (ScreenSwapHost). The selection, an open folder and its scroll
// are the shell's and carry over.
public sealed partial class HandheldShellViewModel
{
    /// <summary>The 16:9 screen's width on the canvases (both are 1080 tall).</summary>
    public const double WideCanvasWidth = 1920;

    /// <summary>The Thor's second screen (1240×1080).</summary>
    public const double NarrowCanvasWidth = 1240;

    private Action<bool>? _saveScreensSwapped;
    private bool _swapping;

    /// <summary>Home as the Launcher draws it on the 1240×1080 screen (5×4 pages).</summary>
    public HandheldHomeScreenViewModel NarrowHome { get; } =
        new(HandheldHomeGrid.Standard, NarrowCanvasWidth, HandheldFolderNavigation.VisibleColumns);

    /// <summary>Home as the Launcher draws it on the 16:9 screen (7×3 pages).</summary>
    public HandheldHomeScreenViewModel WideHome { get; } =
        new(HandheldHomeGrid.Wide, WideCanvasWidth, HandheldFolderNavigation.WideVisibleColumns);

    /// <summary>Home on the screen the Launcher is on now: what the pad and touch move through.</summary>
    public HandheldHomeScreenViewModel Home => IsSwapped ? WideHome : NarrowHome;

    private HandheldHomeScreenViewModel OtherHome => IsSwapped ? NarrowHome : WideHome;

    private HandheldHomeScreenViewModel[] Screens => [NarrowHome, WideHome];

    /// <summary>The home shape for a Launcher on the 16:9 screen (<paramref name="wide"/>) or the narrow one.</summary>
    public HandheldHomeScreenViewModel HomeFor(bool wide) => wide ? WideHome : NarrowHome;

    /// <summary>
    /// The platform's swap. It is handed the change to make (<paramref name="apply"/>) and decides when, around its
    /// slide. Null (macOS, tests): the swap applies at once.
    /// </summary>
    public Func<Action, Task>? ScreenSwapHost { get; set; }

    /// <summary>True while the Launcher is on the 16:9 screen and the Stage on the narrow one.</summary>
    public bool IsSwapped { get; private set; }

    /// <summary>The shape home is drawn in on the Launcher's current screen.</summary>
    public HandheldHomeGrid HomeGrid => Home.Grid;

    public int HomeColumns => HomeGrid.Columns;
    public int HomeRows => HomeGrid.Rows;

    /// <summary>The Launcher's canvas: 1240 wide on the narrow screen, 1920 on the 16:9 one; always 1080 tall.</summary>
    public double LauncherCanvasWidth => Home.CanvasWidth;

    /// <summary>The Stage's canvas: 1920 wide on the 16:9 screen, 1240 on the narrow one; always 1080 tall.</summary>
    public double StageCanvasWidth => IsSwapped ? NarrowCanvasWidth : WideCanvasWidth;

    public bool IsLauncherWide => IsSwapped;
    public bool IsStageNarrow => IsSwapped;

    /// <summary>Folder columns shown at once (four, or seven on the 16:9 screen), with one more peeking in.</summary>
    public int FolderVisibleColumns => Home.FolderVisibleColumns;

    /// <summary>The page's name in the 16:9 Launcher's spare height ("Page 2 of 3").</summary>
    public string PageLabel => Home.PageLabel;

    private int SlotsPerPage => HomeGrid.SlotsPerPage;

    /// <summary>Pages the Launcher shows: the saved slots cut into pages of the current shape.</summary>
    private int ViewPageCount => HomeGrid.PageCount(_layout.SlotCount);

    /// <summary>
    /// The saved slots padded with empty ones to whole pages of the current shape (40 slots are two 7×3 pages, the
    /// last two slots empty). Edits through <see cref="HandheldHomeLayout.WithSlots"/> trim the padding off again.
    /// </summary>
    private IReadOnlyList<HandheldSlot> HomeSlots() => PadToPages(_layout.AllSlots(), HomeGrid);

    /// <summary>
    /// <paramref name="slots"/> cut to whole pages of <paramref name="grid"/>: padded with empty slots, never fewer
    /// pages than the saved layout needs, and long enough for anything placed in a padding slot.
    /// </summary>
    private IReadOnlyList<HandheldSlot> PadToPages(IReadOnlyList<HandheldSlot> slots, HandheldHomeGrid grid)
    {
        var used = 0;
        for (var i = slots.Count - 1; i >= 0; i--)
        {
            if (!slots[i].IsEmpty)
            {
                used = i + 1;
                break;
            }
        }
        var count = grid.PageCount(Math.Max(_layout.SlotCount, used)) * grid.SlotsPerPage;
        if (slots.Count == count)
            return slots;
        var padded = slots.Take(count).ToList();
        while (padded.Count < count)
            padded.Add(HandheldSlot.Empty);
        return padded;
    }

    // The screen in use passes its page changes on as the shell's own (Pages, CurrentPage, the edge hints, …).
    private void OnScreenChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, Home))
            OnPropertyChanged(e.PropertyName);
    }

    /// <summary>A slot by its place across all pages; a padding slot past the saved ones is empty.</summary>
    private HandheldSlot HomeSlotAt(int index) =>
        index >= 0 && index < _layout.SlotCount ? _layout.SlotAt(index) : HandheldSlot.Empty;

    /// <summary>X: swaps the screens, with the slide where the platform has one. Ignored while one is under way.</summary>
    public async Task SwapScreensAsync()
    {
        if (_swapping || _disposed)
            return;
        _swapping = true;
        try
        {
            var target = !IsSwapped;
            if (ScreenSwapHost is { } host)
                await host(() => ApplySwap(target));
            else
                ApplySwap(target);
        }
        catch (Exception)
        {
            // A host that failed mid-slide leaves the screens as they were; the shell keeps working.
        }
        finally
        {
            _swapping = false;
        }
    }

    /// <summary>Takes the given side without the slide or saving it (startup restores the remembered choice).</summary>
    private void SetSwapped(bool swapped)
    {
        if (swapped == IsSwapped)
            return;
        var homeIndex = HomeIndex;
        IsSwapped = swapped;

        // Both shapes are current already: the pad now moves through the other one, from the same slot of the layout.
        _home = HomeGrid.PositionOf(homeIndex);
        if (OpenFolder is { } folder)
        {
            folder.HomeSlot = _home.Slot;
            // The open folder's scroll on the new screen has followed the selection all along.
            folder.FirstVisibleColumn = folder.FirstColumnFor(swapped);
        }

        foreach (var name in new[]
        {
            nameof(IsSwapped), nameof(Home), nameof(HomeGrid), nameof(HomeColumns), nameof(HomeRows), nameof(LauncherCanvasWidth),
            nameof(StageCanvasWidth), nameof(IsLauncherWide), nameof(IsStageNarrow), nameof(FolderVisibleColumns), nameof(Pages),
            nameof(PageDots), nameof(CurrentPage), nameof(PageCount), nameof(HasMultiplePages), nameof(CanPageLeft),
            nameof(CanPageRight), nameof(PageLabel),
        })
        {
            OnPropertyChanged(name);
        }
        UpdateFocus();
    }

    private void ApplySwap(bool swapped)
    {
        if (_disposed || swapped == IsSwapped)
            return;
        SetSwapped(swapped);
        _saveScreensSwapped?.Invoke(swapped);
    }
}
