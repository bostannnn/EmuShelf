using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.Core.Handheld;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.ViewModels;

/// <summary>A platform that has games, as a home folder shows it.</summary>
public sealed record HandheldPlatform(string SystemId, string Name, string ShortName, string AccentColor, int GameCount);

/// <summary>
/// One game in a Handheld folder: the facts the tiles, the Stage and the sorts need, plus the shared
/// <see cref="GameViewModel"/> the launch path takes. Tests build entries without a view model.
/// </summary>
public sealed record HandheldGameEntry(
    long Id,
    string Title,
    string SystemId,
    string SystemName,
    DateTimeOffset? LastPlayedAt,
    int PlayCount,
    TimeSpan Playtime,
    bool IsAvailable,
    string? AchievementText = null,
    GameViewModel? ViewModel = null);

/// <summary>
/// What the Handheld shell needs from the library and the app. <c>MainViewModel</c> implements it over
/// the real library, cover pipeline and launch core; the macOS tests use a fake.
/// </summary>
public interface IHandheldLibrary
{
    /// <summary>Platforms with at least one game, in navigation order.</summary>
    Task<IReadOnlyList<HandheldPlatform>> GetPlatformsAsync(CancellationToken cancellationToken = default);

    /// <summary>A platform's games in the library's order (titles, with multi-disc sets grouped).</summary>
    Task<IReadOnlyList<HandheldGameEntry>> GetGamesAsync(string systemId, CancellationToken cancellationToken = default);

    /// <summary>Releases the view models a folder held, once the folder closes.</summary>
    void ReleaseGames(IReadOnlyList<HandheldGameEntry> games);

    /// <summary>
    /// Decodes a game's cover off the UI thread at <paramref name="decodeWidth"/> pixels: from the cached
    /// thumbnail for a tile, or from the full cover for the Stage. Null when the game has no cover.
    /// </summary>
    Task<Bitmap?> LoadCoverAsync(HandheldGameEntry game, int decodeWidth, bool fullSize, CancellationToken cancellationToken);

    Task LaunchAsync(HandheldGameEntry game);

    Task SwitchModeAsync(InterfaceMode mode);

    void Quit();
}

public enum HandheldZone
{
    Grid,
    Header,
    Dock,
}

/// <summary>One slot on a home page.</summary>
public sealed partial class HandheldHomeSlotViewModel : ObservableObject
{
    public HandheldHomeSlotViewModel(int index, HandheldSlot slot, HandheldPlatform? platform)
    {
        Index = index;
        Slot = slot;
        Platform = platform;
        AccentBrush = platform is not null && Color.TryParse(platform.AccentColor, out var color)
            ? new SolidColorBrush(color)
            : Brushes.Gray;
    }

    public int Index { get; }
    public HandheldSlot Slot { get; }
    public HandheldPlatform? Platform { get; }

    public bool IsEmpty => Platform is null;
    public bool IsFolder => Platform is not null;
    public string Name => Platform?.Name ?? string.Empty;
    public string Monogram => Platform?.ShortName ?? string.Empty;
    public IBrush AccentBrush { get; }
    public string CountText => Platform is null ? string.Empty : HandheldText.GameCount(Platform.GameCount);

    [ObservableProperty]
    public partial bool IsFocused { get; set; }
}

public sealed class HandheldHomePageViewModel(int index, IReadOnlyList<HandheldHomeSlotViewModel> slots)
{
    public int Index { get; } = index;
    public IReadOnlyList<HandheldHomeSlotViewModel> Slots { get; } = slots;
}

public sealed partial class HandheldPageDotViewModel(int index) : ObservableObject
{
    public int Index { get; } = index;

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }
}

/// <summary>A game tile inside an open folder. Owns its decoded cover while it is in the visible window.</summary>
public sealed partial class HandheldGameTileViewModel(int index, HandheldGameEntry entry) : ObservableObject
{
    public int Index { get; } = index;
    public HandheldGameEntry Entry { get; } = entry;
    public string Title => Entry.Title;
    public bool IsUnavailable => !Entry.IsAvailable;
    public int Column => Index / HandheldFolderNavigation.Rows;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    public partial Bitmap? Cover { get; set; }

    public bool HasCover => Cover is not null;

    [ObservableProperty]
    public partial bool IsFocused { get; set; }

    internal CancellationTokenSource? CoverLoad { get; set; }
}

/// <summary>The header bar's two reachable chips: Back, then the sort chip.</summary>
public enum HandheldHeaderItem
{
    Back,
    Sort,
}

/// <summary>An open folder: its games, the sort, and the window of columns realized on screen.</summary>
public sealed partial class HandheldFolderViewModel : ObservableObject
{
    public HandheldFolderViewModel(HandheldPlatform platform, int homeSlot)
    {
        Platform = platform;
        HomeSlot = homeSlot;
        AccentBrush = Color.TryParse(platform.AccentColor, out var color) ? new SolidColorBrush(color) : Brushes.Gray;
    }

    public HandheldPlatform Platform { get; }

    /// <summary>The home slot the folder was opened from, so B lands back on it.</summary>
    public int HomeSlot { get; }

    public string Name => Platform.Name;
    public IBrush AccentBrush { get; }

    /// <summary>Every game in library order, as loaded; <see cref="Items"/> is this in the current sort.</summary>
    public IReadOnlyList<HandheldGameEntry> Source { get; internal set; } = [];

    /// <summary>All the folder's tiles in the current sort. Only <see cref="VisibleTiles"/> are on screen.</summary>
    public IReadOnlyList<HandheldGameTileViewModel> Items { get; internal set; } = [];

    /// <summary>The columns on screen (four plus the one peeking in), column by column.</summary>
    public ObservableCollection<HandheldGameTileViewModel> VisibleTiles { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasGames))]
    public partial bool IsLoaded { get; set; }

    public string CountText => IsLoaded ? HandheldText.GameCount(Items.Count) : string.Empty;
    public bool IsEmpty => IsLoaded && Items.Count == 0;
    public bool HasGames => Items.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortLabel))]
    public partial HandheldFolderSort Sort { get; set; }

    public string SortLabel => HandheldFolderSorting.Label(Sort);

    [ObservableProperty]
    public partial int SelectedIndex { get; set; }

    [ObservableProperty]
    public partial int FirstVisibleColumn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBackFocused))]
    [NotifyPropertyChangedFor(nameof(IsSortFocused))]
    public partial HandheldHeaderItem? FocusedHeaderItem { get; set; }

    public bool IsBackFocused => FocusedHeaderItem == HandheldHeaderItem.Back;
    public bool IsSortFocused => FocusedHeaderItem == HandheldHeaderItem.Sort;

    public HandheldGameTileViewModel? SelectedTile =>
        SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;
}

public enum HandheldDockKind
{
    AllApps,
    App,
    Menu,
}

/// <summary>A dock button: All apps, one of the companion's five pinned slots, or the menu button.</summary>
public sealed partial class HandheldDockItemViewModel(HandheldDockKind kind, SecondScreenSlotViewModel? slot = null)
    : ObservableObject
{
    public HandheldDockKind Kind { get; } = kind;

    /// <summary>The companion dock slot this button shows; the same object the companion's own dock binds.</summary>
    public SecondScreenSlotViewModel? Slot { get; } = slot;

    public bool IsAllApps => Kind == HandheldDockKind.AllApps;
    public bool IsApp => Kind == HandheldDockKind.App;
    public bool IsMenu => Kind == HandheldDockKind.Menu;

    [ObservableProperty]
    public partial bool IsFocused { get; set; }
}

public enum HandheldStageKind
{
    None,
    Folder,
    EmptySlot,
    Game,
    App,
    AllApps,
    Menu,
    Header,
}

/// <summary>What the Stage (the top screen) shows for the current focus.</summary>
public sealed partial class HandheldStageViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFolder))]
    [NotifyPropertyChangedFor(nameof(IsGame))]
    [NotifyPropertyChangedFor(nameof(IsApp))]
    [NotifyPropertyChangedFor(nameof(IsGlyph))]
    [NotifyPropertyChangedFor(nameof(HasPlate))]
    public partial HandheldStageKind Kind { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlate))]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Subtitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Monogram { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IBrush AccentBrush { get; set; } = Brushes.Gray;

    /// <summary>A focused game's cover, decoded for the Stage. Owned (and disposed) by the shell.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArt))]
    public partial Bitmap? Art { get; set; }

    /// <summary>The line glyph drawn on the Stage's card for things that have no art of their own.</summary>
    [ObservableProperty]
    public partial HandheldGlyph Glyph { get; set; }

    /// <summary>A focused dock app's icon. Owned by the companion dock, not by the Stage.</summary>
    [ObservableProperty]
    public partial Bitmap? AppIcon { get; set; }

    /// <summary>"12 / 40" when the focused game has RetroAchievements data; null hides the pill.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAchievements))]
    public partial string? AchievementText { get; set; }

    public bool IsFolder => Kind == HandheldStageKind.Folder;
    public bool IsGame => Kind == HandheldStageKind.Game;
    public bool IsApp => Kind == HandheldStageKind.App;

    /// <summary>The kinds drawn as a plain glyph card: an empty slot, All apps, the menu, the header.</summary>
    public bool IsGlyph => Kind is HandheldStageKind.EmptySlot or HandheldStageKind.AllApps
        or HandheldStageKind.Menu or HandheldStageKind.Header;

    public bool HasArt => Art is not null;
    public bool HasAchievements => !string.IsNullOrEmpty(AchievementText);
    public bool HasPlate => Kind != HandheldStageKind.None && !string.IsNullOrEmpty(Title);
}

/// <summary>A line glyph the Handheld views draw; the views map it to a vector (see <c>HandheldIcons</c>).</summary>
public enum HandheldGlyph
{
    None,
    AllApps,
    Menu,
    Plus,
    Back,
    Sort,
}

/// <summary>One button hint in the Stage's hint pill, e.g. (A, "Open"); a button with no letter draws <see cref="Icon"/>.</summary>
public sealed record HandheldHint(string Button, string Label, HandheldGlyph Icon = HandheldGlyph.None)
{
    public bool HasIcon => Icon != HandheldGlyph.None;
}

public sealed partial class HandheldMenuItemViewModel(string label, Func<Task> invoke) : ObservableObject
{
    public string Label { get; } = label;
    internal Func<Task> Invoke { get; } = invoke;

    [ObservableProperty]
    public partial bool IsFocused { get; set; }
}

internal static class HandheldText
{
    public static string GameCount(int count) => count == 1 ? "1 game" : $"{count} games";

    public static string Playtime(TimeSpan playtime) => playtime.TotalHours >= 1
        ? $"{(int)playtime.TotalHours} h {playtime.Minutes} min played"
        : playtime.TotalMinutes >= 1 ? $"{(int)playtime.TotalMinutes} min played" : "Not played yet";
}
