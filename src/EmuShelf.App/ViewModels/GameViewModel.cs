using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.App.Rendering;
using EmuShelf.Rendering.Shells;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.TexturePacks;

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
    private readonly IReadOnlyList<GameDisc> _discs;

    /// <summary>Fixed cover width; height comes from the platform's canonical ratio.</summary>
    private const double CoverFrameWidth = 188;

    /// <summary>Default frame ratio when a caller omits one.</summary>
    private const double DefaultCoverAspectRatio = 0.708;

    /// <summary>Cover ratio the gamepad grid falls back to ONLY when a view mixes platforms (All
    /// Games and the like). A single-platform view keeps that platform's true cover shape, so its
    /// covers fill the frame with no letterbox bars; only a mixed view — which would otherwise be a
    /// ragged skyline of covers at five different heights — is unified into this one frame, its
    /// covers cropped to fill. 0.708 is the disc-system ratio the library is mostly made of. The
    /// library decides per view whether a tile gets its true height or this one (see
    /// <c>MainViewModel.GamepadCoverHeightFor</c>). See DECISIONS 2026-08-04.</summary>
    internal const double GamepadMixedCoverAspectRatio = 0.708;

    /// <summary>Fixed list-row thumbnail height; the width follows the platform ratio so the
    /// list thumbnail keeps each platform's true cover shape (square for PS1, portrait for
    /// disc-case systems) instead of cropping every cover into one hardcoded portrait box.</summary>
    private const double ListCoverFrameHeight = 52;

    public Game Model { get; private set; }
    /// <summary>The concrete source passed to the emulator when this library card is launched.</summary>
    public Game LaunchModel { get; private set; }
    public IReadOnlyList<GameDisc> Discs => _discs;
    public IReadOnlyList<GameDiscOptionViewModel> DiscOptions { get; }
    public bool IsMultiDisc => _discs.Count > 1;
    public int DiscCount => _discs.Count;
    public int SelectedDiscNumber { get; private set; }
    public string? DiscSelectionKey { get; }
    public string DiscCountText => $"{DiscCount} discs";
    public string SelectedDiscText => $"Disc {SelectedDiscNumber} selected";
    public string DiscBadgeText => $"Disc {SelectedDiscNumber} of {DiscCount}";
    public long Id { get; }
    public string SystemId { get; }
    public string Path { get; }
    public string SystemName { get; }
    public string SystemShortName { get; }
    public string AccentColor { get; }
    public string FormatLabel { get; private set; }

    /// <summary>List-view "Last Played" column: a short local date, or "Never" if the game has not
    /// been launched. Sorted by <see cref="LastPlayedSortKey"/>. From <see cref="Game.LastPlayedAt"/>
    /// (M38).</summary>
    public string LastPlayedText => Model.LastPlayedAt is { } played
        ? played.LocalDateTime.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)
        : "Never";

    /// <summary>Sort key for Last Played; a never-played game sorts oldest.</summary>
    public DateTimeOffset LastPlayedSortKey => Model.LastPlayedAt ?? DateTimeOffset.MinValue;

    /// <summary>List-view "Play Time" column: the accumulated play time as a compact "Nh Nm" string,
    /// or "—" when nothing has been recorded (never played, or every session ended in an app kill).
    /// Sorted by <see cref="PlaytimeSortKey"/>. From <see cref="Game.Playtime"/> (M43).</summary>
    public string PlaytimeText => FormatPlaytime(Model.Playtime);

    /// <summary>Sort key for Play Time; a never-played game (zero) sorts lowest.</summary>
    public TimeSpan PlaytimeSortKey => Model.Playtime;

    /// <summary>List-view "Plays" column: how many times the game has been launched, or "—" when it
    /// has never been played. From <see cref="Game.PlayCount"/> (M43).</summary>
    public string PlayCountText => Model.PlayCount > 0
        ? Model.PlayCount.ToString(CultureInfo.CurrentCulture)
        : RetroAchievementsDisplay.Dash;

    /// <summary>Sort key for Plays.</summary>
    public int PlayCountSortKey => Model.PlayCount;

    /// <summary>Gamepad spotlight caption summarising play activity, e.g. "12h 34m • 5 plays", "3
    /// plays" (when a session was only ever killed mid-play), or empty when the game is unplayed so
    /// the hero hides the line. See <see cref="HasGamepadPlaytime"/>.</summary>
    public string GamepadPlaytimeSummary
    {
        get
        {
            if (Model.PlayCount <= 0)
                return string.Empty;
            var plays = Model.PlayCount == 1 ? "1 play" : $"{Model.PlayCount} plays";
            return Model.Playtime > TimeSpan.Zero ? $"{FormatPlaytime(Model.Playtime)} • {plays}" : plays;
        }
    }

    /// <summary>Whether the game has been launched at least once, so the spotlight shows its play
    /// summary line.</summary>
    public bool HasGamepadPlaytime => Model.PlayCount > 0;

    // Compact play-time label shared by the list column and the gamepad summary: "—" for none,
    // "< 1m" for a sub-minute session, then "Nh Nm" / "Nh" / "Nm" trimming a zero component.
    private static string FormatPlaytime(TimeSpan playtime)
    {
        var totalSeconds = (long)playtime.TotalSeconds;
        if (totalSeconds <= 0)
            return RetroAchievementsDisplay.Dash;

        var totalMinutes = totalSeconds / 60;
        if (totalMinutes < 1)
            return "< 1m";

        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        if (hours <= 0)
            return $"{minutes}m";
        return minutes == 0 ? $"{hours}h" : $"{hours}h {minutes}m";
    }

    /// <summary>List-view "Date Added" column: a short local date. From <see cref="Game.DateAdded"/>.</summary>
    public string DateAddedText => Model.DateAdded.LocalDateTime.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);

    public DateTimeOffset DateAddedSortKey => Model.DateAdded;

    // ---- M40 Phase 4: scraped-metadata columns, fed by the bulk GameDetailsProjection ------------

    private GameDetailsProjection? _detailsProjection;

    /// <summary>Applies this game's bulk metadata projection (or null when it has no stored details),
    /// refreshing every scraped-column cell. Called once per scope build on the load worker, never a
    /// per-row read on the UI thread. See DECISIONS 2026-08-08.</summary>
    public void ApplyDetailsProjection(GameDetailsProjection? projection)
    {
        _detailsProjection = projection;
        OnPropertyChanged(nameof(MetadataCompletenessText));
        OnPropertyChanged(nameof(MetadataCompletenessTooltip));
        OnPropertyChanged(nameof(HasScrapedCover));
        OnPropertyChanged(nameof(HasScrapedScreenshot));
        OnPropertyChanged(nameof(HasScrapedFanart));
        OnPropertyChanged(nameof(HasScrapedLogo));
        OnPropertyChanged(nameof(HasScrapedDescription));
        OnPropertyChanged(nameof(HasScrapedTitleScreen));
        OnPropertyChanged(nameof(HasScrapedBoxBack));
        OnPropertyChanged(nameof(HasScrapedBoxSpine));
        OnPropertyChanged(nameof(HasScrapedPhysicalMedia));
        OnPropertyChanged(nameof(HasScrapedPhysicalMediaTexture));
        OnPropertyChanged(nameof(RatingColumnText));
        OnPropertyChanged(nameof(GenreColumnText));
        OnPropertyChanged(nameof(YearColumnText));
        OnPropertyChanged(nameof(PlayersColumnText));
        OnPropertyChanged(nameof(DeveloperColumnText));
        OnPropertyChanged(nameof(PublisherColumnText));
    }

    /// <summary>"Has a cover" means the game shows one at all (a manual cover counts), not only a
    /// scraped box front — it is the most intuitive reading of the column and of completeness.</summary>
    public bool HasScrapedCover => CoverPath is not null;
    public bool HasScrapedScreenshot => _detailsProjection?.HasScreenshot ?? false;
    public bool HasScrapedFanart => _detailsProjection?.HasFanart ?? false;
    public bool HasScrapedLogo => _detailsProjection?.HasWheel ?? false;
    public bool HasScrapedDescription => _detailsProjection?.HasDescription ?? false;
    public bool HasScrapedTitleScreen => _detailsProjection?.HasTitleScreen ?? false;
    public bool HasScrapedBoxBack => _detailsProjection?.HasBoxBack ?? false;
    public bool HasScrapedBoxSpine => _detailsProjection?.HasBoxSpine ?? false;
    public bool HasScrapedPhysicalMedia => _detailsProjection?.HasPhysicalMedia ?? false;
    public bool HasScrapedPhysicalMediaTexture => _detailsProjection?.HasPhysicalMediaTexture ?? false;

    /// <summary>
    /// Absolute path to the selected ScreenScraper physical-support texture. For SNES this is the
    /// flattened label artwork; it is decoded only while the game is near the visible 3D shelf.
    /// </summary>
    public string? PhysicalMediaTexturePath { get; private set; }

    public void ApplyPhysicalMediaTexturePath(string? path)
    {
        var normalized = string.IsNullOrWhiteSpace(path) ? null : path;
        if (string.Equals(PhysicalMediaTexturePath, normalized, StringComparison.Ordinal))
            return;

        PhysicalMediaTexturePath = normalized;
        OnPropertyChanged(nameof(PhysicalMediaTexturePath));
    }

    /// <summary>Absolute path to the selected ScreenScraper box back, for a keep case's rear face.</summary>
    public string? BoxBackPath { get; private set; }

    /// <summary>Absolute path to the selected ScreenScraper box spine.</summary>
    public string? BoxSpinePath { get; private set; }

    public void ApplyBoxBackPath(string? path)
    {
        var normalized = string.IsNullOrWhiteSpace(path) ? null : path;
        if (string.Equals(BoxBackPath, normalized, StringComparison.Ordinal))
            return;

        BoxBackPath = normalized;
        OnPropertyChanged(nameof(BoxBackPath));
    }

    public void ApplyBoxSpinePath(string? path)
    {
        var normalized = string.IsNullOrWhiteSpace(path) ? null : path;
        if (string.Equals(BoxSpinePath, normalized, StringComparison.Ordinal))
            return;

        BoxSpinePath = normalized;
        OnPropertyChanged(nameof(BoxSpinePath));
    }

    /// <summary>
    /// The file this medium's given face is painted from, or null when that face has no scraped
    /// art and should fall back to the platform tint.
    /// </summary>
    /// <remarks>
    /// Only faces that come off disk appear here. A keep case's front is deliberately absent: it
    /// reuses <see cref="CoverImage"/>, which the library has already decoded and cached for the
    /// grid, rather than decoding the same file a second time for the shelf.
    /// </remarks>
    public string? ShelfArtworkPath(ShelfArtworkFace face)
    {
        var slots = ShelfMediaProfile.ArtworkSlots;
        return face switch
        {
            ShelfArtworkFace.Front when (slots & PhysicalArtworkSlots.CartridgeSupport) != 0 =>
                PhysicalMediaTexturePath,
            // The same scraped support texture as a cartridge's label, on the medium it belongs to:
            // for a disc system ScreenScraper's support art is a picture of the disc, not the box.
            ShelfArtworkFace.DiscLabel when (slots & PhysicalArtworkSlots.DiscLabel) != 0 =>
                PhysicalMediaTexturePath,
            ShelfArtworkFace.Back when (slots & PhysicalArtworkSlots.Back) != 0 => BoxBackPath,
            ShelfArtworkFace.Spine when (slots & PhysicalArtworkSlots.Spine) != 0 => BoxSpinePath,
            _ => null,
        };
    }

    // Completeness tracks the artwork/text ScreenScraper reliably offers, so a fully scraped game can
    // actually reach the top of the scale. The sparse secondary kinds (box back/spine, cartridge/disc
    // and its texture) are near-absent for most games — especially arcade — so they are presence-only
    // columns, deliberately excluded here rather than leaving a well-scraped game reading as forever
    // incomplete.
    private int CompletenessCount =>
        (HasScrapedCover ? 1 : 0) + (HasScrapedScreenshot ? 1 : 0) + (HasScrapedFanart ? 1 : 0) +
        (HasScrapedLogo ? 1 : 0) + (HasScrapedDescription ? 1 : 0) + (HasScrapedTitleScreen ? 1 : 0);

    /// <summary>A game reads as never-scraped only when it has no stored details at all — the
    /// projection is null, i.e. it is absent from the media, metadata, and provider-match tables —
    /// and has no cover. A game that matched a provider but returned nothing still has a (mostly
    /// empty) projection, so it correctly shows <c>0/5</c> ("scraped but empty") rather than the
    /// never-scraped dash; this is why the projection carries <c>HasProviderMatch</c>.</summary>
    private bool IsUnscraped => _detailsProjection is null && CoverPath is null;

    /// <summary>Metadata completeness column: <c>n/6</c> (cover, screenshot, fan art, logo,
    /// description, title screen) or an em dash when the game has never been scraped.</summary>
    public string MetadataCompletenessText =>
        IsUnscraped ? RetroAchievementsDisplay.Dash : $"{CompletenessCount}/6";

    /// <summary>Sort key for completeness: -1 never scraped, otherwise the present-asset count, so a
    /// least-complete-first sort surfaces the games that still need work.</summary>
    public int MetadataCompletenessSortKey => IsUnscraped ? -1 : CompletenessCount;

    public string MetadataCompletenessTooltip => IsUnscraped
        ? "Not scraped yet."
        : $"Cover: {YesNo(HasScrapedCover)}\nScreenshot: {YesNo(HasScrapedScreenshot)}\n" +
          $"Fan art: {YesNo(HasScrapedFanart)}\nLogo: {YesNo(HasScrapedLogo)}\n" +
          $"Description: {YesNo(HasScrapedDescription)}\nTitle screen: {YesNo(HasScrapedTitleScreen)}";

    public string RatingColumnText => FormatRating(_detailsProjection?.Rating);

    /// <summary>Numeric rating (0–10) for sorting; -1 when unrated, so unrated games group with the
    /// other unscored rows — first on an ascending sort, last on descending — matching how the
    /// completeness column treats never-scraped games.</summary>
    public double RatingSortKey =>
        double.TryParse(FormatRating(_detailsProjection?.Rating), NumberStyles.Number,
            CultureInfo.InvariantCulture, out var score)
            ? score
            : -1;

    public string GenreColumnText => Dash(_detailsProjection?.Genre);
    public string YearColumnText => Dash(ExtractYear(_detailsProjection?.ReleaseDate));
    public int YearSortKey =>
        int.TryParse(ExtractYear(_detailsProjection?.ReleaseDate), out var year) ? year : 0;
    public string PlayersColumnText => Dash(_detailsProjection?.Players);
    public string DeveloperColumnText => Dash(_detailsProjection?.Developer);
    public string PublisherColumnText => Dash(_detailsProjection?.Publisher);

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string Dash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? RetroAchievementsDisplay.Dash : value.Trim();

    // ScreenScraper stores a 0–20 rating; present it as the 0–10 score the spotlight also shows. A
    // missing field or a stored 0 means unrated (ScreenScraper writes 0 for "no rating"), shown as a
    // dash so it is not mistaken for a genuine zero score.
    private static string FormatRating(string? raw)
    {
        if (raw is null ||
            !double.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var provider) ||
            provider <= 0)
            return RetroAchievementsDisplay.Dash;
        return Math.Clamp(provider / 2.0, 0, 10).ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static string? ExtractYear(string? releaseDate)
    {
        if (string.IsNullOrWhiteSpace(releaseDate))
            return null;
        foreach (var token in releaseDate.Split('-', '/', '.', ' '))
            if (token.Length == 4 && int.TryParse(token, out _))
                return token;
        return null;
    }

    public IImage? PlatformArtwork { get; }
    private double _coverWidth;
    private double _coverHeight;
    private double _shelfCoverHeight;
    private double _gamepadCoverHeight;

    /// <summary>Width of this tile's cover. The library recomputes it from the viewport width
    /// (see <see cref="ApplyCoverLayout"/>) so a whole number of columns fills the row.</summary>
    public double CoverWidth { get => _coverWidth; private set => SetProperty(ref _coverWidth, value); }

    /// <summary>Cover height for the current width, preserving the platform's aspect ratio.</summary>
    public double CoverHeight { get => _coverHeight; private set => SetProperty(ref _coverHeight, value); }

    public double ListCoverWidth { get; private set; }
    public double ListCoverHeight { get; }

    /// <summary>Displayed cover aspect ratio (width:height). This is the platform's canonical
    /// frame for the whole session: every cover of a system is drawn into one frame and filled
    /// (UniformToFill), so a system's tiles are uniform and one off-ratio scan can never balloon
    /// the shared shelf. See DECISIONS 2026-07-17 and 2026-08-02.</summary>
    public double CoverAspectRatio { get; }

    /// <summary>Height of the grid cover shelf this tile sits in: the tallest cover in the
    /// current view, so a mixed collection bottom-aligns covers to one baseline while a single
    /// short-cover platform (square PS1 art) still packs tightly.</summary>
    public double ShelfCoverHeight
    {
        get => _shelfCoverHeight;
        private set => SetProperty(ref _shelfCoverHeight, value);
    }

    /// <summary>Height of this tile's gamepad cover frame. In a single-platform view it equals
    /// <see cref="CoverHeight"/> (the platform's true shape, no bars); in a mixed view the library
    /// passes one uniform height for every tile so the grid is even. See
    /// <see cref="GamepadMixedCoverAspectRatio"/>.</summary>
    public double GamepadCoverHeight
    {
        get => _gamepadCoverHeight;
        private set => SetProperty(ref _gamepadCoverHeight, value);
    }

    /// <summary>Legacy flat-fallback cover size. The GPU scene uses
    /// <see cref="ShelfMediaProfile"/>'s physical dimensions instead.</summary>
    public double GamepadShelfCoverHeight => GamepadShelfCoverTargetHeight;
    public double GamepadShelfCoverWidth => Math.Round(GamepadShelfCoverTargetHeight * CoverAspectRatio);

    /// <summary>The hero cover's height on the couch physical-media shelf, in device-independent px.</summary>
    internal const double GamepadShelfCoverTargetHeight = 300;

    /// <summary>The authored physical medium for the legacy one-hero/fallback contract, or null.</summary>
    public MediaShell? ShelfMediaShell => MediaShellMap.ForSystem(SystemId);

    /// <summary>
    /// Real-world dimensions and scene presentation for this game. Unlike
    /// <see cref="ShelfMediaShell"/>, this is never null: an unauthored system receives a thin
    /// cover card so it can travel through the same continuous 3D shelf as authored media.
    /// </summary>
    public PhysicalMediaProfile ShelfMediaProfile =>
        MediaShellMap.ProfileForSystem(SystemId, CoverAspectRatio);

    /// <summary>Per-system accent as a colour, tinting the 3D scene and unprinted faces.</summary>
    public Color ShelfAccent => Color.Parse(AccentColor);

    /// <summary>
    /// False once the GPU hero has been ruled out for this session, which sends every game back to
    /// its flat cover.
    /// </summary>
    /// <remarks>
    /// Set by the library when <c>MediaShelf3DControl</c> reports that it could not bring up a GL
    /// context — a headless session, a driver that will not give us GLES 3.0, a remote desktop. The
    /// couch shelf has to stay usable there, so this is a normal outcome rather than an error.
    /// </remarks>
    public bool ShelfHeroSupported
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShelfUses3DHero));
        }
    } = true;

    /// <summary>True when the shelf should swap this game's flat cover for the rotatable 3D medium:
    /// it is focused, its system has an authored shell, and the GPU path came up. Cover art is
    /// optional; without it the renderer shows the shell's unlabelled/tinted panel.</summary>
    public bool ShelfUses3DHero =>
        IsFocused && ShelfHeroSupported && ShelfMediaShell is not null;

    /// <summary>Sets the cover width (recomputed from the current viewport) and the shared desktop
    /// shelf height; the desktop cover height follows the platform aspect ratio. The gamepad frame
    /// height is decided per view by the library — null means "use this platform's true height",
    /// which a single-platform view always does; a mixed view passes one uniform height.</summary>
    public void ApplyCoverLayout(double coverWidth, double shelfCoverHeight, double? gamepadCoverHeight = null)
    {
        CoverWidth = coverWidth;
        CoverHeight = Math.Round(coverWidth / CoverAspectRatio);
        ShelfCoverHeight = shelfCoverHeight;
        GamepadCoverHeight = gamepadCoverHeight ?? CoverHeight;
    }
    public IAsyncRelayCommand<GameViewModel?> LaunchCommand { get; }
    public IAsyncRelayCommand<GameViewModel?> SaveTitleCommand { get; }
    public IAsyncRelayCommand<GameViewModel?> SetCoverCommand { get; }
    public IAsyncRelayCommand<GameViewModel?> LoadCoverCommand { get; }
    public IAsyncRelayCommand<GameViewModel?> OpenAchievementsCommand { get; }
    public IAsyncRelayCommand<GameViewModel?> ScrapeCommand { get; }
    public IAsyncRelayCommand<GameViewModel?> ShowInFolderCommand { get; }
    public IAsyncRelayCommand<GameViewModel?> OpenTextureFolderCommand { get; }

    /// <summary>Platform-native label for the "reveal this game's file in the OS file manager"
    /// context-menu action, so it reads correctly on each desktop.</summary>
    public static string ShowInFolderLabel { get; } =
        OperatingSystem.IsMacOS() ? "Reveal in Finder"
        : OperatingSystem.IsWindows() ? "Show in File Explorer"
        : "Show in file manager";
    public IAsyncRelayCommand RemoveSelectedCommand { get; }
    public IAsyncRelayCommand ScrapeSelectedCommand { get; }

    /// <summary>Whether the "Open texture folder" context action applies — true only for the
    /// systems whose emulator supports replacement-texture packs. The library sets it from
    /// <c>TexturePackProviderRegistry</c> so the view model never names an emulator.</summary>
    public bool CanOpenTextureFolder { get; }
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
        ? $"Cannot launch {DisplayTitle}: it is no longer listed by its external emulator library."
        : IsExternalSourceGame
            ? $"Cannot launch {DisplayTitle}: the path recorded by its external emulator library could not be found."
        : $"Cannot launch {DisplayTitle}: its game file could not be found.";

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial bool IsAvailable { get; set; }

    partial void OnIsAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(AvailabilityText));
        OnPropertyChanged(nameof(UnavailableBadgeText));
        OnPropertyChanged(nameof(UnavailableTooltip));
        OnPropertyChanged(nameof(UnavailableLaunchStatus));
    }

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

    /// <summary>Context-menu copy supplied by the library's shared selection model.</summary>
    [ObservableProperty]
    public partial string SelectionRemovalText { get; set; } = "Remove from library…";

    [ObservableProperty]
    public partial string SelectionScrapeText { get; set; } = "Scrape selected with ScreenScraper…";

    /// <summary>Shown only when more than one game is selected; single games use the per-game scrape.</summary>
    [ObservableProperty]
    public partial bool CanScrapeSelection { get; set; }

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

    /// <summary>The large fan-art image shown behind the gamepad spotlight hero. Only the focused
    /// game keeps a decoded bitmap; it is released when focus moves so memory stays bounded.</summary>
    [ObservableProperty]
    public partial Bitmap? FanartImage { get; set; }

    public bool HasFanartImage => FanartImage is not null;

    /// <summary>Absolute path to the selected fan-art asset, or null when the game has none. Set the
    /// first time the spotlight view resolves this game's scraped details.</summary>
    public string? FanartPath { get; private set; }

    /// <summary>The game's logo (ScreenScraper "wheel") shown large in the spotlight hero above the
    /// title. Only the focused game keeps a decoded bitmap; released as focus moves.</summary>
    [ObservableProperty]
    public partial Bitmap? WheelImage { get; set; }

    public bool HasWheelImage => WheelImage is not null;

    /// <summary>Absolute path to the selected logo asset, or null when the game has none.</summary>
    public string? WheelPath { get; private set; }

    /// <summary>Star score (0–10) shown in the spotlight hero, already formatted, or null when the
    /// game is unrated. Derived from the scraped provider rating in the library view model.</summary>
    [ObservableProperty]
    public partial string? RatingText { get; set; }

    public bool HasRating => RatingText is not null;

    private IReadOnlyList<string> _spotlightFacts = [];

    /// <summary>The spotlight hero's metadata chips: scraped genre, year, players, developer, and
    /// publisher — only the fields that were present, one entry per chip. Empty until the details
    /// resolve / when the game is unscraped. The launch source is shown separately as a caption (see
    /// <see cref="GamepadSubtitle"/>).</summary>
    public IReadOnlyList<string> SpotlightFacts => _spotlightFacts;

    /// <summary>The first row of hero chips: genre, year, players. Kept on their own line so the
    /// identity facts stay together, and each row centres independently (a single WrapPanel would
    /// left-pack the wrapped remainder off-centre).</summary>
    public IReadOnlyList<string> SpotlightFactsPrimary =>
        _spotlightFacts.Count > SpotlightPrimaryFactCount
            ? _spotlightFacts.Take(SpotlightPrimaryFactCount).ToArray()
            : _spotlightFacts;

    /// <summary>The second row of hero chips: developer, publisher — everything past the first three.
    /// Empty (and hidden) when there is nothing to spill onto a second line.</summary>
    public IReadOnlyList<string> SpotlightFactsSecondary =>
        _spotlightFacts.Count > SpotlightPrimaryFactCount
            ? _spotlightFacts.Skip(SpotlightPrimaryFactCount).ToArray()
            : [];

    public bool HasSpotlightSecondaryFacts => _spotlightFacts.Count > SpotlightPrimaryFactCount;

    private const int SpotlightPrimaryFactCount = 3;

    private string? _scrapedTitle;

    /// <summary>The title shown across every library view — grid, list, and the gamepad spotlight.
    /// It prefers the normalized title scraped from a metadata provider (ScreenScraper) when one is
    /// available, so a file named "Prince of Persia - The Sands of Time (USA) (En,Fr,Es)" reads as
    /// "Prince of Persia: The Sands of Time". A deliberate user rename always wins (its origin is
    /// <see cref="GameTitleOrigin.User"/>); a game with no scraped title keeps its own
    /// <see cref="Title"/>, which still tracks an in-place rename. The underlying record's
    /// <see cref="Title"/> is left untouched either way. See DECISIONS 2026-08-09.</summary>
    public string DisplayTitle =>
        _scrapedTitle is not null && Model.TitleOrigin != GameTitleOrigin.User ? _scrapedTitle : Title;

    /// <summary>Whether the spotlight hero shows the title in the logo's place. True only once the
    /// details have resolved and confirmed the game has no logo art, so a game that does have one
    /// never flashes its title in the gap before the logo bitmap decodes.</summary>
    public bool ShowSpotlightTitleFallback => AreSpotlightDetailsLoaded && WheelPath is null;

    /// <summary>Applies the normalized provider title used for display; a null/blank value clears it
    /// so the game falls back to its own <see cref="Title"/>.</summary>
    public void ApplyScrapedTitle(string? scrapedTitle)
    {
        _scrapedTitle = string.IsNullOrWhiteSpace(scrapedTitle) ? null : scrapedTitle;
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(UnavailableLaunchStatus));
    }

    /// <summary>Guards the one-time-per-game details resolve (fan-art/logo paths + rating) the
    /// spotlight hero needs, so scrolling the list re-loads a game's details at most once.</summary>
    public bool AreSpotlightDetailsLoaded { get; set; }

    /// <summary>Sort key for the Achievements column: -1 when the game has no set, 0 when a set
    /// exists but progress hasn't loaded, otherwise the number of unlocked achievements.</summary>
    public int AchievementSortKey { get; private set; } = -1;

    /// <summary>Couch-distance achievement copy for the focused-game dock.</summary>
    public string GamepadAchievementCountText =>
        TryGetAchievementProgress(out var awarded, out var total)
            ? $"{awarded}/{total}"
            : "—/—";

    public double GamepadAchievementProgressRatio =>
        TryGetAchievementProgress(out var awarded, out var total)
            ? Math.Clamp(awarded / (double)total, 0, 1)
            : 0;

    /// <summary>The actual source that A will launch, kept compact for the focused-game dock.</summary>
    public string GamepadSubtitle
    {
        get
        {
            var trimmedPath = LaunchModel.Path.TrimEnd(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar);
            var fileName = System.IO.Path.GetFileName(trimmedPath);
            var source = string.IsNullOrWhiteSpace(fileName) ? LaunchModel.Title : fileName;
            return source;
        }
    }

    /// <summary>Applies a resolved achievement presentation from the display state machine.</summary>
    public void ApplyAchievementsDisplay(RetroAchievementsDisplay display)
    {
        ShowAchievementMark = display.ShowMark;
        AchievementsColumnText = display.ColumnText;
        AchievementsTooltip = display.Tooltip;
        AchievementSortKey = ComputeAchievementSortKey(display);
    }

    partial void OnAchievementsColumnTextChanged(string value)
    {
        OnPropertyChanged(nameof(GamepadAchievementCountText));
        OnPropertyChanged(nameof(GamepadAchievementProgressRatio));
    }

    private bool TryGetAchievementProgress(out int awarded, out int total)
    {
        awarded = 0;
        total = 0;
        var slash = AchievementsColumnText.IndexOf('/');
        return slash > 0 &&
               int.TryParse(AchievementsColumnText.AsSpan(0, slash), out awarded) &&
               int.TryParse(AchievementsColumnText.AsSpan(slash + 1), out total) &&
               awarded >= 0 && total > 0;
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

    /// <summary>Whether the cover shows the texture-pack mark (a confirmed, usable installed pack).</summary>
    [ObservableProperty]
    public partial bool ShowTextureMark { get; set; }

    /// <summary>List-view Textures column: <c>Installed</c>, a pack count, or an em dash.</summary>
    [ObservableProperty]
    public partial string TexturesColumnText { get; set; } = TexturePackDisplay.Dash;

    [ObservableProperty]
    public partial string TexturesTooltip { get; set; } = TexturePackDisplay.NotScanned.Tooltip;

    /// <summary>Sort key for the Textures column: -1 unknown, 0 no pack, otherwise the pack count.</summary>
    public int TextureSortKey { get; private set; } = -1;

    /// <summary>Applies a resolved texture-pack presentation from the display state machine.</summary>
    public void ApplyTexturePackDisplay(TexturePackDisplay display)
    {
        ShowTextureMark = display.ShowMark;
        TexturesColumnText = display.ColumnText;
        TexturesTooltip = display.Tooltip;
        TextureSortKey = display.SortKey;
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
        IAsyncRelayCommand<GameViewModel?>? loadCoverCommand = null,
        IImage? platformArtwork = null,
        double coverAspectRatio = DefaultCoverAspectRatio,
        IAsyncRelayCommand<GameViewModel?>? openAchievementsCommand = null,
        IAsyncRelayCommand? removeSelectedCommand = null,
        IAsyncRelayCommand? scrapeSelectedCommand = null,
        IReadOnlyList<GameDisc>? discs = null,
        GameDisc? selectedDisc = null,
        string? displayTitle = null,
        string? discSelectionKey = null,
        Func<GameViewModel, GameDisc, Task>? launchDiscAction = null,
        IAsyncRelayCommand<GameViewModel?>? scrapeCommand = null,
        IAsyncRelayCommand<GameViewModel?>? showInFolderCommand = null,
        IAsyncRelayCommand<GameViewModel?>? openTextureFolderCommand = null,
        bool canOpenTextureFolder = false)
    {
        Model = game;
        _discs = discs ?? [new GameDisc(1, game)];
        var resolvedSelectedDisc = selectedDisc ?? _discs[0];
        LaunchModel = resolvedSelectedDisc.Game;
        SelectedDiscNumber = resolvedSelectedDisc.Number;
        DiscSelectionKey = discSelectionKey;
        DiscOptions = _discs
            .Select(disc => new GameDiscOptionViewModel(
                disc,
                selectedDisc => launchDiscAction?.Invoke(this, selectedDisc) ?? Task.CompletedTask,
                disc.Game.Id == LaunchModel.Id))
            .ToArray();
        Id = game.Id;
        SystemId = game.SystemId;
        Path = game.Path;
        Title = displayTitle ?? game.Title;
        IsAvailable = LaunchModel.IsAvailable;
        CoverPath = game.CoverPath;
        SystemName = systemName;
        SystemShortName = systemShortName;
        AccentColor = accentColor;
        PlatformArtwork = platformArtwork ??
            EmuShelf.App.ViewModels.PlatformArtwork.ForSystem(game.SystemId);
        // One canonical frame per platform, shared by the real cover and the placeholder, so a
        // system's covers are uniform. The real cover fills it (UniformToFill), which crops only the
        // ~2px of outer bleed on an off-ratio scan and never lets a single tall scan balloon the
        // shared shelf so every other cover renders half-height. See DECISIONS 2026-07-17/2026-08-02.
        CoverAspectRatio = coverAspectRatio;
        // Default to the fixed frame width until the library recomputes it from the viewport.
        ApplyCoverLayout(CoverFrameWidth, Math.Round(CoverFrameWidth / coverAspectRatio));
        // List rows share one height; their width follows the platform's canonical cover shape so a
        // square PS1 cover stays square instead of being cropped into a portrait thumbnail.
        ListCoverHeight = ListCoverFrameHeight;
        ListCoverWidth = Math.Round(ListCoverFrameHeight * coverAspectRatio);
        FormatLabel = GetFormatLabel(LaunchModel);
        DraftTitle = game.Title;
        LaunchCommand = launchCommand ?? NoGameCommand;
        SaveTitleCommand = saveTitleCommand ?? NoGameCommand;
        SetCoverCommand = setCoverCommand ?? NoGameCommand;
        LoadCoverCommand = loadCoverCommand ?? NoGameCommand;
        OpenAchievementsCommand = openAchievementsCommand ?? NoGameCommand;
        ScrapeCommand = scrapeCommand ?? NoGameCommand;
        ShowInFolderCommand = showInFolderCommand ?? NoGameCommand;
        OpenTextureFolderCommand = openTextureFolderCommand ?? NoGameCommand;
        CanOpenTextureFolder = canOpenTextureFolder;
        RemoveSelectedCommand = removeSelectedCommand ?? NoCommand;
        ScrapeSelectedCommand = scrapeSelectedCommand ?? NoCommand;
    }

    /// <summary>Applies a successfully remembered disc to the currently displayed title set.</summary>
    public void SetSelectedDisc(GameDisc selectedDisc)
    {
        if (!_discs.Any(disc => disc.Game.Id == selectedDisc.Game.Id))
            throw new ArgumentException("The selected disc does not belong to this title set.", nameof(selectedDisc));

        LaunchModel = selectedDisc.Game;
        SelectedDiscNumber = selectedDisc.Number;
        IsAvailable = selectedDisc.Game.IsAvailable;
        FormatLabel = GetFormatLabel(LaunchModel);
        OnPropertyChanged(nameof(LaunchModel));
        OnPropertyChanged(nameof(SelectedDiscNumber));
        OnPropertyChanged(nameof(SelectedDiscText));
        OnPropertyChanged(nameof(DiscBadgeText));
        OnPropertyChanged(nameof(FormatLabel));
        OnPropertyChanged(nameof(GamepadSubtitle));
        OnPropertyChanged(nameof(UnavailableLaunchStatus));
        foreach (var option in DiscOptions)
            option.IsCurrent = option.Disc.Game.Id == selectedDisc.Game.Id;
    }

    private static string GetFormatLabel(Game game) =>
        System.IO.Path.GetExtension(game.Path) is { Length: > 1 } extension
            ? extension[1..].ToUpperInvariant()
            : "FOLDER";

    partial void OnTitleChanged(string value)
    {
        Model = Model with { Title = value };
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(UnavailableLaunchStatus));
        // DisplayTitle falls back to Title whenever no scraped title is showing (none was applied,
        // or the user has renamed this game), so an in-place rename must refresh it.
        OnPropertyChanged(nameof(DisplayTitle));
    }

    partial void OnCoverImageChanging(Bitmap? value)
    {
        if (!ReferenceEquals(CoverImage, value))
            CoverImage?.Dispose();
    }

    // The frame never adopts the loaded bitmap's own ratio: the cover fills the platform's canonical
    // frame (UniformToFill in the tile), which keeps every tile of a system uniform and stops one
    // off-ratio scan from ballooning the shared shelf. So loading a cover only toggles HasCoverImage.
    partial void OnCoverImageChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasCoverImage));
        OnPropertyChanged(nameof(ShelfUses3DHero));
    }

    partial void OnIsFocusedChanged(bool value) => OnPropertyChanged(nameof(ShelfUses3DHero));

    partial void OnFanartImageChanging(Bitmap? value)
    {
        if (!ReferenceEquals(FanartImage, value))
            FanartImage?.Dispose();
    }

    partial void OnFanartImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasFanartImage));

    partial void OnWheelImageChanging(Bitmap? value)
    {
        if (!ReferenceEquals(WheelImage, value))
            WheelImage?.Dispose();
    }

    partial void OnWheelImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasWheelImage));

    partial void OnRatingTextChanged(string? value) => OnPropertyChanged(nameof(HasRating));

    /// <summary>Records the fan-art path, logo path, formatted rating, and scraped metadata facts
    /// resolved from this game's details. The bitmaps are loaded separately, only while it is the
    /// spotlight hero. The facts (genre/year/players/developer/publisher) each render as a chip.</summary>
    public void ApplySpotlightDetails(string? fanartPath, string? wheelPath, string? ratingText, IReadOnlyList<string> facts)
    {
        FanartPath = fanartPath;
        WheelPath = wheelPath;
        RatingText = ratingText;
        _spotlightFacts = facts;
        OnPropertyChanged(nameof(SpotlightFacts));
        OnPropertyChanged(nameof(SpotlightFactsPrimary));
        OnPropertyChanged(nameof(SpotlightFactsSecondary));
        OnPropertyChanged(nameof(HasSpotlightSecondaryFacts));
        AreSpotlightDetailsLoaded = true;
        OnPropertyChanged(nameof(ShowSpotlightTitleFallback));
    }

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

    public void Dispose()
    {
        CoverImage = null;
        FanartImage = null;
        WheelImage = null;
    }

    /// <summary>Up-to-two-letter monogram for the placeholder cover, taken from the displayed name.</summary>
    public string Initials
    {
        get
        {
            var trimmed = DisplayTitle.Trim();
            if (trimmed.Length == 0)
                return "?";

            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return string.Concat(char.ToUpperInvariant(parts[0][0]), char.ToUpperInvariant(parts[1][0]));
            return trimmed[..Math.Min(2, trimmed.Length)].ToUpperInvariant();
        }
    }
}
