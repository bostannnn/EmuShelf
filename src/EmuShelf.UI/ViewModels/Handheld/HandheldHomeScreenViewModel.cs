using System;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// Home as one screen draws it (M45 H5): the saved layout cut into pages of that screen's shape, 5×4 on the Thor's
/// 1240×1080 screen and 7×3 on the 16:9 one, with its own current page and dots. The shell keeps both shapes current
/// at all times, so a Launcher already built for the other screen can take over the moment X swaps the screens,
/// without anything being rebuilt.
/// </summary>
public sealed partial class HandheldHomeScreenViewModel : ObservableObject
{
    internal HandheldHomeScreenViewModel(HandheldHomeGrid grid, double canvasWidth, int folderVisibleColumns)
    {
        Grid = grid;
        CanvasWidth = canvasWidth;
        FolderVisibleColumns = folderVisibleColumns;
    }

    public HandheldHomeGrid Grid { get; }

    /// <summary>The Launcher's canvas on this screen (1240 or 1920 wide, 1080 tall); a home page is this wide.</summary>
    public double CanvasWidth { get; }

    /// <summary>The 16:9 screen's shape (7×3, with the page named in the spare height).</summary>
    public bool IsWide => Grid == HandheldHomeGrid.Wide;

    /// <summary>Folder columns shown at once on this screen, with one more peeking in.</summary>
    public int FolderVisibleColumns { get; }

    public ObservableCollection<HandheldHomePageViewModel> Pages { get; } = [];
    public ObservableCollection<HandheldPageDotViewModel> PageDots { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    [NotifyPropertyChangedFor(nameof(CanPageLeft))]
    [NotifyPropertyChangedFor(nameof(CanPageRight))]
    public partial HandheldHomePageViewModel? CurrentPage { get; internal set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMultiplePages))]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    [NotifyPropertyChangedFor(nameof(CanPageRight))]
    public partial int PageCount { get; internal set; }

    /// <summary>A folder is open over home (the edge hints hide).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPageLeft))]
    [NotifyPropertyChangedFor(nameof(CanPageRight))]
    public partial bool IsFolderOpen { get; internal set; }

    public bool HasMultiplePages => PageCount > 1;

    /// <summary>There is a page to the left / right of the one on screen (the edge hints).</summary>
    public bool CanPageLeft => !IsFolderOpen && CurrentPage is { Index: > 0 };

    public bool CanPageRight => !IsFolderOpen && CurrentPage is { } page && page.Index < PageCount - 1;

    /// <summary>The page's name in the 16:9 Launcher's spare height ("Page 2 of 3").</summary>
    public string PageLabel => string.Create(CultureInfo.CurrentCulture, $"Page {(CurrentPage?.Index ?? 0) + 1} of {Math.Max(1, PageCount)}");

    /// <summary>The focused folder tile showing its peek covers on this screen.</summary>
    internal HandheldHomeSlotViewModel? PeekSlot { get; set; }

    internal void ShowPage(int page)
    {
        CurrentPage = Pages.Count > page ? Pages[page] : null;
        foreach (var dot in PageDots)
            dot.IsCurrent = dot.Index == page;
    }
}
