using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.App.Services;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.ViewModels;

// Settings in Handheld mode (M45 H6a, decision 18). Settings is an app on the grid: a system tile that opens it, movable
// and foldable into one of your folders, never removable; the Start menu keeps a Settings row too. Open, it is
// master-detail across the screens: the Launcher lists the sections and rows (HandheldSettingsViewModel over Gamepad
// mode's projection), the Stage shows what the focused one is, its value and its description.
public sealed partial class HandheldShellViewModel
{
    /// <summary>Settings, open over the Launcher (and its card on the Stage); null when closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsOpen))]
    [NotifyPropertyChangedFor(nameof(IsLauncherOverlayOpen))]
    public partial HandheldSettingsViewModel? Settings { get; private set; }

    public bool IsSettingsOpen => Settings is not null;

    private bool _openingSettings;
    private bool _dispatchingSettings;
    private bool _settingsChangedMeanwhile;
    private HandheldSettingsCard? _settingCard;

    /// <summary>Opens Settings (A on its tile, or the Start menu's Settings row). Nothing happens if it cannot open now.</summary>
    public async Task OpenSettingsAsync()
    {
        if (Settings is not null || _openingSettings || _disposed)
            return;
        _openingSettings = true;
        GamepadSettingsViewModel? projection;
        try
        {
            projection = await _library.CreateSettingsAsync();
        }
        finally
        {
            _openingSettings = false;
        }
        if (projection is null)
            return;
        if (_disposed || Settings is not null)
        {
            _library.CloseSettings(projection, saved: false);
            return;
        }
        // Whatever was open on the Launcher goes first: Settings takes the whole screen.
        if (IsMenuOpen)
            CloseMenu();
        Sheet = null;
        var settings = new HandheldSettingsViewModel(projection);
        settings.Changed += OnSettingsChanged;
        projection.CloseRequested += saved => CloseSettings(settings, saved);
        _settingCard = null;
        Settings = settings;
        UpdateFocus();
    }

    /// <summary>Settings closed itself (B, or Save and close): home, the folder or the Title page is where it was.</summary>
    private void CloseSettings(HandheldSettingsViewModel settings, bool saved)
    {
        if (!ReferenceEquals(Settings, settings))
            return;
        Settings = null;
        settings.Changed -= OnSettingsChanged;
        settings.Dispose();
        _library.CloseSettings(settings.Projection, saved);
        _settingCard = null;
        UpdateFocus();
    }

    private bool DispatchSettingsAction(HandheldSettingsViewModel settings, GamepadAction action)
    {
        // The Stage follows once per press, and a held direction waits until it settles, as on home.
        var navigating = action is GamepadAction.NavigateUp or GamepadAction.NavigateDown or GamepadAction.NavigateLeft
            or GamepadAction.NavigateRight;
        _stageHold = null;
        _stageDefer = navigating && _heldDirection.Press() ? HandheldMotion.HeldDirectionSettle : null;
        _dispatchingSettings = true;
        _settingsChangedMeanwhile = false;
        try
        {
            settings.Dispatch(action);
        }
        finally
        {
            _dispatchingSettings = false;
            if (_settingsChangedMeanwhile && ReferenceEquals(Settings, settings))
                RefreshSettingsStage();
            _stageDefer = null;
        }
        return true;
    }

    // Changes during a press are taken together at its end; others (a rebuild after a scan, a status line) at once.
    private void OnSettingsChanged()
    {
        if (_dispatchingSettings)
        {
            _settingsChangedMeanwhile = true;
            return;
        }
        RefreshSettingsStage();
    }

    private void RefreshSettingsStage()
    {
        if (StageDelay() is { } delay)
            ScheduleStage(delay);
        else
            UpdateStage();
        UpdateHints();
    }

    /// <summary>
    /// The Stage's card: a new subject (another row, section or choice) pops in; a change to the same one (a switch
    /// flipped, a status line) is pictured again in place.
    /// </summary>
    private void ShowSettingsStage(HandheldSettingsViewModel settings)
    {
        var card = settings.Card;
        if (Stage.Kind == HandheldStageKind.Setting && card == _settingCard)
            return;
        // ShowStage pops the centre only when the kind or the title changes.
        ShowStage(HandheldStageKind.Setting, card.Title, card.Eyebrow);
        Stage.SettingEyebrow = card.Eyebrow;
        Stage.SettingValue = card.Value;
        Stage.SettingDescription = card.Description;
        Stage.SettingStatus = card.Status;
        Stage.IsSettingWarning = card.IsWarning;
        _settingCard = card;
        Stage.SettingVersion++;
    }

    // ---- The Settings tile ----

    /// <summary>The Stage for the focused Settings tile, on home or in a folder: its glyph on the accent, with a plate.</summary>
    private void ShowSettingsTileStage(string? subtitle = null) =>
        ShowStage(HandheldStageKind.SettingsTile, HandheldSettingsText.Name, subtitle ?? HandheldSettingsText.Subtitle);

    /// <summary>
    /// The Settings tile's actions sheet: Open, Move, Add to folder… on home; Open and Move to home inside a folder. It has
    /// no Remove: Settings can be moved and folded, never lost (decision 18).
    /// </summary>
    private HandheldSheetViewModel SettingsSheet(HandheldUserFolder? inFolder)
    {
        var items = new List<HandheldSheetItemViewModel> { new("Open", null, () => _ = OpenSettingsAsync()) };
        if (inFolder is null)
        {
            items.Add(new("Move", "Drop on one of your folders to put it there", StartMove));
            if (_layout.Folders.Count > 0)
                items.Add(new("Add to folder…", null, () => ShowSheet(SettingsToFolderSheet())));
        }
        else
        {
            items.Add(new("Move to home", "Out of this folder, onto home", MoveSettingsToHome));
        }
        var subtitle = inFolder is null ? "Always on home or in one of your folders" : $"In {inFolder.Name}";
        return new HandheldSheetViewModel(HandheldSettingsText.Name, subtitle, items)
        {
            Swatch = BrushOf(HandheldSettingsTileColor),
            SwatchGlyph = HandheldGlyph.Settings,
        };
    }

    private HandheldSheetViewModel SettingsToFolderSheet()
    {
        var items = _layout.Folders.Select(folder =>
        {
            var id = folder.Id;
            return new HandheldSheetItemViewModel(folder.Name, null, () => PutSettingsIn(id));
        });
        return new HandheldSheetViewModel("Add to folder", HandheldSettingsText.Name, items)
        {
            Swatch = BrushOf(HandheldSettingsTileColor),
            SwatchGlyph = HandheldGlyph.Settings,
        };
    }

    /// <summary>The Settings tile's colour: the Launcher's indigo accent, so it reads as neither a platform nor your folder.</summary>
    public const string HandheldSettingsTileColor = "#5B58D9";

    /// <summary>Puts Settings in one of your folders (Y → Add to folder…, or dropped on it in move mode).</summary>
    public void PutSettingsIn(string folderId)
    {
        if (_layout.FindFolder(folderId) is not { } folder)
            return;
        ApplyLayout(_layout.WithSettingsIn(folderId));
        ShowNotice($"Settings is in {folder.Name}");
        RefreshOpenUserFolder(folderId);
    }

    /// <summary>Takes Settings out of its folder onto home's first empty slot.</summary>
    public void MoveSettingsToHome()
    {
        if (_layout.SettingsFolder is not { } folder)
            return;
        ApplyLayout(_layout.WithSettingsOnHome(-1, out var placed));
        ShowNotice($"Settings is on home, page {(placed / SlotsPerPage) + 1}");
        RefreshOpenUserFolder(folder.Id);
    }

    // ---- Hints ----

    private void AddSettingsHints(HandheldSettingsViewModel settings)
    {
        foreach (var hint in settings.Hints)
            _nextHints.Add(hint);
    }

}
