using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.App.Services;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// Typing in the Big Type look (M47 B9, the mock's <c>openKb</c>, <c>kbTop</c>, <c>kbMeta</c> and <c>typeWord</c>): whichever keyboard
/// is open (naming a folder, Search, Edit title, a Settings text row, a scraper's field) is drawn as the mock's keyboard on the Launcher,
/// and the word being typed is the Stage's name, with what is being typed as the label and the mock's line under it. Today's look keeps
/// its own keyboards and cards.
/// </summary>
public partial class HandheldShellViewModel
{
    /// <summary>The keyboard open now, as the Big Type keyboard and Stage show it; null when none (and always in today's look).</summary>
    [ObservableProperty]
    public partial HandheldTypingViewModel? Typing { get; private set; }

    // The keyboard is known before anything hears that the Launcher's overlay closed: on the device the native Launcher then waits for
    // the keys to drop before it takes the screen back.
    partial void OnNamingChanged(HandheldNamingViewModel? value) => SyncTyping();

    partial void OnPanelChanged(HandheldPanelViewModel? value) => SyncTyping();

    partial void OnSettingsChanged(HandheldSettingsViewModel? value) => SyncTyping();

    /// <summary>Finds the keyboard open now and keeps <see cref="Typing"/> on it (one per keyboard, so the view's keys stay).</summary>
    private void SyncTyping()
    {
        if (!HandheldTokens.IsBigType)
        {
            Typing = null;
            return;
        }
        object? owner = Settings is { } settings ? (settings.IsTyping ? settings.Keyboard : null)
            : Panel is { } panel ? (panel.IsTyping ? panel.Keyboard : null)
            : Naming;
        if (owner is null)
        {
            Typing = null;
            return;
        }
        if (ReferenceEquals(Typing?.Owner, owner))
            return;
        Typing = owner switch
        {
            HandheldNamingViewModel naming => HandheldTypingViewModel.ForNaming(naming),
            HandheldTextKeyboardViewModel keyboard when Settings is { } open => HandheldTypingViewModel.ForText(keyboard, HandheldSettingsText.Name,
                () => open.Projection.TextEntryDescription ?? string.Empty),
            HandheldTextKeyboardViewModel keyboard when Panel is HandheldSearchPanel search => HandheldTypingViewModel.ForSearch(keyboard, search),
            HandheldTextKeyboardViewModel keyboard when Panel is { } open => HandheldTypingViewModel.ForText(keyboard, open.Heading,
                () => open.Status ?? open.Card.Description ?? string.Empty),
            _ => null,
        };
    }

    /// <summary>
    /// The Stage while typing (the mock's kbTop): the label says what is typed, the name is the word (or its placeholder, faintly), the
    /// line is the mock's (letters used, matches) or the field's own description, no second line, no case. On a Search hit the Stage is
    /// that game's, as on a game anywhere (the mock's focusRes).
    /// </summary>
    private HandheldListStageText? TypingStageText()
    {
        if (Typing is not { } typing || Panel is HandheldSearchPanel { IsOnHits: true })
        {
            Stage.IsTyped = false;
            Stage.IsPlaceholder = false;
            return null;
        }
        var shown = typing.Shown;
        Stage.IsTyped = true;
        Stage.IsPlaceholder = shown.Length == 0;
        return new HandheldListStageText(typing.Kicker, shown.Length == 0 ? typing.Placeholder : shown, typing.Line, string.Empty);
    }

    /// <summary>A bump a panel's or Settings' keyboard kept for the ring (Big Type: an edge, a full word, a space first or twice).</summary>
    private void ForwardBump(bool bumped, GamepadAction? action)
    {
        if (!bumped)
            return;
        Bumped?.Invoke(action is { } pressed && pressed is GamepadAction.NavigateUp or GamepadAction.NavigateDown or
            GamepadAction.NavigateLeft or GamepadAction.NavigateRight
            ? ToDirection(pressed)
            : HandheldDirection.Right);
    }

    /// <summary>A on a Search hit (M47 B9, the mock's startGame from the keyboard): Search closes and the game plays, as A on a game does.</summary>
    private async Task PlayFromSearchAsync(HandheldSearchPanel panel, long gameId)
    {
        ClosePanel(panel);
        System.Collections.Generic.IReadOnlyList<HandheldGameEntry> games;
        try
        {
            games = await _library.GetGamesByIdAsync([gameId]);
        }
        catch (Exception)
        {
            return;
        }
        if (_disposed || games.Count == 0)
        {
            _library.ReleaseGames(games);
            return;
        }
        OpenOrPlay(games[0]);
    }

    /// <summary>A tap on a Search hit: the first focuses it, a second plays it.</summary>
    public void TapSearchHit(HandheldSearchHitViewModel hit)
    {
        if (Panel is HandheldSearchPanel search)
            Run(search, () => search.TapHit(hit));
    }

    /// <summary>A tap on a key of the Big Type keyboard, whichever owns it: the ring goes there, the key pulses and types.</summary>
    public void TapTypingKey(HandheldKeyViewModel key)
    {
        if (Naming is not null && Settings is null && Panel is null)
            PressNamingKey(key);
        else if (Settings is { } settings)
            settings.PressKey(key);
        else
            TapPanelKey(key);
        ForwardBump(Settings?.Keyboard?.TakeBump() == true || (Settings is null && Panel?.TakeBump() == true), null);
    }
}

/// <summary>
/// One keyboard as the Big Type look shows it (M47 B9): its heading and note on the Launcher, its keys (the owner's, placed as the
/// mock's), a rename's colours or Search's hits above them, and on the Stage what is typed, its placeholder and its line.
/// </summary>
public sealed class HandheldTypingViewModel : ObservableObject
{
    private readonly Func<string> _text;
    private readonly Func<string> _line;
    private readonly Func<char> _mask;

    private HandheldTypingViewModel(object owner, ObservableCollection<HandheldKeyRowViewModel> keyRows, string heading, string note,
        string kicker, string placeholder, Func<string> text, Func<string> line, Func<char>? mask = null)
    {
        Owner = owner;
        // The invisible field (a hardware keyboard) follows what the pad types.
        if (owner is System.ComponentModel.INotifyPropertyChanged changing)
        {
            changing.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(Text))
                    OnPropertyChanged(nameof(Text));
            };
        }
        KeyRows = keyRows;
        Heading = heading;
        Note = note;
        Kicker = kicker;
        Placeholder = placeholder;
        _text = text;
        _line = line;
        _mask = mask ?? (() => '\0');
    }

    /// <summary>The naming view model or the text keyboard this shows.</summary>
    public object Owner { get; }

    public HandheldNamingViewModel? Naming => Owner as HandheldNamingViewModel;

    public HandheldSearchPanel? Search { get; private init; }

    public ObservableCollection<HandheldKeyRowViewModel> KeyRows { get; }

    /// <summary>The keyboard's heading on the Launcher (the mock's "Name the folder", "Search").</summary>
    public string Heading { get; }

    /// <summary>The note under it (the mock's "It lands on the slot you picked."); empty for none.</summary>
    public string Note { get; }

    public bool HasNote => Note.Length > 0;

    /// <summary>The Stage's label while typing (the mock's "New folder", "Search").</summary>
    public string Kicker { get; }

    /// <summary>The Stage's name with nothing typed (the mock's "Name", "Search").</summary>
    public string Placeholder { get; }

    /// <summary>
    /// What is typed: the owner's text (the invisible field binds it two-way, so a hardware keyboard types there too). A key typed there
    /// moves the ring to that key on the keys shown, as the mock's <c>kbJump</c> does for a typed letter (M47 B14): Delete for a letter
    /// taken off, Space for a space; a character not on this page leaves the ring where it is.
    /// </summary>
    public string Text
    {
        get => _text();
        set
        {
            var before = _text();
            if (Naming is { } naming)
                naming.Text = value;
            else if (Owner is HandheldTextKeyboardViewModel keyboard)
                keyboard.Text = value;
            FollowTyped(before, _text());
        }
    }

    private void FollowTyped(string before, string after)
    {
        if (!HandheldTokens.IsBigType || before == after)
            return;
        HandheldKeyViewModel? key = null;
        var keys = KeyRows.SelectMany(row => row.Keys).ToList();
        if (after.Length == before.Length + 1 && after.StartsWith(before, StringComparison.Ordinal))
        {
            var typed = after[^1];
            key = typed == ' '
                ? keys.FirstOrDefault(candidate => candidate.Kind == HandheldKeyKind.Space)
                : keys.FirstOrDefault(candidate => candidate.Kind == HandheldKeyKind.Character &&
                    string.Equals(candidate.Value, typed.ToString(), StringComparison.OrdinalIgnoreCase));
        }
        else if (after.Length == before.Length - 1 && before.StartsWith(after, StringComparison.Ordinal))
        {
            key = keys.FirstOrDefault(candidate => candidate.Kind == HandheldKeyKind.Delete);
        }
        if (key is null)
            return;
        if (Naming is { } naming)
            naming.FocusKey(key);
        else if (Search is { } search)
            search.FocusTypedKey(key);
        else if (Owner is HandheldTextKeyboardViewModel keyboard)
            keyboard.FocusKey(key);
    }


    /// <summary>What is typed, as the Stage shows it (a secret as dots until Y shows it).</summary>
    public string Shown => _mask() is var mask && mask != '\0' ? new string(mask, _text().Length) : _text();

    /// <summary>The Stage's line (the mock's kbMeta, or the field's own description).</summary>
    public string Line => _line();

    /// <summary>A naming keyboard's colours (the app's: a folder's colour is chosen as it is named); null for the others.</summary>
    public ObservableCollection<HandheldColorChipViewModel>? Colors => Naming?.Colors;

    public bool HasColors => Colors is { Count: > 0 };

    /// <summary>The colours sit as a row of keys above the letters (the app's place; the mock names a folder in the next colour).</summary>
    public Thickness ColoursMargin => new(HandheldBigTypeKeys.CentredLeft(Colors?.Count ?? 0), HandheldBigTypeKeys.RowTop(-1), 0, 0);

    public ObservableCollection<HandheldSearchHitViewModel>? Hits => Search?.Hits;

    public bool HasHits => Search is not null;

    public static HandheldTypingViewModel ForNaming(HandheldNamingViewModel naming) =>
        new(naming, naming.KeyRows, naming.BigTypeHeading, naming.Note, naming.BigTypeKicker, naming.Placeholder, () => naming.Text,
            () => naming.LetterCount);

    public static HandheldTypingViewModel ForSearch(HandheldTextKeyboardViewModel keyboard, HandheldSearchPanel search) =>
        new(keyboard, keyboard.KeyRows, "Search", string.Empty, "Search", "Search", () => keyboard.Text, () => search.BigTypeLine) { Search = search };

    /// <summary>Any other field: its title as the heading and the placeholder, what it belongs to as the label.</summary>
    public static HandheldTypingViewModel ForText(HandheldTextKeyboardViewModel keyboard, string kicker, Func<string> line) =>
        new(keyboard, keyboard.KeyRows, keyboard.Title, string.Empty, kicker, keyboard.Title, () => keyboard.Text, line,
            () => keyboard.MaskChar);
}
