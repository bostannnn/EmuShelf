using System;
using System.Linq;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Tests;

/// <summary>
/// The app-owned couch keyboard that replaces the system IME for gamepad text entry (the OS keyboard
/// can't be moved off the main screen by a third-party app and covers the field on a handheld). These
/// exercise the platform-agnostic engine: key layout, D-pad navigation, Shift/Symbols layers, and that
/// activations write straight into the target field via the supplied delegates.
/// </summary>
public sealed class GamepadKeyboardViewModelTests
{
    private sealed class Field
    {
        public string Text = string.Empty;
    }

    private static GamepadKeyboardViewModel Build(Field field, Action? onSubmit = null) =>
        new(
            title: "Search",
            placeholder: "Enter a title",
            getText: () => field.Text,
            setText: value => field.Text = value,
            doneLabel: "Done",
            onSubmit: onSubmit);

    private static GamepadKeyViewModel KeyByGlyph(GamepadKeyboardViewModel keyboard, string glyph) =>
        keyboard.Rows.SelectMany(row => row).First(key => key.Glyph == glyph);

    private static GamepadKeyViewModel KeyByKind(GamepadKeyboardViewModel keyboard, GamepadKeyKind kind) =>
        keyboard.Rows.SelectMany(row => row).First(key => key.Kind == kind);

    private static void Tap(GamepadKeyboardViewModel keyboard, GamepadKeyViewModel key) =>
        keyboard.PressKeyCommand.Execute(key);

    [Fact]
    public void TappingLetters_WritesThroughToTheField_AndMirrorsText()
    {
        var field = new Field();
        var keyboard = Build(field);

        Tap(keyboard, KeyByGlyph(keyboard, "h"));
        Tap(keyboard, KeyByGlyph(keyboard, "i"));

        Assert.Equal("hi", field.Text);
        Assert.Equal("hi", keyboard.Text);
        Assert.True(keyboard.HasText);
    }

    [Fact]
    public void Backspace_RemovesTheLastCharacter()
    {
        var field = new Field();
        var keyboard = Build(field);
        Tap(keyboard, KeyByGlyph(keyboard, "a"));
        Tap(keyboard, KeyByGlyph(keyboard, "b"));

        Tap(keyboard, KeyByKind(keyboard, GamepadKeyKind.Backspace));

        Assert.Equal("a", field.Text);

        // Backspacing an empty field is a no-op, not a crash.
        Tap(keyboard, KeyByKind(keyboard, GamepadKeyKind.Backspace));
        Tap(keyboard, KeyByKind(keyboard, GamepadKeyKind.Backspace));
        Assert.Equal(string.Empty, field.Text);
    }

    [Fact]
    public void Space_InsertsASpace()
    {
        var field = new Field();
        var keyboard = Build(field);
        Tap(keyboard, KeyByGlyph(keyboard, "a"));

        Tap(keyboard, KeyByKind(keyboard, GamepadKeyKind.Space));

        Assert.Equal("a ", field.Text);
    }

    [Fact]
    public void Shift_CapitalisesTheNextLetterOnly_ThenReleases()
    {
        var field = new Field();
        var keyboard = Build(field);

        Tap(keyboard, KeyByKind(keyboard, GamepadKeyKind.Shift));
        Assert.True(keyboard.IsShifted);

        // Letter keys re-label to uppercase while Shift is held.
        Tap(keyboard, KeyByGlyph(keyboard, "M"));
        Assert.Equal("M", field.Text);
        Assert.False(keyboard.IsShifted); // sticky Shift would trap "METALGEAR"; it releases after one key

        Tap(keyboard, KeyByGlyph(keyboard, "a"));
        Assert.Equal("Ma", field.Text);
    }

    [Fact]
    public void SymbolsToggle_SwapsTheLayout_AndSymbolKeysInsert()
    {
        var field = new Field();
        var keyboard = Build(field);

        // Letters layer has no '@'.
        Assert.DoesNotContain(keyboard.Rows.SelectMany(row => row), key => key.Glyph == "@");

        Tap(keyboard, KeyByKind(keyboard, GamepadKeyKind.SymbolsToggle));
        Tap(keyboard, KeyByGlyph(keyboard, "@"));
        Assert.Equal("@", field.Text);

        // ABC toggles back to letters.
        Tap(keyboard, KeyByKind(keyboard, GamepadKeyKind.SymbolsToggle));
        Assert.Contains(keyboard.Rows.SelectMany(row => row), key => key.Glyph == "q");
    }

    [Fact]
    public void Navigation_MovesTheFocusRing_AndClampsAtEdges()
    {
        var field = new Field();
        var keyboard = Build(field);

        // Constructor lands on the top-left letter, 'q'.
        Assert.Equal("q", keyboard.FocusedKey!.Glyph);

        keyboard.Dispatch(GamepadAction.NavigateRight);
        Assert.Equal("w", keyboard.FocusedKey!.Glyph);

        keyboard.Dispatch(GamepadAction.NavigateUp); // to the digits row, same column
        Assert.Equal("2", keyboard.FocusedKey!.Glyph);

        keyboard.Dispatch(GamepadAction.NavigateLeft);
        Assert.Equal("1", keyboard.FocusedKey!.Glyph);

        keyboard.Dispatch(GamepadAction.NavigateLeft); // clamps at the row's left edge
        Assert.Equal("1", keyboard.FocusedKey!.Glyph);

        keyboard.Dispatch(GamepadAction.NavigateUp); // clamps at the top row
        Assert.Equal("1", keyboard.FocusedKey!.Glyph);
    }

    [Fact]
    public void ExactlyOneKeyIsFocusedAtATime()
    {
        var field = new Field();
        var keyboard = Build(field);

        keyboard.Dispatch(GamepadAction.NavigateDown);
        keyboard.Dispatch(GamepadAction.NavigateRight);

        Assert.Equal(1, keyboard.Rows.SelectMany(row => row).Count(key => key.IsFocused));
    }

    [Fact]
    public void Confirm_ActivatesTheFocusedKey()
    {
        var field = new Field();
        var keyboard = Build(field);

        // Focus starts on 'q'; A presses it.
        keyboard.Dispatch(GamepadAction.Confirm);

        Assert.Equal("q", field.Text);
    }

    [Fact]
    public void Done_InvokesSubmit()
    {
        var field = new Field();
        var submitted = 0;
        var keyboard = Build(field, onSubmit: () => submitted++);

        Tap(keyboard, KeyByKind(keyboard, GamepadKeyKind.Done));

        Assert.Equal(1, submitted);
    }

    [Fact]
    public void Dispatch_OnlyConsumesDirectionalAndConfirm()
    {
        var field = new Field();
        var keyboard = Build(field);

        Assert.True(keyboard.Dispatch(GamepadAction.NavigateLeft));
        Assert.True(keyboard.Dispatch(GamepadAction.Confirm));
        // Cancel / Menu are the overlay's job (back out, open menu), never the keyboard's.
        Assert.False(keyboard.Dispatch(GamepadAction.Cancel));
        Assert.False(keyboard.Dispatch(GamepadAction.Menu));
    }

    [Fact]
    public void ExistingFieldText_IsPreservedAndAppended()
    {
        var field = new Field { Text = "Metal" };
        var keyboard = Build(field);

        Assert.Equal("Metal", keyboard.Text);

        Tap(keyboard, KeyByKind(keyboard, GamepadKeyKind.Space));
        Tap(keyboard, KeyByKind(keyboard, GamepadKeyKind.Shift));
        Tap(keyboard, KeyByGlyph(keyboard, "G"));

        Assert.Equal("Metal G", field.Text);
    }
}
