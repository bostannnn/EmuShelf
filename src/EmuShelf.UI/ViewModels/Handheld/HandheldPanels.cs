using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.App.Services;

namespace EmuShelf.App.ViewModels;

// Handheld mode's panels (M45 H6c): the screens a Y-sheet row or the Start menu opens that Gamepad mode draws as overlays
// (the scraper, the batch scraper, cover search, a game's achievements, Search, Edit title). Each is laid across the two
// screens the way Settings is: what you choose from on the Launcher (a list of rows, or the drawn keyboard), and on the
// Stage a card about the focused row (its value, a preview, a badge, the results). The logic stays in Gamepad mode's view
// models; a panel only maps their focus targets to rows, routes the pad and says what the card shows.

/// <summary>What kind of thing a panel row is: it decides how the Launcher draws it and what A does there.</summary>
public enum HandheldPanelRowKind
{
    /// <summary>A switch or a check (a field to apply, "Replace values…"): A flips it.</summary>
    Toggle,

    /// <summary>Text to type (a username, a title to search): A opens the drawn keyboard.</summary>
    Text,

    /// <summary>A command (Apply, Connect, Search, Start): A runs it.</summary>
    Button,

    /// <summary>One of a list (a match, a cover, an achievement): A picks or opens it.</summary>
    Item,
}

/// <summary>One row of a panel's list on the Launcher.</summary>
public sealed partial class HandheldPanelRowViewModel : ObservableObject
{
    public HandheldPanelRowViewModel(HandheldPanelRowKind kind, string label, object? source = null)
    {
        Kind = kind;
        Label = label;
        Source = source;
    }

    public HandheldPanelRowKind Kind { get; }

    /// <summary>What the row stands for in the wrapped view model (a field row, a match, a cover, an achievement).</summary>
    public object? Source { get; }

    [ObservableProperty]
    public partial string Label { get; set; }

    /// <summary>The trailing text: a field's proposed value, a typed name, a resolution; empty hides it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValue), nameof(Trailing))]
    public partial string? Value { get; set; }

    public bool HasValue => !string.IsNullOrEmpty(Value);

    /// <summary>A line under the label (a match's system, an achievement's points); empty hides it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail), nameof(Trailing))]
    public partial string? Detail { get; set; }

    /// <summary>What the Big Type look sets at the row's right (M47 B7: type only, no second line): its value, else its line.</summary>
    public string? Trailing => HasValue ? Value : Detail;

    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    /// <summary>False for a row that is shown but cannot change (a value you set yourself, kept as it is).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocked))]
    public partial bool IsEnabled { get; set; } = true;

    /// <summary>The panel's main action (Apply, Start, Search): drawn in the accent.</summary>
    public bool IsPrimary { get; init; }

    /// <summary>A thumbnail before the label (a cover, a badge, a scraped image). Owned by the wrapped view model.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    public partial Bitmap? Image { get; set; }

    public bool HasImage => Image is not null;

    /// <summary>A row with a picture slot keeps it while the picture loads, so the list does not jump.</summary>
    public bool HasImageSlot { get; init; }

    /// <summary>A locked badge (an achievement not yet earned): its picture is dimmed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocked))]
    public partial bool IsDimmed { get; set; }

    /// <summary>Drawn faint in the Big Type look (tokens.js <c>RowLockedOpacity</c>): a locked badge, or a row that cannot change.</summary>
    public bool IsLocked => IsDimmed || !IsEnabled;

    [ObservableProperty]
    public partial bool IsFocused { get; set; }

    public bool IsToggle => Kind == HandheldPanelRowKind.Toggle;
    public bool IsText => Kind == HandheldPanelRowKind.Text;
    public bool IsButton => Kind == HandheldPanelRowKind.Button;
    public bool IsItem => Kind == HandheldPanelRowKind.Item;
}

/// <summary>A chip above a panel's rows (an achievement filter: All, Locked, Unlocked); LB/RB step through them.</summary>
public sealed partial class HandheldPanelChipViewModel(string label) : ObservableObject
{
    [ObservableProperty]
    public partial string Label { get; set; } = label;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// What the Stage's card shows for an open panel: where it is (the eyebrow), the subject, its value large, a description, a
/// status line, and a picture (a cover, a badge, a preview) or a fan of covers (Search's results). Equal cards are the same
/// picture: the native Stage pictures it again only when it changes.
/// </summary>
public sealed record HandheldPanelCard(
    string Eyebrow,
    string Title,
    string? Value = null,
    string? Description = null,
    string? Status = null,
    Bitmap? Image = null,
    IReadOnlyList<Bitmap>? Fan = null,
    double? Progress = null,
    bool IsWarning = false,
    HandheldGlyph Glyph = HandheldGlyph.None,
    bool IsSquareImage = false)
{
    public bool HasValue => !string.IsNullOrEmpty(Value);
    public bool HasDescription => !string.IsNullOrEmpty(Description);
    public bool HasStatus => !string.IsNullOrEmpty(Status);
    public bool HasImage => Image is not null;
    public bool HasFan => Fan is { Count: > 0 };
    public bool HasProgress => Progress is not null;
    public double ProgressPercent => Math.Clamp((Progress ?? 0) * 100, 0, 100);
    public bool HasGlyph => Glyph != HandheldGlyph.None;

    /// <summary>The fan as drawn: each cover turned a little more the further it is from the middle.</summary>
    public IReadOnlyList<HandheldFanCover> FanCovers => Fan is not { Count: > 0 } fan
        ? []
        : fan.Select((cover, index) =>
        {
            var offset = index - ((fan.Count - 1) / 2.0);
            return new HandheldFanCover(cover, Avalonia.Media.Transformation.TransformOperations.Parse(
                FormattableString.Invariant($"translateY({Math.Abs(offset) * 14:0}px) rotate({offset * 6:0.#}deg)")));
        }).ToList();

    // Records compare their list by reference; the fan's covers are compared one by one.
    public bool Equals(HandheldPanelCard? other) =>
        other is not null && Eyebrow == other.Eyebrow && Title == other.Title && Value == other.Value &&
        Description == other.Description && Status == other.Status && ReferenceEquals(Image, other.Image) &&
        Progress == other.Progress && IsWarning == other.IsWarning && Glyph == other.Glyph && IsSquareImage == other.IsSquareImage &&
        FanEquals(Fan, other.Fan);

    public override int GetHashCode() => HashCode.Combine(Eyebrow, Title, Value, Description, Status, Image, Progress, Fan?.Count);

    private static bool FanEquals(IReadOnlyList<Bitmap>? a, IReadOnlyList<Bitmap>? b)
    {
        if (a is null || b is null)
            return a is null && b is null;
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (!ReferenceEquals(a[i], b[i]))
                return false;
        }
        return true;
    }
}

/// <summary>One cover of Search's fan on the Stage, with its turn.</summary>
public sealed record HandheldFanCover(Bitmap Cover, Avalonia.Media.ITransform Transform);

/// <summary>
/// A screen Handheld mode lays across both displays (M45 H6c): a heading and a list of rows (or the drawn keyboard) on the
/// Launcher, a card on the Stage. It owns the pad while open; B (or its own Close) asks the shell to close it.
/// </summary>
public abstract partial class HandheldPanelViewModel : ObservableObject, IDisposable
{
    private bool _disposed;

    /// <summary>What the panel is ("Scrape with ScreenScraper", "Search").</summary>
    public abstract string Heading { get; }

    /// <summary>What it acts on (the game's title); empty hides it.</summary>
    public virtual string Subheading => string.Empty;

    public bool HasSubheading => !string.IsNullOrEmpty(Subheading);

    /// <summary>The count the Big Type look sets beside the heading (M47 B7, the mock's <c>pcount</c>); null for none.</summary>
    public virtual string? Count => null;

    /// <summary>
    /// A picture the Big Type Stage sets beside the name for the focused row, where a game's case stands (the mock's panels: "the case
    /// above wears the one you are on"): cover search's candidate, and since M47 B14 the card's own picture (a scraper's preview of a
    /// cover, fan art or a screenshot, an achievement's badge); null for none, and while typing.
    /// </summary>
    public virtual Bitmap? BigTypeCover => IsListing ? Card.Image : null;

    /// <summary>What the Big Type Stage says about the focused row (M47 B7): from <see cref="Card"/> unless a panel says otherwise.</summary>
    public virtual HandheldListStageText BigTypeStage
    {
        get
        {
            var card = Card;
            return HandheldListStageText.Of(card.Eyebrow, card.Title, card.Value, card.Description, card.Status ?? Status);
        }
    }

    public bool HasCount => !string.IsNullOrEmpty(Count);

    /// <summary>The line under the heading: what the panel is doing or what happened ("Looking up…", "Applied 3 fields").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string? Status { get; protected set; }

    public bool HasStatus => !string.IsNullOrEmpty(Status);

    /// <summary>Working (a lookup, a search, a download): the status line shows the busy mark.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; protected set; }

    public ObservableCollection<HandheldPanelRowViewModel> Rows { get; } = [];

    public ObservableCollection<HandheldPanelChipViewModel> Chips { get; } = [];

    public bool HasChips => Chips.Count > 0;

    /// <summary>What to say when there are no rows (a filter with nothing in it); empty hides it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmptyText))]
    public partial string? EmptyText { get; protected set; }

    public bool HasEmptyText => Rows.Count == 0 && !string.IsNullOrEmpty(EmptyText);

    /// <summary>The focused row, or -1 when none (a message with nothing to choose).</summary>
    [ObservableProperty]
    public partial int FocusIndex { get; private set; } = -1;

    public HandheldPanelRowViewModel? FocusedRow => FocusIndex >= 0 && FocusIndex < Rows.Count ? Rows[FocusIndex] : null;

    /// <summary>The drawn keyboard while a text row is being typed (or the whole panel, for Search and Edit title).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTyping))]
    [NotifyPropertyChangedFor(nameof(IsListing))]
    public partial HandheldTextKeyboardViewModel? Keyboard { get; protected set; }

    public bool IsTyping => Keyboard is not null;
    public bool IsListing => Keyboard is null;

    /// <summary>The Stage's card for what is focused now.</summary>
    public abstract HandheldPanelCard Card { get; }

    /// <summary>The button hints for the state the panel is in.</summary>
    public abstract IEnumerable<HandheldHint> Hints { get; }

    /// <summary>Raised whenever the card, the hints or the focus may have changed.</summary>
    public event Action? Changed;

    /// <summary>The panel is done (B, Close, a pick that finished it). The shell closes and disposes it.</summary>
    public event Action? CloseRequested;

    /// <summary>Pad input while the panel is open. Every button is the panel's.</summary>
    public abstract void Dispatch(GamepadAction action);

    /// <summary>A tap on a row: it takes the focus and does what A does.</summary>
    public virtual void TapRow(HandheldPanelRowViewModel row)
    {
        var index = Rows.IndexOf(row);
        if (index < 0 || IsTyping)
            return;
        Focus(index);
        Dispatch(GamepadAction.Confirm);
    }

    /// <summary>A key of the drawn keyboard tapped.</summary>
    public virtual void PressKey(HandheldKeyViewModel key)
    {
    }

    /// <summary>
    /// The Back chip: leaves what is being typed without keeping it (B there deletes a letter), else does what B does.
    /// </summary>
    public void Back()
    {
        if (IsTyping)
            CancelTyping();
        else
            Dispatch(GamepadAction.Cancel);
    }

    /// <summary>Leaves the keyboard as its Cancel key does (a panel that is only a keyboard closes).</summary>
    protected virtual void CancelTyping() => RequestClose();

    /// <summary>A tap on a chip.</summary>
    public virtual void TapChip(HandheldPanelChipViewModel chip)
    {
    }

    private bool _bumped;

    /// <summary>The press could not do anything here (Big Type: the ring nudges, as the mock's bump).</summary>
    protected void Bump() => _bumped = true;

    /// <summary>Whether the last press bumped (the panel's own, or its keyboard's); reading it clears it.</summary>
    internal bool TakeBump()
    {
        var bumped = _bumped | (Keyboard?.TakeBump() ?? false);
        _bumped = false;
        return bumped;
    }

    protected void RaiseChanged()
    {
        if (_disposed)
            return;
        // The hints under the keyboard follow the state too, as does the count beside the heading.
        OnPropertyChanged(nameof(Hints));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(HasCount));
        Changed?.Invoke();
    }

    protected void RequestClose()
    {
        if (!_disposed)
            CloseRequested?.Invoke();
    }

    /// <summary>Marks one row focused (clamped; -1 when there are none).</summary>
    protected void Focus(int index)
    {
        var next = Rows.Count == 0 ? -1 : Math.Clamp(index, 0, Rows.Count - 1);
        for (var i = 0; i < Rows.Count; i++)
            Rows[i].IsFocused = i == next;
        FocusIndex = next;
        OnPropertyChanged(nameof(FocusedRow));
        OnPropertyChanged(nameof(ListFocusIndex));
        OnPropertyChanged(nameof(GridFocusRow));
    }

    // ---- The Big Type look's grid of pictures (M47 B7: cover search's candidates, as the mock draws them) ----

    /// <summary>How many to a row the Big Type look sets the rows with pictures in (0: none, every row is a row of type).</summary>
    public virtual int GridColumns => 0;

    private bool InGrid(HandheldPanelRowViewModel row) => GridColumns > 0 && row.HasImageSlot;

    /// <summary>The rows the Big Type look sets as type: all of them, or those without a picture when it has a grid.</summary>
    public IReadOnlyList<HandheldPanelRowViewModel> ListRows => GridColumns > 0 ? Rows.Where(row => !InGrid(row)).ToList() : Rows;

    /// <summary>The rows the Big Type look sets as a grid of pictures.</summary>
    public IReadOnlyList<HandheldPanelRowViewModel> GridRows => GridColumns > 0 ? Rows.Where(InGrid).ToList() : [];

    /// <summary>The focused row's row of the grid (-1 when the focus is not in it).</summary>
    public int GridFocusRow => FocusedRow is { } row && InGrid(row) ? GridRows.ToList().IndexOf(row) / GridColumns : -1;

    /// <summary>The focused row's place among <see cref="ListRows"/> (-1 when the focus is in the grid).</summary>
    public int ListFocusIndex => FocusedRow is { } row && !InGrid(row) ? ListRows.ToList().IndexOf(row) : -1;

    /// <summary>
    /// A move in the Big Type grid, as a step through the rows (they are one list underneath): left and right along a row, up and down
    /// a row of the grid; up from its first row to the last row of type. Null when the focus is not in the grid (the move is the
    /// panel's own); 0 at an edge.
    /// </summary>
    protected int? GridStep(GamepadAction action)
    {
        if (GridColumns <= 0 || FocusedRow is not { } row || !InGrid(row))
            return null;
        var first = Rows.ToList().FindIndex(InGrid);
        var count = Rows.Count(InGrid);
        var at = FocusIndex - first;
        var columns = GridColumns;
        return action switch
        {
            GamepadAction.NavigateLeft => at % columns > 0 ? -1 : 0,
            GamepadAction.NavigateRight => at % columns < columns - 1 && at + 1 < count ? 1 : 0,
            GamepadAction.NavigateUp => at >= columns ? -columns : first > 0 ? -(at + 1) : 0,
            GamepadAction.NavigateDown => at + columns < count ? columns : (at / columns) < ((count - 1) / columns) ? count - 1 - at : 0,
            _ => null,
        };
    }

    /// <summary>Replaces the rows (a new state of the wrapped view model) and focuses <paramref name="focus"/>.</summary>
    protected void SetRows(IEnumerable<HandheldPanelRowViewModel> rows, int focus)
    {
        Rows.Clear();
        foreach (var row in rows)
            Rows.Add(row);
        OnPropertyChanged(nameof(ListRows));
        OnPropertyChanged(nameof(GridRows));
        Focus(focus);
        OnPropertyChanged(nameof(HasEmptyText));
    }

    protected void SetChips(IEnumerable<string> labels, int selected)
    {
        Chips.Clear();
        var index = 0;
        foreach (var label in labels)
            Chips.Add(new HandheldPanelChipViewModel(label) { IsSelected = index++ == selected });
        OnPropertyChanged(nameof(HasChips));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Changed = null;
        CloseRequested = null;
        DisposeCore();
    }

    protected virtual void DisposeCore()
    {
    }

    protected bool IsDisposed => _disposed;

    /// <summary>A short, single-line version of a value for a row (a long description shows on the Stage).</summary>
    protected static string? Short(string? text, int length = 48)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= length ? line : line[..(length - 1)].TrimEnd() + "…";
    }
}
