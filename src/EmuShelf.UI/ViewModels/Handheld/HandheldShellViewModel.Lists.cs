namespace EmuShelf.App.ViewModels;

/// <summary>
/// The Big Type look's lists on the Stage (M47 B7): while the Start menu, a sheet, Settings or a panel is open, the focused row is
/// the Stage's name, with what it is above it and its value or what it does under it. Today's look keeps its cards and is untouched.
/// </summary>
public partial class HandheldShellViewModel
{
    /// <summary>A list has the Launcher: the Start menu, a sheet, Settings or a panel.</summary>
    public bool IsListOpen => Settings is not null || Panel is not null || IsMenuOpen || Sheet is not null;

    /// <summary>
    /// Whether the last press opened a list (true) or closed one (false); null when it did neither. The Big Type Stage's name (the
    /// focused row) rises from the hinge into a list and drops in out of one, as into and out of a folder (M47 B7).
    /// </summary>
    public bool? LastListOpened { get; private set; }

    private bool ListOpened
    {
        set => LastListOpened = value;
    }

    // What the Stage would say with no list open (the focus under it), and what an open list says instead. The item's text is written
    // through these, so a list's never flickers back to it: each Stage text changes once per update (its rolling line too).
    private HandheldListStageText? _listText;
    private string _underFacts = string.Empty;

    private string BtLabel
    {
        set
        {
            if (_listText is null)
                Stage.Label = value;
        }
    }

    private string _underName = string.Empty;
    private bool _underMissing;

    private string BtName
    {
        set
        {
            _underName = value;
            if (_listText is null || KeepsName)
                Stage.Name = value;
        }
    }

    private string BtLine
    {
        set
        {
            if (_listText is null)
                Stage.Line = value;
        }
    }

    private string BtFacts
    {
        set
        {
            _underFacts = value;
            if (_listText is null || KeepsName)
                Stage.Facts = value;
        }
    }

    private bool BtIsMissing
    {
        set
        {
            _underMissing = value;
            if (_listText is null || KeepsName)
                Stage.IsMissing = value;
        }
    }

    /// <summary>The Stage follows a sheet's or the menu's focus in the Big Type look (today's look keeps what was under them).</summary>
    private void RefreshListStage()
    {
        if (HandheldTokens.IsBigType)
            UpdateStage();
    }

    /// <summary>
    /// The Big Type Stage's text while a list is open, over what <see cref="UpdateStageCore"/> set: the label says where (the menu,
    /// what a sheet acts on, Settings and its section, the panel), the name is the focused row, the line its value or what it does,
    /// the second line what more there is to say.
    /// </summary>
    private void ApplyBigTypeListText()
    {
        if (_listText is not { } list)
            return;
        Stage.Label = list.Label;
        Stage.Line = list.Line;
        if (KeepsName)
        {
            // The Y sheet and the Start menu keep the name (and the second line) of what they were opened over: the mock's
            // focusSheet lands with keep: true. Only the label (the kicker) and the line (the row's meta or hint) change.
            Stage.Name = _underName;
            Stage.Facts = _underFacts;
            Stage.IsMissing = _underMissing;
            return;
        }
        Stage.Name = list.Name;
        Stage.Facts = list.Facts;
        Stage.IsMissing = false;
    }

    /// <summary>
    /// Big Type's cover search (M47 B7, the mock's focusPanel: the case wears the candidate): the game's case stands where it did, its
    /// front the focused candidate. The candidate is lent to the Stage, never disposed by it; the panel takes it back as it closes.
    /// </summary>
    private void DressCaseWithCandidate()
    {
        if (!HandheldTokens.IsBigType || Settings is not null || Panel is not HandheldCoverSearchPanel { Game: { } game } covers ||
            covers.BigTypeCover is not { } candidate)
        {
            return;
        }
        if (!ReferenceEquals(Stage.Medium, game.ViewModel?.ShelfMediaShell))
        {
            Stage.Medium = game.ViewModel?.ShelfMediaShell;
            _platforms.TryGetValue(game.SystemId, out var platform);
            if (HandheldTokens.ConsoleColour(platform) is { } accent)
                Stage.MediumAccent = accent;
        }
        if (!ReferenceEquals(Stage.Art, candidate))
            ReplaceStageArt(candidate, borrowed: true);
    }

    /// <summary>The panel is closing: the Stage gives back a candidate it was lent before the panel disposes it.</summary>
    private void ReturnCandidate()
    {
        Stage.ListCover = null;
        if (_stageArtBorrowed && Panel is null)
            ReplaceStageArt(null, borrowed: false);
    }

    /// <summary>A sheet or the Start menu is the list: the Stage keeps its name (the mock's <c>keep: true</c>).</summary>
    private bool KeepsName => Settings is null && Panel is null && (IsMenuOpen || Sheet is not null);

    /// <summary>Before the Stage is updated: whether a list is open and what it says (null for none, or in today's look).</summary>
    private void TakeBigTypeListText()
    {
        _listText = null;
        if (!HandheldTokens.IsBigType || (_drift?.Shown is not null && CanDrift))
            return;
        Stage.IsListTyping = Settings?.IsTyping == true || (Settings is null && Panel?.IsTyping == true);
        Stage.ListCover = Settings is null && Panel is { IsListing: true } covers ? covers.BigTypeCover : null;
        _listText = TypingStageText() ?? BigTypeListText();
    }

    private HandheldListStageText? BigTypeListText()
    {
        HandheldListStageText? text = null;
        if (Settings is { IsTyping: false } settings)
        {
            // The mock's focusSet: on the rail "Settings" over the section's name, its subtitle under it; on a row "Settings ·
            // Section" over the row's label, its meta (a switch's On or Off, a value, else its hint) under it; no second line.
            var card = settings.Card;
            var where = card.Eyebrow == HandheldSettingsText.Name || card.Eyebrow.StartsWith("Setup", System.StringComparison.Ordinal)
                ? card.Eyebrow
                : $"{HandheldSettingsText.Name} · {card.Eyebrow}";
            var rail = settings.Projection.IsRailFocused && settings.IsNormal;
            var meta = rail ? card.Description : string.IsNullOrEmpty(card.Value) ? card.Description : card.Value;
            text = new HandheldListStageText(where, card.Title, meta, string.Empty);
        }
        else if (Settings is null && Panel is { IsListing: true } panel)
        {
            text = panel.BigTypeStage;
        }
        else if (Settings is null && Panel is null && IsMenuOpen && MenuItems.Count > MenuIndex)
        {
            // The mock's Start menu is a sheet (kicker "Menu"): the row's meta, a switch's On or Off, else its hint.
            var item = MenuItems[MenuIndex];
            text = new HandheldListStageText("Menu", string.Empty, item.IsToggle ? (item.IsOn ? "On" : "Off") : item.Hint ?? string.Empty, string.Empty);
        }
        else if (Settings is null && Panel is null && Sheet is { FocusedItem: { } row } sheet)
        {
            // The mock's focusSheet: the kicker over the name it was opened on, the row's meta or hint under it.
            text = new HandheldListStageText(sheet.Kicker ?? sheet.Title, string.Empty, row.Note ?? string.Empty, string.Empty);
        }
        return text;
    }
}

/// <summary>What the Big Type Stage says about a list's focused row (M47 B7): where, the row, its value, and more.</summary>
public readonly record struct HandheldListStageText(string Label, string Name, string Line, string Facts)
{
    /// <summary>
    /// From a card: the value is the line when there is one (its description then under it), else the description is. Each is one line
    /// on the Stage (M47 B10): a description of several lines (Activity's game: its plays, then when it was first and last played) gives
    /// its first line to the line, after the value, and the rest to the second line, so neither runs into the other.
    /// </summary>
    public static HandheldListStageText Of(string where, string title, string? value, string? description, string? status)
    {
        var lines = (description ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length > 1)
        {
            var first = string.IsNullOrEmpty(value) ? lines[0] : $"{value} · {lines[0]}";
            return new(where, title, first, string.Join(" · ", lines.Skip(1)));
        }
        return string.IsNullOrEmpty(value)
            ? new(where, title, description ?? string.Empty, status ?? string.Empty)
            : new(where, title, value, string.IsNullOrEmpty(description) ? status ?? string.Empty : description);
    }
}
