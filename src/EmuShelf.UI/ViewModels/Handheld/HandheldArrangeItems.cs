using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.ViewModels;

/// <summary>One row of the Launcher's actions sheet: a label, an optional note, and what it does.</summary>
public sealed partial class HandheldSheetItemViewModel(string label, string? note, Action run) : ObservableObject
{
    public string Label { get; } = label;
    public string? Note { get; } = note;
    public bool HasNote => !string.IsNullOrEmpty(Note);
    internal Action Run { get; } = run;

    [ObservableProperty]
    public partial bool IsFocused { get; set; }
}

/// <summary>
/// The actions sheet (Y) on the Launcher: a heading for what it acts on and a list of rows. Every "Remove" row
/// names the place it means (decision 12). Confirmations (deleting a folder) are sheets too.
/// </summary>
public sealed partial class HandheldSheetViewModel : ObservableObject
{
    public HandheldSheetViewModel(string title, string? subtitle, IEnumerable<HandheldSheetItemViewModel> items)
    {
        Title = title;
        Subtitle = subtitle;
        foreach (var item in items)
            Items.Add(item);
        Focus(0);
    }

    public string Title { get; }
    public string? Subtitle { get; }
    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);

    /// <summary>What the sheet acts on, beside its title (as in the mockup): a game's cover (owned elsewhere)…</summary>
    public Bitmap? Thumbnail { get; init; }

    /// <summary>…or a swatch in a folder's colour with its monogram or glyph.</summary>
    public IBrush? Swatch { get; init; }

    public string? SwatchText { get; init; }
    public HandheldGlyph SwatchGlyph { get; init; }
    public bool HasThumbnail => Thumbnail is not null;
    public bool HasSwatch => Thumbnail is null && Swatch is not null;
    public bool HasSwatchGlyph => SwatchGlyph != HandheldGlyph.None;
    public ObservableCollection<HandheldSheetItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial int Index { get; private set; }

    public HandheldSheetItemViewModel? FocusedItem => Index >= 0 && Index < Items.Count ? Items[Index] : null;

    public void Focus(int index)
    {
        Index = Math.Clamp(index, 0, Math.Max(0, Items.Count - 1));
        for (var i = 0; i < Items.Count; i++)
            Items[i].IsFocused = i == Index;
    }
}

/// <summary>A colour chip under the folder's name.</summary>
public sealed partial class HandheldColorChipViewModel(string color) : ObservableObject
{
    public string Color { get; } = color;
    public IBrush Brush { get; } = new SolidColorBrush(Avalonia.Media.Color.Parse(color));

    /// <summary>The folder's colour.</summary>
    [ObservableProperty]
    public partial bool IsChosen { get; set; }

    /// <summary>Where the selector is.</summary>
    [ObservableProperty]
    public partial bool IsFocused { get; set; }
}

/// <summary>The two parts of the naming panel the pad moves between: the colour chips and the keys.</summary>
public enum HandheldNamingZone
{
    Colours,
    Keys,
}

public enum HandheldKeyKind
{
    Character,
    Space,
    Delete,
    Done,

    /// <summary>Settings' keyboard (M45 H6a): capitals, symbols, and leaving without saving.</summary>
    Shift,
    Symbols,
    Cancel,
}

/// <summary>One key of the naming keyboard.</summary>
public sealed partial class HandheldKeyViewModel(string label, HandheldKeyKind kind) : ObservableObject
{
    public string Label { get; } = label;
    public HandheldKeyKind Kind { get; } = kind;
    public bool IsWide => Kind != HandheldKeyKind.Character;
    public bool IsDone => Kind == HandheldKeyKind.Done;

    /// <summary>Settings' keyboard sizes its keys itself: Space wider, the other named keys medium.</summary>
    public bool IsSpace => Kind == HandheldKeyKind.Space;

    public bool IsMid => Kind is not (HandheldKeyKind.Character or HandheldKeyKind.Space);

    [ObservableProperty]
    public partial bool IsFocused { get; set; }
}

public sealed class HandheldKeyRowViewModel(IReadOnlyList<HandheldKeyViewModel> keys)
{
    public IReadOnlyList<HandheldKeyViewModel> Keys { get; } = keys;
}

/// <summary>
/// Naming (or renaming and recolouring) one of your folders on the bottom screen, while the Stage shows the
/// folder with the name following every key (decision 11). The keyboard is the mockup's, drawn on the
/// Launcher and driven by the pad (A types, B deletes, Y is a space, X or Start finishes; up from the top row
/// reaches the colour chips): Android's own keyboard cannot open on the Thor's second screen (DECISIONS, H4).
/// The field still takes a hardware keyboard. Nothing is saved until Done: B on an empty name leaves home as
/// it was.
/// </summary>
public sealed partial class HandheldNamingViewModel : ObservableObject
{
    public const int MaxNameLength = 24;
    public const string DefaultName = "New folder";

    private static readonly string[] CharacterRows = ["qwertyuiop", "asdfghjkl'", "zxcvbnm-&"];

    internal HandheldNamingViewModel(bool isNew, string name, string color, int slotIndex, IReadOnlyList<long> gameIds,
        string? folderId, int removePinFrom)
    {
        IsNew = isNew;
        SlotIndex = slotIndex;
        GameIds = gameIds;
        FolderId = folderId;
        RemovePinFrom = removePinFrom;
        _originalName = isNew || string.IsNullOrWhiteSpace(name) ? DefaultName : name;
        Text = isNew ? string.Empty : name;
        foreach (var chip in HandheldHomeLayout.FolderColors)
            Colors.Add(new HandheldColorChipViewModel(chip));
        foreach (var row in CharacterRows)
            KeyRows.Add(new HandheldKeyRowViewModel(row.Select(ch => new HandheldKeyViewModel(ch.ToString(), HandheldKeyKind.Character)).ToList()));
        KeyRows.Add(new HandheldKeyRowViewModel(
        [
            new HandheldKeyViewModel("Space", HandheldKeyKind.Space),
            new HandheldKeyViewModel("Delete", HandheldKeyKind.Delete),
            new HandheldKeyViewModel("Done", HandheldKeyKind.Done),
        ]));
        var chosen = HandheldHomeLayout.FolderColors.ToList().IndexOf(color);
        ColorIndex = chosen < 0 ? 0 : chosen;
        ChooseColor(ColorIndex);
        MoveTo(HandheldNamingZone.Keys);
    }

    public bool IsNew { get; }
    public string Heading => IsNew ? "New folder" : "Rename";
    public string Placeholder => IsNew ? "Name it…" : "Name";

    /// <summary>The home slot (across pages) the new folder lands on; for a rename, the folder's slot.</summary>
    internal int SlotIndex { get; }

    /// <summary>The games a new folder starts with (none from an empty slot, two from a merge, one from Add to folder…).</summary>
    internal IReadOnlyList<long> GameIds { get; }

    /// <summary>The folder being renamed; null for a new one.</summary>
    internal string? FolderId { get; }

    /// <summary>A merge's carried pin, which leaves home when the folder is made; -1 for none.</summary>
    internal int RemovePinFrom { get; }

    public ObservableCollection<HandheldColorChipViewModel> Colors { get; } = [];
    public ObservableCollection<HandheldKeyRowViewModel> KeyRows { get; } = [];

    /// <summary>The name as typed; the field binds it two-way (a hardware keyboard types there).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name))]
    public partial string Text { get; set; } = string.Empty;

    partial void OnTextChanged(string value)
    {
        if (value.Length > MaxNameLength)
            Text = value[..MaxNameLength];
        NameChanged?.Invoke();
    }

    /// <summary>What the folder will be called: the text trimmed; while it is blank, "New folder" (or, renaming, the old name).</summary>
    public string Name => Text.Trim() is { Length: > 0 } typed ? typed : _originalName;

    private readonly string _originalName;

    [ObservableProperty]
    public partial int ColorIndex { get; private set; }

    public string Color => HandheldHomeLayout.FolderColors[ColorIndex];
    public IBrush ColorBrush => Colors[ColorIndex].Brush;

    [ObservableProperty]
    public partial HandheldNamingZone Zone { get; private set; }

    /// <summary>The chip the selector is on while in the colour row.</summary>
    [ObservableProperty]
    public partial int FocusedColor { get; private set; }

    public int KeyRow { get; private set; }
    public int KeyColumn { get; private set; }
    public HandheldKeyViewModel FocusedKey => KeyRows[KeyRow].Keys[KeyColumn];

    /// <summary>Raised on every change the Stage shows (the name as typed, the colour).</summary>
    public event Action? NameChanged;

    internal void MoveTo(HandheldNamingZone zone)
    {
        Zone = zone;
        if (zone == HandheldNamingZone.Colours)
            FocusedColor = ColorIndex;
        UpdateFocus();
    }

    internal bool MoveColor(int delta)
    {
        var next = FocusedColor + delta;
        if (next < 0 || next >= Colors.Count)
            return false;
        FocusedColor = next;
        UpdateFocus();
        return true;
    }

    internal void ChooseColor(int index)
    {
        ColorIndex = Math.Clamp(index, 0, Colors.Count - 1);
        FocusedColor = ColorIndex;
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(ColorBrush));
        UpdateFocus();
        NameChanged?.Invoke();
    }

    /// <summary>Moves across the keys; up from the top row reaches the colour chips. False at an edge.</summary>
    internal bool MoveKey(int rows, int columns)
    {
        if (rows != 0)
        {
            var row = KeyRow + rows;
            if (row < 0)
            {
                MoveTo(HandheldNamingZone.Colours);
                return true;
            }
            if (row >= KeyRows.Count)
                return false;
            // Keep roughly the same place across rows of different lengths.
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

    /// <summary>Focuses a key (a tap on it).</summary>
    internal void FocusKey(HandheldKeyViewModel key)
    {
        for (var row = 0; row < KeyRows.Count; row++)
        {
            var column = KeyRows[row].Keys.ToList().IndexOf(key);
            if (column < 0)
                continue;
            KeyRow = row;
            KeyColumn = column;
            MoveTo(HandheldNamingZone.Keys);
            return;
        }
    }

    /// <summary>Types a character: a capital at the start and after a space, as the mockup does. False when full.</summary>
    internal bool Type(string character)
    {
        if (Text.Length >= MaxNameLength)
            return false;
        var capital = (Text.Length == 0 || Text.EndsWith(' ')) && character != " ";
        Text += capital ? character.ToUpperInvariant() : character;
        return true;
    }

    /// <summary>Deletes the last character. False when there is nothing to delete.</summary>
    internal bool DeleteLast()
    {
        if (Text.Length == 0)
            return false;
        Text = Text[..^1];
        return true;
    }

    private void UpdateFocus()
    {
        for (var i = 0; i < Colors.Count; i++)
        {
            Colors[i].IsChosen = i == ColorIndex;
            Colors[i].IsFocused = Zone == HandheldNamingZone.Colours && i == FocusedColor;
        }
        for (var row = 0; row < KeyRows.Count; row++)
        {
            for (var column = 0; column < KeyRows[row].Keys.Count; column++)
                KeyRows[row].Keys[column].IsFocused = Zone == HandheldNamingZone.Keys && row == KeyRow && column == KeyColumn;
        }
    }
}
