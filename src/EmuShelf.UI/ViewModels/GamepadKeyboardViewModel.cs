using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;

namespace EmuShelf.App.ViewModels;

/// <summary>What a key does when activated. Only <see cref="Character"/> carries text.</summary>
public enum GamepadKeyKind
{
    Character,
    Space,
    Backspace,
    Shift,
    SymbolsToggle,
    Done,
}

/// <summary>
/// One key on the app-owned couch keyboard. Focus is view-model state (<see cref="IsFocused"/>), not
/// Avalonia focus, so the same key grid renders identically whether it is shown on the main screen or
/// mirrored onto the Thor's second screen, and is driven entirely by gamepad dispatch.
/// </summary>
public sealed partial class GamepadKeyViewModel : ObservableObject
{
    public GamepadKeyViewModel(GamepadKeyKind kind, string glyph, string? value = null, double weight = 1)
    {
        Kind = kind;
        Value = value ?? glyph;
        Weight = weight;
        Glyph = glyph;
    }

    public GamepadKeyKind Kind { get; }

    /// <summary>Relative width of the key within its row (Space is wider); the view scales a base cell by this.</summary>
    public double Weight { get; }

    /// <summary>Rendered width in DIPs — a fixed base cell scaled by <see cref="Weight"/>. Sized so a ten-key
    /// row fits both the main screen and the narrower Thor second screen without a per-host layout pass.</summary>
    public double KeyWidth => Weight * 46;

    [ObservableProperty]
    public partial string Glyph { get; set; }

    /// <summary>The text inserted for a <see cref="GamepadKeyKind.Character"/> key (upper/lower per Shift).</summary>
    public string Value { get; private set; }

    [ObservableProperty]
    public partial bool IsFocused { get; set; }

    /// <summary>Action keys (Shift, Symbols, Backspace, Done, Space) are styled apart from letter keys.</summary>
    public bool IsActionKey => Kind != GamepadKeyKind.Character;

    /// <summary>Re-labels a letter key when Shift flips, keeping the glyph and inserted value in step.</summary>
    internal void SetCharacter(string glyph)
    {
        Glyph = glyph;
        Value = glyph;
    }
}

/// <summary>
/// An app-drawn, gamepad-navigable on-screen keyboard. It replaces the system IME for couch text entry:
/// the system keyboard (Gboard) cannot be moved off the main screen by a third-party app, and it covers
/// the search field and results on a handheld, so text is entered through this grid instead and written
/// straight into the target field via the supplied delegates — no OS keyboard is raised.
///
/// Carries no platform types, so it is desktop-testable and can be mirrored onto the Thor's second-screen
/// Presentation (driven by the same main-thread gamepad dispatch) as well as shown as a main-screen strip.
/// </summary>
public sealed partial class GamepadKeyboardViewModel : ObservableObject
{
    private readonly Func<string> _getText;
    private readonly Action<string> _setText;
    private readonly Action? _onSubmit;
    private readonly List<GamepadKeyViewModel> _letterKeys = [];

    private int _rowIndex;
    private int _columnIndex;
    private bool _symbols;

    public GamepadKeyboardViewModel(
        string title,
        string placeholder,
        Func<string> getText,
        Action<string> setText,
        string doneLabel,
        Action? onSubmit = null)
    {
        Title = title;
        Placeholder = placeholder;
        _getText = getText;
        _setText = setText;
        _onSubmit = onSubmit;
        DoneLabel = doneLabel;
        _doneKey = new GamepadKeyViewModel(GamepadKeyKind.Done, doneLabel, weight: 3);
        BuildRows();
        Text = _getText();
        FocusKey(1, 0); // land on the top-left letter (q), a natural resting spot
    }

    public string Title { get; }

    public string Placeholder { get; }

    /// <summary>Label of the confirm key: "Search" for the library filter, "Save" for a rename, etc.</summary>
    public string DoneLabel { get; }

    /// <summary>A live mirror of the target field, so the panel can show what has been typed on either screen.</summary>
    [ObservableProperty]
    public partial string Text { get; set; }

    [ObservableProperty]
    public partial bool IsShifted { get; set; }

    public bool HasText => !string.IsNullOrEmpty(Text);

    public ObservableCollection<IReadOnlyList<GamepadKeyViewModel>> Rows { get; } = [];

    private readonly GamepadKeyViewModel _doneKey;

    partial void OnTextChanged(string value) => OnPropertyChanged(nameof(HasText));

    /// <summary>Routes a directional/confirm controller action into the grid. Returns true when consumed.</summary>
    public bool Dispatch(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.NavigateLeft:
                Move(0, -1);
                return true;
            case GamepadAction.NavigateRight:
                Move(0, +1);
                return true;
            case GamepadAction.NavigateUp:
                Move(-1, 0);
                return true;
            case GamepadAction.NavigateDown:
                Move(+1, 0);
                return true;
            case GamepadAction.Confirm:
                ActivateFocused();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Activates the focused key (used by both controller Confirm and a direct tap on the key).</summary>
    public void Activate(GamepadKeyViewModel key)
    {
        switch (key.Kind)
        {
            case GamepadKeyKind.Character:
            case GamepadKeyKind.Space:
                Insert(key.Value);
                break;
            case GamepadKeyKind.Backspace:
                Backspace();
                break;
            case GamepadKeyKind.Shift:
                IsShifted = !IsShifted;
                ApplyShift();
                break;
            case GamepadKeyKind.SymbolsToggle:
                _symbols = !_symbols;
                BuildRows();
                break;
            case GamepadKeyKind.Done:
                _onSubmit?.Invoke();
                break;
        }
    }

    public void ActivateFocused()
    {
        if (FocusedKey is { } key)
            Activate(key);
    }

    /// <summary>Tap/click on a key (touch on the second screen, mouse on desktop): move the ring to it, then
    /// activate — so the controller focus stays consistent with what the user just pressed.</summary>
    [RelayCommand]
    private void PressKey(GamepadKeyViewModel? key)
    {
        if (key is null)
            return;
        for (var row = 0; row < Rows.Count; row++)
        {
            var cells = Rows[row];
            for (var column = 0; column < cells.Count; column++)
            {
                if (ReferenceEquals(cells[column], key))
                {
                    FocusKey(row, column);
                    Activate(key);
                    return;
                }
            }
        }
        Activate(key);
    }

    public GamepadKeyViewModel? FocusedKey =>
        _rowIndex >= 0 && _rowIndex < Rows.Count &&
        _columnIndex >= 0 && _columnIndex < Rows[_rowIndex].Count
            ? Rows[_rowIndex][_columnIndex]
            : null;

    private void Insert(string text)
    {
        Text = _getText() + text;
        _setText(Text);
        // A sticky Shift would trap uppercase after the first letter, which is worse with a controller than
        // a tap; drop it after one character so names read "Metal Gear", not "METAL GEAR".
        if (IsShifted)
        {
            IsShifted = false;
            ApplyShift();
        }
    }

    private void Backspace()
    {
        var current = _getText();
        if (current.Length == 0)
            return;
        Text = current[..^1];
        _setText(Text);
    }

    private void Move(int rowDelta, int columnDelta)
    {
        var row = Math.Clamp(_rowIndex + rowDelta, 0, Rows.Count - 1);
        var columnCount = Rows[row].Count;
        // Keep the horizontal position when stepping between rows of different lengths, clamped to the row.
        var column = columnDelta != 0
            ? Math.Clamp(_columnIndex + columnDelta, 0, columnCount - 1)
            : Math.Clamp(_columnIndex, 0, columnCount - 1);
        FocusKey(row, column);
    }

    private void FocusKey(int row, int column)
    {
        if (FocusedKey is { } previous)
            previous.IsFocused = false;
        _rowIndex = Math.Clamp(row, 0, Rows.Count - 1);
        _columnIndex = Math.Clamp(column, 0, Rows[_rowIndex].Count - 1);
        if (FocusedKey is { } next)
            next.IsFocused = true;
    }

    private void ApplyShift()
    {
        foreach (var key in _letterKeys)
            key.SetCharacter(IsShifted ? key.Value.ToUpperInvariant() : key.Value.ToLowerInvariant());
    }

    private void BuildRows()
    {
        var focusedRow = _rowIndex;
        var focusedColumn = _columnIndex;
        foreach (var row in Rows)
            foreach (var key in row)
                key.IsFocused = false;
        Rows.Clear();
        _letterKeys.Clear();

        var digits = Row("1234567890");
        if (_symbols)
        {
            Rows.Add(digits);
            Rows.Add(Row("!@#$%^&*()"));
            Rows.Add(Row("-_=+[]{}\\/"));
            Rows.Add(RowWithBackspace(":;'\",.?<>"));
            Rows.Add(BottomRow(letters: false));
        }
        else
        {
            Rows.Add(digits);
            Rows.Add(Row("qwertyuiop"));
            Rows.Add(Row("asdfghjkl"));
            Rows.Add(ShiftLettersBackspaceRow("zxcvbnm"));
            Rows.Add(BottomRow(letters: true));
        }

        if (IsShifted)
            ApplyShift();

        // Restore the focus ring near where it was, so toggling Shift/Symbols does not fling the selector.
        FocusKey(focusedRow, focusedColumn);
    }

    private List<GamepadKeyViewModel> Row(string chars)
    {
        var row = new List<GamepadKeyViewModel>(chars.Length);
        foreach (var c in chars)
            row.Add(NewCharKey(c.ToString()));
        return row;
    }

    private List<GamepadKeyViewModel> RowWithBackspace(string chars)
    {
        var row = Row(chars);
        row.Add(new GamepadKeyViewModel(GamepadKeyKind.Backspace, "⌫", weight: 1.4));
        return row;
    }

    private List<GamepadKeyViewModel> ShiftLettersBackspaceRow(string chars)
    {
        var row = new List<GamepadKeyViewModel>
        {
            new(GamepadKeyKind.Shift, "⇧", weight: 1.4),
        };
        foreach (var c in chars)
            row.Add(NewCharKey(c.ToString()));
        row.Add(new GamepadKeyViewModel(GamepadKeyKind.Backspace, "⌫", weight: 1.4));
        return row;
    }

    private List<GamepadKeyViewModel> BottomRow(bool letters) =>
    [
        new(GamepadKeyKind.SymbolsToggle, letters ? "?123" : "ABC", weight: 1.6),
        new(GamepadKeyKind.Space, " ", value: " ", weight: 5),
        _doneKey,
    ];

    private GamepadKeyViewModel NewCharKey(string glyph)
    {
        var key = new GamepadKeyViewModel(GamepadKeyKind.Character, glyph);
        // Only alphabetic keys track Shift; digits and symbols are unaffected by it.
        if (glyph.Length == 1 && char.IsLetter(glyph[0]))
            _letterKeys.Add(key);
        return key;
    }
}
