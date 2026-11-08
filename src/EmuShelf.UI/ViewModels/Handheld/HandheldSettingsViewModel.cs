using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.App.Services;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.ViewModels;

/// <summary>One section in Settings' list on the Launcher (Library, Emulators, …, Themes, Display, About).</summary>
public sealed partial class HandheldSettingsSectionViewModel(SettingsSection section, string title) : ObservableObject
{
    /// <summary>The section; <see cref="SettingsSection.Themes"/> is the theme gallery.</summary>
    public SettingsSection Section { get; } = section;

    public string Title { get; } = title;

    /// <summary>A word or two on what needs a look ("Steam needs attention", "1 connected"); empty hides it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string Status { get; set; } = string.Empty;

    public bool HasStatus => !string.IsNullOrEmpty(Status);

    [ObservableProperty]
    public partial bool IsWarning { get; set; }

    /// <summary>The section on show.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>The selector is on it: the list of sections has the pad.</summary>
    [ObservableProperty]
    public partial bool IsFocused { get; set; }
}

/// <summary>What the Stage's Settings card says: the detail half of Settings' master-detail (M45 H6a).</summary>
public sealed record HandheldSettingsCard(string Eyebrow, string Title, string Value, string Description, string Status,
    bool IsWarning);

/// <summary>
/// Settings in Handheld mode (M45 H6a): Gamepad mode's controller projection (<see cref="GamepadSettingsViewModel"/>,
/// the same sections, rows, row kinds, choosers and confirmations, the same Save and close) laid across the two screens.
/// The Launcher lists the sections and the rows; the Stage shows the focused row's value and description
/// (<see cref="Card"/>). Text rows type on the drawn keyboard (<see cref="Keyboard"/>), since Android's own cannot
/// open on the Thor's second screen. Everything else is the projection's: this only routes the pad and says what to show.
/// </summary>
public sealed partial class HandheldSettingsViewModel : ObservableObject, IDisposable
{
    private bool _disposed;

    public HandheldSettingsViewModel(GamepadSettingsViewModel projection)
    {
        Projection = projection;
        foreach (var page in projection.Pages)
            Sections.Add(new HandheldSettingsSectionViewModel(page, TitleOf(page)));
        projection.PropertyChanged += OnProjectionChanged;
        projection.Rows.CollectionChanged += (_, _) => RaiseChanged();
        SyncSections();
        SyncKeyboard();
    }

    /// <summary>The projection Gamepad mode's Settings overlay shows: all values and actions live there.</summary>
    public GamepadSettingsViewModel Projection { get; }

    public ObservableCollection<HandheldSettingsSectionViewModel> Sections { get; } = [];

    public ObservableCollection<GamepadSettingsRowViewModel> Rows => Projection.Rows;

    public IReadOnlyList<ThemeChoiceViewModel> ThemeChoices => Projection.ThemeChoices;

    public ObservableCollection<GamepadChoiceOptionViewModel> ChoiceOptions => Projection.ChoiceOptions;

    public string SectionTitle => Projection.SectionTitle;

    public bool IsThemes => Projection.IsThemesSection;

    public bool IsRows => !Projection.IsThemesSection;

    public bool IsChoicePickerOpen => Projection.IsChoicePickerOpen;

    public bool IsConfirmationOpen => Projection.IsConfirmationOpen;

    /// <summary>The confirmation's two answers: Cancel (focused first) and the action.</summary>
    public bool IsCancelFocused => Projection.IsConfirmationOpen && !Projection.IsConfirmChoiceSelected;

    public bool IsConfirmFocused => Projection.IsConfirmationOpen && Projection.IsConfirmChoiceSelected;

    /// <summary>The drawn keyboard while a text row is being edited; null otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTyping))]
    public partial HandheldTextKeyboardViewModel? Keyboard { get; private set; }

    public bool IsTyping => Keyboard is not null;

    /// <summary>Raised whenever what the Stage or the hints show may have changed.</summary>
    public event Action? Changed;

    /// <summary>Pad input while Settings is open. Every button is Settings' own (none reaches home behind it).</summary>
    public bool Dispatch(GamepadAction action)
    {
        if (_disposed)
            return true;
        if (Keyboard is { } keyboard)
        {
            DispatchKeyboard(keyboard, action);
            return true;
        }
        switch (action)
        {
            // X swaps the screens elsewhere; Settings keeps it, as the menu and sheets do.
            case GamepadAction.Search:
            case GamepadAction.ResetRotation:
                return true;
        }
        Projection.Dispatch(action);
        return true;
    }

    /// <summary>The system Back button: what B does.</summary>
    public void Back() => Dispatch(GamepadAction.Cancel);

    // ---- Touch (the Launcher's view wiring calls these) ----

    /// <summary>A tap on a section: show it.</summary>
    public void TapSection(HandheldSettingsSectionViewModel section)
    {
        if (!Projection.IsNormal)
            return;
        if (section.Section == SettingsSection.Themes)
            Projection.SelectThemesCommand.Execute(null);
        else
            Projection.SelectSectionCommand.Execute(section.Section);
    }

    /// <summary>A tap on a row: it takes the focus and does what A does.</summary>
    public void TapRow(GamepadSettingsRowViewModel row)
    {
        if (Projection.IsNormal && Rows.Contains(row))
            _ = row.SelectCommand.ExecuteAsync(null);
    }

    public void TapChoice(GamepadChoiceOptionViewModel option)
    {
        if (Projection.IsChoicePickerOpen && ChoiceOptions.Contains(option))
            option.SelectCommand.Execute(null);
    }

    /// <summary>A tap on one of the confirmation's answers.</summary>
    public void TapConfirmation(bool confirm)
    {
        if (!Projection.IsConfirmationOpen)
            return;
        if (confirm)
            Projection.ChooseConfirmationConfirmCommand.Execute(null);
        else
            Projection.ChooseConfirmationCancelCommand.Execute(null);
    }

    public void TapTheme(ThemeChoiceViewModel theme)
    {
        if (Projection.IsNormal)
            Projection.SelectThemeChoiceCommand.Execute(theme);
    }

    /// <summary>A tap outside the open chooser or confirmation: it closes, as B does.</summary>
    public void TapOutside()
    {
        if (Projection.IsChoicePickerOpen)
            Projection.CancelChoicePicker();
        else if (Projection.IsConfirmationOpen)
            Projection.CancelConfirmation();
    }

    /// <summary>A key of the drawn keyboard: A on it, or a tap.</summary>
    public void PressKey(HandheldKeyViewModel key)
    {
        if (Keyboard is not { } keyboard)
            return;
        keyboard.FocusKey(key);
        Press(keyboard, key);
    }

    // ---- The keyboard ----

    private void DispatchKeyboard(HandheldTextKeyboardViewModel keyboard, GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.NavigateUp:
                keyboard.MoveKey(-1, 0);
                break;
            case GamepadAction.NavigateDown:
                keyboard.MoveKey(1, 0);
                break;
            case GamepadAction.NavigateLeft:
                keyboard.MoveKey(0, -1);
                break;
            case GamepadAction.NavigateRight:
                keyboard.MoveKey(0, 1);
                break;
            case GamepadAction.Confirm:
                Press(keyboard, keyboard.FocusedKey);
                return;
            case GamepadAction.Cancel:
                // B deletes, as on the naming keyboard; with nothing left to delete it leaves without saving.
                if (!keyboard.DeleteLast())
                    Projection.CancelTextEntry();
                break;
            case GamepadAction.Actions:
                // Y shows or hides a secret (as in Gamepad mode's Settings), and is a space anywhere else.
                if (keyboard.IsSecret)
                    Projection.RevealSecret = !Projection.RevealSecret;
                else
                    keyboard.Type(" ");
                break;
            case GamepadAction.PreviousPlatform:
                keyboard.ToggleShift();
                break;
            case GamepadAction.NextPlatform:
                keyboard.ToggleSymbols();
                break;
            case GamepadAction.Search:
            case GamepadAction.Menu:
                Projection.CommitTextEntry();
                return;
        }
        RaiseChanged();
    }

    private void Press(HandheldTextKeyboardViewModel keyboard, HandheldKeyViewModel key)
    {
        switch (key.Kind)
        {
            case HandheldKeyKind.Character:
                keyboard.Type(key.Label);
                break;
            case HandheldKeyKind.Space:
                keyboard.Type(" ");
                break;
            case HandheldKeyKind.Delete:
                keyboard.DeleteLast();
                break;
            case HandheldKeyKind.Shift:
                keyboard.ToggleShift();
                break;
            case HandheldKeyKind.Symbols:
                keyboard.ToggleSymbols();
                break;
            case HandheldKeyKind.Cancel:
                Projection.CancelTextEntry();
                return;
            case HandheldKeyKind.Done:
                Projection.CommitTextEntry();
                return;
        }
        RaiseChanged();
    }

    private void SyncKeyboard()
    {
        if (Projection.IsTextEntryOpen)
        {
            Keyboard ??= new HandheldTextKeyboardViewModel(Projection);
            Keyboard.IsSecret = Projection.IsSecretEntry;
        }
        else
        {
            Keyboard = null;
        }
    }

    // ---- What the Stage shows ----

    /// <summary>The Stage's card for what has the focus: a row, a section, a choice, a confirmation or the text being typed.</summary>
    public HandheldSettingsCard Card
    {
        get
        {
            var section = Projection.SectionTitle;
            var status = Projection.HasStatus && !Projection.IsThemesSection ? Projection.StatusText : string.Empty;
            if (Projection.IsTextEntryOpen)
            {
                var draft = Projection.DraftText;
                var shown = draft.Length == 0 ? "Not entered yet"
                    : Projection.IsSecretEntry && !Projection.RevealSecret ? new string('•', Math.Min(draft.Length, 24)) : draft;
                return new(section, Projection.TextEntryTitle, shown, Projection.TextEntryDescription, string.Empty, false);
            }
            if (Projection.IsChoicePickerOpen)
            {
                var option = ChoiceOptions.ElementAtOrDefault(Projection.FocusedChoiceIndex);
                return new(section, Projection.ChoicePickerTitle, option?.DisplayName ?? string.Empty,
                    Projection.ChoicePickerDescription, string.Empty, false);
            }
            if (Projection.IsConfirmationOpen)
            {
                return new(section, Projection.ConfirmationTitle, Projection.IsConfirmChoiceSelected ? ConfirmLabel : "Cancel",
                    Projection.ConfirmationText, string.Empty, false);
            }
            if (Projection.IsRailFocused)
            {
                var focused = Sections.FirstOrDefault(item => item.IsSelected);
                return new(HandheldSettingsText.Name, section, focused?.Status ?? string.Empty, Projection.SectionDescription, status,
                    focused?.IsWarning == true);
            }
            if (Projection.IsThemesSection)
            {
                var theme = ThemeChoices.ElementAtOrDefault(Projection.FocusedThemeIndex);
                return theme is null
                    ? new(HandheldSettingsText.Name, section, string.Empty, Projection.SectionDescription, status, false)
                    : new(section, theme.Name, theme.IsSelected ? "In use" : string.Empty, theme.Description, status, false);
            }
            if (Projection.FocusedRow is not { } row)
                return new(HandheldSettingsText.Name, section, string.Empty, Projection.SectionDescription, status, false);
            // A platform's own row says which platform it belongs to ("Emulators · Super Nintendo").
            var platform = row.IsSummary || row.SystemId is null
                ? null
                : Rows.FirstOrDefault(other => other.IsSummary && other.SystemId == row.SystemId)?.Label;
            var value = ValueOf(row);
            return new(platform is null ? section : $"{section} · {platform}", row.Label, value, WithoutLeadingValue(row.Description, value),
                status, row.IsWarning);
        }
    }

    /// <summary>What the confirmation's action is called on its button.</summary>
    public string ConfirmLabel => Projection.ConfirmationTitle.Length > 0 ? Projection.ConfirmationTitle : "Continue";

    // The value the Stage shows large: a switch's state, a choice, a folder, a name; an action's word if it has one.
    private static string ValueOf(GamepadSettingsRowViewModel row) => row.Kind switch
    {
        GamepadSettingsRowKind.Action => row.ActionText,
        _ => row.Value,
    };

    // A switch's description opens with its state ("Hidden · …") for Gamepad mode's one-line rows; the Stage already shows
    // the state large above it.
    private static string WithoutLeadingValue(string description, string value)
    {
        if (value.Length == 0 || !description.StartsWith(value + " · ", StringComparison.Ordinal) || description.Length <= value.Length + 3)
            return description;
        var rest = description[(value.Length + 3)..];
        return char.ToUpperInvariant(rest[0]) + rest[1..];
    }

    /// <summary>The Stage's button hints for the state Settings is in.</summary>
    public IEnumerable<HandheldHint> Hints
    {
        get
        {
            if (Keyboard is { } keyboard)
            {
                yield return new HandheldHint("A", "Type");
                yield return new HandheldHint("B", keyboard.HasText ? "Delete" : "Cancel");
                yield return new HandheldHint("Y", keyboard.IsSecret ? Projection.RevealSecret ? "Hide" : "Show" : "Space");
                yield return new HandheldHint("X", "Done");
                yield break;
            }
            if (Projection.IsChoicePickerOpen || Projection.IsConfirmationOpen)
            {
                yield return new HandheldHint("A", "Choose");
                yield return new HandheldHint("B", "Back");
                yield break;
            }
            if (Projection.IsRailFocused)
            {
                yield return new HandheldHint("A", "Open");
            }
            else if (Projection.IsThemesSection)
            {
                yield return new HandheldHint("A", "Use");
            }
            else if (Projection.FocusedRow is { } row && row.CanActivate && !row.IsSaveRow)
            {
                yield return new HandheldHint("A", row.Kind switch
                {
                    GamepadSettingsRowKind.Toggle => "Switch",
                    GamepadSettingsRowKind.Choice => "Choose",
                    GamepadSettingsRowKind.Text or GamepadSettingsRowKind.Secret => "Edit",
                    GamepadSettingsRowKind.Summary => row.IsExpanded ? "Close" : "Open",
                    _ => "Select",
                });
            }
            if (Projection.HasActionsHint)
                yield return new HandheldHint("Y", Projection.ActionsHint);
            yield return new HandheldHint("B", "Close");
            // Start, drawn as the menu glyph the home hints use for it.
            yield return new HandheldHint(string.Empty, "Save", HandheldGlyph.Menu);
        }
    }

    // ---- Following the projection ----

    private void OnProjectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GamepadSettingsViewModel.IsTextEntryOpen):
            case nameof(GamepadSettingsViewModel.IsSecretEntry):
                SyncKeyboard();
                break;
            case nameof(GamepadSettingsViewModel.DraftText):
                Keyboard?.OnDraftChanged();
                break;
            case nameof(GamepadSettingsViewModel.RevealSecret):
                Keyboard?.OnRevealChanged();
                break;
            case nameof(GamepadSettingsViewModel.IsThemesSection):
                OnPropertyChanged(nameof(IsThemes));
                OnPropertyChanged(nameof(IsRows));
                break;
            case nameof(GamepadSettingsViewModel.IsChoicePickerOpen):
                OnPropertyChanged(nameof(IsChoicePickerOpen));
                break;
            case nameof(GamepadSettingsViewModel.IsConfirmationOpen):
            case nameof(GamepadSettingsViewModel.IsConfirmChoiceSelected):
                OnPropertyChanged(nameof(IsConfirmationOpen));
                OnPropertyChanged(nameof(IsCancelFocused));
                OnPropertyChanged(nameof(IsConfirmFocused));
                OnPropertyChanged(nameof(ConfirmLabel));
                break;
            case nameof(GamepadSettingsViewModel.SectionTitle):
                OnPropertyChanged(nameof(SectionTitle));
                break;
        }
        SyncSections();
        RaiseChanged();
    }

    private void SyncSections()
    {
        var current = Projection.IsThemesSection ? SettingsSection.Themes : Projection.SelectedSection;
        foreach (var item in Sections)
        {
            item.IsSelected = item.Section == current;
            item.IsFocused = item.IsSelected && Projection.IsRailFocused && Projection.IsNormal;
            var (status, warning) = item.Section switch
            {
                SettingsSection.Emulators => (Projection.EmulatorsRailStatus, Projection.IsEmulatorsRailWarning),
                SettingsSection.Hotkeys => (Projection.HotkeysRailStatus, false),
                SettingsSection.RetroAchievements => (Projection.RetroAchievementsRailStatus, false),
                SettingsSection.ArtworkMetadata => (Projection.ArtworkRailStatus, false),
                SettingsSection.Saves => (Projection.SavesRailStatus, Projection.IsSavesRailWarning),
                SettingsSection.TexturePacks => (Projection.TexturePacksRailStatus, false),
                SettingsSection.Themes => (Projection.ThemesRailStatus, false),
                SettingsSection.Display => (Projection.DisplayRailStatus, false),
                SettingsSection.About => (Projection.AboutRailStatus, false),
                _ => (Projection.LibraryRailStatus, false),
            };
            item.Status = status;
            item.IsWarning = warning;
        }
    }

    private void RaiseChanged()
    {
        if (!_disposed)
            Changed?.Invoke();
    }

    internal static string TitleOf(SettingsSection section) => section switch
    {
        SettingsSection.Emulators => "Emulators",
        SettingsSection.Hotkeys => "Hotkeys",
        SettingsSection.RetroAchievements => "Achievements",
        SettingsSection.ArtworkMetadata => "Artwork & metadata",
        SettingsSection.Saves => "Saves",
        SettingsSection.TexturePacks => "Texture packs",
        SettingsSection.Themes => "Themes",
        SettingsSection.Display => "Display",
        SettingsSection.About => "About",
        _ => "Library",
    };

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Projection.PropertyChanged -= OnProjectionChanged;
        Changed = null;
    }
}

/// <summary>
/// The drawn keyboard Settings' text rows type on (a name, a key, a password): the naming keyboard's look, with digits,
/// capitals (Shift, or LB) and symbols (RB), Cancel and Done. It types straight into the projection's draft, so the field
/// above it and the Stage both follow every key, and a hardware keyboard typing into the field works too.
/// </summary>
public sealed partial class HandheldTextKeyboardViewModel : ObservableObject
{
    public const int MaxLength = 256;

    private static readonly string[] LetterRows = ["1234567890", "qwertyuiop", "asdfghjkl@", "zxcvbnm-_."];
    private static readonly string[] SymbolRows = ["1234567890", "!@#$%^&*()", "-_=+[]{}\\|", ";:'\",.<>/?"];

    private readonly GamepadSettingsViewModel _projection;

    internal HandheldTextKeyboardViewModel(GamepadSettingsViewModel projection)
    {
        _projection = projection;
        Build();
    }

    public ObservableCollection<HandheldKeyRowViewModel> KeyRows { get; } = [];

    /// <summary>The draft as typed (the field binds it two-way, so a hardware keyboard types there).</summary>
    public string Text
    {
        get => _projection.DraftText;
        set
        {
            var text = value.Length > MaxLength ? value[..MaxLength] : value;
            if (text != _projection.DraftText)
                _projection.DraftText = text;
        }
    }

    public bool HasText => Text.Length > 0;

    public string Title => _projection.TextEntryTitle;

    /// <summary>A secret (a password, an API key): masked until Y shows it.</summary>
    [ObservableProperty]
    public partial bool IsSecret { get; set; }

    [ObservableProperty]
    public partial bool IsShifted { get; private set; }

    [ObservableProperty]
    public partial bool IsSymbols { get; private set; }

    public int KeyRow { get; private set; }
    public int KeyColumn { get; private set; }
    public HandheldKeyViewModel FocusedKey => KeyRows[KeyRow].Keys[KeyColumn];

    internal void OnDraftChanged()
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(HasText));
    }

    /// <summary>The field's mask: a secret shows dots until Y shows it ('\0' shows the text).</summary>
    public char MaskChar => IsSecret && !_projection.RevealSecret ? '●' : '\0';

    partial void OnIsSecretChanged(bool value) => OnPropertyChanged(nameof(MaskChar));

    internal void OnRevealChanged() => OnPropertyChanged(nameof(MaskChar));

    internal bool Type(string character)
    {
        if (Text.Length >= MaxLength)
            return false;
        Text += character;
        return true;
    }

    internal bool DeleteLast()
    {
        if (Text.Length == 0)
            return false;
        Text = Text[..^1];
        return true;
    }

    internal void ToggleShift()
    {
        IsShifted = !IsShifted;
        if (IsShifted)
            IsSymbols = false;
        Build();
    }

    internal void ToggleSymbols()
    {
        IsSymbols = !IsSymbols;
        if (IsSymbols)
            IsShifted = false;
        Build();
    }

    /// <summary>Moves across the keys, keeping roughly the same place across rows of different lengths. False at an edge.</summary>
    internal bool MoveKey(int rows, int columns)
    {
        if (rows != 0)
        {
            var row = KeyRow + rows;
            if (row < 0 || row >= KeyRows.Count)
                return false;
            var from = KeyRows[KeyRow].Keys.Count;
            var to = KeyRows[row].Keys.Count;
            KeyColumn = Math.Min((int)Math.Round(KeyColumn * (double)to / from), to - 1);
            KeyRow = row;
        }
        else
        {
            var column = KeyColumn + columns;
            if (column < 0 || column >= KeyRows[KeyRow].Keys.Count)
                return false;
            KeyColumn = column;
        }
        UpdateFocus();
        return true;
    }

    internal void FocusKey(HandheldKeyViewModel key)
    {
        for (var row = 0; row < KeyRows.Count; row++)
        {
            var column = KeyRows[row].Keys.ToList().IndexOf(key);
            if (column < 0)
                continue;
            KeyRow = row;
            KeyColumn = column;
            UpdateFocus();
            return;
        }
    }

    // The rows for the current case and set; the focus keeps its place.
    private void Build()
    {
        KeyRows.Clear();
        foreach (var row in IsSymbols ? SymbolRows : LetterRows)
        {
            var keys = row.Select(ch => IsShifted ? char.ToUpperInvariant(ch) : ch)
                .Select(ch => new HandheldKeyViewModel(ch.ToString(), HandheldKeyKind.Character))
                .ToList();
            KeyRows.Add(new HandheldKeyRowViewModel(keys));
        }
        KeyRows.Add(new HandheldKeyRowViewModel(
        [
            new HandheldKeyViewModel(IsShifted ? "abc" : "Shift", HandheldKeyKind.Shift),
            new HandheldKeyViewModel(IsSymbols ? "abc" : "#+=", HandheldKeyKind.Symbols),
            new HandheldKeyViewModel("Space", HandheldKeyKind.Space),
            new HandheldKeyViewModel("Delete", HandheldKeyKind.Delete),
            new HandheldKeyViewModel("Cancel", HandheldKeyKind.Cancel),
            new HandheldKeyViewModel("Done", HandheldKeyKind.Done),
        ]));
        KeyRow = Math.Clamp(KeyRow, 0, KeyRows.Count - 1);
        KeyColumn = Math.Clamp(KeyColumn, 0, KeyRows[KeyRow].Keys.Count - 1);
        UpdateFocus();
    }

    private void UpdateFocus()
    {
        for (var row = 0; row < KeyRows.Count; row++)
        {
            for (var column = 0; column < KeyRows[row].Keys.Count; column++)
                KeyRows[row].Keys[column].IsFocused = row == KeyRow && column == KeyColumn;
        }
    }
}
