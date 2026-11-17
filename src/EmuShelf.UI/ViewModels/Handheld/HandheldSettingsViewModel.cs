using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.App.Services;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.ViewModels;

/// <summary>One section in the Launcher’s Settings list.</summary>
public sealed partial class HandheldSettingsSectionViewModel(SettingsSection section, string title) : ObservableObject
{
    public SettingsSection Section { get; } = section;

    /// <summary>In the setup wizard (M45 H6d) the list holds its steps instead of sections: this entry's step.</summary>
    public SetupStep? Step { get; init; }

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
/// Settings in Handheld mode (M45 H6a): the shared controller projection (<see cref="GamepadSettingsViewModel"/>)
/// lays out the handheld sections across the two screens.
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
        if (projection.IsSetupMode)
        {
            // The setup wizard (M45 H6d): its steps take the sections' place, each with its outcome line, and follow it.
            foreach (var step in projection.SetupRail.Steps)
            {
                Sections.Add(new HandheldSettingsSectionViewModel(SettingsSection.General, step.Label) { Step = step.Step });
                step.PropertyChanged += OnSetupStepChanged;
            }
            projection.SetupRail.PropertyChanged += OnSetupStepChanged;
        }
        else
        {
            foreach (var page in projection.Pages)
                Sections.Add(new HandheldSettingsSectionViewModel(page, projection.TitleFor(page)));
        }
        projection.PropertyChanged += OnProjectionChanged;
        projection.Rows.CollectionChanged += (_, _) => RaiseChanged();
        SyncSections();
        SyncKeyboard();
    }

    /// <summary>The projection Gamepad mode's Settings overlay shows: all values and actions live there.</summary>
    public GamepadSettingsViewModel Projection { get; }

    public ObservableCollection<HandheldSettingsSectionViewModel> Sections { get; } = [];

    public ObservableCollection<GamepadSettingsRowViewModel> Rows => Projection.Rows;

    public ObservableCollection<GamepadChoiceOptionViewModel> ChoiceOptions => Projection.ChoiceOptions;

    public string SectionTitle => Projection.SectionTitle;

    /// <summary>The setup wizard, laid across the two screens like Settings (M45 H6d).</summary>
    public bool IsSetup => Projection.IsSetupMode;

    /// <summary>The list's heading: Settings, or Setup with where the wizard is.</summary>
    public string Heading => IsSetup ? "Setup" : HandheldSettingsText.Name;

    /// <summary>The app's version, which the Big Type Launcher rolls into the dock row's corner while Settings is open (M47 B7, the mock's).</summary>
    public string Version => Projection.AboutRailStatus;

    /// <summary>The wizard's way forward (Start): Continue, or Finish on the last step; and what comes next.</summary>
    public string StartLabel => Projection.SetupRail.StartLabel;

    public string StartDetail => Projection.SetupRail.StartDetail;

    /// <summary>"Step 3 of 6": where the wizard is.</summary>
    public string StepText
    {
        get
        {
            var steps = Projection.SetupRail.Steps;
            var current = steps.ToList().FindIndex(step => step.IsCurrent);
            return current < 0 ? string.Empty : $"Step {current + 1} of {steps.Count}";
        }
    }

    public bool IsChoicePickerOpen => Projection.IsChoicePickerOpen;

    /// <summary>Neither a choice's list nor a confirmation stands in for the rows (the Big Type look shows those in their place, M47 B7).</summary>
    public bool IsNormal => !Projection.IsChoicePickerOpen && !Projection.IsConfirmationOpen;

    /// <summary>The section (or setup step) on show, as an index into <see cref="Sections"/>; -1 for none (M47 B7: its rail keeps it in view).</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    public partial int SelectedSectionIndex { get; private set; } = -1;

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

    /// <summary>A tap on a section (a step, in the wizard): show it.</summary>
    public void TapSection(HandheldSettingsSectionViewModel section)
    {
        if (!Projection.IsNormal)
            return;
        if (section.Step is { } step)
            Projection.GoToSetupStep(step);
        else
            Projection.SelectSectionCommand.Execute(section.Section);
    }

    /// <summary>A tap on the wizard's Continue (or Finish): what Start does.</summary>
    public void TapStart()
    {
        if (IsSetup && Projection.IsNormal)
            Projection.SetupRail.StartCommand?.Execute(null);
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

    private void DispatchKeyboard(HandheldTextKeyboardViewModel keyboard, GamepadAction action) =>
        Apply(keyboard.Dispatch(action));

    private void Press(HandheldTextKeyboardViewModel keyboard, HandheldKeyViewModel key) => Apply(keyboard.Press(key));

    // What a key or a button did on the keyboard: the projection commits, cancels or reveals; anything else re-pictures.
    private void Apply(HandheldKeyOutcome outcome)
    {
        switch (outcome)
        {
            case HandheldKeyOutcome.Commit:
                Projection.CommitTextEntry();
                return;
            case HandheldKeyOutcome.Cancel:
                Projection.CancelTextEntry();
                return;
            case HandheldKeyOutcome.ToggleReveal:
                Projection.RevealSecret = !Projection.RevealSecret;
                break;
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
            // In the wizard the eyebrow says where it is ("Step 3 of 6 · Games & emulators"), and with nothing else to report
            // the status line says what Continue leads to.
            var section = IsSetup && StepText.Length > 0 ? $"{StepText} · {Projection.SectionTitle}" : Projection.SectionTitle;
            var status = Projection.HasStatus ? Projection.StatusText
                : IsSetup ? StartDetail : string.Empty;
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
                return new(IsSetup ? $"Setup · {StepText}" : HandheldSettingsText.Name, Projection.SectionTitle,
                    focused?.Status ?? string.Empty, Projection.SectionDescription, status, focused?.IsWarning == true);
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
    // The setup wizard's rows open with it as a sentence instead ("On. A game closed…", M45 H6d).
    private static string WithoutLeadingValue(string description, string value)
    {
        if (value.Length == 0)
            return description;
        var prefix = description.StartsWith(value + " · ", StringComparison.Ordinal) ? value + " · "
            : description.StartsWith(value + ". ", StringComparison.Ordinal) ? value + ". " : null;
        if (prefix is null || description.Length <= prefix.Length)
            return description;
        var rest = description[prefix.Length..];
        return char.ToUpperInvariant(rest[0]) + rest[1..];
    }

    /// <summary>The Stage's button hints for the state Settings is in.</summary>
    public IEnumerable<HandheldHint> Hints
    {
        get
        {
            if (Keyboard is { } keyboard)
            {
                foreach (var hint in keyboard.Hints())
                    yield return hint;
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
            if (IsSetup)
            {
                // The wizard walks with B (the previous step, or out on the first) and Start (the next, or Finish).
                yield return new HandheldHint("B", "Back");
                yield return new HandheldHint(string.Empty, StartLabel, HandheldGlyph.Menu);
                yield break;
            }
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
            case nameof(GamepadSettingsViewModel.IsChoicePickerOpen):
                OnPropertyChanged(nameof(IsChoicePickerOpen));
                OnPropertyChanged(nameof(IsNormal));
                break;
            case nameof(GamepadSettingsViewModel.IsConfirmationOpen):
            case nameof(GamepadSettingsViewModel.IsConfirmChoiceSelected):
                OnPropertyChanged(nameof(IsConfirmationOpen));
                OnPropertyChanged(nameof(IsNormal));
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

    // A step's outcome line, the step on show, or the Start chip changed (the wizard refreshes them as it goes).
    private void OnSetupStepChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed)
            return;
        OnPropertyChanged(nameof(StartLabel));
        OnPropertyChanged(nameof(StartDetail));
        OnPropertyChanged(nameof(StepText));
        SyncSections();
        RaiseChanged();
    }

    private void SyncSections()
    {
        SyncSectionsCore();
        var selected = -1;
        for (var i = 0; i < Sections.Count; i++)
        {
            if (Sections[i].IsSelected)
            {
                selected = i;
                break;
            }
        }
        SelectedSectionIndex = selected;
    }

    private void SyncSectionsCore()
    {
        if (IsSetup)
        {
            foreach (var item in Sections)
            {
                if (Projection.SetupRail.Steps.FirstOrDefault(step => step.Step == item.Step) is not { } step)
                    continue;
                item.IsSelected = step.IsCurrent;
                item.IsFocused = step.IsCurrent && Projection.IsRailFocused && Projection.IsNormal;
                item.Status = step.Status;
                item.IsWarning = step.IsWarning;
            }
            return;
        }
        var current = Projection.SelectedSection;
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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Projection.PropertyChanged -= OnProjectionChanged;
        if (IsSetup)
        {
            foreach (var step in Projection.SetupRail.Steps)
                step.PropertyChanged -= OnSetupStepChanged;
            Projection.SetupRail.PropertyChanged -= OnSetupStepChanged;
        }
        Changed = null;
    }
}

/// <summary>What a press on the drawn keyboard did, for its owner to act on.</summary>
public enum HandheldKeyOutcome
{
    /// <summary>Nothing the owner needs to do (an edge of the keys).</summary>
    None,

    /// <summary>The text, the focus or the keys changed.</summary>
    Changed,

    /// <summary>Done (the key, X or Start).</summary>
    Commit,

    /// <summary>Cancel (the key, or B with nothing left to delete).</summary>
    Cancel,

    /// <summary>Y on a secret: show or hide it.</summary>
    ToggleReveal,

    /// <summary>The key could not type here (Big Type's rules: a full word, a space first or twice, nothing to delete): it bumps.</summary>
    Bumped,
}

/// <summary>
/// The drawn keyboard Handheld mode types on (M45 H6a, shared by Search, Edit title and the scraper's fields in H6c): the
/// naming keyboard's look, with digits, capitals (Shift, or LB) and symbols (RB), Cancel and Done. It types straight into
/// its owner's text (Settings' draft, a search, a title), so the field above it and the Stage both follow every key, and a
/// hardware keyboard typing into the field works too. Android's own keyboard cannot open on the Thor's second screen.
/// </summary>
public sealed partial class HandheldTextKeyboardViewModel : ObservableObject
{
    public const int MaxLength = 256;

    private static readonly string[] LetterRows = ["1234567890", "qwertyuiop", "asdfghjkl@", "zxcvbnm-_."];
    private static readonly string[] SymbolRows = ["1234567890", "!@#$%^&*()", "-_=+[]{}\\|", ";:'\",.<>/?"];

    private readonly Func<string> _getText;
    private readonly Action<string> _setText;
    private readonly Func<string> _title;
    private readonly Func<bool> _isRevealed;

    internal HandheldTextKeyboardViewModel(GamepadSettingsViewModel projection)
        : this(() => projection.DraftText, text => projection.DraftText = text, () => projection.TextEntryTitle,
            () => projection.RevealSecret)
    {
    }

    /// <summary>A keyboard over any text: read and written through <paramref name="getText"/> / <paramref name="setText"/>.</summary>
    internal HandheldTextKeyboardViewModel(Func<string> getText, Action<string> setText, Func<string> title, Func<bool>? isRevealed = null)
    {
        _getText = getText;
        _setText = setText;
        _title = title;
        _isRevealed = isRevealed ?? (() => false);
        Build();
    }

    public ObservableCollection<HandheldKeyRowViewModel> KeyRows { get; } = [];

    /// <summary>The draft as typed (the field binds it two-way, so a hardware keyboard types there).</summary>
    public string Text
    {
        get => _getText();
        set
        {
            var text = value.Length > MaxLength ? value[..MaxLength] : value;
            if (text == _getText())
                return;
            _setText(text);
            OnDraftChanged();
        }
    }

    public bool HasText => Text.Length > 0;

    public string Title => _title();

    /// <summary>A secret (a password, an API key): masked until Y shows it.</summary>
    [ObservableProperty]
    public partial bool IsSecret { get; set; }

    [ObservableProperty]
    public partial bool IsShifted { get; private set; }

    [ObservableProperty]
    public partial bool IsSymbols { get; private set; }

    /// <summary>
    /// The Big Type keyboard's page (M47 B9): 0 the mock's letters, then the app's other characters, behind the key at the end of the
    /// third row (or RB).
    /// </summary>
    public int Page { get; private set; }

    /// <summary>
    /// Typing by the mock's rule (Search, under Big Type): a capital at the start and after a space, lower case otherwise, no space
    /// first or twice, and no Shift key. Other keyboards keep their own case (Shift, or LB).
    /// </summary>
    public bool AutoCapital
    {
        get => _autoCapital;
        set
        {
            if (_autoCapital == value)
                return;
            _autoCapital = value;
            Build();
        }
    }

    private bool _autoCapital;

    public int KeyRow { get; private set; }
    public int KeyColumn { get; private set; }
    public HandheldKeyViewModel FocusedKey => KeyRows[KeyRow].Keys[KeyColumn];

    internal void OnDraftChanged()
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(HasText));
        if (HandheldTokens.IsBigType)
            HandheldBigTypeKeys.FollowText(KeyRows, HasText);
    }

    /// <summary>The field's mask: a secret shows dots until Y shows it ('\0' shows the text).</summary>
    public char MaskChar => IsSecret && !_isRevealed() ? '●' : '\0';

    /// <summary>Whether a secret is showing (Y toggled it).</summary>
    public bool IsRevealed => _isRevealed();

    partial void OnIsSecretChanged(bool value) => OnPropertyChanged(nameof(MaskChar));

    internal void OnRevealChanged()
    {
        OnPropertyChanged(nameof(MaskChar));
        OnPropertyChanged(nameof(IsRevealed));
    }

    /// <summary>
    /// A pad button on the keyboard: the D-pad moves over the keys, A types the focused one, B deletes (and with nothing
    /// left to delete, cancels), Y is a space (or shows a secret), LB is Shift, RB the symbols, X and Start are Done.
    /// </summary>
    internal HandheldKeyOutcome Dispatch(GamepadAction action) => Noted(DispatchCore(action));

    private HandheldKeyOutcome DispatchCore(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.NavigateUp:
                return MoveKey(-1, 0) ? HandheldKeyOutcome.Changed : Edge;
            case GamepadAction.NavigateDown:
                return MoveKey(1, 0) ? HandheldKeyOutcome.Changed : Edge;
            case GamepadAction.NavigateLeft:
                return MoveKey(0, -1) ? HandheldKeyOutcome.Changed : Edge;
            case GamepadAction.NavigateRight:
                return MoveKey(0, 1) ? HandheldKeyOutcome.Changed : Edge;
            case GamepadAction.Confirm:
                return Press(FocusedKey);
            case GamepadAction.Cancel:
                return DeleteLast() ? HandheldKeyOutcome.Changed : HandheldKeyOutcome.Cancel;
            case GamepadAction.Actions when IsSecret:
                return HandheldKeyOutcome.ToggleReveal;
            case GamepadAction.Actions:
                return Type(" ") || !HandheldTokens.IsBigType ? HandheldKeyOutcome.Changed : HandheldKeyOutcome.Bumped;
            case GamepadAction.PreviousPlatform when HandheldTokens.IsBigType && AutoCapital:
                return HandheldKeyOutcome.None;
            case GamepadAction.PreviousPlatform:
                ToggleShift();
                return HandheldKeyOutcome.Changed;
            case GamepadAction.NextPlatform when HandheldTokens.IsBigType:
                NextPage();
                return HandheldKeyOutcome.Changed;
            case GamepadAction.NextPlatform:
                ToggleSymbols();
                return HandheldKeyOutcome.Changed;
            case GamepadAction.Search:
            case GamepadAction.Menu:
                return HandheldKeyOutcome.Commit;
            default:
                return HandheldKeyOutcome.None;
        }
    }

    // At an edge of the keys: nothing in today's look; Big Type's keyboard bumps there, as the mock's does.
    private static HandheldKeyOutcome Edge => HandheldTokens.IsBigType ? HandheldKeyOutcome.Bumped : HandheldKeyOutcome.None;

    /// <summary>A key: A on it, or a tap (the caller focuses it first).</summary>
    internal HandheldKeyOutcome Press(HandheldKeyViewModel key) => Noted(PressCore(key));

    // A bump is kept for the shell, which owns the ring (TakeBump).
    private HandheldKeyOutcome Noted(HandheldKeyOutcome outcome)
    {
        if (outcome == HandheldKeyOutcome.Bumped)
            _bumped = true;
        return outcome;
    }

    private bool _bumped;

    /// <summary>Whether the last press bumped (Big Type: the ring nudges); reading it clears it.</summary>
    internal bool TakeBump()
    {
        var bumped = _bumped;
        _bumped = false;
        return bumped;
    }

    private HandheldKeyOutcome PressCore(HandheldKeyViewModel key)
    {
        FocusKey(key);
        key.Presses++;
        switch (key.Kind)
        {
            case HandheldKeyKind.Character when HandheldTokens.IsBigType && AutoCapital:
                return Type(key.Value) ? HandheldKeyOutcome.Changed : HandheldKeyOutcome.Bumped;
            case HandheldKeyKind.Character:
                Type(key.Label);
                break;
            case HandheldKeyKind.Space when HandheldTokens.IsBigType && AutoCapital:
                return Type(" ") ? HandheldKeyOutcome.Changed : HandheldKeyOutcome.Bumped;
            case HandheldKeyKind.Delete when HandheldTokens.IsBigType:
                // With nothing to delete the key reads Cancel and leaves, as B does (M47 B14: the keyboard by touch alone).
                return DeleteLast() ? HandheldKeyOutcome.Changed : HandheldKeyOutcome.Cancel;
            case HandheldKeyKind.Symbols when HandheldTokens.IsBigType:
                NextPage();
                break;
            case HandheldKeyKind.Space:
                Type(" ");
                break;
            case HandheldKeyKind.Delete:
                DeleteLast();
                break;
            case HandheldKeyKind.Shift:
                ToggleShift();
                break;
            case HandheldKeyKind.Symbols:
                ToggleSymbols();
                break;
            case HandheldKeyKind.Cancel:
                return HandheldKeyOutcome.Cancel;
            case HandheldKeyKind.Done:
                return HandheldKeyOutcome.Commit;
        }
        return HandheldKeyOutcome.Changed;
    }

    /// <summary>The hints a keyboard shows (the Stage's pill, and the row under the keys).</summary>
    public IEnumerable<HandheldHint> Hints(string done = "Done")
    {
        yield return new HandheldHint("A", "Type");
        yield return new HandheldHint("B", HasText ? "Delete" : "Cancel");
        yield return new HandheldHint("Y", IsSecret ? IsRevealed ? "Hide" : "Show" : "Space");
        yield return new HandheldHint("X", done);
    }

    internal bool Type(string character)
    {
        if (HandheldTokens.IsBigType && AutoCapital)
        {
            if (HandheldBigTypeKeys.Typed(Text, character, MaxLength) is not { } typed)
                return false;
            Text = typed;
            return true;
        }
        if (Text.Length >= MaxLength)
            return false;
        Text += character;
        return true;
    }

    /// <summary>The Big Type keyboard's next page of characters; the focus stays on the page key.</summary>
    internal void NextPage()
    {
        Page = (Page + 1) % HandheldBigTypeKeys.Pages.Count;
        Build();
        KeyRow = 2;
        KeyColumn = KeyRows[2].Keys.Count - 1;
        UpdateFocus();
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
        _blurred = false;
        if (rows != 0 && HandheldTokens.IsBigType)
        {
            // The mock's kbKey: up and down go to the nearest key across.
            var target = KeyRow + rows;
            if (target < 0 || target >= KeyRows.Count)
                return false;
            KeyColumn = HandheldBigTypeKeys.Nearest(KeyRows[target].Keys, FocusedKey.CentreX);
            KeyRow = target;
            UpdateFocus();
            return true;
        }
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

    /// <summary>No key has the focus (the pad is on Search's hits above the keys).</summary>
    internal void Blur()
    {
        _blurred = true;
        UpdateFocus();
    }

    private bool _blurred;

    /// <summary>The focus on a key by its place.</summary>
    internal void FocusAt(int row, int column)
    {
        _blurred = false;
        KeyRow = Math.Clamp(row, 0, KeyRows.Count - 1);
        KeyColumn = Math.Clamp(column, 0, KeyRows[KeyRow].Keys.Count - 1);
        UpdateFocus();
    }

    internal void FocusKey(HandheldKeyViewModel key)
    {
        _blurred = false;
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
        if (HandheldTokens.IsBigType)
        {
            // The mock's keys (M47 B9): its letters, the app's other characters on the next pages; Shift where the case is the typist's.
            var shifted = IsShifted;
            var auto = AutoCapital;
            foreach (var bigRow in HandheldBigTypeKeys.Build(Page, shiftKey: !auto, shifted ? "abc" : "Shift",
                ch => auto || shifted || !char.IsLetter(ch) ? ch.ToString() : char.ToLowerInvariant(ch).ToString()))
                KeyRows.Add(bigRow);
            HandheldBigTypeKeys.FollowText(KeyRows, HasText);
            KeyRow = Math.Clamp(KeyRow, 0, KeyRows.Count - 1);
            KeyColumn = Math.Clamp(KeyColumn, 0, KeyRows[KeyRow].Keys.Count - 1);
            UpdateFocus();
            return;
        }
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
                KeyRows[row].Keys[column].IsFocused = !_blurred && row == KeyRow && column == KeyColumn;
        }
    }
}
