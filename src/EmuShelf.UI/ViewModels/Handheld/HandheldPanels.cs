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
    [NotifyPropertyChangedFor(nameof(HasValue))]
    public partial string? Value { get; set; }

    public bool HasValue => !string.IsNullOrEmpty(Value);

    /// <summary>A line under the label (a match's system, an achievement's points); empty hides it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    public partial string? Detail { get; set; }

    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    /// <summary>False for a row that is shown but cannot change (a value you set yourself, kept as it is).</summary>
    [ObservableProperty]
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
    public partial bool IsDimmed { get; set; }

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

    protected void RaiseChanged()
    {
        if (_disposed)
            return;
        // The hints under the keyboard follow the state too.
        OnPropertyChanged(nameof(Hints));
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
    }

    /// <summary>Replaces the rows (a new state of the wrapped view model) and focuses <paramref name="focus"/>.</summary>
    protected void SetRows(IEnumerable<HandheldPanelRowViewModel> rows, int focus)
    {
        Rows.Clear();
        foreach (var row in rows)
            Rows.Add(row);
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
