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
    /// <summary>Parity id of the Desktop field the Y action stands in for ("saves.disconnect" on the Google
    /// Drive row), so a field folded into Y still counts as reachable on the couch.</summary>
    public string SecondaryKey { get; private set; } = string.Empty;
    public bool IsNormalRow => !IsSaveRow;
    public bool HasPlatformIcon => !string.IsNullOrEmpty(SystemId);
    /// <summary>True for gamepad-only view-state controls (e.g. expand inventory) that have no Desktop
    /// settings field and must not participate in the executable parity comparison.</summary>
    public bool ExcludeFromParity { get; private set; }
    public bool CanActivate => IsEnabled && Kind is not GamepadSettingsRowKind.Information;
    public string ParityId =>
        GamepadSettingsRowSpec.CoversOwnKey(Kind, Key, ExcludeFromParity) ? Key : string.Empty;
    /// <summary>Every Desktop field id this row covers: its own (A) and the one behind Y, if any. Derived
    /// rather than stored so it cannot drift from the row it describes.</summary>
    public IEnumerable<string> ParityIds
    {
        get
        {
            if (ParityId.Length > 0)
                yield return ParityId;
            // A Y key only counts once the action behind it is wired; a key with no handler names a
            // field no press can reach, however the row is worded.
            if (SecondaryKey.Length > 0 && SecondaryActivate is not null)
                yield return SecondaryKey;
        }
    }
    public bool IsSaveRow => Key == "common.save";
    public bool IsToggle => Kind == GamepadSettingsRowKind.Toggle;
    public bool IsToggleOn => ToggleValue == true;
    public bool IsChoice => Kind == GamepadSettingsRowKind.Choice;
    public bool IsAction => Kind == GamepadSettingsRowKind.Action;
    public bool IsEditableValue => Kind is GamepadSettingsRowKind.Text or
        GamepadSettingsRowKind.Secret or GamepadSettingsRowKind.Folder or GamepadSettingsRowKind.File;
    public bool IsInformation => Kind == GamepadSettingsRowKind.Information;
    /// <summary>Every action, edit and pick ends in the same chevron; A does what the label says. An action
    /// row's Value is either an "A …" prompt (hidden — the chevron is the prompt) or a word to show before the
    /// chevron: what A does when the label alone does not say ("Sync now"), or that it is running ("Working…").</summary>
    public bool ShowsChevron => Kind is GamepadSettingsRowKind.Action or
        GamepadSettingsRowKind.Text or GamepadSettingsRowKind.Secret or
        GamepadSettingsRowKind.Folder or GamepadSettingsRowKind.File;
    public bool HasActionText =>
        IsAction && Value.Length > 0 && !Value.StartsWith("A ", StringComparison.OrdinalIgnoreCase);
    public string ActionText => HasActionText ? Value : string.Empty;
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
        SecondaryKey = spec.SecondaryKey ?? string.Empty;
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
        OnPropertyChanged(nameof(IsSaveRow));
        OnPropertyChanged(nameof(IsToggle));
        OnPropertyChanged(nameof(IsToggleOn));
        OnPropertyChanged(nameof(IsChoice));
        OnPropertyChanged(nameof(IsAction));
        OnPropertyChanged(nameof(IsEditableValue));
        OnPropertyChanged(nameof(IsInformation));
        OnPropertyChanged(nameof(ShowsChevron));
        OnPropertyChanged(nameof(HasActionText));
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(SecondaryKey));
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
    string? SecondaryConfirmationText = null,
    string? SecondaryKey = null,
    bool SettingsOnly = false)
{
    /// <summary>True when a row's own key names a Desktop field. Read-only rows, the Save row and
    /// couch-only view state do not. The one place this rule lives: both the AutomationId the
    /// snapshot test reads off a realized row and the parity sweep ask it, so they cannot disagree.</summary>
    public static bool CoversOwnKey(GamepadSettingsRowKind kind, string key, bool excludeFromParity) =>
        kind is not GamepadSettingsRowKind.Information && key != "common.save" && !excludeFromParity;

    /// <summary>The Desktop field ids this row makes reachable: its own key, plus the field folded into
    /// its Y action once that action is wired.</summary>
    public static IEnumerable<string> ParityIdsOf(GamepadSettingsRowSpec spec)
    {
        if (CoversOwnKey(spec.Kind, spec.Key, spec.ExcludeFromParity))
            yield return spec.Key;
        // Only a Y that has a handler counts. The label may come and go with a busy flag — Desktop's own
        // button is visible-but-disabled in the same states — but a SecondaryKey with nothing behind it
        // names a field no press can reach, and the parity sweep must not paper over that.
        if (!string.IsNullOrEmpty(spec.SecondaryKey) && spec.SecondaryActivate is not null)
            yield return spec.SecondaryKey;
    }
}

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
    private bool _storageGrantedRead;
    private bool _cachedStorageGranted;
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
    /// <summary>The one platform whose rows are shown beneath its summary, per section; absent = all
    /// collapsed. Single-open keeps the list short (the point of the summaries) and the focus predictable,
    /// and keying it by section means opening PS2 in Emulators does not also open it in Saves.</summary>
    private readonly Dictionary<SettingsSection, string> _expandedBySection = [];
    private bool _savesStepOpenedMissingFolder;
    /// <summary>While set, every platform summary projects its rows, so a parity sweep sees every field a
    /// user can reach by opening a platform, not just the one platform currently open.</summary>
    private bool _projectEveryPlatform;
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
            OnPropertyChanged(nameof(DisplayRailStatus));
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
            OnPropertyChanged(nameof(DisplayRailStatus));
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

    /// <summary>The row list is shown for the model sections; the gallery replaces it on Themes.</summary>
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
        SettingsSection.Display => "Display",
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
        SettingsSection.Display =>
            "How the shelf itself is drawn. Both settings apply to gaming mode only, and take effect as you change them.",
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
    public string ArtworkRailStatus => _settings.IsScreenScraperConnected ? "ScreenScraper connected" : string.Empty;
    /// <summary>Like Emulators: the rail names the platform that needs a look (no save folder, a detection
    /// error, a sync notice) before it says anything routine.</summary>
    public string SavesRailStatus => _savesRailStatus;
    public bool IsSavesRailWarning => _savesRailWarning;
    private string _savesRailStatus = string.Empty;
    private bool _savesRailWarning;

    private void ComputeSavesRailStatus()
    {
        // Save folders exist for cloud sync, so a user who never connected hears nothing about them. The
        // probe that fills NeedsFolder runs on any visit to the Saves section, connected or not, so
        // without this gate the rail would warn about a feature that was never switched on.
        if (!_settings.IsCloudConnected)
        {
            _savesRailWarning = false;
            _savesRailStatus = string.Empty;
            return;
        }

        var attention = _settings.CloudPlatforms.FirstOrDefault(platform =>
            platform.NeedsFolder || platform.HasDetectionError || platform.HasLastNotice);
        if (attention is not null)
        {
            _savesRailWarning = true;
            // A detection error on a platform whose folder was picked by hand is not a missing folder;
            // the same guard SavePlatformsNeedingAFolder applies, so the rail and the wizard chip agree.
            _savesRailStatus = attention.NeedsFolder
                || (attention.HasDetectionError && attention.NormalizedOverride is null)
                ? $"{attention.DisplayName} needs a save folder"
                : $"{attention.DisplayName} needs attention";
            return;
        }
        _savesRailWarning = false;
        _savesRailStatus = "Google Drive";
    }
    public string TexturePacksRailStatus => string.Empty;
    /// <summary>What is switched on, so the rail answers "is the tube on?" without opening the page.
    /// Silent when neither is, like the Artwork rail line — and short enough to survive the rail's
    /// ~98px status column, which cut the longer wording mid-word.</summary>
    public string DisplayRailStatus => (CrtScreenEffect, AmbientThemeFromArtwork) switch
    {
        (true, true) => "Both on",
        (true, false) => "CRT on",
        (false, true) => "Artwork colours on",
        _ => string.Empty,
    };
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
        _storageGrantedRead = false;
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
    public bool IsDisplaySection => !IsThemesSection && SelectedSection == SettingsSection.Display;
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
        var sections = settings.Sections
            .Where(section => section is not SettingsSection.Themes)
            .ToList();
        // Display is couch-only, so it is spliced in here rather than coming from the settings model:
        // the CRT tube and artwork-matched colours change how the shelf behind this panel is drawn, and
        // Desktop deliberately offers neither (a toggle whose effect is invisible from its own window is
        // worse than no toggle). It sits next to Themes so appearance is two neighbouring pages.
        var beforeAbout = sections.IndexOf(SettingsSection.About);
        sections.Insert(beforeAbout >= 0 ? beforeAbout : sections.Count, SettingsSection.Display);
        Sections = sections;

        if (_setup is not null)
        {
            // The steps this device gets, in order. Storage access and the data folder were answered by the
            // pre-boot page; they stay reachable here (to see the answer, and to move the folder) so the
            // rail is one wizard the user can walk up and down. The rest are live only when the feature
            // exists here (a second screen, the close-on-return setting, cloud saves), so a phone with none
            // of them sees a short wizard, not a list of dead ends.
            _liveSetupSteps.Add(SetupStep.StorageAccess);
            _liveSetupSteps.Add(SetupStep.DataFolder);
            if (_setup.HasSecondScreen)
                _liveSetupSteps.Add(SetupStep.SecondScreen);
            if (_settings.HasCloseEmulatorOnReturn)
                _liveSetupSteps.Add(SetupStep.ClosingGames);
            _liveSetupSteps.Add(SetupStep.GamesAndEmulators);
            if (_settings.HasCloudSaves && Sections.Contains(SettingsSection.Saves))
                _liveSetupSteps.Add(SetupStep.Saves);
            foreach (var step in _liveSetupSteps)
                SetupRail.Steps.Add(new SetupStepViewModel(step));
            SetupRail.StartCommand = new AsyncRelayCommand(AdvanceSetupAsync);
            // Open on the first step that still has something to decide: the two pre-boot steps are done.
            _setupIndex = Math.Min(2, _liveSetupSteps.Count - 1);
            SelectedSection = SectionForSetupStep(CurrentSetupStep);
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
            // Except in the wizard, whose legend offers no shoulders: swallow them here too, so the rail
            // cannot do what the content column refuses to (see the setup block further down).
            if (IsSetupMode && action is GamepadAction.PreviousPlatform or GamepadAction.NextPlatform)
                return true;

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
                    // The gallery is the whole page now, so the first column is what steps out to the rail.
                    if (FocusedThemeIndex % ThemeColumns == 0)
                        EnterRail();
                    else
                        MoveThemeFocus(-1, 0);
                    return true;
                case GamepadAction.NavigateRight:
                    MoveThemeFocus(1, 0);
                    return true;
                case GamepadAction.NavigateUp:
                    MoveThemeFocus(0, -1);
                    return true;
                case GamepadAction.NavigateDown:
                    MoveThemeFocus(0, 1);
                    return true;
                case GamepadAction.Confirm:
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
            // Steps are walked with START/B (and Up/Down once Left has put focus on the rail); LB/RB are
            // swallowed in both columns so nothing jumps to a step the legend never offered.
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
            // Desktop places Themes right before About; match that slot instead of appending at the end,
            // and keep it immediately ahead of Display so picking a theme and adjusting how it is drawn
            // are neighbours.
            var slot = pages.IndexOf(SettingsSection.Display);
            if (slot < 0)
                slot = pages.IndexOf(SettingsSection.About);
            pages.Insert(slot >= 0 ? slot : pages.Count, SettingsSection.Themes);
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

        var index = FocusedRowIndex + step;
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
        OnPropertyChanged(nameof(IsDisplaySection));
        OnPropertyChanged(nameof(IsAboutSection));
        OnPropertyChanged(nameof(IsRowsVisible));
        OnPropertyChanged(nameof(IsThemesVisible));
        UpdateThemeFocus();
        FocusRevision++;
    }

    partial void OnFocusedThemeIndexChanged(int value)
    {
        UpdateThemeFocus();
        FocusRevision++;
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
        OnPropertyChanged(nameof(IsDisplaySection));
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
            : list.FindIndex(row => row.Key == preferredKey);
        if (target < 0)
            target = list.FindIndex(row => row.Key != "common.save");
        if (target < 0)
            target = 0;
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

        // One row height for every section: the compact one-line row Emulators shipped with. Only the
        // wizard's explanatory pages (built in BuildSetupRows) keep two-line descriptions.
        foreach (var row in SelectedSection switch
        {
            SettingsSection.Emulators => BuildEmulatorsRows(),
            SettingsSection.Hotkeys => BuildHotkeysRows(),
            SettingsSection.RetroAchievements => BuildRetroAchievementsRows(),
            SettingsSection.ArtworkMetadata => BuildArtworkMetadataRows(),
            SettingsSection.Saves => BuildSaveRows(),
            SettingsSection.TexturePacks => BuildTextureRows(),
            SettingsSection.Display => BuildDisplayRows(),
            SettingsSection.About => BuildAboutRows(),
            _ => BuildGeneralRows(),
        })
        {
            yield return row with { IsCompact = true };
        }
    }

    /// <summary>Every Desktop field id the given section makes reachable on the couch — with every platform
    /// opened, since a collapsed platform's rows are still one A press away. This is what the Desktop ↔
    /// couch parity test compares; <see cref="Rows"/> only holds the platform currently open.</summary>
    public string[] CollectParityIds(string prefix)
    {
        _projectEveryPlatform = true;
        try
        {
            return BuildRows()
                .SelectMany(GamepadSettingsRowSpec.ParityIdsOf)
                .Where(id => id.StartsWith(prefix, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
        }
        finally
        {
            _projectEveryPlatform = false;
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
        yield return ToggleRow(
            "general.empty-platforms",
            "Show empty platforms",
            _settings.ShowEmptyPlatforms
                ? "platforms with no games stay in the top bar"
                : "platforms with no games stay out of the top bar",
            _settings.ShowEmptyPlatforms,
            value => _settings.ShowEmptyPlatforms = value,
            onLabel: "Shown",
            offLabel: "Hidden");
        yield return ActionRow(
            "general.rescan",
            "Rescan all consoles",
            FirstNonEmpty(_settings.MaintenanceStatusText, "Rechecks every console's remembered folders"),
            _settings.IsMaintainingLibrary ? "Working…" : "A RESCAN",
            _settings.RescanAllCommand,
            _settings.CanRescanAll);
        // Mirrors Desktop's general.open-data-folder so a controller can reach the portable data
        // folder too, and so the two surfaces' general.* field sets stay in parity. Skipped where no
        // OS file manager can open the path (Android), so the row never offers a button that only fails.
        // The path is the row's value, where the save-folder rows put theirs.
        if (_settings.HasDataDirectory && _settings.CanRevealFiles)
        {
            yield return ActionRow(
                "general.open-data-folder",
                "Data folder",
                "Library, covers, settings and saves · opens in your file manager",
                _settings.DataDirectory ?? string.Empty,
                _settings.OpenDataFolderCommand,
                enabled: true,
                GamepadSettingsRowKind.Folder);
        }
        // Android has no file manager to open the folder in, so instead of "Open data folder" it offers a
        // way to move it: the folder is a user-chosen shared-storage path. Choosing a new one persists the
        // pointer and restarts; the old data is left where it is. Surface-specific, so excluded from parity.
        if (_settings.CanChangeDataFolder)
        {
            yield return ActionRow(
                "general.change-data-folder",
                "Data folder",
                "Library, covers, settings and saves · choosing another restarts EmuShelf",
                _settings.HasDataDirectory ? _settings.DataDirectory ?? string.Empty : "Not set",
                _settings.ChangeDataFolderCommand,
                // Gate on the broad IsBusy, not just IsWorking: activating this restarts the process, and
                // tearing down mid cloud-sync / RetroAchievements / texture work would race those in-flight
                // writes under the data folder — the same reason Save/Cancel gate on IsBusy.
                enabled: !_settings.IsBusy,
                GamepadSettingsRowKind.Folder,
                excludeFromParity: true);
        }
        if (_runSetupCommand is not null)
        {
            yield return ActionRow(
                "general.run-setup",
                "Run setup again",
                "Second screen, closing games, games & emulators, saves",
                "A OPEN",
                _runSetupCommand,
                !_settings.IsWorking,
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
            onLabel: "Close",
            offLabel: "Keep",
            stateFirst: warning is null) with
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
            var expanded = IsPlatformExpanded(SettingsSection.Emulators, row.SystemId);
            yield return SummaryRow(row, missing);
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
                    _settings.IsMaintainingLibrary ? "Working…" : "A SYNC",
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
                        _settings.IsMaintainingLibrary ? "Working…" : "A RESCAN",
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
                    _settings.IsMaintainingLibrary ? "Working…" : "A RESCAN",
                    row.RescanLibraryCommand,
                    row.CanRescan,
                    isGrouped: true,
                    systemId: row.SystemId) with { IsCompact = true };
            }
        }
    }

    /// <summary>True while the given section shows this platform's rows beneath its summary. A parity
    /// sweep sees every platform open, since a collapsed platform's rows are still one A press away.</summary>
    private bool IsPlatformExpanded(SettingsSection section, string systemId) =>
        _projectEveryPlatform
        || (_expandedBySection.TryGetValue(section, out var open)
            && string.Equals(open, systemId, StringComparison.Ordinal));

    /// <summary>Opens this platform and closes whichever one the section had open. Reads the live state
    /// rather than a flag captured when the row was built, so a row cannot act on a stale reading.</summary>
    private void ToggleExpandedPlatform(SettingsSection section, string systemId)
    {
        if (_expandedBySection.TryGetValue(section, out var open)
            && string.Equals(open, systemId, StringComparison.Ordinal))
            _expandedBySection.Remove(section);
        else
            _expandedBySection[section] = systemId;
    }

    /// <summary>The one shape every section's platform summary takes: artwork and name on the left, that
    /// section's own one-line detail on the right, A opening its rows beneath it one platform at a time.
    /// The wizard's steps use it unchanged, so a platform looks and behaves the same in both.</summary>
    private GamepadSettingsRowSpec PlatformSummaryRow(
        SettingsSection section,
        string key,
        string label,
        string systemId,
        string detail,
        bool warning = false,
        string? secondaryLabel = null,
        Func<Task>? secondaryActivate = null) =>
        ExpandableSummaryRow(section, key, label, systemId, detail, systemId, warning,
            secondaryLabel, secondaryActivate);

    /// <summary>The accordion without a platform behind it: <paramref name="expansionId"/> is what opens
    /// and closes, <paramref name="iconSystemId"/> is what draws artwork, and they are only the same thing
    /// for a platform. ScreenScraper's sign-in uses it to bind three loose rows into one group.</summary>
    private GamepadSettingsRowSpec ExpandableSummaryRow(
        SettingsSection section,
        string key,
        string label,
        string expansionId,
        string detail,
        string? iconSystemId = null,
        bool warning = false,
        string? secondaryLabel = null,
        Func<Task>? secondaryActivate = null)
    {
        return new GamepadSettingsRowSpec(
            key,
            label,
            string.Empty,
            detail,
            GamepadSettingsRowKind.Summary,
            IsEnabled: true,
            Activate: () =>
            {
                ToggleExpandedPlatform(section, expansionId);
                return Task.CompletedTask;
            },
            SystemId: iconSystemId,
            ExcludeFromParity: true,
            IsExpanded: IsPlatformExpanded(section, expansionId),
            IsWarning: warning,
            IsCompact: true,
            SecondaryLabel: secondaryLabel,
            SecondaryActivate: secondaryActivate);
    }

    private GamepadSettingsRowSpec SummaryRow(EmulatorSettingsRowViewModel row, string? missing)
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

        return PlatformSummaryRow(
            SettingsSection.Emulators,
            $"emulators.{row.SystemId}.summary",
            row.SystemName,
            row.SystemId,
            string.Join(" · ", parts),
            warning: missing is not null,
            secondaryLabel: row.CanRescan ? "Rescan" : null,
            secondaryActivate: row.CanRescan ? () => ExecuteAsync(row.RescanLibraryCommand) : null);
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
        // The commit and its date are the description of the version, not two more rows.
        yield return InformationRow(
            "about.version",
            "Version",
            _settings.HasCommitDate
                ? $"Built from {_settings.AppCommitDisplay} · {_settings.AppCommitDateDisplay}"
                : $"Built from {_settings.AppCommitDisplay}",
            _settings.AppVersionDisplay);
        if (_settings.HasUpdateChecker)
        {
            yield return ActionRow(
                "about.check-updates",
                "Check for updates",
                FirstNonEmpty(
                    UpdateStatusHint,
                    "Looks for a newer release on GitHub · only the public releases page is contacted"),
                _settings.IsUpdateBusy ? "Working…" : "A CHECK",
                _settings.CheckForUpdatesCommand,
                !_settings.IsUpdateBusy,
                excludeFromParity: true);
            if (_settings.IsUpdateAvailable)
            {
                yield return ActionRow(
                    "about.install-update",
                    "Install update",
                    "Downloads the new version and restarts · stays in gaming mode",
                    _settings.IsUpdateBusy ? "Working…" : "A UPDATE",
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
            // One account row: the name is its value, Y disconnects. The same rule as the Google Drive and
            // ScreenScraper rows — Y on an account row disconnects it — so no separate red row.
            yield return InformationRow(
                "retro.account",
                "Account",
                "Connected · " + FirstNonEmpty(
                    _settings.RetroAchievementsProgressText,
                    _settings.RetroAchievementsStatusText,
                    "achievement data is display-only in EmuShelf"),
                _settings.ConnectedAccountName ?? string.Empty) with
            {
                SecondaryKey = "retro.disconnect",
                SecondaryLabel = _settings.IsRetroAchievementsBusy ? null : "Disconnect",
                SecondaryActivate = () => ExecuteAsync(_settings.DisconnectRetroAchievementsCommand),
                SecondaryIsDestructive = true,
                SecondaryConfirmationTitle = "Disconnect RetroAchievements?",
                SecondaryConfirmationText = "EmuShelf will remove its saved account connection. Your RetroAchievements account and earned progress stay untouched.",
            };
            if (_settings.HasRetroAchievementsMatchRefresh)
            {
                yield return ActionRow(
                    "retro.refresh",
                    "Refresh game matches",
                    "Retries known games without rehashing unchanged ROMs",
                    _settings.IsRetroAchievementsBusy ? "Working…" : "A REFRESH",
                    _settings.RefreshRetroAchievementsMatchesCommand,
                    _settings.CanRefreshRetroAchievementsMatches);
            }
            yield break;
        }

        yield return TextRow(
            "retro.username",
            "Username",
            "Your RetroAchievements account name",
            _settings.RetroAchievementsUsername,
            false,
            value => _settings.RetroAchievementsUsername = value);
        yield return TextRow(
            "retro.api-key",
            "Web API key",
            "RetroAchievements → Control Panel → Keys · kept on this device, never in settings.json",
            _settings.RetroAchievementsApiKey,
            true,
            value => _settings.RetroAchievementsApiKey = value);
        var ready = _settings.RetroAchievementsUsername.Length > 0 && _settings.RetroAchievementsApiKey.Length > 0;
        yield return ActionRow(
            "retro.connect",
            "Connect",
            ready
                ? "Checks the account, then matches your library"
                : "Enter the username and key first · checks the account, then matches your library",
            _settings.IsRetroAchievementsBusy ? "Connecting…" : "A CONNECT",
            _settings.ConnectRetroAchievementsCommand,
            !_settings.IsRetroAchievementsBusy);
    }

    /// <summary>Couch-only: how the shelf itself is drawn. Both are excluded from the Desktop↔couch
    /// field sweep because Desktop deliberately offers neither — there is no field to be in parity with.
    /// They were the two rows stacked above the theme gallery; on their own page the gallery starts at
    /// the top and these stop competing with it for the first thing you look at.</summary>
    private IEnumerable<GamepadSettingsRowSpec> BuildDisplayRows()
    {
        yield return ToggleRow(
            "display.crt",
            "CRT screen effect",
            "curved, scanned tube on the shelf · costs GPU time",
            CrtScreenEffect,
            value => CrtScreenEffect = value) with { ExcludeFromParity = true };
        yield return ToggleRow(
            "display.ambient",
            "Match colours to game artwork",
            AmbientThemeFromArtwork
                ? "the interface takes its colours from the highlighted game"
                : "the theme is used everywhere",
            AmbientThemeFromArtwork,
            value => AmbientThemeFromArtwork = value) with { ExcludeFromParity = true };
    }

    private IEnumerable<GamepadSettingsRowSpec> BuildArtworkMetadataRows()
    {
        // Built-in catalogue: always available, no account needed. Same stable ids as Desktop's
        // built-in card so the two surfaces stay in parity. No group headers: six rows do not need three.
        yield return ToggleRow(
            "general.metadata-auto",
            "Fetch after import",
            "titles and covers from the bundled catalogue · game files never leave the device",
            _settings.AutomaticallyFetchMetadataAfterImport,
            value => _settings.AutomaticallyFetchMetadataAfterImport = value,
            onLabel: "Automatic",
            offLabel: "Manual");
        yield return ActionRow(
            "general.fetch-metadata",
            "Fetch missing metadata",
            FirstNonEmpty(
                _settings.MetadataProgressText,
                _settings.MetadataStatusText,
                "Fills missing titles and artwork for the current library"),
            _settings.IsMaintainingLibrary ? "Working…" : "A FETCH",
            _settings.FetchAllMetadataCommand,
            _settings.CanFetchAllMetadata);
        yield return ToggleRow(
            "metadata.web-image-search",
            "Web image search",
            "\"Set cover\" can search the web (DuckDuckGo) · results are never applied automatically, you always choose",
            _settings.WebImageSearchEnabled,
            value => _settings.WebImageSearchEnabled = value);

        if (_settings.IsScreenScraperConnected)
        {
            // One account row with the name as its value; Y disconnects (Y on an account row disconnects it).
            yield return InformationRow(
                "scraper.account",
                "ScreenScraper",
                "Connected · titles and artwork are fetched per game, on demand",
                _settings.ScreenScraperConnectedName ?? string.Empty) with
            {
                SecondaryKey = "scraper.disconnect",
                SecondaryLabel = _settings.IsScreenScraperBusy ? null : "Disconnect",
                SecondaryActivate = () => ExecuteAsync(_settings.DisconnectScreenScraperCommand),
                SecondaryIsDestructive = true,
                SecondaryConfirmationTitle = "Disconnect ScreenScraper?",
                SecondaryConfirmationText = "EmuShelf will remove its saved login. Your ScreenScraper account stays untouched.",
            };
            yield break;
        }

        // Signed out, signing in is one job, not three settings: a summary row that opens to its own
        // rows, exactly as a platform does in Emulators and Saves. The indent is what binds them, the
        // section name is said once, and the row is the same shape as the connected account row above.
        var expanded = IsPlatformExpanded(SettingsSection.ArtworkMetadata, ScraperExpansionId);
        yield return ExpandableSummaryRow(
            SettingsSection.ArtworkMetadata,
            "scraper.account.signin",
            "ScreenScraper",
            ScraperExpansionId,
            "Not signed in");
        if (!expanded)
            yield break;

        yield return TextRow(
            "scraper.username",
            "Username",
            "Your ScreenScraper account name",
            _settings.ScreenScraperUsername,
            false,
            value => _settings.ScreenScraperUsername = value,
            isGrouped: true);
        yield return TextRow(
            "scraper.password",
            "Password",
            "Sent to ScreenScraper to sign in · masked, never logged, never written to settings.json",
            _settings.ScreenScraperPassword,
            true,
            value => _settings.ScreenScraperPassword = value,
            isGrouped: true);
        // Mirrors the RetroAchievements Connect row: dimmed until both fields are filled, and saying so,
        // rather than offering a press that can only fail.
        var ready = _settings.ScreenScraperUsername.Length > 0 && _settings.ScreenScraperPassword.Length > 0;
        yield return ActionRow(
            "scraper.connect",
            "Sign in",
            ready
                ? "Checks the account, then artwork is fetched per game, on demand"
                : "Enter the username and password first · checks the account before it is used",
            _settings.IsScreenScraperBusy ? "Connecting…" : "A SIGN IN",
            _settings.ConnectScreenScraperCommand,
            ready && !_settings.IsScreenScraperBusy,
            isGrouped: true);
    }

    /// <summary>The one expandable group in Artwork &amp; Metadata; not a platform, so it needs a name of
    /// its own to key the section's single-open state by.</summary>
    private const string ScraperExpansionId = "screenscraper";

    private IEnumerable<GamepadSettingsRowSpec> BuildSaveRows()
    {
        // The connection is one row, first: "Google Drive" with the state on its line. Connected, A syncs
        // (or stops a running sync) and Y disconnects behind the existing confirmation; disconnected, A
        // connects. The Desktop disconnect button is reachable through Y, which the parity id records.
        if (_settings.IsCloudDisconnected)
        {
            yield return ActionRow(
                "saves.connect",
                "Google Drive",
                _settings.IsCloudBusy
                    ? "Connecting…"
                    : "Not connected · opens Google's sign-in in your browser",
                _settings.IsCloudBusy ? "Connecting…" : "Connect",
                _settings.ConnectCloudCommand,
                !_settings.IsCloudBusy);
            yield return TextRow(
                "saves.cloud-folder",
                "Cloud folder",
                "The Google Drive folder that stores EmuShelf save manifests and copies",
                _settings.CloudFolder,
                false,
                value => _settings.CloudFolder = value);
        }
        else
        {
            var busy = _settings.IsCloudBusy;
            yield return ActionRow(
                busy ? "saves.stop" : "saves.sync",
                "Google Drive",
                "Connected · " + FirstNonEmpty(
                    busy ? _settings.CloudSyncProgressText : string.Empty,
                    _settings.CloudStatusText,
                    "not synced yet"),
                busy ? "Stop sync" : "Sync now",
                busy ? _settings.CancelCloudSyncCommand : _settings.SyncCloudNowCommand,
                !busy || _settings.CancelCloudSyncCommand.CanExecute(null)) with
            {
                // The one-off sync and the disconnect behind its Y both stay in Settings; the wizard's
                // Saves step is for choices. Flagged rather than named by key, because this row's key
                // flips to saves.stop mid-sync and a key list silently stops matching.
                SettingsOnly = true,
                SecondaryKey = "saves.disconnect",
                SecondaryLabel = busy ? null : "Disconnect",
                SecondaryActivate = () => ExecuteAsync(_settings.DisconnectCloudCommand),
                SecondaryIsDestructive = true,
                SecondaryConfirmationTitle = "Disconnect Google Drive?",
                SecondaryConfirmationText = "EmuShelf will disable cloud sync. It will not delete local saves or anything already stored in Google Drive.",
            };
        }

        foreach (var platform in _settings.CloudPlatforms)
        {
            // One summary row per platform (artwork, name, what synced and when); A opens its rows beneath
            // it, one platform at a time, exactly as Emulators does — in the wizard's Saves step too (which
            // only adds that a platform still missing its folder opens itself once, see BuildSetupRows).
            var expanded = IsPlatformExpanded(SettingsSection.Saves, platform.SystemId);
            var (summary, attention) = SaveSummary(platform);
            var systemId = platform.SystemId;
            yield return PlatformSummaryRow(
                SettingsSection.Saves,
                $"saves.{systemId}.summary",
                platform.DisplayName,
                systemId,
                summary,
                attention);
            if (!expanded)
                continue;

            // No override, nothing detected: say so and ask for the folder, instead of the old "Use detected
            // emulator location" that read as if something had been found.
            var location = platform.NormalizedOverride ?? platform.DetectedDirectory
                ?? (platform.NeedsFolder ? "No folder set" : platform.HasProbed ? "Not available" : "Looking…");
            var detail = platform.NeedsFolder
                ? "No save folder found for this emulator · A picks the folder it saves to"
                : FirstNonEmpty(
                    platform.DetectionErrorText,
                    platform.CompatibilityWarning,
                    platform.LastNoticeText,
                    platform.LastResultText,
                    platform.SaveShapeDescription);
            yield return ActionRow(
                platform.FolderFieldId,
                "Save folder",
                detail,
                location,
                platform.PickDirectoryCommand,
                platform.IsIdle,
                GamepadSettingsRowKind.Folder,
                isGrouped: true,
                systemId: systemId) with
            {
                IsWarning = platform.NeedsFolder || platform.HasDetectionError,
            };
            if (platform.SupportsSaveStates)
            {
                yield return ToggleRow(
                    platform.SaveStatesFieldId,
                    "Save states",
                    "syncs manual states before launch and after exit · only when emulator version and CPU match",
                    platform.SyncSaveStates,
                    value => platform.SyncSaveStates = value,
                    platform.IsIdle,
                    isGrouped: true,
                    systemId: systemId);
                // Mirror Desktop: once states sync, the save-state folder gets its own override so a
                // mis-detected state folder can be corrected the same way as the save folder above.
                if (platform.SyncSaveStates)
                {
                    yield return ActionRow(
                        platform.StateFolderFieldId,
                        "Save-state folder",
                        "Leave it detected to follow the emulator · A picks another",
                        string.IsNullOrEmpty(platform.NormalizedStateOverride)
                            ? "Detected from the emulator"
                            : platform.NormalizedStateOverride,
                        platform.PickStateDirectoryCommand,
                        platform.IsIdle,
                        GamepadSettingsRowKind.Folder,
                        isGrouped: true,
                        systemId: systemId);
                }
            }
            if (_settings.IsCloudConnected)
            {
                yield return ActionRow(
                    platform.ReplaceCloudFieldId,
                    "Replace cloud saves",
                    "Local copies win · replaced cloud copies are backed up first",
                    "A REPLACE CLOUD",
                    platform.ReplaceCloudCommand,
                    platform.CanReplace,
                    isDestructive: true,
                    confirmationTitle: $"Replace {platform.DisplayName} cloud saves?",
                    confirmationText: "Local saves become authoritative for this platform. Replaced cloud copies are backed up before the upload.",
                    isGrouped: true,
                    systemId: systemId) with { SettingsOnly = true };
                yield return ActionRow(
                    platform.ReplaceLocalFieldId,
                    "Replace local saves",
                    "Cloud copies win · replaced local copies are backed up first",
                    "A REPLACE LOCAL",
                    platform.ReplaceLocalCommand,
                    platform.CanReplace,
                    isDestructive: true,
                    confirmationTitle: $"Replace {platform.DisplayName} local saves?",
                    confirmationText: "Cloud saves become authoritative for this platform. Replaced local copies are backed up before the download.",
                    isGrouped: true,
                    systemId: systemId) with { SettingsOnly = true };
            }
        }

        // Export is one row: A writes this device's saves, Y also includes the copies that live only in
        // Google Drive. Both Desktop buttons stay reachable (the second through Y), so parity holds
        // regardless of connection state; Y is only offered while connected, as Desktop only enables it then.
        yield return ActionRow(
            "saves.export.device",
            "Export saves",
            _settings.IsCloudConnected
                ? "A portable .zip of this device's saves, save states included · Y also includes cloud-only copies"
                : "A portable .zip of this device's saves, save states included",
            "A EXPORT",
            _settings.ExportDeviceSavesCommand,
            _settings.ExportDeviceSavesCommand.CanExecute(null)) with
        {
            SecondaryKey = "saves.export.cloud",
            SecondaryLabel = _settings.ExportDeviceAndCloudSavesCommand.CanExecute(null) ? "Include cloud" : null,
            SecondaryActivate = () => ExecuteAsync(_settings.ExportDeviceAndCloudSavesCommand),
        };

        if (_settings.HasSyncLog && _settings.CanRevealFiles)
        {
            // Actionable (opens the log in the OS viewer) rather than a dead read-only row where A
            // did nothing. Desktop exposes this as a hyperlink, so it is excluded from field parity.
            // Skipped on Android, where there is no OS viewer to hand the log path to.
            yield return ActionRow(
                "saves.log",
                "Sync activity log",
                "Portable, read-only record of previous save-sync actions · opens in your viewer",
                "A OPEN",
                _settings.OpenSyncLogCommand,
                enabled: true,
                excludeFromParity: true);
        }
    }

    /// <summary>The one line a platform's summary row carries: the problem if there is one (no folder, a
    /// detection error, a sync notice — painted as a warning), else the last sync result.</summary>
    private (string Text, bool Warning) SaveSummary(CloudSavePlatformRowViewModel platform)
    {
        if (platform.NeedsFolder)
            return ("No save folder", true);
        if (platform.HasDetectionError)
            return (platform.DetectionErrorText!, true);
        if (platform.HasLastNotice)
            return (platform.LastNoticeText!, true);
        return (FirstNonEmpty(
            platform.LastResultText,
            _settings.IsCloudConnected ? "Not synced yet" : string.Empty), false);
    }

    private IEnumerable<GamepadSettingsRowSpec> BuildTextureRows()
    {
        yield return ActionRow(
            "textures.rescan",
            "Rescan installed packs",
            FirstNonEmpty(
                _settings.TexturePackStatusText,
                _settings.TexturePackSummary,
                _settings.TexturePackLastScanText,
                "Reads every configured texture folder again · no pack or emulator setting is changed"),
            _settings.IsTexturePackBusy ? "Scanning…" : "A RESCAN",
            _settings.RescanTexturePacksCommand,
            !_settings.IsTexturePackBusy);
        yield return ChoiceRow(
            "textures.emulator-filter",
            "Emulator filter",
            "Limit the list to one emulator",
            _settings.TextureEmulatorFilter,
            _settings.TextureEmulatorFilters,
            value => _settings.TextureEmulatorFilter = value);
        yield return ChoiceRow(
            "textures.status-filter",
            "Status filter",
            "Matched · no game in your library · needs attention",
            _settings.TextureStatusFilter,
            _settings.TextureStatusFilters,
            value => _settings.TextureStatusFilter = value);

        foreach (var platform in _settings.TexturePlatforms)
        {
            // One summary row per platform that opens to its texture folder, as in Emulators and Saves. The
            // folder row is A = pick, Y = back to the folder detected from the emulator; "Use detected folder"
            // no longer needs a row of its own.
            var systemId = platform.SystemId;
            var expanded = IsPlatformExpanded(SettingsSection.TexturePacks, systemId);
            var hasOverride = platform.DirectoryOverride.Length > 0;
            var folder = hasOverride ? platform.DirectoryOverride : platform.DetectedRoot ?? "No folder detected";
            var status = FirstNonEmpty(platform.StatusText, platform.LoadingText);
            yield return PlatformSummaryRow(
                SettingsSection.TexturePacks,
                $"textures.{systemId}.summary",
                platform.DisplayName,
                systemId,
                FirstNonEmpty(status, folder));
            if (!expanded)
                continue;
            yield return ActionRow(
                platform.FolderFieldId,
                "Texture folder",
                hasOverride
                    ? FirstNonEmpty(status, "Your folder") + " · Y returns to the folder detected from the emulator"
                    : FirstNonEmpty(status, "Detected from the emulator"),
                folder,
                new AsyncRelayCommand(() => ExecuteAsync(_settings.BrowseTextureOverrideCommand, platform)),
                !_settings.IsTexturePackBusy,
                GamepadSettingsRowKind.Folder,
                isGrouped: true,
                systemId: systemId) with
            {
                SecondaryKey = platform.DetectedFieldId,
                SecondaryLabel = hasOverride && !_settings.IsTexturePackBusy ? "Use detected" : null,
                SecondaryActivate = () => ExecuteAsync(_settings.ClearTextureOverrideCommand, platform),
            };
        }

        var entries = _settings.TexturePackEntries;
        if (entries.Count == 0)
        {
            yield return InformationRow(
                "textures.empty",
                "No texture packs to show",
                "Rescan after configuring an emulator, or change the filters above",
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
                ? "Read-only inventory · A hides the pack list again"
                : "Read-only inventory · A lists every matched pack",
            _texturePackListExpanded ? "Hide" : "Show",
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
                "Narrow the emulator or status filter above, or browse the full inventory in Desktop Settings",
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
            value.Length == 0 ? "Not entered" : isSecret ? "••••••••" : value,
            isSecret ? GamepadSettingsRowKind.Secret : GamepadSettingsRowKind.Text,
            Activate: () =>
            {
                BeginTextEntry(label, description, value, isSecret, commit);
                return Task.CompletedTask;
            },
            IsGrouped: isGrouped);

    private GamepadSettingsRowSpec ToggleRow(
        string key,
        string label,
        string description,
        bool value,
        Action<bool> set,
        bool enabled = true,
        string onLabel = "On",
        string offLabel = "Off",
        bool isGrouped = false,
        string? systemId = null,
        bool stateFirst = true)
    {
        // The switch draws no words; the description opens with the state ("Off · …") so the row reads
        // without looking at the switch. A warning that replaces the description is left alone.
        var state = value ? onLabel : offLabel;
        void Toggle(int _) => RunLocalEdit(() => set(!value));
        return new GamepadSettingsRowSpec(
            key,
            label,
            stateFirst ? (description.Length == 0 ? state : $"{state} · {description}") : description,
            state,
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
        // Both cached statuses are computed before RefreshSetupRail, which reads them: the wizard chip
        // would otherwise render the previous rebuild's reading.
        _emulatorsRailStatus = ComputeEmulatorsRailStatus();
        ComputeSavesRailStatus();
        RefreshSetupRail();
        OnPropertyChanged(nameof(LibraryRailStatus));
        OnPropertyChanged(nameof(EmulatorsRailStatus));
        OnPropertyChanged(nameof(IsEmulatorsRailWarning));
        OnPropertyChanged(nameof(RetroAchievementsRailStatus));
        OnPropertyChanged(nameof(ArtworkRailStatus));
        OnPropertyChanged(nameof(SavesRailStatus));
        OnPropertyChanged(nameof(IsSavesRailWarning));
        OnPropertyChanged(nameof(ThemesRailStatus));
        OnPropertyChanged(nameof(DisplayRailStatus));
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
        SetupStep.StorageAccess => "Storage access",
        SetupStep.DataFolder => "Data folder",
        SetupStep.SecondScreen => "Playing on the second screen",
        SetupStep.ClosingGames => "Closing games",
        SetupStep.GamesAndEmulators => "Games & emulators",
        SetupStep.Saves => "Saves",
        _ => "Setup",
    };

    private string SetupDescription => CurrentSetupStep switch
    {
        SetupStep.StorageAccess => "EmuShelf reads your games and keeps its library on this device's storage. Android asked you to allow this once.",
        SetupStep.DataFolder => "Where EmuShelf keeps its library, covers, settings and saves. Your game files are never moved.",
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

    // Same treatment for the all-files grant: the pre-boot page answered it, but Android can take it away
    // afterwards, so the step reports the live reading rather than asserting it is held. Cached per screen
    // and re-read on a foreground return, which is when the user comes back from flipping the switch.
    private bool IsStoragePermissionGranted
    {
        get
        {
            if (_setup?.IsStoragePermissionGranted is not { } probe)
                return true;
            if (!_storageGrantedRead)
            {
                _cachedStorageGranted = probe();
                _storageGrantedRead = true;
            }
            return _cachedStorageGranted;
        }
    }

    private IEnumerable<GamepadSettingsRowSpec> BuildSetupRows()
    {
        switch (CurrentSetupStep)
        {
            case SetupStep.StorageAccess:
                yield return IsStoragePermissionGranted
                    ? InformationRow(
                        "setup.storage.grant",
                        "Allow access to all files",
                        "Allowed. Android remembers this until you turn it off in its settings.",
                        "Allowed")
                    : new GamepadSettingsRowSpec(
                        "setup.storage.grant",
                        "Allow access to all files",
                        "Not allowed. EmuShelf can't read your games or its own folder until it is. A opens Android's permission page.",
                        _setup?.RequestStoragePermission is null ? string.Empty : "A OPEN",
                        _setup?.RequestStoragePermission is null
                            ? GamepadSettingsRowKind.Information
                            : GamepadSettingsRowKind.Action,
                        Activate: _setup?.RequestStoragePermission is null
                            ? null
                            : () =>
                            {
                                _setup.RequestStoragePermission();
                                // The answer lands on foreground return; drop the cached reading so the
                                // rebuild that follows re-asks.
                                _storageGrantedRead = false;
                                return Task.CompletedTask;
                            },
                        IsWarning: true,
                        ExcludeFromParity: true);
                yield return InformationRow(
                    "setup.storage.why",
                    "What this is used for",
                    "Reading your games where they are, and writing only inside EmuShelf's own folder. Games are never moved or deleted.",
                    string.Empty);
                break;
            case SetupStep.DataFolder:
                yield return InformationRow(
                    "setup.folder.current",
                    "Data folder",
                    _settings.HasDataDirectory ? _settings.DataDirectory ?? string.Empty : "Not set",
                    string.Empty);
                // Deliberately read-only here, unlike the Library section's row: changing the data folder
                // restarts the process, which would take every answer given so far with it (they are only
                // written by Save, at Finish or on the way out). The step reports the folder; moving it is
                // Settings → Library, where there is nothing in flight to lose.
                yield return InformationRow(
                    "setup.folder.change",
                    "Want it somewhere else?",
                    "Settings → Library → \"Data folder\" moves it. EmuShelf restarts into the new folder and your existing data stays where it is.",
                    string.Empty);
                break;
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
                if (CloseEmulatorOnReturn && CloseOnReturnWarning is not null && _grantCloseOnReturnPrivilege is not null)
                {
                    // The permission gets a row of its own, like storage access and the second screen do,
                    // rather than living only behind Y on the toggle.
                    yield return new GamepadSettingsRowSpec(
                        "setup.closing-games.allow-shizuku",
                        "Allow Shizuku",
                        "Shizuku's permission dialog opens. Start Shizuku first if it is not running.",
                        "A ALLOW",
                        GamepadSettingsRowKind.Action,
                        Activate: async () =>
                        {
                            await _grantCloseOnReturnPrivilege();
                            _closeOnReturnWarningRead = false;
                        },
                        IsWarning: true,
                        ExcludeFromParity: true);
                }
                yield return InformationRow(
                    "setup.closing-games.why",
                    "Why Shizuku",
                    "Android does not let one app close another. Shizuku is a small helper that can. Without it the emulator keeps running in the background.",
                    string.Empty);
                break;
            case SetupStep.GamesAndEmulators:
                foreach (var row in BuildEmulatorsRows())
                    yield return row with { IsCompact = true };
                break;
            case SetupStep.Saves:
            {
                // The step is for choices (connect, save folders, state sync); the one-off "sync all now",
                // the disconnect behind its Y and the per-platform replace actions stay in Settings, and
                // say so on the spec itself. A platform whose save folder could not be detected is the one
                // thing the user must act on here, so its folder row is painted as a warning and says so.
                // The step is Settings' Saves section — the same summary cards, one open at a time — except
                // that the first platform still missing its folder opens itself once, so the row the user
                // must act on is on screen without a press. The folder probe can land after the step is
                // entered, which is why this runs on every rebuild until it has fired, and it never
                // overrides a platform the user opened.
                var needsFolder = SavePlatformsNeedingAFolder().Select(platform => platform.FolderFieldId).ToHashSet(StringComparer.Ordinal);
                if (!_savesStepOpenedMissingFolder
                    && !_expandedBySection.ContainsKey(SettingsSection.Saves)
                    && SavePlatformsNeedingAFolder().FirstOrDefault() is { } needing)
                {
                    _expandedBySection[SettingsSection.Saves] = needing.SystemId;
                    _savesStepOpenedMissingFolder = true;
                }
                foreach (var row in BuildSaveRows())
                {
                    if (row.SettingsOnly)
                        continue;
                    yield return (needsFolder.Contains(row.Key) ? row with { IsWarning = true } : row) with { IsCompact = true };
                }
                break;
            }
        }
    }

    /// <summary>Save platforms with nothing detected (or a detection error) and no manual folder yet.</summary>
    private IEnumerable<CloudSavePlatformRowViewModel> SavePlatformsNeedingAFolder() =>
        _settings.CloudPlatforms.Where(platform =>
            platform.NeedsFolder
            || (!string.IsNullOrEmpty(platform.DetectionErrorText) && string.IsNullOrEmpty(platform.NormalizedOverride)));

    private void RefreshSetupRail()
    {
        if (_setup is null)
            return;

        foreach (var entry in SetupRail.Steps)
        {
            var (status, warning, done) = entry.Step switch
            {
                SetupStep.StorageAccess => IsStoragePermissionGranted
                    ? ("Allowed", false, true)
                    : ("Not allowed", true, false),
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
                // A folder still to pick is the step's own business and names itself; anything else the
                // rail is warning about (a detection error, a sync notice) carries its warning through
                // rather than being painted as a finished step.
                SetupStep.Saves => SavePlatformsNeedingAFolder().Count() is > 0 and var needed
                    ? (needed == 1 ? "1 folder needed" : $"{needed} folders needed", true, false)
                    : IsSavesRailWarning
                        ? (SavesRailStatus, true, false)
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
            && !_expandedBySection.ContainsKey(SettingsSection.Emulators)
            && _gameCountBySystem is not null
            && _settings.Rows.Count > 0
            && _settings.Rows.Sum(row => _gameCountBySystem(row.SystemId)) == 0)
        {
            _expandedBySection[SettingsSection.Emulators] = _settings.Rows[0].SystemId;
        }
    }

    // The row to land on when a step opens: the first save folder that still needs picking, else the top.
    private string? PreferredSetupRowKey() => CurrentSetupStep == SetupStep.Saves
        ? SavePlatformsNeedingAFolder().Select(platform => $"saves.{platform.SystemId}.folder").FirstOrDefault()
        : null;

    /// <summary>
    /// True once the wizard's last step was finished with Save. The host records
    /// <c>SetupCompletedVersion</c> against this rather than against "the settings were saved", because
    /// leaving early also saves — it just does not count as having walked the wizard.
    /// </summary>
    public bool SetupCompleted { get; private set; }

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

        // Finish = the ordinary Save: it persists every edit and raises CloseRequested, and the flag above
        // is what tells the host this was the end of the wizard rather than a save on the way out.
        SetupCompleted = true;
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
            _ = LeaveSetupAsync();
    }

    /// <summary>
    /// B on the first step: out of the wizard, keeping what was answered. Close-on-return and the
    /// per-platform save folders are written only by Save, so simply closing would silently discard every
    /// choice made on the way here — after which the wizard would open again next launch and ask for them
    /// a second time. The wizard is still not recorded as completed (<see cref="SetupCompleted"/> stays
    /// false), so it is offered again; the answers are just already in place when it is.
    /// </summary>
    private async Task LeaveSetupAsync()
    {
        await ExecuteAsync(_settings.SaveCommand);
        // A successful save raises CloseRequested itself (and this projection is disposed with it). If it
        // failed it did not, and B still has to get the user out.
        if (!_disposed)
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
