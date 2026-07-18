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

    /// <summary>Fixed cover width; height comes from the platform's canonical ratio.</summary>
    private const double CoverFrameWidth = 188;

    /// <summary>Default frame ratio (portrait disc case) when a caller omits one.</summary>
    private const double DefaultCoverAspectRatio = 0.708;

    public Game Model { get; private set; }
    public long Id { get; }
    public string SystemId { get; }
    public string Path { get; }
    public string SystemName { get; }
    public string SystemShortName { get; }
    public string AccentColor { get; }
    public string FormatLabel { get; }
    public IImage? PlatformArtwork { get; }
    public double CoverWidth { get; }
    public double CoverHeight { get; }
    public IAsyncRelayCommand<GameViewModel?> LaunchCommand { get; }
    public IAsyncRelayCommand<GameViewModel?> SaveTitleCommand { get; }
    public IAsyncRelayCommand<GameViewModel?> SetCoverCommand { get; }
    public IAsyncRelayCommand<GameViewModel?> RemoveCommand { get; }
    public IAsyncRelayCommand<GameViewModel?> LoadCoverCommand { get; }
    public IAsyncRelayCommand<GameViewModel?> OpenAchievementsCommand { get; }
    public string? CoverPath { get; private set; }
    public int? RetroAchievementsGameId { get; private set; }
    public bool IsCoverLoading { get; set; }
    public int CoverRevision { get; private set; }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial bool IsAvailable { get; set; }

    [ObservableProperty]
    public partial Bitmap? CoverImage { get; set; }

    [ObservableProperty]
    public partial bool IsEditingTitle { get; set; }

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

    /// <summary>Applies a resolved achievement presentation from the display state machine.</summary>
    public void ApplyAchievementsDisplay(RetroAchievementsDisplay display)
    {
        ShowAchievementMark = display.ShowMark;
        AchievementsColumnText = display.ColumnText;
        AchievementsTooltip = display.Tooltip;
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
        IAsyncRelayCommand<GameViewModel?>? openAchievementsCommand = null)
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
        CoverWidth = CoverFrameWidth;
        CoverHeight = Math.Round(CoverFrameWidth / coverAspectRatio);
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
    }

    partial void OnTitleChanged(string value)
    {
        Model = Model with { Title = value };
        OnPropertyChanged(nameof(Initials));
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
