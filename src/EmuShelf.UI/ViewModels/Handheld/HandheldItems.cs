using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.App.Services;
using EmuShelf.Core.Handheld;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Systems;

namespace EmuShelf.App.ViewModels;

/// <summary>A platform that has games, as a home folder shows it.</summary>
public sealed record HandheldPlatform(string SystemId, string Name, string ShortName, string AccentColor, int GameCount,
    SystemEra Era = SystemEra.Disc, IReadOnlyList<string>? Backdrops = null);

/// <summary>
/// The Stage's era backdrop: a pixel grid (cartridge) or scanlines (disc), in one colour. Value equality, so
/// the view crossfades only when the colour or the era actually changes, never on a move within a system.
/// <paramref name="Animated"/> is the system's animated backdrops (the same list instance for every move
/// within a system), which the Thor's native Stage draws instead of the era motif (M45 H7).
/// </summary>
public sealed record HandheldBackdrop(Color Color, SystemEra Era, IReadOnlyList<string>? Animated = null);

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
    GameViewModel? ViewModel = null,
    double AchievementRatio = 0);

/// <summary>
/// What the Title page shows beyond the folder's facts, read when it opens: the emulator that will run the
/// game, its discs, and where it will run (<see cref="LaunchScreenPlanner"/>'s answer).
/// </summary>
public sealed record HandheldTitleDetails(
    string? EmulatorName,
    IReadOnlyList<int> DiscNumbers,
    int SelectedDiscNumber,
    LaunchScreenDecision Screen,
    bool HasSecondScreen,
    bool IsDualScreenSystem)
{
    public bool IsMultiDisc => DiscNumbers.Count > 1;

    /// <summary>A single-disc game on the built-in screen; what a fake or a failed read falls back to.</summary>
    public static HandheldTitleDetails Default { get; } =
        new(null, [1], 1, LaunchScreenDecision.BuiltIn, HasSecondScreen: false, IsDualScreenSystem: false);
}

/// <summary>The scraped art the Title page's hero draws: the fan art and the logo.</summary>
public enum HandheldArtKind
{
    Fanart,
    Logo,
}

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

    /// <summary>
    /// The first <paramref name="count"/> of a platform's games in the same order, built without a view model for
    /// the rest (the covers peeking out of a folder need three, not the whole platform). Release them as usual.
    /// </summary>
    Task<IReadOnlyList<HandheldGameEntry>> GetFirstGamesAsync(string systemId, int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// The given games (by id) in the given order, each with its view model; ids no longer in the library are
    /// skipped. Your folders, the Recent folder and the games pinned to home read their games this way.
    /// </summary>
    Task<IReadOnlyList<HandheldGameEntry>> GetGamesByIdAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken = default);

    /// <summary>Every game id in the library (one per title set), for reconciling the home layout.</summary>
    Task<IReadOnlySet<long>> GetGameIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>The ids of the last <paramref name="count"/> games played, newest first (the Recently Played source).</summary>
    Task<IReadOnlyList<long>> GetRecentGameIdsAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>Releases the view models a folder held, once the folder closes.</summary>
    void ReleaseGames(IReadOnlyList<HandheldGameEntry> games);

    /// <summary>
    /// Decodes a game's cover off the UI thread at <paramref name="decodeWidth"/> pixels: from the cached
    /// thumbnail for a tile, or from the full cover for the Stage. Null when the game has no cover.
    /// </summary>
    Task<Bitmap?> LoadCoverAsync(HandheldGameEntry game, int decodeWidth, bool fullSize, CancellationToken cancellationToken);

    /// <summary>
    /// Decodes a game's scraped fan art or logo off the UI thread. Null when the game has none, which the
    /// Stage answers with the cover on a tinted backdrop.
    /// </summary>
    Task<Bitmap?> LoadArtAsync(HandheldGameEntry game, HandheldArtKind kind, CancellationToken cancellationToken);

    /// <summary>Reads the emulator, discs and launch screen for a game's Title page.</summary>
    Task<HandheldTitleDetails> GetTitleDetailsAsync(HandheldGameEntry game, CancellationToken cancellationToken = default);

    /// <summary>Makes <paramref name="discNumber"/> the disc a multi-disc game starts on; false if it could not be saved.</summary>
    Task<bool> SelectDiscAsync(HandheldGameEntry game, int discNumber);

    /// <summary>Pins the screen a system's games run on ("Always use this screen").</summary>
    void RememberLaunchScreen(string systemId, GameLaunchScreen screen);

    /// <summary>
    /// Runs the app's launch core on the chosen screen (save sync, play recording, the status line). True once
    /// the emulator started; false when the launch was refused or failed, and the status line says why.
    /// </summary>
    Task<bool> LaunchAsync(HandheldGameEntry game, GameLaunchScreen screen);

    Task SwitchModeAsync(InterfaceMode mode);

    /// <summary>
    /// Builds Settings for Handheld mode (M45 H6a): the controller projection Gamepad mode's Settings uses, over a fresh
    /// settings model. Null when it cannot open now (the app is busy, or the read failed and the status line says why).
    /// </summary>
    Task<GamepadSettingsViewModel?> CreateSettingsAsync() => Task.FromResult<GamepadSettingsViewModel?>(null);

    /// <summary>Settings closed (saved or not): the projection is let go of.</summary>
    void CloseSettings(GamepadSettingsViewModel settings, bool saved) => settings.Dispose();

    void Quit();

    // ---- The Y sheet's actions, Search, Add games and the surfaces behind them (M45 H6c) ----
    // Each is Gamepad mode's: the same view models and the same library calls, so both modes do the same thing. The
    // defaults are what a head without them (a test fake) has: nothing to open, and nothing changes.

    /// <summary>
    /// The ids of the games whose name contains <paramref name="query"/> (the library's title or the name shown, as Gamepad
    /// mode's search matches), one per title set, A–Z by the name shown.
    /// </summary>
    Task<IReadOnlyList<long>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<long>>([]);

    /// <summary>A platform's game ids (one per title set), without a view model per game: what a folder's batch scrape takes.</summary>
    async Task<IReadOnlyList<long>> GetPlatformGameIdsAsync(string systemId, CancellationToken cancellationToken = default)
    {
        var games = await GetGamesAsync(systemId, cancellationToken);
        var ids = games.Select(game => game.Id).ToList();
        ReleaseGames(games);
        return ids;
    }

    /// <summary>A game's discs and the one it starts on (one disc for most games).</summary>
    HandheldDiscs DiscsOf(HandheldGameEntry game) => game.ViewModel is { } viewModel
        ? new HandheldDiscs(viewModel.Discs.Select(disc => disc.Number).ToList(), viewModel.SelectedDiscNumber)
        : new HandheldDiscs([1], 1);

    /// <summary>Whether the game has an achievements screen (a RetroAchievements set, or Steam achievements).</summary>
    bool HasAchievementDetails(HandheldGameEntry game) => !string.IsNullOrEmpty(game.AchievementText);

    /// <summary>Gamepad mode's full-screen achievements for the game, cache first (it refreshes itself when stale).</summary>
    Task<AchievementDetailsViewModel?> CreateAchievementsAsync(HandheldGameEntry game) =>
        Task.FromResult<AchievementDetailsViewModel?>(null);

    /// <summary>The name to start editing from: the library's own title (a scraped name shown over it stays as it is).</summary>
    string EditableTitle(HandheldGameEntry game) => game.ViewModel?.Title ?? game.Title;

    /// <summary>Renames the game in EmuShelf's library (never its file). False when it was refused; the status line says why.</summary>
    Task<bool> RenameAsync(HandheldGameEntry game, string title) => Task.FromResult(false);

    /// <summary>Removes the game from EmuShelf's library only; its files are never touched.</summary>
    Task<bool> RemoveFromLibraryAsync(HandheldGameEntry game) => Task.FromResult(false);

    /// <summary>Gamepad mode's ScreenScraper overlay for one game (not yet loading); null when ScreenScraper is unavailable.</summary>
    GamepadScraperViewModel? CreateScraper(HandheldGameEntry game) => null;

    /// <summary>The scraper closed: let go of it, and refresh the library if it applied anything.</summary>
    Task CloseScraperAsync(GamepadScraperViewModel scraper)
    {
        scraper.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Gamepad mode's batch scraper over these games; null when it cannot run.</summary>
    GamepadBatchScraperViewModel? CreateBatchScraper(IReadOnlyList<long> gameIds, string name) => null;

    /// <summary>The batch scraper closed: let go of it, and refresh the library if it wrote anything.</summary>
    Task CloseBatchScraperAsync(GamepadBatchScraperViewModel batch)
    {
        batch.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Gamepad mode's web cover search for the game (not yet searching); null when web image search is off.</summary>
    GamepadCoverSearchViewModel? CreateCoverSearch(HandheldGameEntry game) => null;

    /// <summary>Sets a picked web cover on the game (Gamepad mode's import: thumbnail, palette, status line).</summary>
    Task ApplyCoverAsync(HandheldGameEntry game, PickedGameCover cover) => Task.CompletedTask;

    /// <summary>Why Set cover cannot search here (web image search off, or unavailable), for the notice it shows instead.</summary>
    string CoverSearchUnavailableReason => "Web image search is off. Turn it on in Settings → Artwork & metadata.";

    /// <summary>The platform folder picker (Android's on the Thor); null when cancelled or unusable.</summary>
    Task<string?> PickImportFolderAsync() => Task.FromResult<string?>(null);

    /// <summary>The systems a folder can be imported for, in the library's order (PlayStation 3 imports only from RPCS3).</summary>
    IReadOnlyList<HandheldImportSystem> ImportSystems => [];

    /// <summary>Scans the folder for the system and adds what it finds (Gamepad mode's import; the status line reports it).</summary>
    Task ImportFolderAsync(string folder, string systemId) => Task.CompletedTask;

    /// <summary>The app is busy with library work (an import, a rename): a new one waits.</summary>
    bool IsBusy => false;
}

/// <summary>A game's disc numbers and the one it starts on.</summary>
public sealed record HandheldDiscs(IReadOnlyList<int> Numbers, int Selected);

/// <summary>A system a folder of games can be added for (Add games' chooser).</summary>
public sealed record HandheldImportSystem(string Id, string Name, string ShortName, string AccentColor);

public enum HandheldZone
{
    Grid,
    Header,
    Dock,
}

/// <summary>What kind of folder is open (or focused): a platform's, one of yours, or Recent.</summary>
public enum HandheldFolderKind
{
    Platform,
    User,
    Recent,

    /// <summary>Search's matches (M45 H6c), opened from the keyboard's Done; not on home.</summary>
    Search,
}

/// <summary>
/// A folder as the shell opens it: where its games come from and how its tile looks. <see cref="Key"/> names it
/// for the peek-cover cache (<c>p:ps2</c>, <c>u:…</c>, <c>r:recent</c>).
/// </summary>
public sealed record HandheldFolderSource(
    HandheldFolderKind Kind,
    string Key,
    string Name,
    string AccentColor,
    string Monogram,
    int GameCount,
    HandheldPlatform? Platform = null,
    string? FolderId = null,
    IReadOnlyList<long>? GameIds = null,
    HandheldFolderSort Sort = HandheldFolderSort.Manual,
    bool HoldsSettings = false)
{
    public bool IsUser => Kind == HandheldFolderKind.User;
    public bool IsRecent => Kind == HandheldFolderKind.Recent;
    public bool IsSearch => Kind == HandheldFolderKind.Search;

    /// <summary>The Stage's line under the folder's name.</summary>
    public string Description => Kind switch
    {
        HandheldFolderKind.User when HoldsSettings =>
            $"Your folder · {(GameCount == 0 ? "Settings" : $"{HandheldText.GameCount(GameCount)} and Settings")}",
        HandheldFolderKind.User => $"Your folder · {(GameCount == 0 ? "empty" : HandheldText.GameCount(GameCount))}",
        HandheldFolderKind.Recent => GameCount == 0 ? "Made for you · games you play appear here" : $"Made for you · {HandheldText.GameCount(GameCount)}",
        HandheldFolderKind.Search => $"Search · {HandheldText.GameCount(GameCount)}",
        _ => $"Folder · {HandheldText.GameCount(GameCount)}",
    };

    public static HandheldFolderSource ForPlatform(HandheldPlatform platform) =>
        new(HandheldFolderKind.Platform, "p:" + platform.SystemId, platform.Name, platform.AccentColor, platform.ShortName,
            platform.GameCount, Platform: platform);

    public static HandheldFolderSource ForUser(HandheldUserFolder folder) =>
        new(HandheldFolderKind.User, "u:" + folder.Id, folder.Name, folder.Color, string.Empty, folder.GameIds.Count,
            FolderId: folder.Id, GameIds: folder.GameIds, Sort: folder.Sort, HoldsSettings: folder.HoldsSettings);

    public static HandheldFolderSource ForRecent(IReadOnlyList<long> gameIds) =>
        new(HandheldFolderKind.Recent, "r:recent", "Recent", HandheldHomeLayout.RecentColor, string.Empty, gameIds.Count,
            GameIds: gameIds);

    /// <summary>Search's matches as a folder: named for the query, on the Launcher's indigo, A–Z as the search found them.</summary>
    public static HandheldFolderSource ForSearch(string query, IReadOnlyList<long> gameIds, int generation) =>
        new(HandheldFolderKind.Search, $"s:{generation}", $"“{query}”", SearchColor, string.Empty, gameIds.Count, GameIds: gameIds,
            Sort: HandheldFolderSort.Title);

    /// <summary>The Search folder's colour: the Launcher's indigo accent (it is EmuShelf's own, like Settings).</summary>
    public const string SearchColor = "#5B58D9";
}

/// <summary>How a home slot looks in move mode.</summary>
public enum HandheldMoveLook
{
    None,

    /// <summary>The tile being carried, at the slot it would land on.</summary>
    Carried,

    /// <summary>A game or one of your folders the carried game would merge with.</summary>
    DropTarget,
}

/// <summary>One slot on a home page: a folder (platform, yours, Recent), a pinned game, or an empty place.</summary>
public sealed partial class HandheldHomeSlotViewModel : ObservableObject
{
    public HandheldHomeSlotViewModel(int index, HandheldSlot slot, HandheldFolderSource? folder, HandheldGameEntry? game = null,
        Bitmap? cover = null, bool isMoving = false, HandheldMoveLook moveLook = HandheldMoveLook.None,
        Bitmap? carriedCover = null, string? carriedTitle = null)
    {
        CarriedCover = carriedCover;
        CarriedTitle = carriedTitle;
        Index = index;
        Slot = slot;
        Folder = folder;
        Game = game;
        Cover = cover;
        _isMoving = isMoving;
        MoveLook = moveLook;
        var colour = folder?.AccentColor;
        AccentBrush = colour is not null && Color.TryParse(colour, out var color) ? new SolidColorBrush(color) : Brushes.Gray;
    }

    public int Index { get; }
    public HandheldSlot Slot { get; }

    /// <summary>The folder this slot opens; null for a pinned game or an empty slot.</summary>
    public HandheldFolderSource? Folder { get; }

    /// <summary>The pinned game (owned by the shell's pin cache); null until it is read, or for other kinds.</summary>
    public HandheldGameEntry? Game { get; }

    /// <summary>The pinned game's tile cover (owned by the shell's pin cache).</summary>
    public Bitmap? Cover { get; }

    public HandheldPlatform? Platform => Folder?.Platform;

    public bool IsEmpty => Slot.IsEmpty;
    public bool IsFolder => Folder is not null;
    public bool IsGame => Slot.Kind == HandheldSlotKind.Game;

    /// <summary>The Settings tile: a glyph on the indigo accent, an app on the grid (decision 18).</summary>
    public bool IsSettings => Slot.IsSettings;
    public bool HasCover => Cover is not null;
    public bool IsUnavailable => Game is { IsAvailable: false };
    public bool IsNew => Slot.IsNew;
    public string Name => Folder?.Name ?? Game?.Title ?? (IsSettings ? HandheldSettingsText.Name : string.Empty);
    public string Monogram => Folder?.Monogram ?? string.Empty;

    /// <summary>Your folders and Recent show a glyph on the flap instead of a platform's short name (decision 7).</summary>
    public HandheldGlyph FolderGlyph => Folder?.Kind switch
    {
        HandheldFolderKind.User => HandheldGlyph.Star,
        HandheldFolderKind.Recent => HandheldGlyph.Clock,
        _ => HandheldGlyph.None,
    };

    public bool HasFolderGlyph => FolderGlyph != HandheldGlyph.None;
    public IBrush AccentBrush { get; }
    public string CountText => Folder is null ? string.Empty : HandheldText.GameCount(Folder.GameCount);

    private bool _isMoving;

    /// <summary>Move mode is on: empty slots light up as places to land. Set in place, so lifting a tile rebuilds nothing.</summary>
    public bool IsMoving
    {
        get => _isMoving;
        set
        {
            if (SetProperty(ref _isMoving, value))
                OnPropertyChanged(nameof(IsLandingPlace));
        }
    }

    public HandheldMoveLook MoveLook { get; }
    public bool IsCarried => MoveLook == HandheldMoveLook.Carried;
    public bool IsDropTarget => MoveLook == HandheldMoveLook.DropTarget;
    public bool IsLandingPlace => IsMoving && IsEmpty;

    /// <summary>On a merge target: the carried game, held over it (owned by the shell's pin cache).</summary>
    public Bitmap? CarriedCover { get; }

    public string? CarriedTitle { get; }
    public bool HasCarriedCover => CarriedCover is not null;

    /// <summary>What the slot looks like, for deciding whether a rebuilt page changed and must be pictured again.</summary>
    public string PictureKey =>
        $"{Slot}|{Folder?.Name}|{Folder?.AccentColor}|{Folder?.GameCount}|{Game?.Title}|{Game?.IsAvailable}|{RuntimeRef(Cover)}|{MoveLook}|{RuntimeRef(CarriedCover)}";

    private static int RuntimeRef(object? value) => value is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);

    [ObservableProperty]
    public partial bool IsFocused { get; set; }

    // The first three covers peeking out of a focused folder (owned by the shell's peek cache, shared with the Stage).
    [ObservableProperty]
    public partial Bitmap? PeekA { get; set; }

    [ObservableProperty]
    public partial Bitmap? PeekB { get; set; }

    [ObservableProperty]
    public partial Bitmap? PeekC { get; set; }
}

/// <summary>One home page. Its slots are replaced one at a time when what they show changes, so an edit or a step in
/// move mode rebuilds the tiles that changed, not the page.</summary>
public sealed class HandheldHomePageViewModel(int index, HandheldHomeGrid grid, double width, IEnumerable<HandheldHomeSlotViewModel> slots)
{
    public int Index { get; } = index;
    public ObservableCollection<HandheldHomeSlotViewModel> Slots { get; } = new(slots);

    /// <summary>The page's shape (5×4, or 7×3 on the 16:9 screen) and width on the Launcher's canvas.</summary>
    public int Columns { get; } = grid.Columns;
    public int Rows { get; } = grid.Rows;
    public double Width { get; } = width;
}

public sealed partial class HandheldPageDotViewModel(int index) : ObservableObject
{
    public int Index { get; } = index;

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }
}

/// <summary>
/// A tile inside an open folder: a game, or the Settings tile when it was put in one of your folders (decision 18), which
/// comes first. A game's tile owns its decoded cover while it is in the visible window.
/// </summary>
public sealed partial class HandheldGameTileViewModel(int index, HandheldGameEntry entry, bool isSettings = false) : ObservableObject
{
    /// <summary>What the Settings tile stands on inside a folder: not a game (its id is never a game's).</summary>
    public static HandheldGameEntry SettingsEntry { get; } =
        new(-1, HandheldSettingsText.Name, string.Empty, string.Empty, null, 0, TimeSpan.Zero, IsAvailable: true);

    public int Index { get; } = index;
    public HandheldGameEntry Entry { get; } = entry;
    public bool IsSettings { get; } = isSettings;
    public bool IsGame => !IsSettings;
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

    /// <summary>Your folders' flap.</summary>
    Star,

    /// <summary>The Recent folder's flap.</summary>
    Clock,
}

/// <summary>An open folder: its games, the sort, and the window of columns realized on screen.</summary>
public sealed partial class HandheldFolderViewModel : ObservableObject
{
    public HandheldFolderViewModel(HandheldFolderSource source, int homeSlot)
    {
        Source = source;
        HomeSlot = homeSlot;
        AccentBrush = Color.TryParse(source.AccentColor, out var color) ? new SolidColorBrush(color) : Brushes.Gray;
    }

    /// <summary>Where the folder's games come from (a platform, one of your folders, Recent).</summary>
    public HandheldFolderSource Source { get; internal set; }

    public HandheldPlatform? Platform => Source.Platform;

    /// <summary>The home slot (across pages) whose "New" ribbon comes off when this folder closes; -1 for none.</summary>
    internal int RibbonIndex { get; init; } = -1;

    /// <summary>Recent is sorted for you, newest first: its sort chip is away (decision 14).</summary>
    public bool CanSort => !Source.IsRecent;

    /// <summary>What the empty-folder state says to do.</summary>
    public string EmptyHint => Source.Kind switch
    {
        HandheldFolderKind.User => "Press Y on a game and choose Add to folder…",
        HandheldFolderKind.Recent => "Games you play appear here",
        _ => "Games you add to this platform appear here",
    };

    /// <summary>The home slot the folder was opened from, so B lands back on it (on the page shape in use).</summary>
    public int HomeSlot { get; internal set; }

    /// <summary>The same place across all pages of the saved layout, whichever shape a screen draws home in.</summary>
    public int HomeIndex { get; internal set; }

    public string Name => Source.Name;
    public IBrush AccentBrush { get; }

    /// <summary>Every game in the folder's own order, as loaded; <see cref="Items"/> is this in the current sort.</summary>
    public IReadOnlyList<HandheldGameEntry> Games { get; internal set; } = [];

    /// <summary>All the folder's tiles in the current sort. Only <see cref="VisibleTiles"/> are on screen.</summary>
    public IReadOnlyList<HandheldGameTileViewModel> Items
    {
        get => _items;
        internal set
        {
            // A re-read can empty the folder (its last game or Settings taken out from inside it): the count and the
            // empty state follow.
            _items = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CountText));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasGames));
        }
    }

    private IReadOnlyList<HandheldGameTileViewModel> _items = [];

    /// <summary>The columns on screen (four plus the one peeking in), column by column.</summary>
    public ObservableCollection<HandheldGameTileViewModel> VisibleTiles { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasGames))]
    public partial bool IsLoaded { get; set; }

    // The Settings tile, when it is in the folder, is not a game.
    public string CountText => IsLoaded ? HandheldText.GameCount(Items.Count(tile => !tile.IsSettings)) : string.Empty;
    public bool IsEmpty => IsLoaded && Items.Count == 0;
    public bool HasGames => Items.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortLabel))]
    public partial HandheldFolderSort Sort { get; set; }

    public string SortLabel => HandheldFolderSorting.Label(Sort);

    [ObservableProperty]
    public partial int SelectedIndex { get; set; }

    /// <summary>The first column shown on the screen the Launcher is on now (one of the two below).</summary>
    [ObservableProperty]
    public partial int FirstVisibleColumn { get; set; }

    /// <summary>
    /// The first column shown by the Launcher on the 1240×1080 screen (four columns) and on the 16:9 one (seven).
    /// Both follow the selection all the time, so either Launcher can take over at a swap already scrolled (M45 H5).
    /// </summary>
    [ObservableProperty]
    public partial int NarrowFirstColumn { get; set; }

    [ObservableProperty]
    public partial int WideFirstColumn { get; set; }

    public int FirstColumnFor(bool wide) => wide ? WideFirstColumn : NarrowFirstColumn;

    /// <summary>The tiles are being replaced by a sort or a re-read: a column change now is a jump, not a scroll.</summary>
    public bool IsResorting { get; internal set; }

    /// <summary>
    /// The first column realized in <see cref="VisibleTiles"/>: one behind <see cref="FirstVisibleColumn"/>
    /// when there is one, sitting just off the left edge so a scroll slides it out rather than dropping it.
    /// </summary>
    [ObservableProperty]
    public partial int WindowFirstColumn { get; set; }

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
    /// <summary>A game's Title page is open: its fan art full-bleed with the logo, or its cover on a tint.</summary>
    Hero,

    /// <summary>The Settings tile is focused (on home or in a folder): its glyph on the indigo accent, with a plate.</summary>
    SettingsTile,

    /// <summary>Settings is open: what the focused row (or section, or choice) is, its value and its description.</summary>
    Setting,

    /// <summary>A panel is open (M45 H6c: the scraper, cover search, achievements, Search, Edit title): its card.</summary>
    Panel,
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
    [NotifyPropertyChangedFor(nameof(IsHero))]
    [NotifyPropertyChangedFor(nameof(IsHeroArt))]
    [NotifyPropertyChangedFor(nameof(IsHeroFallback))]
    [NotifyPropertyChangedFor(nameof(IsCentreVisible))]
    [NotifyPropertyChangedFor(nameof(ShowAchievementPill))]
    [NotifyPropertyChangedFor(nameof(HasMedium))]
    [NotifyPropertyChangedFor(nameof(HasFlatCover))]
    [NotifyPropertyChangedFor(nameof(IsSettingsTile))]
    [NotifyPropertyChangedFor(nameof(IsSetting))]
    [NotifyPropertyChangedFor(nameof(IsPanel))]
    public partial HandheldStageKind Kind { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlate))]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Subtitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Monogram { get; set; } = string.Empty;

    /// <summary>The flap's glyph for your folders (a star) and Recent (a clock); null for a platform's monogram.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolderGlyph))]
    public partial HandheldGlyph FolderGlyph { get; set; }

    public bool HasFolderGlyph => FolderGlyph != HandheldGlyph.None;

    // A focused folder's first three covers, peeking out once its flap tilts. Owned by the shell's peek cache.
    [ObservableProperty]
    public partial Bitmap? PeekA { get; set; }

    [ObservableProperty]
    public partial Bitmap? PeekB { get; set; }

    [ObservableProperty]
    public partial Bitmap? PeekC { get; set; }

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
    [NotifyPropertyChangedFor(nameof(ShowAchievementPill))]
    public partial string? AchievementText { get; set; }

    /// <summary>The Title page's fan art, decoded for the full screen. Owned (and disposed) by the shell.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHeroArt))]
    [NotifyPropertyChangedFor(nameof(IsHeroFallback))]
    [NotifyPropertyChangedFor(nameof(HasPlate))]
    [NotifyPropertyChangedFor(nameof(IsCentreVisible))]
    [NotifyPropertyChangedFor(nameof(IsGame))]
    [NotifyPropertyChangedFor(nameof(HasFlatCover))]
    [NotifyPropertyChangedFor(nameof(ShowAchievementPill))]
    public partial Bitmap? Fanart { get; set; }

    /// <summary>The game's logo over the fan art; without one the hero sets the title in type.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLogo))]
    public partial Bitmap? Logo { get; set; }

    /// <summary>
    /// The era motif behind a folder or a game, in the system's colour (on a Title page without fan art, the
    /// colour picked from the cover). Null (an app, an empty slot, the menu) leaves the plain light gradient.
    /// </summary>
    [ObservableProperty]
    public partial HandheldBackdrop? Backdrop { get; set; }

    /// <summary>The focused game's physical medium (case, cartridge, card); null keeps the flat cover.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMedium))]
    [NotifyPropertyChangedFor(nameof(HasFlatCover))]
    public partial EmuShelf.Rendering.Shells.MediaShell? Medium { get; set; }

    /// <summary>The platform colour the medium's studio and unprinted faces take.</summary>
    [ObservableProperty]
    public partial Color MediumAccent { get; set; } = Colors.Gray;

    /// <summary>The medium's turn (right stick, idle sway, the spring), in radians.</summary>
    [ObservableProperty]
    public partial double MediumYaw { get; set; } = MediaRotationModel.RestYaw;

    [ObservableProperty]
    public partial double MediumPitch { get; set; } = MediaRotationModel.RestPitch;

    /// <summary>Whether the last pose was a step of the idle sway (not the stick or the spring).</summary>
    public bool MediumPoseIsSway { get; private set; }

    /// <summary>Counts pose updates: the native Stage takes one per update, not one per angle.</summary>
    [ObservableProperty]
    public partial long MediumPoseVersion { get; private set; }

    /// <summary>Sets the medium's pose in one update.</summary>
    public void SetMediumPose(double yaw, double pitch, bool sway)
    {
        MediumPoseIsSway = sway;
        MediumYaw = yaw;
        MediumPitch = pitch;
        MediumPoseVersion++;
    }

    /// <summary>The GPU path could not start on this device; every game keeps its flat cover from then on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMedium))]
    [NotifyPropertyChangedFor(nameof(HasFlatCover))]
    public partial bool IsMediumUnavailable { get; set; }

    /// <summary>A browsed game is drawn as its physical medium.</summary>
    public bool HasMedium => Kind == HandheldStageKind.Game && Medium is not null && !IsMediumUnavailable;

    /// <summary>A game (or a Title page without fan art) is drawn as its flat cover.</summary>
    public bool HasFlatCover => IsGame && !HasMedium;

    public bool IsHero => Kind == HandheldStageKind.Hero;
    public bool IsHeroArt => IsHero && Fanart is not null;

    /// <summary>A Title page for a game with no fan art: the cover, large, on a tinted backdrop with the plate.</summary>
    public bool IsHeroFallback => IsHero && Fanart is null;

    public bool HasLogo => Logo is not null;

    /// <summary>The centred card (folder, cover, app, glyph) and its plate; the fan-art hero replaces both.</summary>
    public bool IsCentreVisible => !IsHeroArt;

    public bool IsFolder => Kind == HandheldStageKind.Folder;
    public bool IsGame => Kind is HandheldStageKind.Game || IsHeroFallback;
    public bool IsApp => Kind == HandheldStageKind.App;
    public bool IsSettingsTile => Kind == HandheldStageKind.SettingsTile;
    public bool IsSetting => Kind == HandheldStageKind.Setting;
    public bool IsPanel => Kind == HandheldStageKind.Panel;

    /// <summary>
    /// The open panel's card (M45 H6c): a new record whenever it changes, so the view follows it and the native Stage
    /// pictures it again (a new subject also pops, through the title).
    /// </summary>
    [ObservableProperty]
    public partial HandheldPanelCard? PanelCard { get; set; }

    // ---- Settings open (Kind Setting): the card that is the detail half of Settings' master-detail (M45 H6a) ----

    /// <summary>Where the card's subject sits: the section ("Emulators"), or "Settings" for a section itself.</summary>
    [ObservableProperty]
    public partial string SettingEyebrow { get; set; } = string.Empty;

    /// <summary>The focused row's value ("On", "DuckStation", a folder), large; empty hides it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSettingValue))]
    public partial string SettingValue { get; set; } = string.Empty;

    public bool HasSettingValue => !string.IsNullOrEmpty(SettingValue);

    /// <summary>The value reports a problem (an emulator missing, a permission not granted).</summary>
    [ObservableProperty]
    public partial bool IsSettingWarning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSettingDescription))]
    public partial string SettingDescription { get; set; } = string.Empty;

    public bool HasSettingDescription => !string.IsNullOrEmpty(SettingDescription);

    /// <summary>The section's status line (a scan's progress, "Connected as …"); empty hides it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSettingStatus))]
    public partial string SettingStatus { get; set; } = string.Empty;

    public bool HasSettingStatus => !string.IsNullOrEmpty(SettingStatus);

    /// <summary>
    /// Counts changes to the card that keep its subject (a toggle flipped, a status line): the native Stage pictures the
    /// card again on each, without the centre pop a new subject gets.
    /// </summary>
    [ObservableProperty]
    public partial int SettingVersion { get; set; }

    /// <summary>The kinds drawn as a plain glyph card: an empty slot, All apps, the menu, the header.</summary>
    public bool IsGlyph => Kind is HandheldStageKind.EmptySlot or HandheldStageKind.AllApps
        or HandheldStageKind.Menu or HandheldStageKind.Header;

    public bool HasArt => Art is not null;
    public bool HasAchievements => !string.IsNullOrEmpty(AchievementText);

    /// <summary>The corner achievements pill; the fan-art hero carries the count in its meta line instead.</summary>
    public bool ShowAchievementPill => HasAchievements && !IsHeroArt;
    // Settings' card carries its own title, so it has no plate under it.
    public bool HasPlate => Kind is not (HandheldStageKind.None or HandheldStageKind.Setting or HandheldStageKind.Panel) && !IsHeroArt &&
        !string.IsNullOrEmpty(Title);
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

    /// <summary>Your folders' flap.</summary>
    Star,

    /// <summary>The Recent folder's flap.</summary>
    Clock,

    /// <summary>The Settings tile (decision 18).</summary>
    Settings,

    // The panels, the Search folder and the toasts (M45 H6c).
    Search,
    Scrape,
    Cover,
    Edit,
    Trophy,
    Add,
    Remove,
    Alert,
    Working,
    Check,
    Update,
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

/// <summary>The words the Settings tile uses wherever it appears.</summary>
internal static class HandheldSettingsText
{
    public const string Name = "Settings";
    public const string Subtitle = "Library, emulators, saves and more";
}

internal static partial class HandheldText
{
    public static string GameCount(int count) => count == 1 ? "1 game" : $"{count} games";

    public static string Playtime(TimeSpan playtime) => playtime.TotalHours >= 1
        ? $"{(int)playtime.TotalHours} h {playtime.Minutes} min played"
        : playtime.TotalMinutes >= 1 ? $"{(int)playtime.TotalMinutes} min played" : "Not played yet";
}
