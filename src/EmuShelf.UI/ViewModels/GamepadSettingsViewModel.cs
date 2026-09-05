using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Data.Converters;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;
using EmuShelf.Core.Input;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.ViewModels;

public enum GamepadSettingsRowKind
{
    Toggle,
    Choice,
    Action,
    Text,
    Secret,
    Folder,
    File,
    Information,
    /// <summary>A non-focusable platform group heading (artwork + name) that gives the section a
    /// visible hierarchy instead of a flat list of equal-weight rows.</summary>
    Header,
    /// <summary>A focusable one-line platform summary (artwork, name, "emulator · N games") that expands
    /// its per-platform rows in place when activated, so a 15-platform section reads as 15 rows, not 90.</summary>
    Summary,
}

/// <summary>One visible item in the controller-native choice picker opened from a settings row.</summary>
public partial class GamepadChoiceOptionViewModel : ObservableObject
{
    private readonly GamepadSettingsViewModel _owner;

    internal GamepadChoiceOptionViewModel(
        GamepadSettingsViewModel owner,
        int index,
        string displayName,
        bool isSelected)
    {
        _owner = owner;
        Index = index;
        DisplayName = displayName;
        IsSelected = isSelected;
    }

    public int Index { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsFocused { get; set; }

    [RelayCommand]
    private void Select() => _owner.SelectChoiceOption(Index);
}

/// <summary>A single controller-sized row projected from the existing Desktop settings model.</summary>
public partial class GamepadSettingsRowViewModel : ObservableObject
{
    private readonly IGamepadSettingsRowHost _owner;

    internal GamepadSettingsRowViewModel(IGamepadSettingsRowHost owner, GamepadSettingsRowSpec spec)
    {
        _owner = owner;
        Apply(spec);
    }

    public string Key { get; private set; } = string.Empty;
    public string Label { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public GamepadSettingsRowKind Kind { get; private set; }
    public bool IsEnabled { get; private set; }
    public bool IsDestructive { get; private set; }
    public bool? ToggleValue { get; private set; }
    /// <summary>Platform id for artwork on group headers and their member rows; null for generic rows.</summary>
    public string? SystemId { get; private set; }
    /// <summary>True for a member row under a platform header; indents it beneath its group.</summary>
    public bool IsGrouped { get; private set; }
    public bool IsHeader => Kind == GamepadSettingsRowKind.Header;
    public bool IsSummary => Kind == GamepadSettingsRowKind.Summary;
    /// <summary>True while a summary row's platform rows are shown beneath it.</summary>
    public bool IsExpanded { get; private set; }
    /// <summary>True when the row's description/value reports a problem (emulator missing, permission
    /// not granted) and should render in the warning colour.</summary>
    public bool IsWarning { get; private set; }
    /// <summary>True for rows rendered at the compact height with a one-line description.</summary>
    public bool IsCompact { get; private set; }
    public bool HasDescription => !string.IsNullOrEmpty(Description);
    public int DescriptionMaxLines => IsCompact ? 1 : 2;
    public string SummaryChevron => IsExpanded ? "▾" : "›";
    /// <summary>Legend text for the Y action this row offers ("Rescan", "Forget folder"), or empty.</summary>
    public string SecondaryLabel { get; private set; } = string.Empty;
    public bool HasSecondary => !string.IsNullOrEmpty(SecondaryLabel) && IsEnabled;
    public bool IsNormalRow => !IsHeader && !IsSaveRow;
    public bool HasPlatformIcon => !string.IsNullOrEmpty(SystemId);
    /// <summary>True for gamepad-only view-state controls (e.g. expand inventory) that have no Desktop
    /// settings field and must not participate in the executable parity comparison.</summary>
    public bool ExcludeFromParity { get; private set; }
    public bool CanActivate => IsEnabled &&
        Kind is not (GamepadSettingsRowKind.Information or GamepadSettingsRowKind.Header);
    public string ParityId =>
        Kind is not (GamepadSettingsRowKind.Information or GamepadSettingsRowKind.Header)
            && !IsSaveRow && !ExcludeFromParity
            ? Key
            : string.Empty;
    /// <summary>A leading glyph shown for generic rows without platform artwork, categorising the row.</summary>
    public string LeadingGlyph => Kind switch
    {
        GamepadSettingsRowKind.Toggle => "◑",
        GamepadSettingsRowKind.Choice => "⇅",
        GamepadSettingsRowKind.Information => "ℹ",
        _ => ActionGlyph,
    };
    public bool IsSaveRow => Key == "common.save";
    public bool IsToggle => Kind == GamepadSettingsRowKind.Toggle;
    public bool IsToggleOn => ToggleValue == true;
    public bool IsChoice => Kind == GamepadSettingsRowKind.Choice;
    public bool IsAction => Kind == GamepadSettingsRowKind.Action;
    public bool IsEditableValue => Kind is GamepadSettingsRowKind.Text or
        GamepadSettingsRowKind.Secret or GamepadSettingsRowKind.Folder or GamepadSettingsRowKind.File;
    public bool IsInformation => Kind == GamepadSettingsRowKind.Information;
    public bool ShowsActionButton => Kind is GamepadSettingsRowKind.Action or
        GamepadSettingsRowKind.Text or GamepadSettingsRowKind.Secret or
        GamepadSettingsRowKind.Folder or GamepadSettingsRowKind.File;
    /// <summary>True only while an action is actually running: its Value is a status word ("WORKING",
    /// "CONNECTING…") rather than an "A …" prompt. Lets the row show that label in place of the idle
    /// affordance without also labelling merely-disabled rows.</summary>
    public bool ShowsWorkingLabel =>
        IsAction && !Value.StartsWith("A ", StringComparison.OrdinalIgnoreCase);
    public string ActionButtonText => Kind switch
    {
        GamepadSettingsRowKind.Text or GamepadSettingsRowKind.Secret => "EDIT",
        GamepadSettingsRowKind.Folder => "CHOOSE",
        GamepadSettingsRowKind.File => "CHOOSE FILE",
        _ when Value.StartsWith("A ", StringComparison.OrdinalIgnoreCase) => Value[2..],
        _ => Value,
    };
    public string ActionGlyph => Kind switch
    {
        GamepadSettingsRowKind.Text or GamepadSettingsRowKind.Secret or
            GamepadSettingsRowKind.Folder or GamepadSettingsRowKind.File => "✎",
        _ when Key.EndsWith("replace-cloud", StringComparison.Ordinal) => "↑",
        _ when Key.EndsWith("replace-local", StringComparison.Ordinal) => "↓",
        _ when Key.Contains("disconnect", StringComparison.Ordinal) => "×",
        _ when Key.Contains("connect", StringComparison.Ordinal) => "+",
        _ when Key.Contains("rescan", StringComparison.Ordinal) ||
            Key.Contains("refresh", StringComparison.Ordinal) ||
            Key.Contains("sync", StringComparison.Ordinal) => "↻",
        _ when Key.Contains("fetch", StringComparison.Ordinal) => "↓",
        _ when Key.Contains("detected", StringComparison.Ordinal) => "↶",
        _ => "›",
    };
    internal Func<Task>? Activate { get; private set; }
    internal Func<Task>? SecondaryActivate { get; private set; }
    internal bool SecondaryIsDestructive { get; private set; }
    internal string? SecondaryConfirmationTitle { get; private set; }
    internal string? SecondaryConfirmationText { get; private set; }
    internal Action<int>? Adjust { get; private set; }
    internal string? ConfirmationTitle { get; private set; }
    internal string? ConfirmationText { get; private set; }

    [ObservableProperty]
    public partial bool IsFocused { get; set; }

    [RelayCommand]
    private Task SelectAsync() => _owner.FocusAndActivateAsync(this);

    internal void Apply(GamepadSettingsRowSpec spec)
    {
        Key = spec.Key;
        Label = spec.Label;
        Description = spec.Description;
        Value = spec.Value;
        Kind = spec.Kind;
        IsEnabled = spec.IsEnabled;
        IsDestructive = spec.IsDestructive;
        ToggleValue = spec.ToggleValue;
        SystemId = spec.SystemId;
        IsGrouped = spec.IsGrouped;
        ExcludeFromParity = spec.ExcludeFromParity;
        Activate = spec.Activate;
        IsExpanded = spec.IsExpanded;
        IsWarning = spec.IsWarning;
        IsCompact = spec.IsCompact;
        SecondaryLabel = spec.SecondaryLabel ?? string.Empty;
        SecondaryActivate = spec.SecondaryActivate;
        SecondaryIsDestructive = spec.SecondaryIsDestructive;
        SecondaryConfirmationTitle = spec.SecondaryConfirmationTitle;
        SecondaryConfirmationText = spec.SecondaryConfirmationText;
        Adjust = spec.Adjust;
        ConfirmationTitle = spec.ConfirmationTitle;
        ConfirmationText = spec.ConfirmationText;
        OnPropertyChanged(string.Empty);
        OnPropertyChanged(nameof(CanActivate));
        OnPropertyChanged(nameof(ParityId));
        OnPropertyChanged(nameof(IsHeader));
        OnPropertyChanged(nameof(IsSummary));
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(IsWarning));
        OnPropertyChanged(nameof(IsCompact));
        OnPropertyChanged(nameof(HasDescription));
        OnPropertyChanged(nameof(DescriptionMaxLines));
        OnPropertyChanged(nameof(SummaryChevron));
        OnPropertyChanged(nameof(SecondaryLabel));
        OnPropertyChanged(nameof(HasSecondary));
        OnPropertyChanged(nameof(IsNormalRow));
        OnPropertyChanged(nameof(HasPlatformIcon));
        OnPropertyChanged(nameof(IsGrouped));
        OnPropertyChanged(nameof(LeadingGlyph));
        OnPropertyChanged(nameof(IsSaveRow));
        OnPropertyChanged(nameof(IsToggle));
        OnPropertyChanged(nameof(IsToggleOn));
        OnPropertyChanged(nameof(IsChoice));
        OnPropertyChanged(nameof(IsAction));
        OnPropertyChanged(nameof(IsEditableValue));
        OnPropertyChanged(nameof(IsInformation));
        OnPropertyChanged(nameof(ShowsActionButton));
        OnPropertyChanged(nameof(ShowsWorkingLabel));
        OnPropertyChanged(nameof(ActionButtonText));
        OnPropertyChanged(nameof(ActionGlyph));
    }
}

internal sealed record GamepadSettingsRowSpec(
    string Key,
    string Label,
    string Description,
    string Value,
    GamepadSettingsRowKind Kind,
    bool IsEnabled = true,
    bool IsDestructive = false,
    Func<Task>? Activate = null,
    Action<int>? Adjust = null,
    string? ConfirmationTitle = null,
    string? ConfirmationText = null,
    bool? ToggleValue = null,
    string? SystemId = null,
    bool IsGrouped = false,
    bool ExcludeFromParity = false,
    bool IsExpanded = false,
    bool IsWarning = false,
    bool IsCompact = false,
    string? SecondaryLabel = null,
    Func<Task>? SecondaryActivate = null,
    bool SecondaryIsDestructive = false,
    string? SecondaryConfirmationTitle = null,
    string? SecondaryConfirmationText = null);

/// <summary>
/// Controller projection over <see cref="EmulatorSettingsViewModel"/>. It owns only navigation,
/// draft entry, and confirmation state; all values, validation, operations, and persistence remain
/// in the existing settings view model and services.
/// </summary>
public partial class GamepadSettingsViewModel : ViewModelBase, IDisposable, IGamepadSettingsRowHost
{
    private const int ThemeColumns = 3;

    /// <summary>Maps <see cref="RevealSecret"/> to a TextBox PasswordChar: '\0' shows the text
    /// (revealed), '●' masks it. Lets one field toggle its mask without moving controller focus.</summary>
    public static FuncValueConverter<bool, char> SecretMaskChar { get; } =
        new(revealed => revealed ? '\0' : '●');

    private readonly EmulatorSettingsViewModel _settings;
    private readonly IOnScreenKeyboardService _onScreenKeyboard;
    private readonly Dictionary<SettingsSection, string> _focusedRowBySection = [];
    private readonly IReadOnlyList<ThemeChoiceViewModel> _themeChoices;
    private readonly Func<ThemePreference, Task>? _applyTheme;
    private readonly Func<Task>? _openHotkeys;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<EmulatorChoice>> _androidEmulatorChoices;
    private readonly Func<string, int>? _gameCountBySystem;
    private readonly Func<Task>? _refreshGameCounts;
    private readonly Func<EmulatorChoice, bool>? _isEmulatorChoiceInstalled;
    private readonly Func<string?>? _closeOnReturnWarning;
    private readonly Func<Task>? _grantCloseOnReturnPrivilege;
    // Setup-wizard mode (the in-app half of Android first-run setup): the same projection walked as a
    // sequence of steps instead of a rail of sections. Null in ordinary Settings.
    private readonly SetupWizardOptions? _setup;
    private readonly List<SetupStep> _liveSetupSteps = [];
    private int _setupIndex;
    private bool _secondScreenReadyRead;
    private bool _cachedSecondScreenReady;
    // Ordinary Settings only: the Library row that re-runs the wizard on demand (Android).
    private readonly IAsyncRelayCommand? _runSetupCommand;
    // Device probes held for the life of this screen, keyed by choice id; see EmulatorMissingFor.
    private readonly Dictionary<string, bool> _emulatorChoiceInstalled = new(StringComparer.Ordinal);
    private string? _cachedCloseOnReturnWarning;
    private bool _closeOnReturnWarningRead;
    private string _emulatorsRailStatus = string.Empty;
    // Per-platform game counts are a snapshot taken when Settings opened, so a scan started from here has
    // to re-read them. Tracks the falling edge of IsMaintainingLibrary, which every rescan and folder
    // import raises, rather than guessing from status text.
    private bool _maintainingLibrary;
    // The one platform whose rows are shown beneath its summary in the Emulators section; null = all
    // collapsed. Single-open keeps the list short (the point of the summaries) and the focus predictable.
    private string? _expandedSystemId;
    private Func<Task>? _pendingConfirmation;
    private Action<string>? _commitText;
    private Action<string>? _commitChoice;
    private string? _choicePickerRowKey;
    private bool _synchronizingSection;
    private bool _applyingLocalEdit;
    private bool _texturePackListExpanded;
    private bool _disposed;

    public ObservableCollection<GamepadSettingsRowViewModel> Rows { get; } = [];

    public IReadOnlyList<SettingsSection> Sections { get; }

    public EmulatorSettingsViewModel Settings => _settings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SectionTitle))]
    [NotifyPropertyChangedFor(nameof(SectionDescription))]
    public partial SettingsSection SelectedSection { get; set; } = SettingsSection.General;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChoiceRowFocused))]
    public partial int FocusedRowIndex { get; set; }

    [ObservableProperty]
    public partial int FocusRevision { get; set; }

    /// <summary>The controller theme gallery is a dedicated page after the projected row sections;
    /// Desktop represents the same choices with <see cref="SettingsSection.Themes"/>.</summary>
    [ObservableProperty]
    public partial bool IsThemesSection { get; set; }

    [ObservableProperty]
    public partial int FocusedThemeIndex { get; set; }

    /// <summary>True when the left section rail owns focus. Left enters it, Up/Down move sections,
    /// and Right/A return to the content column. Keeps LB/RB as a shortcut from either column.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChoiceRowFocused))]
    public partial bool IsRailFocused { get; set; }

    [ObservableProperty]
    public partial bool IsTextEntryOpen { get; set; }

    [ObservableProperty]
    public partial string TextEntryTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TextEntryDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DraftText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSecretEntry { get; set; }

    /// <summary>Whether a masked secret is temporarily shown as plain text (toggled with Y) so a long
    /// API key or password can be checked before saving. Reset every time the entry opens. The view
    /// keeps one TextBox and just drops its mask, so controller focus never moves off the field.</summary>
    [ObservableProperty]
    public partial bool RevealSecret { get; set; }

    [ObservableProperty]
    public partial int TextEntryRevision { get; set; }

    [ObservableProperty]
    public partial bool IsConfirmationOpen { get; set; }

    [ObservableProperty]
    public partial string ConfirmationTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfirmationText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsConfirmChoiceSelected { get; set; }

    [ObservableProperty]
    public partial bool IsChoicePickerOpen { get; set; }

    [ObservableProperty]
    public partial string ChoicePickerTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ChoicePickerDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int FocusedChoiceIndex { get; set; }

    public ObservableCollection<GamepadChoiceOptionViewModel> ChoiceOptions { get; } = [];

    public bool IsNormal => !IsTextEntryOpen && !IsConfirmationOpen && !IsChoicePickerOpen;

    /// <summary>Whether the focused normal row accepts direct Left/Right adjustment.</summary>
    public bool IsChoiceRowFocused =>
        IsNormal && !IsRailFocused && FocusedRow?.IsChoice == true;

    public IReadOnlyList<ThemeChoiceViewModel> ThemeChoices => _themeChoices;

    public bool ShowThemes => _themeChoices.Count > 0;

    /// <summary>The shared "match colours to game artwork" setting, surfaced in the gamepad Themes
    /// view so it is reachable on a controller (Desktop keeps it in its Themes section). It applies
    /// live through the underlying settings model.</summary>
    public bool AmbientThemeFromArtwork
    {
        get => _settings.AmbientThemeFromArtwork;
        set
        {
            if (_settings.AmbientThemeFromArtwork == value)
                return;
            _settings.AmbientThemeFromArtwork = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Whether the couch shelf is presented through a simulated CRT tube.</summary>
    public bool CrtScreenEffect
    {
        get => _settings.CrtScreenEffect;
        set
        {
            if (_settings.CrtScreenEffect == value)
                return;
            _settings.CrtScreenEffect = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Android only: close the launched emulator when EmuShelf returns to the foreground. Applied
    /// on Save through the underlying settings model, like the other Emulators-section toggles.</summary>
    public bool CloseEmulatorOnReturn
    {
        get => _settings.CloseEmulatorOnReturn;
        set
        {
            if (_settings.CloseEmulatorOnReturn == value)
                return;
            _settings.CloseEmulatorOnReturn = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// True when the ambient toggle owns focus, marked by the -1 sentinel of
    /// <see cref="FocusedThemeIndex"/>.
    /// </summary>
    /// <remarks>
    /// Two negative sentinels now sit above the grid rather than one, so this is an equality test
    /// rather than the old "any negative". A stray &lt; 0 here would light both toggles at once.
    /// </remarks>
    public bool IsAmbientToggleFocused => IsThemesSection && FocusedThemeIndex == AmbientToggleIndex;

    /// <summary>True when the CRT toggle, the topmost focus target in the Themes view, owns focus.</summary>
    public bool IsCrtToggleFocused => IsThemesSection && FocusedThemeIndex == CrtToggleIndex;

    /// <summary>Focus sentinels for the two toggles stacked above the theme grid.</summary>
    private const int AmbientToggleIndex = -1;

    /// <inheritdoc cref="AmbientToggleIndex"/>
    private const int CrtToggleIndex = -2;

    /// <summary>The row list is shown for the four model sections; the gallery replaces it on Themes.</summary>
    public bool IsRowsVisible => IsNormal && !IsThemesSection;

    public bool IsThemesVisible => IsNormal && IsThemesSection;

    public bool IsAutomaticKeyboardAvailable => _onScreenKeyboard.IsSupported;

    public string KeyboardHint => IsAutomaticKeyboardAvailable
        ? "The on-screen keyboard will open automatically. A keyboard also works."
        : "Type with a keyboard.";

    public GamepadSettingsRowViewModel? FocusedRow => Rows.Count == 0
        ? null
        : Rows[Math.Clamp(FocusedRowIndex, 0, Rows.Count - 1)];

    public GamepadSettingsRowViewModel? SaveRow => Rows.FirstOrDefault(row => row.IsSaveRow);

    public string SectionTitle => IsSetupMode ? SetupTitle : IsThemesSection ? "Themes" : SelectedSection switch
    {
        SettingsSection.Emulators => "Emulators",
        SettingsSection.Hotkeys => "Hotkeys",
        SettingsSection.RetroAchievements => "RetroAchievements",
        SettingsSection.ArtworkMetadata => "Artwork & Metadata",
        SettingsSection.Saves => "Saves",
        SettingsSection.TexturePacks => "Texture Packs",
        SettingsSection.About => "About",
        _ => "Library",
    };

    public string SectionDescription => IsSetupMode ? SetupDescription : IsThemesSection
        ? "Personalize EmuShelf's colors. A theme applies instantly and is shared with Desktop mode."
        : SelectedSection switch
    {
        SettingsSection.Emulators when _androidEmulatorChoices.Count > 0 =>
            "Which Android app runs each system, and where its games are.",
        SettingsSection.Emulators =>
            "Import games and manage each system's folders. Edit emulator paths, arguments, and cores in Desktop Settings.",
        SettingsSection.Hotkeys =>
            "Write one in-game hotkey scheme into each emulator and see the Steam Input mapping.",
        SettingsSection.RetroAchievements =>
            "Read achievement sets and your progress. Emulators still own unlocks and submission.",
        SettingsSection.ArtworkMetadata =>
            "Fetch titles and artwork from the built-in catalogue or ScreenScraper, and toggle web image search. Game files are never uploaded.",
        SettingsSection.Saves =>
            "Reconcile emulator saves through your own Google Drive. Game files are never included.",
        SettingsSection.TexturePacks =>
            "Inspect installed replacement textures without changing packs or emulator configuration.",
        SettingsSection.About =>
            "Version, build, and updates. Updating in place keeps gaming mode without dropping to the desktop.",
        _ => "Library visibility, metadata consent, and safe maintenance.",
    };

    // One-line status under each rail entry, so the rail doubles as a glance at what needs attention
    // without opening every section. Kept to a word or two; the section itself carries the detail.
    public string LibraryRailStatus => _gameCountBySystem is null
        ? string.Empty
        : FormatGames(_settings.Rows.Sum(row => _gameCountBySystem(row.SystemId)));

    // Computed once per rebuild rather than in the getter: both this and IsEmulatorsRailWarning are bound,
    // and each evaluation walks every platform's install probe, so a getter would run that scan twice per
    // notification — in every section, since the rail is always on screen.
    public string EmulatorsRailStatus => _emulatorsRailStatus;

    public bool IsEmulatorsRailWarning => !string.IsNullOrEmpty(_emulatorsRailStatus);

    private string ComputeEmulatorsRailStatus()
    {
        var attention = _settings.Rows.FirstOrDefault(row => EmulatorMissingFor(row) is not null);
        if (attention is not null)
            return $"{attention.SystemName} needs attention";
        return CloseOnReturnWarning is not null ? "Shizuku needs attention" : string.Empty;
    }

    public string HotkeysRailStatus => string.Empty;
    public string RetroAchievementsRailStatus => _settings.IsRetroAchievementsConnected
        ? _settings.ConnectedAccountName ?? "Signed in"
        : "Not signed in";
    public string ArtworkRailStatus => _settings.IsScreenScraperConnected ? "Connected" : string.Empty;
    public string SavesRailStatus => _settings.IsCloudConnected ? "Google Drive" : string.Empty;
    public string TexturePacksRailStatus => string.Empty;
    public string ThemesRailStatus => _themeChoices.FirstOrDefault(choice => choice.IsSelected)?.Name ?? string.Empty;
    public string AboutRailStatus => _settings.AppVersionDisplay;

    private static string FormatGames(int count) => count == 1 ? "1 game" : $"{count} games";

    // Both device probes below are binder round trips on Android and answer questions only the system can
    // change (the user grants Shizuku in Shizuku's dialog, installs an emulator from a store), so they are
    // read once and held until App.ForegroundReturned says the user has been away — see RefreshDeviceState.
    private string? CloseOnReturnWarning
    {
        get
        {
            if (!_settings.HasCloseEmulatorOnReturn || !CloseEmulatorOnReturn)
                return null;
            if (!_closeOnReturnWarningRead)
            {
                _cachedCloseOnReturnWarning = _closeOnReturnWarning?.Invoke();
                _closeOnReturnWarningRead = true;
            }
            return _cachedCloseOnReturnWarning;
        }
    }

    /// <summary>The display name of the platform's chosen Android emulator when it is not installed on this
    /// device, else null. Only standalone-app choices are probed; RetroArch cores live inside RetroArch.</summary>
    private string? EmulatorMissingFor(EmulatorSettingsRowViewModel row)
    {
        if (_isEmulatorChoiceInstalled is null || !row.HasEmulatorChoices)
            return null;
        var choice = row.SelectedChoice ?? row.AvailableChoices[0];
        if (!_emulatorChoiceInstalled.TryGetValue(choice.Id, out var installed))
        {
            installed = _isEmulatorChoiceInstalled(choice);
            _emulatorChoiceInstalled[choice.Id] = installed;
        }
        return installed ? null : choice.DisplayName;
    }

    /// <summary>
    /// Drops the cached device probes and rebuilds, so a Shizuku grant or an emulator installed while the
    /// user was away shows up without reopening Settings. Cheap when nothing changed: the rebuild is the
    /// same one any settings edit already runs.
    /// </summary>
    public void RefreshDeviceState()
    {
        if (_disposed)
            return;
        _emulatorChoiceInstalled.Clear();
        _closeOnReturnWarningRead = false;
        _secondScreenReadyRead = false;
        RebuildRows();
    }

    public string StatusText => IsThemesSection ? string.Empty : SelectedSection switch
    {
        SettingsSection.Emulators => EmulatorsSectionStatus(),
        SettingsSection.Hotkeys => FirstNonEmpty(
            _settings.SteamTemplateStatus,
            _settings.HotkeySchemeSummary),
        SettingsSection.RetroAchievements => FirstNonEmpty(
            _settings.RetroAchievementsProgressText,
            _settings.RetroAchievementsStatusText),
        SettingsSection.ArtworkMetadata => FirstNonEmpty(
            _settings.MetadataProgressText,
            _settings.MetadataStatusText,
            _settings.ScreenScraperStatusText),
        SettingsSection.Saves => FirstNonEmpty(
            _settings.CloudSyncProgressText,
            _settings.CloudStatusText),
        SettingsSection.TexturePacks => FirstNonEmpty(
            _settings.TexturePackStatusText,
            _settings.TexturePackSummary,
            _settings.TexturePackLastScanText),
        SettingsSection.About => UpdateStatusHint,
        // General also hosts the Android "change data folder" row, whose rejection/cancellation reason is
        // reported on DataFolderStatusText — surfaced here so a refused pick is visible on the couch pill,
        // not only on Desktop's inline label.
        _ => FirstNonEmpty(
            _settings.StatusText,
            _settings.DataFolderStatusText,
            _settings.MaintenanceStatusText),
    };

    /// <summary>The Emulators section pill prefers the status of the platform the cursor is on, so
    /// acting on one console never surfaces a stale line left by another; the shared rescan-all line
    /// belongs to the Library section, so it is deliberately not a fallback here.</summary>
    private string EmulatorsSectionStatus()
    {
        if (FocusedRow?.SystemId is { Length: > 0 } focusedSystemId
            && _settings.Rows.FirstOrDefault(row => row.SystemId == focusedSystemId) is { } focused
            && !string.IsNullOrWhiteSpace(focused.MaintenanceStatusText))
        {
            return focused.MaintenanceStatusText;
        }
        return _settings.Rows
            .Select(row => row.MaintenanceStatusText)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? string.Empty;
    }

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    /// <summary>Whether the current section has an operation running, so its status pill can show an
    /// indeterminate bar — the same "working" affordance the Desktop settings cards give.</summary>
    public bool IsWorkingInSection => !IsThemesSection && SelectedSection switch
    {
        SettingsSection.Emulators => _settings.IsMaintainingLibrary,
        SettingsSection.Hotkeys => _settings.IsHotkeyBusy,
        SettingsSection.RetroAchievements => _settings.IsRetroAchievementsBusy,
        SettingsSection.ArtworkMetadata => _settings.IsScreenScraperBusy || _settings.IsMaintainingLibrary,
        SettingsSection.Saves => _settings.IsCloudBusy,
        SettingsSection.TexturePacks => _settings.IsTexturePackBusy,
        SettingsSection.About => _settings.IsUpdateBusy,
        _ => _settings.IsMaintainingLibrary,
    };

    public bool IsGeneralSection => !IsThemesSection && SelectedSection == SettingsSection.General;
    public bool IsEmulatorsSection => !IsThemesSection && SelectedSection == SettingsSection.Emulators;
    public bool IsHotkeysSection => !IsThemesSection && SelectedSection == SettingsSection.Hotkeys;
    public bool IsRetroAchievementsSection => !IsThemesSection && SelectedSection == SettingsSection.RetroAchievements;
    public bool IsArtworkMetadataSection => !IsThemesSection && SelectedSection == SettingsSection.ArtworkMetadata;
    public bool IsSavesSection => !IsThemesSection && SelectedSection == SettingsSection.Saves;
    public bool IsTexturePacksSection => !IsThemesSection && SelectedSection == SettingsSection.TexturePacks;
    public bool IsAboutSection => !IsThemesSection && SelectedSection == SettingsSection.About;

    public event Action<bool>? CloseRequested;

    public GamepadSettingsViewModel(
        EmulatorSettingsViewModel settings,
        IOnScreenKeyboardService? onScreenKeyboard = null,
        IReadOnlyList<ThemeChoiceViewModel>? themeChoices = null,
        Func<ThemePreference, Task>? applyTheme = null,
        Func<Task>? openHotkeys = null,
        IReadOnlyDictionary<string, IReadOnlyList<EmulatorChoice>>? androidEmulatorChoices = null,
        Func<string, int>? gameCountBySystem = null,
        Func<EmulatorChoice, bool>? isEmulatorChoiceInstalled = null,
        Func<string?>? closeOnReturnWarning = null,
        Func<Task>? grantCloseOnReturnPrivilege = null,
        Func<Task>? refreshGameCounts = null,
        SetupWizardOptions? setup = null,
        Func<Task>? runSetup = null)
    {
        _settings = settings;
        _setup = setup;
        _runSetupCommand = runSetup is null || setup is not null ? null : new AsyncRelayCommand(runSetup);
        _gameCountBySystem = gameCountBySystem;
        _refreshGameCounts = refreshGameCounts;
        _maintainingLibrary = settings.IsMaintainingLibrary;
        _isEmulatorChoiceInstalled = isEmulatorChoiceInstalled;
        _closeOnReturnWarning = closeOnReturnWarning;
        _grantCloseOnReturnPrivilege = grantCloseOnReturnPrivilege;
        _onScreenKeyboard = onScreenKeyboard ?? UnsupportedOnScreenKeyboardService.Instance;
        _themeChoices = themeChoices ?? [];
        _applyTheme = applyTheme;
        _openHotkeys = openHotkeys;
        _androidEmulatorChoices = androidEmulatorChoices
            ?? new Dictionary<string, IReadOnlyList<EmulatorChoice>>(StringComparer.Ordinal);
        // Both modes present the same section list, in the same order, so the couch surface mirrors
        // Desktop's structure. Only Themes is excluded here: appearance is not part of the settings
        // model, so it is a dedicated gamepad gallery page rather than a projected row section. The rail
        // and LB/RB paging still show Themes in Desktop's slot — right before About — by splicing it back
        // into the ordered page list (see Pages), not by appending it after every section. Emulators
        // projects per-platform library actions plus Android's flat app/core choices;
        // executable paths and launch arguments stay Desktop-only. Hotkeys is a
        // per-emulator × per-action matrix that a controller can't navigate as a flat list, so its
        // section row opens the controller-native GamepadHotkeysViewModel overlay; About projects
        // read-only build info plus the in-place update actions.
        Sections = settings.Sections
            .Where(section => section is not SettingsSection.Themes)
            .ToArray();

        if (_setup is not null)
        {
            // The steps this device gets, in order. Storage access and the data folder are already done
            // (the pre-boot page handled them) and are listed so the rail reads as one wizard; the rest are
            // live only when the feature exists here (a second screen, the close-on-return setting, cloud
            // saves) so a phone with none of them sees a two-step wizard, not a list of dead ends.
            if (_setup.HasSecondScreen)
                _liveSetupSteps.Add(SetupStep.SecondScreen);
            if (_settings.HasCloseEmulatorOnReturn)
                _liveSetupSteps.Add(SetupStep.ClosingGames);
            _liveSetupSteps.Add(SetupStep.GamesAndEmulators);
            if (_settings.HasCloudSaves && Sections.Contains(SettingsSection.Saves))
                _liveSetupSteps.Add(SetupStep.Saves);
            SetupRail.Steps.Add(new SetupStepViewModel(SetupStep.StorageAccess));
            SetupRail.Steps.Add(new SetupStepViewModel(SetupStep.DataFolder));
            foreach (var step in _liveSetupSteps)
                SetupRail.Steps.Add(new SetupStepViewModel(step));
            SetupRail.StartCommand = new AsyncRelayCommand(AdvanceSetupAsync);
            SelectedSection = SectionForSetupStep(_liveSetupSteps[0]);
            PrepareSetupStep();
        }

        _settings.PropertyChanged += OnSettingsPropertyChanged;
        // The update-download coordinator is a separate ObservableObject, so its per-percent progress
        // (StatusText/DownloadPercent) never echoes through _settings.PropertyChanged. Route it through
        // the same rebuild path so the About update rows' hint text stays live during a download.
        if (_settings.Updates is { } updates)
            updates.PropertyChanged += OnSettingsPropertyChanged;
        _settings.CloseRequested += OnSettingsCloseRequested;
        // The Emulators section projects each per-platform row, so a per-row sync/rescan status change
        // (which writes to that row, not the shared settings model) has to rebuild the section too.
        HookCollection(_settings.Rows);
        HookCollection(_settings.CloudPlatforms);
        HookCollection(_settings.TexturePlatforms);
        HookCollection(_settings.TexturePackEntries);
        RebuildRows();
    }

    public bool Dispatch(GamepadAction action)
    {
        if (IsTextEntryOpen)
        {
            switch (action)
            {
                case GamepadAction.Confirm:
                    CommitTextEntry();
                    return true;
                case GamepadAction.Cancel:
                    CancelTextEntry();
                    return true;
                case GamepadAction.Actions when IsSecretEntry:
                    // Y reveals/hides a masked secret so a long key can be checked before saving.
                    RevealSecret = !RevealSecret;
                    return true;
                default:
                    return false;
            }
        }

        if (IsConfirmationOpen)
        {
            switch (action)
            {
                case GamepadAction.NavigateLeft:
                case GamepadAction.NavigateUp:
                    IsConfirmChoiceSelected = false;
                    FocusRevision++;
                    return true;
                case GamepadAction.NavigateRight:
                case GamepadAction.NavigateDown:
                    IsConfirmChoiceSelected = true;
                    FocusRevision++;
                    return true;
                case GamepadAction.Confirm:
                    if (IsConfirmChoiceSelected)
                        _ = ConfirmPendingAsync();
                    else
                        CancelConfirmation();
                    return true;
                case GamepadAction.Cancel:
                    CancelConfirmation();
                    return true;
                default:
                    return false;
            }
        }

        if (IsChoicePickerOpen)
        {
            switch (action)
            {
                case GamepadAction.NavigateLeft:
                case GamepadAction.NavigateUp:
                    MoveChoiceFocus(-1);
                    return true;
                case GamepadAction.NavigateRight:
                case GamepadAction.NavigateDown:
                    MoveChoiceFocus(1);
                    return true;
                case GamepadAction.Confirm:
                    SelectChoiceOption(FocusedChoiceIndex);
                    return true;
                case GamepadAction.Cancel:
                    CancelChoicePicker();
                    return true;
                default:
                    return false;
            }
        }

        // The left section rail is a focus column of its own: Up/Down move sections, Right/A return
        // to the content, and LB/RB still work as a shortcut.
        if (IsRailFocused)
        {
            switch (action)
            {
                case GamepadAction.NavigateUp:
                case GamepadAction.PreviousPlatform:
                    if (IsSetupMode)
                        SelectSetupStep(_setupIndex - 1);
                    else
                        MoveSection(-1);
                    return true;
                case GamepadAction.NavigateDown:
                case GamepadAction.NextPlatform:
                    if (IsSetupMode)
                        SelectSetupStep(_setupIndex + 1);
                    else
                        MoveSection(1);
                    return true;
                case GamepadAction.NavigateRight:
                case GamepadAction.Confirm:
                    ExitRailToContent();
                    return true;
                case GamepadAction.NavigateLeft:
                    return true;
                case GamepadAction.Cancel:
                    if (IsSetupMode)
                        BackSetup();
                    else
                        CloseRequested?.Invoke(false);
                    return true;
                case GamepadAction.Menu:
                    if (IsSetupMode)
                        _ = AdvanceSetupAsync();
                    else if (SaveRow is { } railSave)
                        _ = ActivateAsync(railSave);
                    return true;
                default:
                    return false;
            }
        }

        if (IsThemesSection)
        {
            switch (action)
            {
                case GamepadAction.PreviousPlatform:
                    MoveSection(-1);
                    return true;
                case GamepadAction.NextPlatform:
                    MoveSection(1);
                    return true;
                case GamepadAction.NavigateLeft:
                    // The ambient toggle (-1) and the first grid column step out to the section rail.
                    if (FocusedThemeIndex < 0 || FocusedThemeIndex % ThemeColumns == 0)
                        EnterRail();
                    else
                        MoveThemeFocus(-1, 0);
                    return true;
                case GamepadAction.NavigateRight:
                    if (FocusedThemeIndex >= 0)
                        MoveThemeFocus(1, 0);
                    return true;
                case GamepadAction.NavigateUp:
                    // Up walks the stack above the grid: top grid row -> ambient -> CRT, and stops.
                    if (FocusedThemeIndex == AmbientToggleIndex)
                        FocusedThemeIndex = CrtToggleIndex;
                    else if (FocusedThemeIndex == CrtToggleIndex)
                        return true;
                    else if (FocusedThemeIndex < ThemeColumns)
                        FocusedThemeIndex = AmbientToggleIndex;
                    else
                        MoveThemeFocus(0, -1);
                    return true;
                case GamepadAction.NavigateDown:
                    // Down reverses it, dropping off the ambient toggle into the selected theme.
                    if (FocusedThemeIndex == CrtToggleIndex)
                        FocusedThemeIndex = AmbientToggleIndex;
                    else if (FocusedThemeIndex == AmbientToggleIndex)
                        FocusedThemeIndex = Math.Max(0, IndexOfSelectedTheme());
                    else
                        MoveThemeFocus(0, 1);
                    return true;
                case GamepadAction.Confirm:
                    if (FocusedThemeIndex == CrtToggleIndex)
                        ToggleCrt();
                    else if (FocusedThemeIndex == AmbientToggleIndex)
                        ToggleAmbient();
                    else
                        _ = ApplyFocusedThemeAsync();
                    return true;
                case GamepadAction.Cancel:
                    CloseRequested?.Invoke(false);
                    return true;
                case GamepadAction.Menu:
                    if (SaveRow is { } themeSave)
                        _ = ActivateAsync(themeSave);
                    return true;
                default:
                    return false;
            }
        }

        if (IsSetupMode)
        {
            // Steps are walked with START/B only; LB/RB and Left-to-rail are swallowed so nothing jumps.
            switch (action)
            {
                case GamepadAction.PreviousPlatform:
                case GamepadAction.NextPlatform:
                    return true;
                case GamepadAction.Cancel:
                    BackSetup();
                    return true;
                case GamepadAction.Menu:
                    _ = AdvanceSetupAsync();
                    return true;
            }
        }

        switch (action)
        {
            case GamepadAction.PreviousPlatform:
                MoveSection(-1);
                return true;
            case GamepadAction.NextPlatform:
                MoveSection(1);
                return true;
            case GamepadAction.NavigateUp:
                MoveFocus(-1);
                return true;
            case GamepadAction.NavigateDown:
                MoveFocus(1);
                return true;
            case GamepadAction.NavigateLeft:
                // Choice rows are symmetric: Left/Right make quick changes and A opens the complete
                // list. Other row types retain Left-to-rail navigation.
                if (FocusedRow?.IsChoice == true)
                    AdjustFocused(-1);
                else
                    EnterRail();
                return true;
            case GamepadAction.NavigateRight:
                AdjustFocused(1);
                return true;
            case GamepadAction.Confirm:
                _ = ActivateFocusedAsync();
                return true;
            case GamepadAction.Actions:
                // Y runs the focused row's secondary action (rescan a platform from its summary, forget a
                // folder, grant the Shizuku privilege). Rows without one leave Y unhandled.
                if (FocusedRow is { HasSecondary: true } secondary)
                {
                    _ = ActivateSecondaryAsync(secondary);
                    return true;
                }
                return false;
            case GamepadAction.Cancel:
                CloseRequested?.Invoke(false);
                return true;
            case GamepadAction.Menu:
                if (SaveRow is { } save)
                    _ = ActivateAsync(save);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Legend text for Y on the focused row, or empty when the row offers no Y action.</summary>
    public string ActionsHint =>
        IsNormal && !IsRailFocused && !IsThemesSection && FocusedRow is { HasSecondary: true } row
            ? row.SecondaryLabel
            : string.Empty;

    public bool HasActionsHint => !string.IsNullOrEmpty(ActionsHint);

    private async Task ActivateSecondaryAsync(GamepadSettingsRowViewModel row)
    {
        if (!IsNormal || row.SecondaryActivate is null || !row.IsEnabled)
            return;

        if (row.SecondaryIsDestructive)
        {
            _pendingConfirmation = row.SecondaryActivate;
            ConfirmationTitle = row.SecondaryConfirmationTitle ?? row.SecondaryLabel;
            ConfirmationText = row.SecondaryConfirmationText ?? "Continue with this action?";
            IsConfirmChoiceSelected = false;
            IsConfirmationOpen = true;
            OnModalStateChanged();
            return;
        }

        await row.SecondaryActivate();
        RebuildRows(row.Key);
    }

    private void EnterRail()
    {
        if (!IsNormal)
            return;
        IsRailFocused = true;
        FocusRevision++;
    }

    private void ExitRailToContent()
    {
        IsRailFocused = false;
        if (IsThemesSection)
        {
            if (FocusedThemeIndex < 0 || FocusedThemeIndex >= _themeChoices.Count)
                FocusedThemeIndex = Math.Max(0, IndexOfSelectedTheme());
            UpdateThemeFocus();
        }
        FocusRevision++;
    }

    public void RequestOnScreenKeyboard()
    {
        if (!IsTextEntryOpen)
            return;

        _onScreenKeyboard.TryShow(new OnScreenKeyboardRequest(TextEntryTitle, IsSecretEntry));
    }

    /// <summary>The rail's page order. It mirrors Desktop's section order exactly, including where the
    /// Themes gallery sits — immediately before About, Desktop's always-last section — rather than
    /// appended after it. Themes is the one page that is not a projected-row <see cref="Sections"/>
    /// entry, so it is spliced back in here and only when the controller has theme choices
    /// (<see cref="ShowThemes"/>).</summary>
    private IReadOnlyList<SettingsSection> Pages
    {
        get
        {
            if (!ShowThemes)
                return Sections;
            var pages = Sections.ToList();
            // Desktop places Themes right before About; match that slot instead of appending at the end.
            var about = pages.IndexOf(SettingsSection.About);
            pages.Insert(about >= 0 ? about : pages.Count, SettingsSection.Themes);
            return pages;
        }
    }

    public void MoveSection(int delta)
    {
        if (!IsNormal)
            return;

        var pageCount = Pages.Count;
        if (pageCount == 0)
            return;

        var current = CurrentPageIndex();
        var target = Math.Clamp(current + Math.Sign(delta), 0, pageCount - 1);
        if (target == current)
            return;

        SelectPage(target);
    }

    private int CurrentPageIndex()
    {
        var target = IsThemesSection ? SettingsSection.Themes : SelectedSection;
        var index = Pages.ToList().IndexOf(target);
        return index < 0 ? 0 : index;
    }

    private void SelectPage(int index)
    {
        RememberFocusedRow();
        var pages = Pages;
        var target = pages[Math.Clamp(index, 0, pages.Count - 1)];
        if (target == SettingsSection.Themes)
        {
            EnterThemes();
            return;
        }

        if (IsThemesSection)
            IsThemesSection = false;
        SelectedSection = target;
    }

    private void EnterThemes()
    {
        if (!ShowThemes)
            return;
        IsThemesSection = true;
        var selected = IndexOfSelectedTheme();
        FocusedThemeIndex = selected >= 0 ? selected : 0;
        UpdateThemeFocus();
        FocusRevision++;
    }

    [RelayCommand]
    private void SelectSection(SettingsSection section)
    {
        if (IsNormal && Sections.Contains(section))
        {
            RememberFocusedRow();
            if (IsThemesSection)
                IsThemesSection = false;
            SelectedSection = section;
        }
    }

    [RelayCommand]
    private void SelectThemes()
    {
        if (IsNormal && ShowThemes)
        {
            RememberFocusedRow();
            EnterThemes();
        }
    }

    public void MoveThemeFocus(int deltaX, int deltaY)
    {
        if (!IsNormal || !IsThemesSection || _themeChoices.Count == 0)
            return;

        var index = Math.Clamp(FocusedThemeIndex, 0, _themeChoices.Count - 1);
        if (deltaX != 0)
        {
            // Clamp at real row edges rather than wrapping, mirroring the library grid contract.
            var column = index % ThemeColumns;
            var newColumn = column + Math.Sign(deltaX);
            if (newColumn < 0 || newColumn >= ThemeColumns)
                return;
            var target = index - column + newColumn;
            if (target >= _themeChoices.Count)
                return;
            FocusedThemeIndex = target;
        }
        else if (deltaY != 0)
        {
            var target = index + Math.Sign(deltaY) * ThemeColumns;
            if (target < 0 || target >= _themeChoices.Count)
                return;
            FocusedThemeIndex = target;
        }
    }

    [RelayCommand]
    private Task SelectThemeChoiceAsync(ThemeChoiceViewModel choice)
    {
        var index = _themeChoices.ToList().IndexOf(choice);
        if (index < 0)
            return Task.CompletedTask;
        if (!IsThemesSection)
            EnterThemes();
        FocusedThemeIndex = index;
        UpdateThemeFocus();
        return ApplyThemeAsync(choice);
    }

    private Task ApplyFocusedThemeAsync()
    {
        if (_themeChoices.Count == 0)
            return Task.CompletedTask;
        return ApplyThemeAsync(_themeChoices[Math.Clamp(FocusedThemeIndex, 0, _themeChoices.Count - 1)]);
    }

    private async Task ApplyThemeAsync(ThemeChoiceViewModel choice)
    {
        if (_applyTheme is null)
            return;
        await _applyTheme(choice.Id);
        FocusRevision++;
    }

    private int IndexOfSelectedTheme() => _themeChoices.ToList().FindIndex(choice => choice.IsSelected);

    private void UpdateThemeFocus()
    {
        for (var index = 0; index < _themeChoices.Count; index++)
            _themeChoices[index].IsFocused = IsThemesSection && index == FocusedThemeIndex;
    }

    public void MoveFocus(int delta)
    {
        if (!IsNormal || Rows.Count == 0)
            return;

        var step = Math.Sign(delta);
        if (step == 0)
            return;

        // Step over non-focusable group headers; if there is no focusable row ahead, stay put.
        var index = FocusedRowIndex + step;
        while (index >= 0 && index < Rows.Count && Rows[index].IsHeader)
            index += step;
        if (index < 0 || index >= Rows.Count)
            return;

        FocusedRowIndex = index;
    }

    public void AdjustFocused(int delta)
    {
        if (!IsNormal || FocusedRow?.Adjust is not { } adjust || FocusedRow.IsEnabled == false)
            return;

        adjust(Math.Sign(delta));
        RebuildRows(FocusedRow.Key);
    }

    public Task ActivateFocusedAsync() => FocusedRow is { } row
        ? ActivateAsync(row)
        : Task.CompletedTask;

    public async Task FocusAndActivateAsync(GamepadSettingsRowViewModel row)
    {
        var index = Rows.IndexOf(row);
        if (index < 0)
            return;

        FocusedRowIndex = index;
        await ActivateAsync(row);
    }

    private async Task ActivateAsync(GamepadSettingsRowViewModel row)
    {
        if (!IsNormal || !row.CanActivate || row.Activate is null)
            return;

        if (row.IsDestructive)
        {
            _pendingConfirmation = row.Activate;
            ConfirmationTitle = row.ConfirmationTitle ?? row.Label;
            ConfirmationText = row.ConfirmationText ?? "Continue with this action?";
            IsConfirmChoiceSelected = false;
            IsConfirmationOpen = true;
            OnModalStateChanged();
            return;
        }

        await row.Activate();
        RebuildRows(row.Key);
        FocusRevision++;
    }

    private void BeginTextEntry(
        string title,
        string description,
        string value,
        bool isSecret,
        Action<string> commit)
    {
        TextEntryTitle = title;
        TextEntryDescription = description;
        DraftText = value;
        IsSecretEntry = isSecret;
        RevealSecret = false;
        _commitText = commit;
        IsTextEntryOpen = true;
        TextEntryRevision++;
        OnModalStateChanged();
    }

    private void OpenChoicePicker(
        string rowKey,
        string title,
        string description,
        string value,
        IReadOnlyList<string> choices,
        Action<string> commit)
    {
        if (choices.Count == 0)
            return;

        _choicePickerRowKey = rowKey;
        _commitChoice = commit;
        ChoicePickerTitle = title;
        ChoicePickerDescription = description;
        ChoiceOptions.Clear();
        var selected = choices.ToList().IndexOf(value);
        if (selected < 0)
            selected = 0;
        for (var index = 0; index < choices.Count; index++)
        {
            ChoiceOptions.Add(new GamepadChoiceOptionViewModel(
                this,
                index,
                choices[index],
                index == selected));
        }

        FocusedChoiceIndex = selected;
        IsChoicePickerOpen = true;
        UpdateChoiceOptionFocus();
        OnModalStateChanged();
        FocusRevision++;
    }

    private void MoveChoiceFocus(int delta)
    {
        if (!IsChoicePickerOpen || ChoiceOptions.Count == 0)
            return;
        FocusedChoiceIndex = Math.Clamp(
            FocusedChoiceIndex + Math.Sign(delta),
            0,
            ChoiceOptions.Count - 1);
    }

    partial void OnFocusedChoiceIndexChanged(int value)
    {
        UpdateChoiceOptionFocus();
        if (IsChoicePickerOpen)
            FocusRevision++;
    }

    private void UpdateChoiceOptionFocus()
    {
        for (var index = 0; index < ChoiceOptions.Count; index++)
            ChoiceOptions[index].IsFocused = IsChoicePickerOpen && index == FocusedChoiceIndex;
    }

    internal void SelectChoiceOption(int index)
    {
        if (!IsChoicePickerOpen || index < 0 || index >= ChoiceOptions.Count)
            return;

        var value = ChoiceOptions[index].DisplayName;
        var rowKey = _choicePickerRowKey;
        RunLocalEdit(() => _commitChoice?.Invoke(value));
        CloseChoicePicker();
        RebuildRows(rowKey);
        FocusRevision++;
    }

    public void CancelChoicePicker()
    {
        if (!IsChoicePickerOpen)
            return;
        CloseChoicePicker();
        FocusRevision++;
    }

    [RelayCommand]
    private void DismissChoicePicker() => CancelChoicePicker();

    private void CloseChoicePicker()
    {
        _commitChoice = null;
        _choicePickerRowKey = null;
        IsChoicePickerOpen = false;
        ChoicePickerTitle = string.Empty;
        ChoicePickerDescription = string.Empty;
        ChoiceOptions.Clear();
        OnModalStateChanged();
    }

    public void CommitTextEntry()
    {
        if (!IsTextEntryOpen)
            return;

        RunLocalEdit(() => _commitText?.Invoke(DraftText));
        CloseTextEntry();
        RebuildRows();
    }

    public void CancelTextEntry()
    {
        if (!IsTextEntryOpen)
            return;

        CloseTextEntry();
    }

    [RelayCommand]
    private void SaveTextEntry() => CommitTextEntry();

    [RelayCommand]
    private void DismissTextEntry() => CancelTextEntry();

    private void CloseTextEntry()
    {
        DraftText = string.Empty;
        _commitText = null;
        IsSecretEntry = false;
        RevealSecret = false;
        IsTextEntryOpen = false;
        OnModalStateChanged();
        FocusRevision++;
    }

    private async Task ConfirmPendingAsync()
    {
        var action = _pendingConfirmation;
        CancelConfirmation();
        if (action is not null)
            await action();
        RebuildRows();
        FocusRevision++;
    }

    public void CancelConfirmation()
    {
        _pendingConfirmation = null;
        IsConfirmationOpen = false;
        IsConfirmChoiceSelected = false;
        OnModalStateChanged();
        FocusRevision++;
    }

    [RelayCommand]
    private void ChooseConfirmationCancel() => CancelConfirmation();

    [RelayCommand]
    private Task ChooseConfirmationConfirmAsync() => ConfirmPendingAsync();

    private void OnModalStateChanged()
    {
        OnPropertyChanged(nameof(IsNormal));
        OnPropertyChanged(nameof(IsChoiceRowFocused));
        OnPropertyChanged(nameof(ActionsHint));
        OnPropertyChanged(nameof(HasActionsHint));
        OnPropertyChanged(nameof(KeyboardHint));
        OnPropertyChanged(nameof(IsRowsVisible));
        OnPropertyChanged(nameof(IsThemesVisible));
    }

    partial void OnIsThemesSectionChanged(bool value)
    {
        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(SectionDescription));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(IsWorkingInSection));
        OnPropertyChanged(nameof(IsGeneralSection));
        OnPropertyChanged(nameof(IsEmulatorsSection));
        OnPropertyChanged(nameof(IsHotkeysSection));
        OnPropertyChanged(nameof(IsRetroAchievementsSection));
        OnPropertyChanged(nameof(IsArtworkMetadataSection));
        OnPropertyChanged(nameof(IsSavesSection));
        OnPropertyChanged(nameof(IsTexturePacksSection));
        OnPropertyChanged(nameof(IsAboutSection));
        OnPropertyChanged(nameof(IsRowsVisible));
        OnPropertyChanged(nameof(IsThemesVisible));
        OnPropertyChanged(nameof(IsAmbientToggleFocused));
        OnPropertyChanged(nameof(IsCrtToggleFocused));
        UpdateThemeFocus();
        FocusRevision++;
    }

    partial void OnFocusedThemeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsAmbientToggleFocused));
        OnPropertyChanged(nameof(IsCrtToggleFocused));
        UpdateThemeFocus();
        FocusRevision++;
    }

    /// <summary>Toggles the ambient (cover-art recolour) setting from the Themes view; also lands
    /// focus on the toggle so a pointer click and a controller press read the same.</summary>
    [RelayCommand]
    private void ToggleAmbient()
    {
        if (!IsThemesSection)
            return;
        FocusedThemeIndex = AmbientToggleIndex;
        AmbientThemeFromArtwork = !AmbientThemeFromArtwork;
    }

    /// <summary>Toggles the CRT presentation from the Themes view; also lands focus on the toggle so
    /// a pointer click and a controller press read the same.</summary>
    [RelayCommand]
    private void ToggleCrt()
    {
        if (!IsThemesSection)
            return;
        FocusedThemeIndex = CrtToggleIndex;
        CrtScreenEffect = !CrtScreenEffect;
    }

    partial void OnSelectedSectionChanged(SettingsSection value)
    {
        _synchronizingSection = true;
        try
        {
            _settings.SelectedSection = value;
        }
        finally
        {
            _synchronizingSection = false;
        }
        OnPropertyChanged(nameof(IsGeneralSection));
        OnPropertyChanged(nameof(IsEmulatorsSection));
        OnPropertyChanged(nameof(IsHotkeysSection));
        OnPropertyChanged(nameof(IsRetroAchievementsSection));
        OnPropertyChanged(nameof(IsArtworkMetadataSection));
        OnPropertyChanged(nameof(IsSavesSection));
        OnPropertyChanged(nameof(IsTexturePacksSection));
        OnPropertyChanged(nameof(IsAboutSection));
        RebuildRows(_focusedRowBySection.GetValueOrDefault(value));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(IsWorkingInSection));
        FocusRevision++;
    }

    partial void OnFocusedRowIndexChanged(int value)
    {
        for (var index = 0; index < Rows.Count; index++)
            Rows[index].IsFocused = index == value;
        RememberFocusedRow();
        OnPropertyChanged(nameof(FocusedRow));
        OnPropertyChanged(nameof(IsChoiceRowFocused));
        OnPropertyChanged(nameof(ActionsHint));
        OnPropertyChanged(nameof(HasActionsHint));
        FocusRevision++;
    }

    private void RebuildRows(string? preferredKey = null)
    {
        if (_disposed)
            return;

        preferredKey ??= FocusedRow?.Key ?? _focusedRowBySection.GetValueOrDefault(SelectedSection);
        var specs = BuildRows().ToArray();
        var sameShape = Rows.Count == specs.Length && Rows.Select(row => row.Key).SequenceEqual(specs.Select(spec => spec.Key));
        if (sameShape)
        {
            for (var index = 0; index < specs.Length; index++)
                Rows[index].Apply(specs[index]);
        }
        else
        {
            Rows.Clear();
            foreach (var spec in specs)
                Rows.Add(new GamepadSettingsRowViewModel(this, spec));
        }

        var list = Rows.ToList();
        var target = preferredKey is null
            ? -1
            : list.FindIndex(row => row.Key == preferredKey && !row.IsHeader);
        if (target < 0)
            target = list.FindIndex(row => !row.IsHeader && row.Key != "common.save");
        if (target < 0)
            target = list.FindIndex(row => !row.IsHeader);
        FocusedRowIndex = Rows.Count == 0 ? 0 : target >= 0 ? target : 0;
        for (var index = 0; index < Rows.Count; index++)
            Rows[index].IsFocused = index == FocusedRowIndex;
        RememberFocusedRow();
        OnPropertyChanged(nameof(FocusedRow));
        OnPropertyChanged(nameof(IsChoiceRowFocused));
        OnPropertyChanged(nameof(ActionsHint));
        OnPropertyChanged(nameof(HasActionsHint));
        OnPropertyChanged(nameof(SaveRow));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(IsWorkingInSection));
        NotifyRailStatuses();

        // A shape change replaced the Rows collection wholesale. The view renders these into an
        // ItemsRepeater that does not re-realize its virtualized children on a bare collection reset;
        // it only relays out when a FocusRevision bump drives RevealGamepadOverlayFocus. D-pad rebuilds
        // bump it themselves, but async rebuilds (e.g. a folder import refreshing a platform's remembered
        // folders) reach RebuildRows through OnSettingsPropertyChanged, which does not — so without this
        // the new rows stay blank until the section is re-entered. Bump here so every structural rebuild
        // forces the repeater to re-lay-out, regardless of what triggered it.
        if (!sameShape)
            FocusRevision++;
    }

    private IEnumerable<GamepadSettingsRowSpec> BuildRows()
    {
        if (IsSetupMode)
        {
            // No "Save and close" row: the wizard moves with START (the rail chip), and Finish is the save.
            foreach (var row in BuildSetupRows())
                yield return row;
            yield break;
        }

        // Keep Save one D-pad step above the first section-specific row. Some sections can contain
        // dozens of platform rows, so placing it at the tail would make committing a small change
        // require traversing the entire inventory.
        yield return ActionRow(
            "common.save",
            "Save and close",
            "Keep these changes and return to Menu.",
            _settings.IsSaving ? "Saving…" : "A SAVE",
            _settings.SaveCommand,
            !_settings.IsWorking);

        foreach (var row in SelectedSection switch
        {
            SettingsSection.Emulators => BuildEmulatorsRows(),
            SettingsSection.Hotkeys => BuildHotkeysRows(),
            SettingsSection.RetroAchievements => BuildRetroAchievementsRows(),
            SettingsSection.ArtworkMetadata => BuildArtworkMetadataRows(),
            SettingsSection.Saves => BuildSaveRows(),
            SettingsSection.TexturePacks => BuildTextureRows(),
            SettingsSection.About => BuildAboutRows(),
            _ => BuildGeneralRows(),
        })
        {
            yield return row;
        }
    }

    /// <summary>The update rows' hint text. While a download runs, the shared coordinator's StatusText
    /// carries the live percentage ("Downloading update… 42%"), so prefer it over the static line the
    /// Desktop view model sets once at kickoff; checks and idle state fall back to that static text.</summary>
    private string UpdateStatusHint =>
        _settings.Updates is { IsBusy: true } updates && !string.IsNullOrWhiteSpace(updates.StatusText)
            ? updates.StatusText
            : _settings.UpdateStatusText;

    private IEnumerable<GamepadSettingsRowSpec> BuildGeneralRows()
    {
        if (_runSetupCommand is not null)
        {
            yield return ActionRow(
                "general.run-setup",
                "Run setup again",
                "Walk through the first-run steps again: second screen, closing games, games and emulators, saves.",
                "A OPEN",
                _runSetupCommand,
                !_settings.IsWorking,
                excludeFromParity: true) with { IsCompact = true };
        }

        yield return ToggleRow(
            "general.empty-platforms",
            "Empty platforms",
            "Empty platforms stay available when adding games and configuring emulators. Platforms with temporarily unavailable games remain visible.",
            _settings.ShowEmptyPlatforms,
            value => _settings.ShowEmptyPlatforms = value,
            onLabel: "SHOW",
            offLabel: "HIDE");
        yield return ActionRow(
            "general.rescan",
            "Rescan all consoles",
            "Recheck every console's remembered folders. Per-platform rescans and folder management live in the Emulators section.",
            _settings.IsMaintainingLibrary ? "WORKING" : "A RESCAN",
            _settings.RescanAllCommand,
            _settings.CanRescanAll);
        // Mirrors Desktop's general.open-data-folder so a controller can reach the portable data
        // folder too, and so the two surfaces' general.* field sets stay in parity. Skipped where no
        // OS file manager can open the path (Android), so the row never offers a button that only fails.
        if (_settings.HasDataDirectory && _settings.CanRevealFiles)
        {
            yield return ActionRow(
                "general.open-data-folder",
                "Open data folder",
                "Your library database, covers, settings, and saves live here. EmuShelf never touches your game files.",
                "A OPEN",
                _settings.OpenDataFolderCommand,
                enabled: true);
        }
        // Android has no file manager to open the folder in, so instead of "Open data folder" it offers a
        // way to move it: the folder is a user-chosen shared-storage path. Choosing a new one persists the
        // pointer and restarts; the old data is left where it is. Surface-specific, so excluded from parity.
        if (_settings.CanChangeDataFolder)
        {
            var location = _settings.HasDataDirectory
                ? $" It's at {_settings.DataDirectory} now."
                : string.Empty;
            yield return ActionRow(
                "general.change-data-folder",
                "Data folder",
                "Your library database, covers, settings, saves, and downloaded artwork live here — EmuShelf "
                    + "never touches your game files." + location + " Choose a different folder to move it; "
                    + "EmuShelf restarts, and your existing data stays where it is.",
                "A CHANGE",
                _settings.ChangeDataFolderCommand,
                // Gate on the broad IsBusy, not just IsWorking: activating this restarts the process, and
                // tearing down mid cloud-sync / RetroAchievements / texture work would race those in-flight
                // writes under the data folder — the same reason Save/Cancel gate on IsBusy.
                enabled: !_settings.IsBusy,
                excludeFromParity: true);
        }
    }

    /// <summary>
    /// Per-platform Android emulator choices and library actions, grouped under each platform's header.
    /// Executable paths and launch arguments stay Desktop-only. Android RetroArch core paths are the
    /// exception: the Android head has no Desktop mode, so it projects standalone apps and the known
    /// compatible RetroArch core filenames supplied by the Android composition path as one flat picker.
    /// The remaining rows cover PS3 sync (the one platform "Rescan all" skips), per-platform rescan,
    /// and remembered-folder management.
    /// </summary>
    /// <summary>
    /// The close-on-return toggle. The description reports the one thing that can silently break this
    /// setting (the Shizuku grant) instead of explaining what Shizuku is; Y requests the grant right here.
    /// Shared by the Emulators section and the wizard's Closing games step, which words it in full.
    /// </summary>
    private GamepadSettingsRowSpec CloseOnReturnRow(string label, string description, bool compact)
    {
        var warning = CloseOnReturnWarning;
        return ToggleRow(
            "emulators.close-on-return",
            label,
            warning ?? description,
            CloseEmulatorOnReturn,
            value => CloseEmulatorOnReturn = value,
            onLabel: "CLOSE",
            offLabel: "KEEP") with
        {
            IsCompact = compact,
            IsWarning = warning is not null,
            SecondaryLabel = warning is not null && _grantCloseOnReturnPrivilege is not null ? "Allow Shizuku" : null,
            // Requesting the grant only raises Shizuku's dialog; the answer lands later. Drop the cached
            // reading so the rebuild that follows re-asks, which covers the case where the permission was
            // already granted. The grant made in that dialog is picked up by RefreshDeviceState on return.
            SecondaryActivate = warning is not null && _grantCloseOnReturnPrivilege is not null
                ? async () =>
                {
                    await _grantCloseOnReturnPrivilege();
                    _closeOnReturnWarningRead = false;
                }
                : null,
        };
    }

    private IEnumerable<GamepadSettingsRowSpec> BuildEmulatorsRows()
    {
        // In the wizard this setting has its own step (Closing games), so the list holds only platforms.
        if (_settings.HasCloseEmulatorOnReturn && !IsSetupMode)
        {
            yield return CloseOnReturnRow(
                "Close emulator on return",
                "Force-stop the game's emulator when you come back, so it stops draining the battery.",
                compact: true);
        }

        foreach (var row in _settings.Rows)
        {
            var missing = EmulatorMissingFor(row);
            var expanded = string.Equals(row.SystemId, _expandedSystemId, StringComparison.Ordinal);
            yield return SummaryRow(row, missing, expanded);
            if (!expanded)
                continue;

            if (_androidEmulatorChoices.TryGetValue(row.SystemId, out var choices) && choices.Count > 0)
                yield return AndroidEmulatorChoiceRow(row, missing);
            if (OperatingSystem.IsAndroid() && !row.IsDualScreen)
                yield return LaunchScreenChoiceRow(row);
            if (row.HasSyncLibrary && !OperatingSystem.IsAndroid())
            {
                yield return ActionRow(
                    row.SyncFieldId,
                    "Sync RPCS3 library",
                    "Read the RPCS3 game list to import PlayStation 3 titles.",
                    _settings.IsMaintainingLibrary ? "WORKING" : "A SYNC",
                    row.SyncLibraryCommand,
                    row.CanSyncLibrary,
                    isGrouped: true,
                    systemId: row.SystemId) with { IsCompact = true };
            }
            if (row.HasFolderManagement)
            {
                foreach (var folder in row.LibraryFolders)
                {
                    // The folder row is the rescan: A rechecks it (the platform's remembered folders),
                    // Y forgets it. Its line carries the availability and the platform's last scan result.
                    yield return ActionRow(
                        $"emulators.{row.SystemId}.folder.{folder.Id}",
                        folder.Path,
                        FolderDescription(row, folder),
                        _settings.IsMaintainingLibrary ? "WORKING" : "A RESCAN",
                        row.RescanLibraryCommand,
                        row.CanRescan,
                        isGrouped: true,
                        systemId: row.SystemId,
                        excludeFromParity: true) with
                    {
                        IsCompact = true,
                        IsWarning = folder.IsMissing,
                        SecondaryLabel = row.CanManageLibraryFolders ? "Forget folder" : null,
                        SecondaryActivate = row.CanManageLibraryFolders ? () => ExecuteAsync(folder.ForgetCommand) : null,
                        SecondaryIsDestructive = true,
                        SecondaryConfirmationTitle = "Forget this folder?",
                        SecondaryConfirmationText = "EmuShelf stops rescanning this folder. Games already imported and the files on disk are left untouched.",
                    };
                }
                yield return ActionRow(
                    row.AddFolderFieldId,
                    "Add game folder",
                    string.Empty,
                    "A ADD FOLDER",
                    row.AddLibraryFolderCommand,
                    row.CanManageLibraryFolders,
                    isGrouped: true,
                    systemId: row.SystemId) with { IsCompact = true };
            }
            else if (row.HasRescanLibrary)
            {
                yield return ActionRow(
                    row.RescanFieldId,
                    "Rescan library",
                    "Recheck this console's remembered folders for added or removed games.",
                    _settings.IsMaintainingLibrary ? "WORKING" : "A RESCAN",
                    row.RescanLibraryCommand,
                    row.CanRescan,
                    isGrouped: true,
                    systemId: row.SystemId) with { IsCompact = true };
            }
        }
    }

    private GamepadSettingsRowSpec SummaryRow(EmulatorSettingsRowViewModel row, string? missing, bool expanded)
    {
        var parts = new List<string>();
        if (missing is not null)
            parts.Add($"{missing} not installed");
        else if (row.HasEmulatorChoices)
            parts.Add((row.SelectedChoice ?? row.AvailableChoices[0]).DisplayName);
        else if (!string.IsNullOrWhiteSpace(row.EmulatorName))
            parts.Add(row.EmulatorName);
        if (_gameCountBySystem is not null)
            parts.Add(FormatGames(_gameCountBySystem(row.SystemId)));
        if (!string.IsNullOrWhiteSpace(row.MaintenanceStatusText))
            parts.Add(row.MaintenanceStatusText);

        var key = $"emulators.{row.SystemId}.summary";
        return new GamepadSettingsRowSpec(
            key,
            row.SystemName,
            string.Empty,
            string.Join(" · ", parts),
            GamepadSettingsRowKind.Summary,
            IsEnabled: true,
            Activate: () =>
            {
                _expandedSystemId = expanded ? null : row.SystemId;
                return Task.CompletedTask;
            },
            SystemId: row.SystemId,
            ExcludeFromParity: true,
            IsExpanded: expanded,
            IsWarning: missing is not null,
            IsCompact: true,
            SecondaryLabel: row.CanRescan ? "Rescan" : null,
            SecondaryActivate: row.CanRescan ? () => ExecuteAsync(row.RescanLibraryCommand) : null);
    }

    private static string FolderDescription(EmulatorSettingsRowViewModel row, LibraryFolderRowViewModel folder)
    {
        var parts = new List<string> { folder.AvailabilityText };
        if (!string.IsNullOrWhiteSpace(row.MaintenanceStatusText))
            parts.Add(row.MaintenanceStatusText);
        return string.Join(" · ", parts);
    }

    private GamepadSettingsRowSpec LaunchScreenChoiceRow(EmulatorSettingsRowViewModel row)
    {
        var labels = EmulatorSettingsRowViewModel.LaunchScreenOrder
            .Select(EmulatorSettingsRowViewModel.LaunchScreenLabel)
            .ToList();
        return ChoiceRow(
            $"emulators.{row.SystemId}.launch-screen",
            "Launch screen",
            "When a second screen is connected · “Ask each time” shows a chooser at launch",
            EmulatorSettingsRowViewModel.LaunchScreenLabel(row.LaunchScreen),
            labels,
            selected => row.LaunchScreen = EmulatorSettingsRowViewModel.LaunchScreenOrder
                .First(screen => string.Equals(
                    EmulatorSettingsRowViewModel.LaunchScreenLabel(screen), selected, StringComparison.Ordinal)),
            isGrouped: true,
            systemId: row.SystemId,
            excludeFromParity: true) with { IsCompact = true };
    }

    private GamepadSettingsRowSpec AndroidEmulatorChoiceRow(EmulatorSettingsRowViewModel row, string? missing)
    {
        var choices = row.AvailableChoices.Select(choice => choice.DisplayName).ToList();
        var value = row.SelectedChoice?.DisplayName ?? choices[0];
        return ChoiceRow(
            $"emulators.{row.SystemId}.emulator",
            "Emulator",
            missing is not null
                ? $"{missing} is not installed on this device · pick another or install it first"
                : "Standalone apps and RetroArch cores are equal choices · install the app or core first",
            value,
            choices,
            selected =>
            {
                var choice = row.AvailableChoices.First(candidate =>
                    string.Equals(candidate.DisplayName, selected, StringComparison.Ordinal));
                row.SelectedChoice = choice;
            },
            isGrouped: true,
            systemId: row.SystemId,
            excludeFromParity: true) with { IsCompact = true, IsWarning = missing is not null };
    }

    /// <summary>
    /// Hotkeys is a per-emulator × per-action matrix that a controller can't navigate as a flat row
    /// list, so — like Themes — the section is an entry point: a read-only scheme summary plus a row
    /// that opens the controller-native <c>GamepadHotkeysViewModel</c> overlay. B returns to Settings.
    /// </summary>
    private IEnumerable<GamepadSettingsRowSpec> BuildHotkeysRows()
    {
        yield return InformationRow(
            "hotkeys.scheme",
            "In-game hotkey scheme",
            "One keyboard scheme (rewind, fast-forward, save, load, close) is written into each emulator; a Steam Input preset maps it to controller chords.",
            _settings.HotkeySchemeSummary);
        if (_openHotkeys is not null)
        {
            yield return new GamepadSettingsRowSpec(
                "hotkeys.open",
                "Open hotkey editor",
                "Apply the scheme per emulator, install the Steam Input template, and see each emulator's applied status.",
                "A OPEN",
                GamepadSettingsRowKind.Action,
                Activate: () => _openHotkeys(),
                ExcludeFromParity: true);
        }
    }

    /// <summary>
    /// Read-only build info plus the in-place update actions. Desktop keeps the same content in its
    /// About section; the update actions matter on a controller because an AppImage/Deck update
    /// re-execs in place, so installing from gaming mode never drops to the desktop.
    /// </summary>
    private IEnumerable<GamepadSettingsRowSpec> BuildAboutRows()
    {
        yield return InformationRow(
            "about.version",
            "Version",
            "Follows the newest release tag on GitHub.",
            _settings.AppVersionDisplay);
        yield return InformationRow(
            "about.commit",
            "Last commit",
            "The exact source this build was compiled from.",
            _settings.AppCommitDisplay);
        if (_settings.HasCommitDate)
        {
            yield return InformationRow(
                "about.commit-date",
                "Committed",
                string.Empty,
                _settings.AppCommitDateDisplay);
        }
        if (_settings.HasUpdateChecker)
        {
            yield return ActionRow(
                "about.check-updates",
                "Check for updates",
                !string.IsNullOrWhiteSpace(UpdateStatusHint)
                    ? UpdateStatusHint
                    : "Look on GitHub for a newer EmuShelf. Only the public releases page is contacted.",
                _settings.IsUpdateBusy ? "WORKING" : "A CHECK",
                _settings.CheckForUpdatesCommand,
                !_settings.IsUpdateBusy,
                excludeFromParity: true);
            if (_settings.IsUpdateAvailable)
            {
                yield return ActionRow(
                    "about.install-update",
                    "Install update",
                    "Download the new version and restart. On the Steam Deck this stays in gaming mode.",
                    _settings.IsUpdateBusy ? "WORKING" : "A UPDATE",
                    _settings.InstallUpdateCommand,
                    !_settings.IsUpdateBusy,
                    excludeFromParity: true);
            }
        }
    }

    private IEnumerable<GamepadSettingsRowSpec> BuildRetroAchievementsRows()
    {
        if (_settings.IsRetroAchievementsConnected)
        {
            yield return HeaderRow("retro.account-header", "RetroAchievements account");
            yield return InformationRow(
                "retro.account",
                "Connected account",
                "Achievement data is display-only in EmuShelf.",
                _settings.ConnectedAccountName ?? string.Empty,
                isGrouped: true);
            if (_settings.HasRetroAchievementsMatchRefresh)
            {
                yield return ActionRow(
                    "retro.refresh",
                    "Refresh game matches",
                    "Refresh catalogues and retry known games without rehashing unchanged ROMs.",
                    _settings.IsRetroAchievementsBusy ? "WORKING" : "A REFRESH",
                    _settings.RefreshRetroAchievementsMatchesCommand,
                    _settings.CanRefreshRetroAchievementsMatches,
                    isGrouped: true);
            }
            yield return ActionRow(
                "retro.disconnect",
                "Disconnect RetroAchievements",
                "Remove the locally stored account connection. Earned achievements are not changed.",
                "A DISCONNECT",
                _settings.DisconnectRetroAchievementsCommand,
                !_settings.IsRetroAchievementsBusy,
                isDestructive: true,
                confirmationTitle: "Disconnect RetroAchievements?",
                confirmationText: "EmuShelf will remove its saved account connection. Your RetroAchievements account and earned progress stay untouched.",
                isGrouped: true);
            yield break;
        }

        // Group the credentials under a sign-in header so username / key / Connect read as one unit
        // instead of three rows identical to every other setting.
        yield return HeaderRow("retro.signin-header", "Sign in to RetroAchievements");
        yield return TextRow(
            "retro.username",
            "Username",
            "Your RetroAchievements account name.",
            _settings.RetroAchievementsUsername,
            false,
            value => _settings.RetroAchievementsUsername = value,
            isGrouped: true);
        yield return TextRow(
            "retro.api-key",
            "Web API key",
            "From RetroAchievements Control Panel → Keys. It is masked, never logged, and never written to settings.json.",
            _settings.RetroAchievementsApiKey,
            true,
            value => _settings.RetroAchievementsApiKey = value,
            isGrouped: true);
        yield return ActionRow(
            "retro.connect",
            "Connect",
            "Validate the account, match your library, and fetch progress.",
            _settings.IsRetroAchievementsBusy ? "CONNECTING…" : "A CONNECT",
            _settings.ConnectRetroAchievementsCommand,
            !_settings.IsRetroAchievementsBusy,
            isGrouped: true);
    }

    private IEnumerable<GamepadSettingsRowSpec> BuildArtworkMetadataRows()
    {
        // Built-in catalogue: always available, no account needed. Same stable ids as Desktop's
        // built-in card so the two surfaces stay in parity.
        yield return HeaderRow("metadata.builtin-header", "Built-in catalogue");
        yield return ToggleRow(
            "general.metadata-auto",
            "Fetch after import",
            "Exact titles and covers from bundled catalogues are downloaded only after you opt in. Game files and paths are never uploaded.",
            _settings.AutomaticallyFetchMetadataAfterImport,
            value => _settings.AutomaticallyFetchMetadataAfterImport = value,
            onLabel: "AUTO",
            offLabel: "MANUAL",
            isGrouped: true);
        yield return ActionRow(
            "general.fetch-metadata",
            "Fetch missing metadata",
            "Fill missing titles and artwork for the current library after your metadata opt-in.",
            _settings.IsMaintainingLibrary ? "WORKING" : "A FETCH",
            _settings.FetchAllMetadataCommand,
            _settings.CanFetchAllMetadata,
            isGrouped: true);

        // Web image search: the manual "Set cover" picker toggle.
        yield return HeaderRow("metadata.web-header", "Web image search");
        yield return ToggleRow(
            "metadata.web-image-search",
            "Web image search",
            "Let the \"Set cover\" picker search the web (DuckDuckGo) for cover images. Results are unverified and never applied automatically — you always choose.",
            _settings.WebImageSearchEnabled,
            value => _settings.WebImageSearchEnabled = value,
            onLabel: "ON",
            offLabel: "OFF",
            isGrouped: true);

        // No standalone "ScreenScraper" header: it stacked directly above the "Sign in to ScreenScraper"
        // / "ScreenScraper account" sub-header below, so it only ate vertical space on the couch surface.
        // The sub-headers already name the provider.
        if (_settings.IsScreenScraperConnected)
        {
            yield return HeaderRow("scraper.account-header", "ScreenScraper account");
            yield return InformationRow(
                "scraper.account",
                "Connected account",
                "Titles and artwork are fetched on demand from the per-game scraper.",
                _settings.ScreenScraperConnectedName ?? string.Empty,
                isGrouped: true);
            yield return ActionRow(
                "scraper.disconnect",
                "Disconnect ScreenScraper",
                "Remove the locally stored login. Your ScreenScraper account is not changed.",
                "A DISCONNECT",
                _settings.DisconnectScreenScraperCommand,
                !_settings.IsScreenScraperBusy,
                isDestructive: true,
                confirmationTitle: "Disconnect ScreenScraper?",
                confirmationText: "EmuShelf will remove its saved login. Your ScreenScraper account stays untouched.",
                isGrouped: true);
            yield break;
        }

        yield return HeaderRow("scraper.signin-header", "Sign in to ScreenScraper");
        yield return TextRow(
            "scraper.username",
            "Username",
            "Your ScreenScraper account name.",
            _settings.ScreenScraperUsername,
            false,
            value => _settings.ScreenScraperUsername = value,
            isGrouped: true);
        yield return TextRow(
            "scraper.password",
            "Password",
            "Passed directly to ScreenScraper to sign in. It is masked, never logged, and never written to settings.json.",
            _settings.ScreenScraperPassword,
            true,
            value => _settings.ScreenScraperPassword = value,
            isGrouped: true);
        yield return ActionRow(
            "scraper.connect",
            "Connect",
            "Validate the account so per-game scraping can fetch titles and artwork.",
            _settings.IsScreenScraperBusy ? "CONNECTING…" : "A CONNECT",
            _settings.ConnectScreenScraperCommand,
            !_settings.IsScreenScraperBusy,
            isGrouped: true);
    }

    private IEnumerable<GamepadSettingsRowSpec> BuildSaveRows()
    {
        // Connection first: Connect (when disconnected) or the connected summary + Sync all now sits
        // above the per-platform folder rows, matching the Desktop layout — on a controller the
        // primary action must not sit below every platform row.
        if (_settings.IsCloudDisconnected)
        {
            // Connect is the primary action and the default folder just works, so it leads; the cloud
            // folder is a single detail row beneath it.
            yield return ActionRow(
                "saves.connect",
                "Connect Google Drive",
                "Open Google's sign-in in your browser and enable the configured save platforms.",
                _settings.IsCloudBusy ? "CONNECTING…" : "A CONNECT",
                _settings.ConnectCloudCommand,
                !_settings.IsCloudBusy);
            yield return TextRow(
                "saves.cloud-folder",
                "Cloud folder",
                "The Google Drive folder that stores EmuShelf save manifests and copies.",
                _settings.CloudFolder,
                false,
                value => _settings.CloudFolder = value);
        }
        else
        {
            if (_settings.IsCloudBusy)
            {
                yield return ActionRow(
                    "saves.stop",
                    "Stop sync",
                    "Already transferred batches remain safe; the next sync continues from them.",
                    "A STOP",
                    _settings.CancelCloudSyncCommand,
                    _settings.CancelCloudSyncCommand.CanExecute(null));
            }
            else
            {
                yield return ActionRow(
                    "saves.sync",
                    "Sync all now",
                    "Reconcile every configured platform with the cloud.",
                    "A SYNC",
                    _settings.SyncCloudNowCommand,
                    true);
            }
            yield return ActionRow(
                "saves.disconnect",
                "Disconnect Google Drive",
                "Disable EmuShelf cloud sync. Local and cloud saves remain untouched.",
                "A DISCONNECT",
                _settings.DisconnectCloudCommand,
                !_settings.IsCloudBusy,
                isDestructive: true,
                confirmationTitle: "Disconnect Google Drive?",
                confirmationText: "EmuShelf will disable cloud sync. It will not delete local saves or anything already stored in Google Drive.");
        }

        foreach (var platform in _settings.CloudPlatforms)
        {
            // A per-platform header groups this platform's folder, states, and replace rows so the
            // section reads as a hierarchy rather than a flat list. Member labels drop the platform
            // name because the header already carries it; the stable ids (Keys) are unchanged.
            yield return HeaderRow(
                $"saves.{platform.SystemId}.header", platform.DisplayName, platform.SystemId);
            var location = platform.NormalizedOverride ?? platform.DetectedDirectory ?? "Use detected emulator location";
            var detail = FirstNonEmpty(
                platform.DetectionErrorText,
                platform.CompatibilityWarning,
                platform.LastNoticeText,
                platform.LastResultText,
                platform.SaveShapeDescription);
            yield return ActionRow(
                $"saves.{platform.SystemId}.folder",
                "Save folder",
                detail,
                location,
                platform.PickDirectoryCommand,
                platform.IsIdle,
                GamepadSettingsRowKind.Folder,
                isGrouped: true,
                systemId: platform.SystemId);
            if (platform.SupportsSaveStates)
            {
                yield return ToggleRow(
                    $"saves.{platform.SystemId}.states",
                    "Save states",
                    "Sync manual states before launch and after exit only when emulator version and CPU architecture are compatible.",
                    platform.SyncSaveStates,
                    value => platform.SyncSaveStates = value,
                    platform.IsIdle,
                    isGrouped: true,
                    systemId: platform.SystemId);
                // Mirror Desktop: once states sync, the save-state folder gets its own override so a
                // mis-detected state folder can be corrected the same way as the save folder above.
                if (platform.SyncSaveStates)
                {
                    yield return ActionRow(
                        $"saves.{platform.SystemId}.states-folder",
                        "Save-state folder",
                        "Correct a mis-detected save-state folder. Leave it detected to follow the emulator.",
                        string.IsNullOrEmpty(platform.NormalizedStateOverride)
                            ? "Use the detected save-state folder"
                            : platform.NormalizedStateOverride,
                        platform.PickStateDirectoryCommand,
                        platform.IsIdle,
                        GamepadSettingsRowKind.Folder,
                        isGrouped: true,
                        systemId: platform.SystemId);
                }
            }
            if (_settings.IsCloudConnected)
            {
                yield return ActionRow(
                    $"saves.{platform.SystemId}.replace-cloud",
                    "Replace cloud saves",
                    "Upload this platform's local saves over cloud copies. Replaced cloud copies are backed up.",
                    "A REPLACE CLOUD",
                    platform.ReplaceCloudCommand,
                    platform.CanReplace,
                    isDestructive: true,
                    confirmationTitle: $"Replace {platform.DisplayName} cloud saves?",
                    confirmationText: "Local saves become authoritative for this platform. Replaced cloud copies are backed up before the upload.",
                    isGrouped: true,
                    systemId: platform.SystemId);
                yield return ActionRow(
                    $"saves.{platform.SystemId}.replace-local",
                    "Replace local saves",
                    "Download this platform's cloud saves over local copies. Replaced local copies are backed up.",
                    "A REPLACE LOCAL",
                    platform.ReplaceLocalCommand,
                    platform.CanReplace,
                    isDestructive: true,
                    confirmationTitle: $"Replace {platform.DisplayName} local saves?",
                    confirmationText: "Cloud saves become authoritative for this platform. Replaced local copies are backed up before the download.",
                    isGrouped: true,
                    systemId: platform.SystemId);
            }
        }

        // Export mirrors Desktop's always-present buttons: the device export needs no connection, the
        // cloud export is enabled only while connected. Both rows are always projected so field parity
        // with Desktop holds regardless of connection state.
        yield return ActionRow(
            "saves.export.device",
            "Export saves (this device)",
            "Save a portable .zip of this machine's saves — save states included — to use on another device.",
            "A EXPORT",
            _settings.ExportDeviceSavesCommand,
            _settings.ExportDeviceSavesCommand.CanExecute(null));
        yield return ActionRow(
            "saves.export.cloud",
            "Export saves (this device + cloud)",
            "Also include saves that live only in your connected Google Drive. Connect first to enable this.",
            "A EXPORT",
            _settings.ExportDeviceAndCloudSavesCommand,
            _settings.ExportDeviceAndCloudSavesCommand.CanExecute(null));

        if (_settings.HasSyncLog && _settings.CanRevealFiles)
        {
            // Actionable (opens the log in the OS viewer) rather than a dead read-only row where A
            // did nothing. Desktop exposes this as a hyperlink, so it is excluded from field parity.
            // Skipped on Android, where there is no OS viewer to hand the log path to.
            yield return ActionRow(
                "saves.log",
                "Open sync activity log",
                "Portable, read-only record of previous save-sync actions.",
                "A OPEN",
                _settings.OpenSyncLogCommand,
                enabled: true,
                excludeFromParity: true);
        }
    }

    private IEnumerable<GamepadSettingsRowSpec> BuildTextureRows()
    {
        yield return ActionRow(
            "textures.rescan",
            "Rescan installed packs",
            "Read every configured texture root again. No pack or emulator setting is changed.",
            _settings.IsTexturePackBusy ? "SCANNING…" : "A RESCAN",
            _settings.RescanTexturePacksCommand,
            !_settings.IsTexturePackBusy);
        yield return ChoiceRow(
            "textures.emulator-filter",
            "Emulator filter",
            "Limit the inventory to one emulator.",
            _settings.TextureEmulatorFilter,
            _settings.TextureEmulatorFilters,
            value => _settings.TextureEmulatorFilter = value);
        yield return ChoiceRow(
            "textures.status-filter",
            "Status filter",
            "Show matched packs, packs without library games, or entries needing attention.",
            _settings.TextureStatusFilter,
            _settings.TextureStatusFilters,
            value => _settings.TextureStatusFilter = value);

        foreach (var platform in _settings.TexturePlatforms)
        {
            yield return HeaderRow(
                $"textures.{platform.SystemId}.header", platform.DisplayName, platform.SystemId);
            yield return ActionRow(
                $"textures.{platform.SystemId}.folder",
                "Texture folder",
                FirstNonEmpty(platform.StatusText, platform.LoadingText),
                platform.DirectoryOverride.Length > 0
                    ? platform.DirectoryOverride
                    : platform.DetectedRoot ?? "No folder detected",
                new AsyncRelayCommand(() => ExecuteAsync(_settings.BrowseTextureOverrideCommand, platform)),
                !_settings.IsTexturePackBusy,
                GamepadSettingsRowKind.Folder,
                isGrouped: true,
                systemId: platform.SystemId);
            yield return ActionRow(
                $"textures.{platform.SystemId}.detected",
                "Use detected folder",
                "Clear only EmuShelf's folder override and return to emulator-based detection.",
                "A USE DETECTED",
                new AsyncRelayCommand(() => ExecuteAsync(_settings.ClearTextureOverrideCommand, platform)),
                !_settings.IsTexturePackBusy,
                isGrouped: true,
                systemId: platform.SystemId);
        }

        var entries = _settings.TexturePackEntries;
        if (entries.Count == 0)
        {
            yield return InformationRow(
                "textures.empty",
                "No texture packs to show",
                "Rescan after configuring an emulator, or change the filters above.",
                string.Empty);
            yield break;
        }

        // The per-pack inventory can run to hundreds of entries — noise for a settings screen. Keep
        // it collapsed behind an explicit control; the header pill already carries the matched/
        // attention totals, which is what a controller user usually needs.
        var countLabel = entries.Count == 1 ? "1 installed pack" : $"{entries.Count} installed packs";
        yield return new GamepadSettingsRowSpec(
            "textures.inventory-toggle",
            countLabel,
            _texturePackListExpanded
                ? "Read-only inventory. Press A to hide the pack list again."
                : "Read-only inventory. Press A to list every matched pack.",
            _texturePackListExpanded ? "HIDE" : "SHOW",
            GamepadSettingsRowKind.Action,
            Activate: () =>
            {
                _texturePackListExpanded = !_texturePackListExpanded;
                return Task.CompletedTask;
            },
            ExcludeFromParity: true);

        if (!_texturePackListExpanded)
            yield break;

        const int maxInventoryRows = 24;
        foreach (var entry in entries.Take(maxInventoryRows))
        {
            yield return InformationRow(
                $"textures.pack.{entry.EmulatorName}.{entry.SourcePath}",
                entry.PackKey,
                FirstNonEmpty(entry.MatchedGames, entry.EmulatorName, entry.SourcePath),
                entry.StatusText);
        }

        if (entries.Count > maxInventoryRows)
        {
            yield return InformationRow(
                "textures.inventory-more",
                $"+{entries.Count - maxInventoryRows} more not shown",
                "Narrow the emulator or status filter above, or browse the full inventory in Desktop Settings.",
                string.Empty);
        }
    }

    private GamepadSettingsRowSpec TextRow(
        string key,
        string label,
        string description,
        string value,
        bool isSecret,
        Action<string> commit,
        bool isGrouped = false) => new(
            key,
            label,
            description,
            isSecret ? (value.Length == 0 ? "Not entered" : "••••••••") : value,
            isSecret ? GamepadSettingsRowKind.Secret : GamepadSettingsRowKind.Text,
            Activate: () =>
            {
                BeginTextEntry(label, description, value, isSecret, commit);
                return Task.CompletedTask;
            },
            IsGrouped: isGrouped);

    // A group header. Platform groups pass a systemId for artwork; generic groups (sign-in,
    // advanced) pass none and render as a plain subheading over their indented members.
    private static GamepadSettingsRowSpec HeaderRow(string key, string label, string? systemId = null) =>
        new(
            key,
            label,
            string.Empty,
            string.Empty,
            GamepadSettingsRowKind.Header,
            IsEnabled: false,
            SystemId: systemId);

    private GamepadSettingsRowSpec ToggleRow(
        string key,
        string label,
        string description,
        bool value,
        Action<bool> set,
        bool enabled = true,
        string onLabel = "ON",
        string offLabel = "OFF",
        bool isGrouped = false,
        string? systemId = null)
    {
        void Toggle(int _) => RunLocalEdit(() => set(!value));
        return new GamepadSettingsRowSpec(
            key,
            label,
            description,
            value ? onLabel : offLabel,
            GamepadSettingsRowKind.Toggle,
            enabled,
            Activate: () =>
            {
                RunLocalEdit(() => set(!value));
                return Task.CompletedTask;
            },
            Adjust: Toggle,
            ToggleValue: value,
            SystemId: systemId,
            IsGrouped: isGrouped);
    }

    private GamepadSettingsRowSpec ChoiceRow(
        string key,
        string label,
        string description,
        string value,
        IReadOnlyList<string> choices,
        Action<string> set,
        bool isGrouped = false,
        string? systemId = null,
        bool excludeFromParity = false)
    {
        void Move(int delta)
        {
            if (choices.Count == 0)
                return;
            var index = choices.ToList().IndexOf(value);
            if (index < 0)
                index = 0;
            // Wrap rather than clamp: the row only advances (A/Right), so without wrapping the last
            // option would be a dead-end with no controller input able to reach earlier values again.
            var next = ((index + Math.Sign(delta)) % choices.Count + choices.Count) % choices.Count;
            RunLocalEdit(() => set(choices[next]));
        }

        return new GamepadSettingsRowSpec(
            key,
            label,
            description,
            value,
            GamepadSettingsRowKind.Choice,
            Activate: () =>
            {
                OpenChoicePicker(key, label, description, value, choices, set);
                return Task.CompletedTask;
            },
            Adjust: Move,
            SystemId: systemId,
            IsGrouped: isGrouped,
            ExcludeFromParity: excludeFromParity);
    }

    private GamepadSettingsRowSpec ActionRow(
        string key,
        string label,
        string description,
        string value,
        ICommand command,
        bool enabled,
        GamepadSettingsRowKind kind = GamepadSettingsRowKind.Action,
        bool isDestructive = false,
        string? confirmationTitle = null,
        string? confirmationText = null,
        bool isGrouped = false,
        string? systemId = null,
        bool excludeFromParity = false) => new(
            key,
            label,
            description,
            value,
            kind,
            enabled,
            isDestructive,
            () => ExecuteAsync(command),
            ConfirmationTitle: confirmationTitle,
            ConfirmationText: confirmationText,
            SystemId: systemId,
            IsGrouped: isGrouped,
            ExcludeFromParity: excludeFromParity);

    private static GamepadSettingsRowSpec InformationRow(
        string key,
        string label,
        string description,
        string value,
        bool isGrouped = false) => new(
            key,
            label,
            description,
            value,
            GamepadSettingsRowKind.Information,
            IsEnabled: true,
            IsGrouped: isGrouped);

    private static Task ExecuteAsync(ICommand command, object? parameter = null)
    {
        if (!command.CanExecute(parameter))
            return Task.CompletedTask;
        return command is IAsyncRelayCommand asyncCommand
            ? asyncCommand.ExecuteAsync(parameter)
            : ExecuteSynchronous(command, parameter);
    }

    private static Task ExecuteSynchronous(ICommand command, object? parameter)
    {
        command.Execute(parameter);
        return Task.CompletedTask;
    }

    /// <summary>Re-reads the per-platform game counts the host captured when Settings opened, then rebuilds
    /// so every "N games" line and the Library rail total reflect what the scan just imported.</summary>
    private async Task RefreshGameCountsAsync()
    {
        if (_refreshGameCounts is null)
            return;

        await _refreshGameCounts();
        if (!_disposed)
            RebuildRows();
    }

    private void NotifyRailStatuses()
    {
        _emulatorsRailStatus = ComputeEmulatorsRailStatus();
        RefreshSetupRail();
        OnPropertyChanged(nameof(LibraryRailStatus));
        OnPropertyChanged(nameof(EmulatorsRailStatus));
        OnPropertyChanged(nameof(IsEmulatorsRailWarning));
        OnPropertyChanged(nameof(RetroAchievementsRailStatus));
        OnPropertyChanged(nameof(ArtworkRailStatus));
        OnPropertyChanged(nameof(SavesRailStatus));
        OnPropertyChanged(nameof(ThemesRailStatus));
        OnPropertyChanged(nameof(AboutRailStatus));
    }

    private void RememberFocusedRow()
    {
        if (FocusedRow is { } row)
            _focusedRowBySection[SelectedSection] = row.Key;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A local toggle/choice/text edit writes to the settings model, which echoes PropertyChanged
        // back here. Skip the rebuild for those: the caller that made the edit runs one explicit
        // RebuildRows itself, so honoring this echo too would rebuild the entire list twice per press.
        if (_synchronizingSection || _applyingLocalEdit)
            return;
        // A rescan or folder import just finished, so the game counts captured when Settings opened are
        // now wrong. Re-read them off the UI thread and rebuild again when they land.
        var maintaining = _settings.IsMaintainingLibrary;
        if (_maintainingLibrary && !maintaining)
            _ = RefreshGameCountsAsync();
        _maintainingLibrary = maintaining;
        RebuildRows();
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(IsWorkingInSection));
    }

    // A synchronous edit that writes straight to the Desktop settings model. Suppresses the echoed
    // rebuild (see OnSettingsPropertyChanged) so only the caller's explicit RebuildRows runs.
    private void RunLocalEdit(Action edit)
    {
        _applyingLocalEdit = true;
        try
        {
            edit();
        }
        finally
        {
            _applyingLocalEdit = false;
        }
    }

    private void OnSettingsCloseRequested(bool saved) => CloseRequested?.Invoke(saved);

    private void HookCollection<T>(ObservableCollection<T> collection) where T : class
    {
        foreach (var item in collection.OfType<INotifyPropertyChanged>())
            item.PropertyChanged += OnSettingsPropertyChanged;
        collection.CollectionChanged += OnCollectionChanged;
    }

    private void UnhookCollection<T>(ObservableCollection<T> collection) where T : class
    {
        foreach (var item in collection.OfType<INotifyPropertyChanged>())
            item.PropertyChanged -= OnSettingsPropertyChanged;
        collection.CollectionChanged -= OnCollectionChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<INotifyPropertyChanged>())
                item.PropertyChanged -= OnSettingsPropertyChanged;
        }
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<INotifyPropertyChanged>())
                item.PropertyChanged += OnSettingsPropertyChanged;
        }
        RebuildRows();
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    // ----- Setup-wizard mode -------------------------------------------------------------------------

    partial void OnIsRailFocusedChanged(bool value) => SetupRail.IsRailFocused = value;

    /// <summary>True when this projection is the in-app half of the Android setup wizard.</summary>
    public bool IsSetupMode => _setup is not null;

    /// <summary>The wizard rail (steps + START chip) the shared rail view binds to; empty outside setup mode.</summary>
    public SetupWizardRailModel SetupRail { get; } = new();

    /// <summary>The step whose rows are showing; <see cref="SetupStep.GamesAndEmulators"/> outside setup mode.</summary>
    public SetupStep CurrentSetupStep =>
        _liveSetupSteps.Count == 0 ? SetupStep.GamesAndEmulators : _liveSetupSteps[_setupIndex];

    private bool IsLastSetupStep => _setupIndex >= _liveSetupSteps.Count - 1;

    private static SettingsSection SectionForSetupStep(SetupStep step) => step switch
    {
        SetupStep.GamesAndEmulators => SettingsSection.Emulators,
        SetupStep.Saves => SettingsSection.Saves,
        _ => SettingsSection.General,
    };

    private string SetupTitle => CurrentSetupStep switch
    {
        SetupStep.SecondScreen => "Playing on the second screen",
        SetupStep.ClosingGames => "Closing games",
        SetupStep.GamesAndEmulators => "Games & emulators",
        SetupStep.Saves => "Saves",
        _ => "Setup",
    };

    private string SetupDescription => CurrentSetupStep switch
    {
        SetupStep.SecondScreen => "This device has a second screen. A game can run there while the library stays here.",
        SetupStep.ClosingGames => "What happens to the emulator when you come back to EmuShelf from a game.",
        SetupStep.GamesAndEmulators => "Open a system to add the folders its games are in, and check which app plays it.",
        SetupStep.Saves => "Back up emulator saves through your own Google Drive, and tell EmuShelf where each emulator keeps them.",
        _ => string.Empty,
    };

    // Read once per screen and again after a foreground return (the user flips the switch in Android's
    // Accessibility settings and comes back), like the other device probes.
    private bool IsSecondScreenReturnReady
    {
        get
        {
            if (_setup is null)
                return true;
            if (!_secondScreenReadyRead)
            {
                _cachedSecondScreenReady = _setup.IsSecondScreenReturnReady();
                _secondScreenReadyRead = true;
            }
            return _cachedSecondScreenReady;
        }
    }

    private IEnumerable<GamepadSettingsRowSpec> BuildSetupRows()
    {
        switch (CurrentSetupStep)
        {
            case SetupStep.SecondScreen:
            {
                var ready = IsSecondScreenReturnReady;
                yield return new GamepadSettingsRowSpec(
                    "setup.second-screen.return",
                    "Bring EmuShelf back when a game closes",
                    ready
                        ? "On. A game closed on the second screen returns you to the library."
                        : "Off. Needs Android's accessibility permission. A opens that page.",
                    ready ? "On" : "A OPEN",
                    ready ? GamepadSettingsRowKind.Information : GamepadSettingsRowKind.Action,
                    Activate: ready
                        ? null
                        : () =>
                        {
                            _setup!.RequestSecondScreenReturn();
                            // The answer lands on foreground return; forget the cached reading so that
                            // rebuild re-asks.
                            _secondScreenReadyRead = false;
                            return Task.CompletedTask;
                        },
                    IsWarning: !ready,
                    ExcludeFromParity: true);
                yield return InformationRow(
                    "setup.second-screen.privacy",
                    "What EmuShelf can see",
                    "Only which app is open on the second screen. Never what it shows.",
                    string.Empty);
                break;
            }
            case SetupStep.ClosingGames:
                yield return CloseOnReturnRow(
                    "Close the emulator when I come back",
                    "The emulator stops running in the background, so it does not drain the battery.",
                    compact: false);
                yield return InformationRow(
                    "setup.closing-games.why",
                    "Why Shizuku",
                    "Android does not let one app close another. Shizuku is a small helper that can. Without it the emulator keeps running in the background.",
                    string.Empty);
                break;
            case SetupStep.GamesAndEmulators:
                foreach (var row in BuildEmulatorsRows())
                    yield return row;
                break;
            case SetupStep.Saves:
            {
                // The step is for choices (connect, save folders, state sync); the one-off "sync all now",
                // the disconnect and the per-platform replace actions stay in Settings. A platform whose
                // save folder could not be detected is the one thing the user must act on here, so its
                // folder row is painted as a warning and says so in plain words.
                var needsFolder = SavePlatformsNeedingAFolder().Select(platform => $"saves.{platform.SystemId}.folder").ToHashSet(StringComparer.Ordinal);
                foreach (var row in BuildSaveRows())
                {
                    if (row.Key is "saves.sync" or "saves.disconnect"
                        || row.Key.EndsWith("replace-cloud", StringComparison.Ordinal)
                        || row.Key.EndsWith("replace-local", StringComparison.Ordinal))
                        continue;
                    yield return needsFolder.Contains(row.Key)
                        ? row with
                        {
                            Description = "No save folder found for this emulator. A picks the folder it saves to.",
                            Value = "Not set",
                            IsWarning = true,
                        }
                        : row;
                }
                break;
            }
        }
    }

    /// <summary>Save platforms whose folder detection failed and that have no manual folder yet.</summary>
    private IEnumerable<CloudSavePlatformRowViewModel> SavePlatformsNeedingAFolder() =>
        _settings.CloudPlatforms.Where(platform =>
            !string.IsNullOrEmpty(platform.DetectionErrorText) && string.IsNullOrEmpty(platform.NormalizedOverride));

    private void RefreshSetupRail()
    {
        if (_setup is null)
            return;

        foreach (var entry in SetupRail.Steps)
        {
            var (status, warning, done) = entry.Step switch
            {
                SetupStep.StorageAccess => ("Allowed", false, true),
                SetupStep.DataFolder => (_setup.DataFolderStatus, false, true),
                SetupStep.SecondScreen => IsSecondScreenReturnReady ? ("On", false, true) : ("Off", true, false),
                SetupStep.ClosingGames => !CloseEmulatorOnReturn
                    ? ("Keep running", false, true)
                    : CloseOnReturnWarning is not null
                        ? ("Needs Shizuku", true, false)
                        : ("Close", false, true),
                // Only missing emulators count here; the Shizuku gap belongs to the Closing games step.
                SetupStep.GamesAndEmulators => _settings.Rows.FirstOrDefault(row => EmulatorMissingFor(row) is not null) is { } attention
                    ? ($"{attention.SystemName} needs attention", true, false)
                    : (LibraryRailStatus, false, LibraryRailStatus.Length > 0),
                SetupStep.Saves => SavePlatformsNeedingAFolder().Count() is > 0 and var needed
                    ? (needed == 1 ? "1 folder needed" : $"{needed} folders needed", true, false)
                    : string.IsNullOrEmpty(SavesRailStatus)
                        ? ("Not connected", false, false)
                        : (SavesRailStatus, false, true),
                _ => (string.Empty, false, false),
            };
            entry.Status = status;
            entry.IsWarning = warning;
            entry.IsDone = done;
            entry.IsCurrent = entry.Step == CurrentSetupStep;
        }

        SetupRail.StartLabel = IsLastSetupStep ? "Finish" : "Continue";
        SetupRail.StartDetail = IsLastSetupStep
            ? "Open the library"
            : $"Next: {SetupStepLabels.For(_liveSetupSteps[_setupIndex + 1])}";
        SetupRail.IsStartEnabled = !_settings.IsWorking;
    }

    private void SelectSetupStep(int index)
    {
        if (_setup is null || index < 0 || index >= _liveSetupSteps.Count)
            return;

        RememberFocusedRow();
        _setupIndex = index;
        if (IsThemesSection)
            IsThemesSection = false;
        var section = SectionForSetupStep(CurrentSetupStep);
        if (SelectedSection != section)
            SelectedSection = section;
        PrepareSetupStep();
        RebuildRows(preferredKey: PreferredSetupRowKey());
        OnPropertyChanged(nameof(CurrentSetupStep));
        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(SectionDescription));
        FocusRevision++;
    }

    // An empty library (the genuine first run) opens the first system so "Add game folder" is on screen
    // without a press; a populated one keeps the list collapsed as Settings does.
    private void PrepareSetupStep()
    {
        if (CurrentSetupStep == SetupStep.GamesAndEmulators
            && _expandedSystemId is null
            && _gameCountBySystem is not null
            && _settings.Rows.Count > 0
            && _settings.Rows.Sum(row => _gameCountBySystem(row.SystemId)) == 0)
        {
            _expandedSystemId = _settings.Rows[0].SystemId;
        }
    }

    // The row to land on when a step opens: the first save folder that still needs picking, else the top.
    private string? PreferredSetupRowKey() => CurrentSetupStep == SetupStep.Saves
        ? SavePlatformsNeedingAFolder().Select(platform => $"saves.{platform.SystemId}.folder").FirstOrDefault()
        : null;

    /// <summary>START: the next step, or on the last step the save that finishes the wizard.</summary>
    private async Task AdvanceSetupAsync()
    {
        if (_setup is null || !IsNormal)
            return;

        if (!IsLastSetupStep)
        {
            SelectSetupStep(_setupIndex + 1);
            return;
        }

        // Finish = the ordinary Save: it persists every edit and raises CloseRequested(saved: true), which
        // the host treats as wizard completion.
        await ExecuteAsync(_settings.SaveCommand);
    }

    /// <summary>B: the previous step, or on the first step leave the wizard without finishing it.</summary>
    private void BackSetup()
    {
        if (_setup is null)
            return;

        if (_setupIndex > 0)
            SelectSetupStep(_setupIndex - 1);
        else
            CloseRequested?.Invoke(false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _settings.PropertyChanged -= OnSettingsPropertyChanged;
        if (_settings.Updates is { } updates)
            updates.PropertyChanged -= OnSettingsPropertyChanged;
        _settings.CloseRequested -= OnSettingsCloseRequested;
        UnhookCollection(_settings.Rows);
        UnhookCollection(_settings.CloudPlatforms);
        UnhookCollection(_settings.TexturePlatforms);
        UnhookCollection(_settings.TexturePackEntries);
        // The theme choices are owned by the shell and outlive this projection; leave no stale focus.
        foreach (var choice in _themeChoices)
            choice.IsFocused = false;
        DraftText = string.Empty;
        _commitText = null;
        _pendingConfirmation = null;
    }
}
