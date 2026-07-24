using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Library;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// Presentation wrapper around a <see cref="Game"/>. Availability is observable so the
/// startup check can flip a game to "unavailable" in place. It also owns the realized
/// cover bitmap, editable title state, and selection state used by both library views.
/// </summary>
public partial class GameViewModel : ObservableObject, IDisposable
{
    private static readonly IAsyncRelayCommand<GameViewModel?> NoGameCommand =
        new AsyncRelayCommand<GameViewModel?>(_ => Task.CompletedTask);
    private static readonly IAsyncRelayCommand NoCommand =
        new AsyncRelayCommand(() => Task.CompletedTask);

    /// <summary>Fixed cover width; height comes from the platform's canonical ratio.</summary>
    private const double CoverFrameWidth = 188;

    /// <summary>Default frame ratio (portrait disc case) when a caller omits one.</summary>
    private const double DefaultCoverAspectRatio = 0.708;

    /// <summary>Fixed list-row thumbnail height; the width follows the platform ratio so the
    /// list thumbnail keeps each platform's true cover shape (square for PS1, portrait for
    /// disc-case systems) instead of cropping every cover into one hardcoded portrait box.</summary>
    private const double ListCoverFrameHeight = 52;

    public Game Model { get; private set; }
    public long Id { get; }
    public string SystemId { get; }
    public string Path { get; }
    public string SystemName { get; }
    public string SystemShortName { get; }
    public string AccentColor { get; }
    public string FormatLabel { get; }
    public IImage? PlatformArtwork { get; }
    private double _coverWidth;
    private double _coverHeight;
    private double _shelfCoverHeight;

    /// <summary>Width of this tile's cover. The library recomputes it from the viewport width
    /// (see <see cref="ApplyCoverLayout"/>) so a whole number of columns fills the row.</summary>
    public double CoverWidth { get => _coverWidth; private set => SetProperty(ref _coverWidth, value); }

    /// <summary>Cover height for the current width, preserving the platform's aspect ratio.</summary>
    public double CoverHeight { get => _coverHeight; private set => SetProperty(ref _coverHeight, value); }

    public double ListCoverWidth { get; }
    public double ListCoverHeight { get; }
    /// <summary>Shared Gamepad artwork well height keeps mixed-system All Games rows stable.</summary>
    public double GamepadCoverFrameHeight { get; } = 280;

    /// <summary>Platform cover aspect ratio (width:height); the library uses it to choose the
    /// shelf height for a mixed view.</summary>
    public double CoverAspectRatio { get; }

    /// <summary>Height of the grid cover shelf this tile sits in: the tallest cover in the
    /// current view, so a mixed collection bottom-aligns covers to one baseline while a single
    /// short-cover platform (square PS1 art) still packs tightly.</summary>
    public double ShelfCoverHeight
    {
        get => _shelfCoverHeight;
        private set => SetProperty(ref _shelfCoverHeight, value);
    }

    /// <summary>Sets the cover width (recomputed from the current viewport) and the shared shelf
    /// height; the cover height follows from the platform aspect ratio.</summary>
    public void ApplyCoverLayout(double coverWidth, double shelfCoverHeight)
    {
        CoverWidth = coverWidth;
        CoverHeight = Math.Round(coverWidth / CoverAspectRatio);
        ShelfCoverHeight = shelfCoverHeight;
    }
    public IAsyncRelayCommand<GameViewModel?> LaunchCommand { get; }
    public IAsyncRelayCommand<GameViewModel?> SaveTitleCommand { get; }
    public IAsyncRelayCommand<GameViewModel?> SetCoverCommand { get; }
    public IAsyncRelayCommand<GameViewModel?> RemoveCommand { get; }
    public IAsyncRelayCommand<GameViewModel?> LoadCoverCommand { get; }
    public IAsyncRelayCommand<GameViewModel?> OpenAchievementsCommand { get; }
    public IAsyncRelayCommand RemoveSelectedCommand { get; }
    public string? CoverPath { get; private set; }
    public int? RetroAchievementsGameId { get; private set; }
    public bool IsCoverLoading { get; set; }
    public int CoverRevision { get; private set; }
    public bool IsExternalSourceGame => Model.ExternalSourceId is not null;
    public bool IsExternalSourceMissing =>
        IsExternalSourceGame && Model.IsPresentInExternalSource == false;
    public string AvailabilityText => IsAvailable
        ? "Available"
        : IsExternalSourceMissing ? "Source missing" : "Unavailable";
    public string UnavailableBadgeText => IsExternalSourceMissing ? "SOURCE MISSING" : "FILE MISSING";
    public string UnavailableTooltip => IsExternalSourceMissing
        ? "This game is no longer listed by its external emulator library. Sync that library again or remove this entry from EmuShelf."
        : IsExternalSourceGame
            ? "The path recorded by its external emulator library could not be found. Reconnect its drive, then sync that library."
        : "The saved game path could not be found. Reconnect its drive or re-add the file.";
    public string UnavailableLaunchStatus => IsExternalSourceMissing
        ? $"Cannot launch {Title}: it is no longer listed by its external emulator library."
        : IsExternalSourceGame
            ? $"Cannot launch {Title}: the path recorded by its external emulator library could not be found."
        : $"Cannot launch {Title}: its game file could not be found.";

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial bool IsAvailable { get; set; }

    [ObservableProperty]
    public partial Bitmap? CoverImage { get; set; }

    [ObservableProperty]
    public partial bool IsEditingTitle { get; set; }

    /// <summary>Controller focus is intentionally independent from desktop multi-selection.</summary>
    [ObservableProperty]
    public partial bool IsFocused { get; set; }

    [ObservableProperty]
    public partial string DraftTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Whether the grid tile shows the achievement/trophy mark (a confirmed RA match).</summary>
    [ObservableProperty]
    public partial bool ShowAchievementMark { get; set; }

    /// <summary>List-view Achievements column: an <c>awarded/total</c> fraction or an em dash.</summary>
    [ObservableProperty]
    public partial string AchievementsColumnText { get; set; } = RetroAchievementsDisplay.Dash;

    [ObservableProperty]
    public partial string AchievementsTooltip { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanOpenAchievementDetails { get; set; }

    public bool HasCoverImage => CoverImage is not null;

    /// <summary>Sort key for the Achievements column: -1 when the game has no set, 0 when a set
    /// exists but progress hasn't loaded, otherwise the number of unlocked achievements.</summary>
    public int AchievementSortKey { get; private set; } = -1;

    /// <summary>Applies a resolved achievement presentation from the display state machine.</summary>
    public void ApplyAchievementsDisplay(RetroAchievementsDisplay display)
    {
        ShowAchievementMark = display.ShowMark;
        AchievementsColumnText = display.ColumnText;
        AchievementsTooltip = display.Tooltip;
        AchievementSortKey = ComputeAchievementSortKey(display);
    }

    private static int ComputeAchievementSortKey(RetroAchievementsDisplay display)
    {
        if (!display.ShowMark)
            return -1;
        var slash = display.ColumnText.IndexOf('/');
        return slash > 0 && int.TryParse(display.ColumnText.AsSpan(0, slash), out var awarded)
            ? awarded
            : 0;
    }

    /// <summary>Only confirmed achievement-bearing catalogue links can open the detail popup.</summary>
    public void ApplyAchievementLink(int? retroAchievementsGameId)
    {
        RetroAchievementsGameId = retroAchievementsGameId;
        CanOpenAchievementDetails = retroAchievementsGameId is not null;
    }

    public GameViewModel(
        Game game,
        string systemName,
        string systemShortName,
        string accentColor,
        IAsyncRelayCommand<GameViewModel?>? launchCommand = null,
        IAsyncRelayCommand<GameViewModel?>? saveTitleCommand = null,
        IAsyncRelayCommand<GameViewModel?>? setCoverCommand = null,
        IAsyncRelayCommand<GameViewModel?>? removeCommand = null,
        IAsyncRelayCommand<GameViewModel?>? loadCoverCommand = null,
        IImage? platformArtwork = null,
        double coverAspectRatio = DefaultCoverAspectRatio,
        IAsyncRelayCommand<GameViewModel?>? openAchievementsCommand = null,
        IAsyncRelayCommand? removeSelectedCommand = null)
    {
        Model = game;
        Id = game.Id;
        SystemId = game.SystemId;
        Path = game.Path;
        Title = game.Title;
        IsAvailable = game.IsAvailable;
        CoverPath = game.CoverPath;
        SystemName = systemName;
        SystemShortName = systemShortName;
        AccentColor = accentColor;
        PlatformArtwork = platformArtwork ??
            EmuShelf.App.ViewModels.PlatformArtwork.ForSystem(game.SystemId);
        // One fixed frame per platform, shared by the real cover and the placeholder,
        // so a system's covers are uniform (see the grid tile in MainWindow.axaml).
        CoverAspectRatio = coverAspectRatio;
        // Default to the fixed frame width until the library recomputes it from the viewport.
        ApplyCoverLayout(CoverFrameWidth, Math.Round(CoverFrameWidth / coverAspectRatio));
        // List rows share one height so they align; the width follows the platform ratio so a
        // square PS1 cover stays square instead of being cropped into a portrait thumbnail.
        ListCoverHeight = ListCoverFrameHeight;
        ListCoverWidth = Math.Round(ListCoverFrameHeight * coverAspectRatio);
        FormatLabel = System.IO.Path.GetExtension(game.Path) is { Length: > 1 } extension
            ? extension[1..].ToUpperInvariant()
            : "FOLDER";
        DraftTitle = game.Title;
        LaunchCommand = launchCommand ?? NoGameCommand;
        SaveTitleCommand = saveTitleCommand ?? NoGameCommand;
        SetCoverCommand = setCoverCommand ?? NoGameCommand;
        RemoveCommand = removeCommand ?? NoGameCommand;
        LoadCoverCommand = loadCoverCommand ?? NoGameCommand;
        OpenAchievementsCommand = openAchievementsCommand ?? NoGameCommand;
        RemoveSelectedCommand = removeSelectedCommand ?? NoCommand;
    }

    partial void OnTitleChanged(string value)
    {
        Model = Model with { Title = value };
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(UnavailableLaunchStatus));
    }

    partial void OnCoverImageChanging(Bitmap? value)
    {
        if (!ReferenceEquals(CoverImage, value))
            CoverImage?.Dispose();
    }

    partial void OnCoverImageChanged(Bitmap? value) =>
        OnPropertyChanged(nameof(HasCoverImage));

    [RelayCommand]
    private void BeginEditTitle()
    {
        DraftTitle = Title;
        IsEditingTitle = true;
    }

    [RelayCommand]
    private void CancelEditTitle()
    {
        DraftTitle = Title;
        IsEditingTitle = false;
    }

    public void CompleteTitleEdit(string title)
    {
        Title = title;
        DraftTitle = title;
        IsEditingTitle = false;
    }

    public void ApplyCover(string coverPath, Bitmap image)
    {
        ApplyCoverPath(coverPath);
        CoverImage = image;
    }

    public void ApplyCoverPath(string coverPath)
    {
        CoverRevision++;
        CoverPath = coverPath;
        Model = Model with { CoverPath = coverPath };
        CoverImage = null;
    }

    public void Dispose() => CoverImage = null;

    /// <summary>Up-to-two-letter monogram for the placeholder cover.</summary>
    public string Initials
    {
        get
        {
            var trimmed = Title.Trim();
            if (trimmed.Length == 0)
                return "?";

            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return string.Concat(char.ToUpperInvariant(parts[0][0]), char.ToUpperInvariant(parts[1][0]));
            return trimmed[..Math.Min(2, trimmed.Length)].ToUpperInvariant();
        }
    }
}
