using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;
using EmuShelf.Core.Input;
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
}

/// <summary>A single controller-sized row projected from the existing Desktop settings model.</summary>
public partial class GamepadSettingsRowViewModel : ObservableObject
{
    private readonly GamepadSettingsViewModel _owner;

    internal GamepadSettingsRowViewModel(GamepadSettingsViewModel owner, GamepadSettingsRowSpec spec)
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
        _ when Key.Contains("fetch", StringComparison.Ordinal) ||
            Key.Contains("rclone", StringComparison.Ordinal) => "↓",
        _ when Key.Contains("detected", StringComparison.Ordinal) => "↶",
        _ => "›",
    };
    internal Func<Task>? Activate { get; private set; }
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
        Adjust = spec.Adjust;
        ConfirmationTitle = spec.ConfirmationTitle;
        ConfirmationText = spec.ConfirmationText;
        OnPropertyChanged(string.Empty);
        OnPropertyChanged(nameof(CanActivate));
        OnPropertyChanged(nameof(ParityId));
        OnPropertyChanged(nameof(IsHeader));
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
    bool ExcludeFromParity = false);

/// <summary>
/// Controller projection over <see cref="EmulatorSettingsViewModel"/>. It owns only navigation,
/// draft entry, and confirmation state; all values, validation, operations, and persistence remain
/// in the existing settings view model and services.
/// </summary>
public partial class GamepadSettingsViewModel : ViewModelBase, IDisposable
{
    private const int ThemeColumns = 3;

    private readonly EmulatorSettingsViewModel _settings;
    private readonly IOnScreenKeyboardService _onScreenKeyboard;
    private readonly Dictionary<SettingsSection, string> _focusedRowBySection = [];
    private readonly IReadOnlyList<ThemeChoiceViewModel> _themeChoices;
    private readonly Func<ThemePreference, Task>? _applyTheme;
    private Func<Task>? _pendingConfirmation;
    private Action<string>? _commitText;
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

    public bool IsNormal => !IsTextEntryOpen && !IsConfirmationOpen;

    public IReadOnlyList<ThemeChoiceViewModel> ThemeChoices => _themeChoices;

    public bool ShowThemes => _themeChoices.Count > 0;

    /// <summary>The row list is shown for the four model sections; the gallery replaces it on Themes.</summary>
    public bool IsRowsVisible => IsNormal && !IsThemesSection;

    public bool IsThemesVisible => IsNormal && IsThemesSection;

    public bool IsAutomaticKeyboardAvailable => _onScreenKeyboard.IsSupported;

    public string KeyboardHint => IsAutomaticKeyboardAvailable
        ? "The on-screen keyboard will open automatically. A keyboard also works."
        : "Use Steam + X for the Steam keyboard, or type with a hardware keyboard.";

    public GamepadSettingsRowViewModel? FocusedRow => Rows.Count == 0
        ? null
        : Rows[Math.Clamp(FocusedRowIndex, 0, Rows.Count - 1)];

    public GamepadSettingsRowViewModel? SaveRow => Rows.FirstOrDefault(row => row.IsSaveRow);

    public string SectionTitle => IsThemesSection ? "Themes" : SelectedSection switch
    {
        SettingsSection.RetroAchievements => "RetroAchievements",
        SettingsSection.ScreenScraper => "ScreenScraper",
        SettingsSection.Saves => "Saves",
        SettingsSection.TexturePacks => "Texture Packs",
        _ => "General",
    };

    public string SectionDescription => IsThemesSection
        ? "Personalize EmuShelf's colors. A theme applies instantly and is shared with Desktop mode."
        : SelectedSection switch
    {
        SettingsSection.RetroAchievements =>
            "Read achievement sets and your progress. Emulators still own unlocks and submission.",
        SettingsSection.ScreenScraper =>
            "Sign in to fetch titles and artwork from ScreenScraper. Game files are never uploaded.",
        SettingsSection.Saves =>
            "Reconcile emulator saves through your own rclone remote. Game files are never included.",
        SettingsSection.TexturePacks =>
            "Inspect installed replacement textures without changing packs or emulator configuration.",
        _ => "Library visibility, metadata consent, and safe maintenance.",
    };

    public string StatusText => IsThemesSection ? string.Empty : SelectedSection switch
    {
        SettingsSection.RetroAchievements => FirstNonEmpty(
            _settings.RetroAchievementsProgressText,
            _settings.RetroAchievementsStatusText),
        SettingsSection.ScreenScraper => _settings.ScreenScraperStatusText,
        SettingsSection.Saves => FirstNonEmpty(
            _settings.CloudSyncProgressText,
            _settings.CloudStatusText),
        SettingsSection.TexturePacks => FirstNonEmpty(
            _settings.TexturePackStatusText,
            _settings.TexturePackSummary,
            _settings.TexturePackLastScanText),
        _ => FirstNonEmpty(
            _settings.StatusText,
            _settings.MetadataProgressText,
            _settings.MetadataStatusText,
            _settings.MaintenanceStatusText),
    };

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);
    public bool IsGeneralSection => !IsThemesSection && SelectedSection == SettingsSection.General;
    public bool IsRetroAchievementsSection => !IsThemesSection && SelectedSection == SettingsSection.RetroAchievements;
    public bool IsScreenScraperSection => !IsThemesSection && SelectedSection == SettingsSection.ScreenScraper;
    public bool IsSavesSection => !IsThemesSection && SelectedSection == SettingsSection.Saves;
    public bool IsTexturePacksSection => !IsThemesSection && SelectedSection == SettingsSection.TexturePacks;

    public event Action<bool>? CloseRequested;

    public GamepadSettingsViewModel(
        EmulatorSettingsViewModel settings,
        IOnScreenKeyboardService? onScreenKeyboard = null,
        IReadOnlyList<ThemeChoiceViewModel>? themeChoices = null,
        Func<ThemePreference, Task>? applyTheme = null)
    {
        _settings = settings;
        _onScreenKeyboard = onScreenKeyboard ?? UnsupportedOnScreenKeyboardService.Instance;
        _themeChoices = themeChoices ?? [];
        _applyTheme = applyTheme;
        // Emulators is a Desktop-only slice for now; Themes is presented as the gamepad gallery page
        // rather than a projected row section, so both are excluded from the derived section list.
        Sections = settings.Sections
            .Where(section => section is not (SettingsSection.Emulators or SettingsSection.Themes))
            .ToArray();

        _settings.PropertyChanged += OnSettingsPropertyChanged;
        _settings.CloseRequested += OnSettingsCloseRequested;
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

        // The left section rail is a focus column of its own: Up/Down move sections, Right/A return
        // to the content, and LB/RB still work as a shortcut.
        if (IsRailFocused)
        {
            switch (action)
            {
                case GamepadAction.NavigateUp:
                case GamepadAction.PreviousPlatform:
                    MoveSection(-1);
                    return true;
                case GamepadAction.NavigateDown:
                case GamepadAction.NextPlatform:
                    MoveSection(1);
                    return true;
                case GamepadAction.NavigateRight:
                case GamepadAction.Confirm:
                    ExitRailToContent();
                    return true;
                case GamepadAction.NavigateLeft:
                    return true;
                case GamepadAction.Cancel:
                    CloseRequested?.Invoke(false);
                    return true;
                case GamepadAction.Menu:
                    if (SaveRow is { } railSave)
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
                    // Left from the first column steps out to the section rail.
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
                // Left leaves the content for the section rail; values are changed with A (or Right).
                EnterRail();
                return true;
            case GamepadAction.NavigateRight:
                AdjustFocused(1);
                return true;
            case GamepadAction.Confirm:
                _ = ActivateFocusedAsync();
                return true;
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

    public void MoveSection(int delta)
    {
        if (!IsNormal)
            return;

        var pageCount = Sections.Count + (ShowThemes ? 1 : 0);
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
        if (IsThemesSection)
            return Sections.Count;
        var index = Sections.ToList().IndexOf(SelectedSection);
        return index < 0 ? 0 : index;
    }

    private void SelectPage(int index)
    {
        RememberFocusedRow();
        if (index >= Sections.Count)
        {
            EnterThemes();
            return;
        }

        if (IsThemesSection)
            IsThemesSection = false;
        SelectedSection = Sections[Math.Clamp(index, 0, Sections.Count - 1)];
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

    internal async Task FocusAndActivateAsync(GamepadSettingsRowViewModel row)
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
        _commitText = commit;
        IsTextEntryOpen = true;
        TextEntryRevision++;
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
        OnPropertyChanged(nameof(IsGeneralSection));
        OnPropertyChanged(nameof(IsRetroAchievementsSection));
        OnPropertyChanged(nameof(IsScreenScraperSection));
        OnPropertyChanged(nameof(IsSavesSection));
        OnPropertyChanged(nameof(IsTexturePacksSection));
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
        OnPropertyChanged(nameof(IsRetroAchievementsSection));
        OnPropertyChanged(nameof(IsScreenScraperSection));
        OnPropertyChanged(nameof(IsSavesSection));
        OnPropertyChanged(nameof(IsTexturePacksSection));
        RebuildRows(_focusedRowBySection.GetValueOrDefault(value));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasStatus));
        FocusRevision++;
    }

    partial void OnFocusedRowIndexChanged(int value)
    {
        for (var index = 0; index < Rows.Count; index++)
            Rows[index].IsFocused = index == value;
        RememberFocusedRow();
        OnPropertyChanged(nameof(FocusedRow));
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
        OnPropertyChanged(nameof(SaveRow));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasStatus));
    }

    private IEnumerable<GamepadSettingsRowSpec> BuildRows()
    {
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
            SettingsSection.RetroAchievements => BuildRetroAchievementsRows(),
            SettingsSection.ScreenScraper => BuildScreenScraperRows(),
            SettingsSection.Saves => BuildSaveRows(),
            SettingsSection.TexturePacks => BuildTextureRows(),
            _ => BuildGeneralRows(),
        })
        {
            yield return row;
        }
    }

    private IEnumerable<GamepadSettingsRowSpec> BuildGeneralRows()
    {
        yield return ToggleRow(
            "general.empty-platforms",
            "Empty platforms",
            "Empty platforms stay available when adding games and configuring emulators. Platforms with temporarily unavailable games remain visible.",
            _settings.ShowEmptyPlatforms,
            value => _settings.ShowEmptyPlatforms = value,
            onLabel: "SHOW",
            offLabel: "HIDE");
        yield return ToggleRow(
            "general.metadata-auto",
            "Metadata and artwork",
            "Titles and individual covers are downloaded only after you opt in. Game files and paths are never uploaded.",
            _settings.AutomaticallyFetchMetadataAfterImport,
            value => _settings.AutomaticallyFetchMetadataAfterImport = value,
            onLabel: "AUTO",
            offLabel: "MANUAL");
        yield return ActionRow(
            "general.rescan",
            "Rescan all consoles",
            "Recheck every console's remembered folders. Individual rescans remain available per platform in Desktop Settings.",
            _settings.IsMaintainingLibrary ? "WORKING" : "A RESCAN",
            _settings.RescanAllCommand,
            _settings.CanRescanAll);
        yield return ActionRow(
            "general.fetch-metadata",
            "Fetch missing metadata",
            "Fill missing titles and artwork for the current library after your metadata opt-in.",
            _settings.IsMaintainingLibrary ? "WORKING" : "A FETCH",
            _settings.FetchAllMetadataCommand,
            _settings.CanFetchAllMetadata);
    }

    private IEnumerable<GamepadSettingsRowSpec> BuildRetroAchievementsRows()
    {
        if (_settings.IsRetroAchievementsConnected)
        {
            yield return InformationRow(
                "retro.account",
                "Connected account",
                "Achievement data is display-only in EmuShelf.",
                _settings.ConnectedAccountName ?? string.Empty);
            if (_settings.HasRetroAchievementsMatchRefresh)
            {
                yield return ActionRow(
                    "retro.refresh",
                    "Refresh game matches",
                    "Refresh catalogues and retry known games without rehashing unchanged ROMs.",
                    _settings.IsRetroAchievementsBusy ? "WORKING" : "A REFRESH",
                    _settings.RefreshRetroAchievementsMatchesCommand,
                    _settings.CanRefreshRetroAchievementsMatches);
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
                confirmationText: "EmuShelf will remove its saved account connection. Your RetroAchievements account and earned progress stay untouched.");
            yield break;
        }

        yield return TextRow(
            "retro.username",
            "Username",
            "Your RetroAchievements account name.",
            _settings.RetroAchievementsUsername,
            false,
            value => _settings.RetroAchievementsUsername = value);
        yield return TextRow(
            "retro.api-key",
            "Web API key",
            "From RetroAchievements Control Panel → Keys. It is masked, never logged, and never written to settings.json.",
            _settings.RetroAchievementsApiKey,
            true,
            value => _settings.RetroAchievementsApiKey = value);
        yield return ActionRow(
            "retro.connect",
            "Connect",
            "Validate the account, match your library, and fetch progress.",
            _settings.IsRetroAchievementsBusy ? "CONNECTING…" : "A CONNECT",
            _settings.ConnectRetroAchievementsCommand,
            !_settings.IsRetroAchievementsBusy);
    }

    private IEnumerable<GamepadSettingsRowSpec> BuildScreenScraperRows()
    {
        if (_settings.IsScreenScraperConnected)
        {
            yield return InformationRow(
                "scraper.account",
                "Connected account",
                "Titles and artwork are fetched on demand from the per-game scraper.",
                _settings.ScreenScraperConnectedName ?? string.Empty);
            yield return ActionRow(
                "scraper.disconnect",
                "Disconnect ScreenScraper",
                "Remove the locally stored login. Your ScreenScraper account is not changed.",
                "A DISCONNECT",
                _settings.DisconnectScreenScraperCommand,
                !_settings.IsScreenScraperBusy,
                isDestructive: true,
                confirmationTitle: "Disconnect ScreenScraper?",
                confirmationText: "EmuShelf will remove its saved login. Your ScreenScraper account stays untouched.");
            yield break;
        }

        yield return TextRow(
            "scraper.username",
            "Username",
            "Your ScreenScraper account name.",
            _settings.ScreenScraperUsername,
            false,
            value => _settings.ScreenScraperUsername = value);
        yield return TextRow(
            "scraper.password",
            "Password",
            "Passed directly to ScreenScraper to sign in. It is masked, never logged, and never written to settings.json.",
            _settings.ScreenScraperPassword,
            true,
            value => _settings.ScreenScraperPassword = value);
        yield return ActionRow(
            "scraper.connect",
            "Connect",
            "Validate the account so per-game scraping can fetch titles and artwork.",
            _settings.IsScreenScraperBusy ? "CONNECTING…" : "A CONNECT",
            _settings.ConnectScreenScraperCommand,
            !_settings.IsScreenScraperBusy);
    }

    private IEnumerable<GamepadSettingsRowSpec> BuildSaveRows()
    {
        if (_settings.IsRcloneMissing)
        {
            yield return ActionRow(
                "saves.rclone",
                "Get rclone",
                $"Cloud sync needs rclone. EmuShelf installs it at {_settings.RcloneExpectedPath}.",
                _settings.IsDownloadingRclone ? "DOWNLOADING…" : "A DOWNLOAD",
                _settings.DownloadRcloneCommand,
                !_settings.IsDownloadingRclone);
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

        if (_settings.IsCloudDisconnected)
        {
            yield return TextRow(
                "saves.remote",
                "rclone remote name",
                "The local rclone remote that owns your Google Drive connection.",
                _settings.CloudRemoteName,
                false,
                value => _settings.CloudRemoteName = value);
            yield return TextRow(
                "saves.cloud-folder",
                "Cloud folder",
                "The folder inside the remote that stores EmuShelf save manifests and copies.",
                _settings.CloudFolder,
                false,
                value => _settings.CloudFolder = value);
            yield return ActionRow(
                "saves.oauth-json",
                "Import Google OAuth client JSON",
                "Optional. A personal client avoids shared-client rate limits. Its secret passes directly to rclone and is never retained by EmuShelf.",
                string.IsNullOrWhiteSpace(_settings.CloudClientId) ? "A CHOOSE FILE" : "CLIENT LOADED",
                _settings.ImportGoogleClientCommand,
                !_settings.IsCloudBusy,
                GamepadSettingsRowKind.File);
            yield return ActionRow(
                "saves.connect",
                "Connect Google Drive",
                "Open Google's sign-in through rclone and enable the configured save platforms.",
                _settings.IsCloudBusy ? "CONNECTING…" : "A CONNECT",
                _settings.ConnectCloudCommand,
                !_settings.IsCloudBusy && !_settings.IsRcloneMissing);
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

        if (_settings.HasSyncLog)
        {
            yield return InformationRow(
                "saves.log",
                "Sync activity log",
                "Portable, read-only record of previous save-sync actions.",
                _settings.SyncLogPath);
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
        Action<string> commit) => new(
            key,
            label,
            description,
            isSecret ? (value.Length == 0 ? "Not entered" : "••••••••") : value,
            isSecret ? GamepadSettingsRowKind.Secret : GamepadSettingsRowKind.Text,
            Activate: () =>
            {
                BeginTextEntry(label, description, value, isSecret, commit);
                return Task.CompletedTask;
            });

    private static GamepadSettingsRowSpec HeaderRow(string key, string label, string systemId) =>
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
        Action<string> set)
    {
        void Move(int delta)
        {
            if (choices.Count == 0)
                return;
            var index = choices.ToList().IndexOf(value);
            if (index < 0)
                index = 0;
            RunLocalEdit(() => set(choices[Math.Clamp(index + Math.Sign(delta), 0, choices.Count - 1)]));
        }

        return new GamepadSettingsRowSpec(
            key,
            label,
            description,
            value,
            GamepadSettingsRowKind.Choice,
            Activate: () =>
            {
                Move(1);
                return Task.CompletedTask;
            },
            Adjust: Move);
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
        string? systemId = null) => new(
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
            IsGrouped: isGrouped);

    private static GamepadSettingsRowSpec InformationRow(
        string key,
        string label,
        string description,
        string value) => new(
            key,
            label,
            description,
            value,
            GamepadSettingsRowKind.Information,
            IsEnabled: true);

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
        RebuildRows();
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasStatus));
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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _settings.PropertyChanged -= OnSettingsPropertyChanged;
        _settings.CloseRequested -= OnSettingsCloseRequested;
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
