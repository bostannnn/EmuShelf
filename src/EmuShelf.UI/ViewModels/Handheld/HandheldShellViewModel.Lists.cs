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

    private string BtName
    {
        set
        {
            if (_listText is null)
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
            if (_listText is null)
                Stage.Facts = value;
            else if (Sheet is not null && Settings is null && Panel is null && !IsMenuOpen)
                Stage.Facts = value;
        }
    }

    private bool BtIsMissing
    {
        set
        {
            if (_listText is null)
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
        Stage.Name = list.Name;
        Stage.Line = list.Line;
        // A sheet keeps saying what it acts on under it (a game's emulator and last played, a folder's last game).
        Stage.Facts = Sheet is not null && Settings is null && Panel is null && !IsMenuOpen ? _underFacts : list.Facts;
        Stage.IsMissing = false;
    }

    /// <summary>Before the Stage is updated: whether a list is open and what it says (null for none, or in today's look).</summary>
    private void TakeBigTypeListText()
    {
        _listText = null;
        if (!HandheldTokens.IsBigType || (_drift?.Shown is not null && CanDrift))
            return;
        Stage.IsListTyping = Settings?.IsTyping == true || (Settings is null && Panel?.IsTyping == true);
        Stage.ListCover = Settings is null && Panel is { IsListing: true } covers ? covers.BigTypeCover : null;
        _listText = BigTypeListText();
    }

    private HandheldListStageText? BigTypeListText()
    {
        HandheldListStageText? text = null;
        if (Settings is { IsTyping: false } settings)
        {
            var card = settings.Card;
            var where = card.Eyebrow == HandheldSettingsText.Name || card.Eyebrow.StartsWith("Setup", System.StringComparison.Ordinal)
                ? card.Eyebrow
                : $"{HandheldSettingsText.Name} · {card.Eyebrow}";
            text = HandheldListStageText.Of(where, card.Title, card.Value, card.Description, card.Status);
        }
        else if (Settings is null && Panel is { IsListing: true } panel)
        {
            text = panel.BigTypeStage;
        }
        else if (Settings is null && Panel is null && IsMenuOpen && MenuItems.Count > MenuIndex)
        {
            var item = MenuItems[MenuIndex];
            text = new HandheldListStageText("Menu", item.Label, item.IsToggle ? (item.IsOn ? "On" : "Off") : item.Hint ?? string.Empty, string.Empty);
        }
        else if (Settings is null && Panel is null && Sheet is { FocusedItem: { } row } sheet)
        {
            text = new HandheldListStageText(sheet.Title, row.Label, row.Note ?? sheet.Subtitle ?? string.Empty, string.Empty);
        }
        return text;
    }
}

/// <summary>What the Big Type Stage says about a list's focused row (M47 B7): where, the row, its value, and more.</summary>
public readonly record struct HandheldListStageText(string Label, string Name, string Line, string Facts)
{
    /// <summary>From a card: the value is the line when there is one (its description then under it), else the description is.</summary>
    public static HandheldListStageText Of(string where, string title, string? value, string? description, string? status) =>
        string.IsNullOrEmpty(value)
            ? new(where, title, description ?? string.Empty, status ?? string.Empty)
            : new(where, title, value, string.IsNullOrEmpty(description) ? status ?? string.Empty : description);
}
