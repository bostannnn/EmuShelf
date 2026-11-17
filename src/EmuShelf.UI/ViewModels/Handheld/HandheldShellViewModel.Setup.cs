using System.Threading.Tasks;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// Setup in the Big Type look (M47 B14, the mock's empty library and <c>openPanel('wiz')</c>): with no games the Stage says so and A
/// on an empty slot starts setup, as the Start menu's Add games does; three questions are asked in big type, then the count of games
/// climbs as they are found, and setup ends on home. Today's look keeps Add games' picker and sheet. The device's own setup (storage,
/// the second screen, closing games, each system's folders and emulator, saves) is today's wizard in either look: on a first start,
/// and from Settings' "Run setup again".
/// </summary>
public sealed partial class HandheldShellViewModel
{
    /// <summary>The empty library's line and second line under Big Type (the mock's <c>describe</c> with no library).</summary>
    public const string EmptyLibraryLine = "A starts setup";
    public const string EmptyLibraryFacts = "Or open the menu and choose Add games";

    // Setup began on an empty library: once games are there, home lands on the first console's folder (the mock's setFocus(idx('ps2'))).
    private bool _setupLanding;

    /// <summary>Add games (the Start menu, and A on an empty slot with no games): Big Type's setup, or today's picker and sheet.</summary>
    public Task AddGamesOrSetupAsync()
    {
        if (!HandheldTokens.IsBigType)
            return AddGamesAsync();
        OpenSetup();
        return Task.CompletedTask;
    }

    private void OpenSetup()
    {
        if (_disposed || Settings is not null || Panel is HandheldSetupPanel)
            return;
        var fromEmpty = IsLibraryEmpty;
        var panel = new HandheldSetupPanel(_library, _clock);
        panel.StepChanged += direction =>
        {
            // The next question's letters come from the way setup went (the mock's wizStep: R on, L back).
            if (ReferenceEquals(Panel, panel))
                LastMoveDirection = direction;
        };
        panel.Finished += added => FinishSetup(panel, added, fromEmpty);
        OpenPanel(panel);
    }

    /// <summary>The mock's wizScan ending: setup goes; on an empty library home lands on the first folder, else the line says what was found.</summary>
    private void FinishSetup(HandheldSetupPanel panel, int added, bool fromEmpty)
    {
        if (!ReferenceEquals(Panel, panel))
            return;
        var connect = panel.ConnectsAchievements;
        ClosePanel(panel);
        if (fromEmpty)
        {
            _setupLanding = true;
            if (TakeSetupLanding() is { } landing)
            {
                _home = PositionOf(landing);
                ShowPage(_home.Page);
                UpdateFocus();
            }
        }
        else
        {
            ShowNotice(added == 1 ? "Found 1 new game" : $"Found {added} new games");
        }
        if (connect)
            _ = OpenAchievementsSettingsAsync();
    }

    /// <summary>The first slot with a console's folder, once setup that began on an empty library has games (null: not yet).</summary>
    private int? TakeSetupLanding()
    {
        if (!_setupLanding || _platforms.Count == 0)
            return null;
        for (var index = 0; index < _layout.SlotCount; index++)
        {
            if (FolderSourceFor(_layout.SlotAt(index)) is { Kind: HandheldFolderKind.Platform })
            {
                _setupLanding = false;
                return index;
            }
        }
        return null;
    }

    /// <summary>"Connect RetroAchievements" was the answer: once setup is done Settings opens on its RetroAchievements section.</summary>
    private async Task OpenAchievementsSettingsAsync()
    {
        await OpenSettingsAsync();
        if (Settings is { } settings)
        {
            settings.Projection.SelectedSection = SettingsSection.RetroAchievements;
            UpdateFocus();
        }
    }

    /// <summary>The empty library's Stage under Big Type: "No games yet", A starts setup, or the menu's Add games.</summary>
    private void ApplyBigTypeEmptyLibraryText()
    {
        if (!HandheldTokens.IsBigType)
            return;
        BtLine = EmptyLibraryLine;
        BtFacts = EmptyLibraryFacts;
    }
}
