using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.App.Services;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.ViewModels;

// The Y sheet's actions, Search, Add games and the screens behind them (M45 H6c). Every action is Gamepad mode's, through
// the library seam; the screens it opens that Gamepad mode draws as overlays are panels here, laid across both displays
// the way Settings is (rows or the drawn keyboard on the Launcher, a card on the Stage). Anything that changed a game (a
// title, a cover, scraped data, a removal) reads it again, so home, the open folder and the Title page show the result.
public sealed partial class HandheldShellViewModel
{
    /// <summary>The open panel (over the Launcher, with its card on the Stage); null when none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPanelOpen))]
    [NotifyPropertyChangedFor(nameof(IsLauncherOverlayOpen))]
    public partial HandheldPanelViewModel? Panel { get; private set; }

    public bool IsPanelOpen => Panel is not null;

    /// <summary>How long Search waits for typing to pause before it reads the library (the host's; zero in tests).</summary>
    public TimeSpan SearchDebounce { get; set; } = TimeSpan.FromMilliseconds(250);

    private Func<HandheldPanelViewModel, Task>? _panelClosed;
    private bool _dispatchingPanel;
    private bool _panelChangedMeanwhile;
    private HandheldPanelCard? _panelCard;
    private string _lastSearch = string.Empty;
    private int _searchGeneration;

    /// <summary>
    /// Opens a panel over the Launcher. <paramref name="closed"/> runs once it closes (B, Close, a finished pick), before it
    /// is disposed: it lets go of the wrapped view model and reads back what changed.
    /// </summary>
    private void OpenPanel(HandheldPanelViewModel panel, Func<HandheldPanelViewModel, Task>? closed = null)
    {
        if (_disposed || Settings is not null)
        {
            panel.Dispose();
            if (closed is not null)
                _ = closed(panel);
            return;
        }
        if (Panel is { } open)
            ClosePanel(open);
        // A panel takes the Launcher: the menu or a sheet that opened it goes first.
        if (IsMenuOpen)
            CloseMenu();
        Sheet = null;
        panel.Changed += () => OnPanelContentChanged(panel);
        panel.CloseRequested += () => ClosePanel(panel);
        _panelClosed = closed;
        _panelCard = null;
        Panel = panel;
        UpdateFocus();
    }

    /// <summary>Closes the panel back onto what was under it (home, the folder, the Title page).</summary>
    private void ClosePanel(HandheldPanelViewModel panel)
    {
        if (!ReferenceEquals(Panel, panel))
            return;
        var closed = _panelClosed;
        _panelClosed = null;
        Panel = null;
        _panelCard = null;
        // The card's pictures belong to the panel's view models, which go now: the Stage lets go of them first.
        Stage.PanelCard = null;
        try
        {
            if (closed is not null)
                _ = RunClosedAsync(closed, panel);
        }
        finally
        {
            panel.Dispose();
        }
        if (!_disposed)
            UpdateFocus();
    }

    // What a panel's close reads back runs after the panel is gone; a failure there is reported, never thrown.
    private async Task RunClosedAsync(Func<HandheldPanelViewModel, Task> closed, HandheldPanelViewModel panel)
    {
        try
        {
            await closed(panel);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ShowNotice($"Something went wrong: {ex.Message}", HandheldGlyph.Alert);
        }
    }

    private bool DispatchPanelAction(HandheldPanelViewModel panel, GamepadAction action)
    {
        // The Stage follows once per press, and a held direction waits until it settles, as on home.
        var navigating = action is GamepadAction.NavigateUp or GamepadAction.NavigateDown or GamepadAction.NavigateLeft
            or GamepadAction.NavigateRight;
        _stageHold = null;
        _stageDefer = navigating && _heldDirection.Press() ? HandheldMotion.HeldDirectionSettle : null;
        _dispatchingPanel = true;
        _panelChangedMeanwhile = false;
        try
        {
            panel.Dispatch(action);
        }
        finally
        {
            _dispatchingPanel = false;
            if (_panelChangedMeanwhile && ReferenceEquals(Panel, panel))
                RefreshPanelStage();
            _stageDefer = null;
        }
        return true;
    }

    // Changes during a press are taken together at its end; others (a search landing, a badge loading) at once.
    private void OnPanelContentChanged(HandheldPanelViewModel panel)
    {
        if (!ReferenceEquals(Panel, panel))
            return;
        if (_dispatchingPanel)
        {
            _panelChangedMeanwhile = true;
            return;
        }
        RefreshPanelStage();
    }

    private void RefreshPanelStage()
    {
        // Only a held direction waits; anything else shows at once, since the card it replaces may hold pictures that are
        // about to go (a refreshed set's badges, a search's covers).
        if (_stageDefer is { } defer)
            ScheduleStage(defer);
        else
            UpdateStage();
        UpdateHints();
    }

    /// <summary>The Stage's card for the panel: a new subject pops in, a change to the same one is pictured again in place.</summary>
    private void ShowPanelStage(HandheldPanelViewModel panel)
    {
        var card = panel.Card;
        if (Stage.Kind == HandheldStageKind.Panel && card == _panelCard)
            return;
        // ShowStage pops the centre only when the kind or the subject changes. While typing, the card's title is the text
        // itself: it is pictured again in place on each key, not popped.
        ShowStage(HandheldStageKind.Panel, panel.IsTyping ? card.Eyebrow : card.Title, card.Eyebrow);
        Stage.PanelCard = card;
        _panelCard = card;
    }

    // ---- Touch on a panel (the Launcher's view wiring) ----

    public void TapPanelRow(HandheldPanelRowViewModel row)
    {
        if (Panel is { } panel)
            Run(panel, () => panel.TapRow(row));
    }

    public void TapPanelKey(HandheldKeyViewModel key)
    {
        if (Panel is { } panel)
            Run(panel, () => panel.PressKey(key));
    }

    public void TapPanelChip(HandheldPanelChipViewModel chip)
    {
        if (Panel is { } panel)
            Run(panel, () => panel.TapChip(chip));
    }

    /// <summary>The panel's Back chip: out of the keyboard without keeping what was typed, else what B does.</summary>
    public void TapPanelBack()
    {
        if (Panel is { } panel)
            Run(panel, panel.Back);
    }

    private void Run(HandheldPanelViewModel panel, Action tap)
    {
        _dispatchingPanel = true;
        _panelChangedMeanwhile = false;
        try
        {
            tap();
        }
        finally
        {
            _dispatchingPanel = false;
            if (_panelChangedMeanwhile && ReferenceEquals(Panel, panel))
                RefreshPanelStage();
        }
    }

    // ---- Search (the Start menu) ----

    /// <summary>Opens Search: the keyboard on the Launcher, the matches fanning on the Stage. It starts from the last query.</summary>
    public Task OpenSearchAsync()
    {
        var panel = new HandheldSearchPanel(_library, _lastSearch, _clock) { Debounce = SearchDebounce };
        panel.ResultsRequested += (query, ids) => ShowSearchResults(panel, query, ids);
        OpenPanel(panel, closed =>
        {
            _lastSearch = ((HandheldSearchPanel)closed).Query.Trim();
            return Task.CompletedTask;
        });
        return Task.CompletedTask;
    }

    /// <summary>Search's Done: the matches open as a folder over home (B there comes back to where you were).</summary>
    private void ShowSearchResults(HandheldSearchPanel panel, string query, IReadOnlyList<long> ids)
    {
        _lastSearch = query;
        ClosePanel(panel);
        CloseTitle();
        // Each search is its own folder, so the peek cache never shows one search's covers for another.
        if (_searchGeneration > 0)
            InvalidatePeek($"s:{_searchGeneration}");
        var source = HandheldFolderSource.ForSearch(query, ids, ++_searchGeneration);
        var slot = OpenFolder?.HomeSlot ?? _home.Slot;
        _ = OpenFolderAsync(source, slot);
    }

    // ---- Add games (the Start menu): the platform's folder picker, then which system they are for ----

    /// <summary>Picks a folder (Android's picker on the Thor), then asks which system its games are for.</summary>
    public async Task AddGamesAsync()
    {
        if (_library.IsBusy)
        {
            ShowNotice("Adding games can start once the current task finishes.");
            return;
        }
        string? folder;
        try
        {
            folder = await _library.PickImportFolderAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ShowNotice($"Could not open the folder picker: {ex.Message}", HandheldGlyph.Alert);
            return;
        }
        if (folder is null || _disposed)
            return;
        var systems = _library.ImportSystems;
        if (systems.Count == 0)
            return;
        var name = Path.GetFileName(folder.TrimEnd('/', '\\'));
        ShowSheet(new HandheldSheetViewModel("Add games", $"From {(name.Length > 0 ? name : folder)} · which system are they for?",
            systems.Select(system => new HandheldSheetItemViewModel(system.Name, system.ShortName, () =>
            {
                // Other library work may have started while the picker was open: the import waits for it.
                if (_library.IsBusy)
                    ShowNotice("Adding games can start once the current task finishes.");
                else
                    _ = _library.ImportFolderAsync(folder, system.Id);
            })))
        {
            Swatch = BrushOf(HandheldSettingsTileColor),
            SwatchGlyph = HandheldGlyph.Add,
        });
    }

    // ---- The game's actions ----

    /// <summary>Gamepad mode's full-screen achievements for the game, with the focused badge on the Stage.</summary>
    private async Task OpenAchievementsAsync(HandheldGameEntry game)
    {
        AchievementDetailsViewModel? details;
        try
        {
            details = await _library.CreateAchievementsAsync(game);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ShowNotice($"Could not open achievements for {game.Title}: {ex.Message}", HandheldGlyph.Alert);
            return;
        }
        if (details is null)
            return; // the status line says why
        var panel = new HandheldAchievementsPanel(details);
        OpenPanel(panel, _ =>
        {
            details.Dispose();
            return Task.CompletedTask;
        });
        if (ReferenceEquals(Panel, panel))
            _ = details.RefreshIfStaleAsync();
    }

    private void OpenTitleEdit(HandheldGameEntry game)
    {
        var panel = new HandheldTitleEditPanel(game, _library.EditableTitle(game));
        panel.Committed += title => _ = RenameAsync(panel, game, title);
        OpenPanel(panel);
        _ = LoadPanelCoverAsync(panel, game);
    }

    private async Task LoadPanelCoverAsync(HandheldTitleEditPanel panel, HandheldGameEntry game)
    {
        try
        {
            var cover = await _library.LoadCoverAsync(game, TitleCoverDecodeWidth, fullSize: true, default);
            if (ReferenceEquals(Panel, panel))
                panel.Cover = cover;
            else
                cover?.Dispose();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The card keeps its glyph without a cover.
        }
    }

    private async Task RenameAsync(HandheldTitleEditPanel panel, HandheldGameEntry game, string title)
    {
        ClosePanel(panel);
        bool renamed;
        try
        {
            renamed = await _library.RenameAsync(game, title);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ShowNotice($"Could not rename {game.Title}: {ex.Message}", HandheldGlyph.Alert);
            return;
        }
        if (renamed)
            await RefreshGameAsync(game.Id);
    }

    private void OpenCoverSearch(HandheldGameEntry game)
    {
        if (_library.CreateCoverSearch(game) is not { } details)
        {
            ShowNotice(_library.CoverSearchUnavailableReason, HandheldGlyph.Alert);
            return;
        }
        var panel = new HandheldCoverSearchPanel(details, game.Title);
        OpenPanel(panel, async _ =>
        {
            var picked = panel.Picked;
            details.Dispose();
            if (picked is null)
                return;
            await _library.ApplyCoverAsync(game, picked);
            await RefreshGameAsync(game.Id);
        });
        if (ReferenceEquals(Panel, panel))
            _ = details.LoadAsync();
    }

    private void OpenScraper(HandheldGameEntry game)
    {
        if (_library.CreateScraper(game) is not { } details)
            return; // the status line says why
        var panel = new HandheldScraperPanel(details, game.Title);
        OpenPanel(panel, async _ =>
        {
            var applied = details.HasAppliedChanges;
            var summary = panel.AppliedSummary;
            await _library.CloseScraperAsync(details);
            // The scrape finished: say what it wrote, and show it.
            if (summary is not null)
                ShowNotice($"{game.Title}: {summary}");
            if (applied)
                await RefreshGameAsync(game.Id);
        });
        if (ReferenceEquals(Panel, panel))
            _ = details.LoadAsync();
    }

    private async Task OpenBatchScraperAsync(HandheldFolderSource source)
    {
        IReadOnlyList<long> ids;
        try
        {
            ids = source.Kind switch
            {
                HandheldFolderKind.Platform when source.Platform is { } platform => await _library.GetPlatformGameIdsAsync(platform.SystemId),
                HandheldFolderKind.Recent => _recentIds,
                _ => source.GameIds ?? [],
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ShowNotice($"Could not read {source.Name}: {ex.Message}", HandheldGlyph.Alert);
            return;
        }
        if (ids.Count == 0 || _disposed)
            return;
        if (_library.CreateBatchScraper(ids, source.Name) is not { } details)
        {
            ShowNotice("ScreenScraper is unavailable right now.", HandheldGlyph.Alert);
            return;
        }
        var panel = new HandheldBatchScraperPanel(details, source.Name);
        OpenPanel(panel, async _ =>
        {
            var applied = details.Batch.AppliedChanges;
            var summary = panel.FinishedSummary;
            await _library.CloseBatchScraperAsync(details);
            if (summary is not null)
                ShowNotice(summary);
            if (applied)
            {
                ReleasePeekCovers();
                await RefreshOpenFolderAsync();
                UpdateFocus();
            }
        });
    }

    /// <summary>A multi-disc game's discs as a sheet, from its tile (on the Title page, the page's own chooser).</summary>
    private HandheldSheetViewModel DiscSheet(HandheldGameEntry game)
    {
        var discs = _library.DiscsOf(game);
        var items = discs.Numbers.Select(number => new HandheldSheetItemViewModel($"Disc {number}",
            number == discs.Selected ? "Starts here" : null, () => _ = ChooseDiscAsync(game, number)));
        var sheet = new HandheldSheetViewModel("Which disc?", game.Title, items) { Thumbnail = CoverFor(game.Id) };
        sheet.Focus(Math.Max(0, discs.Numbers.ToList().IndexOf(discs.Selected)));
        return sheet;
    }

    private async Task ChooseDiscAsync(HandheldGameEntry game, int disc)
    {
        bool saved;
        try
        {
            saved = await _library.SelectDiscAsync(game, disc);
        }
        catch (Exception)
        {
            saved = false;
        }
        ShowNotice(saved ? $"{game.Title} starts on disc {disc}" : "Could not change the disc", saved ? HandheldGlyph.Check : HandheldGlyph.Alert);
    }

    /// <summary>The confirmation for Remove from library, which says the files stay and that home and your folders lose it.</summary>
    private void ConfirmRemoveFromLibrary(HandheldGameEntry game)
    {
        var places = _layout.IsPinned(game.Id) || _layout.Folders.Any(folder => folder.Contains(game.Id))
            ? " It leaves home and your folders too."
            : string.Empty;
        ShowSheet(new HandheldSheetViewModel("Remove from library?",
            $"{game.Title}. Its files stay where they are.{places} A rescan of its folder adds it back.",
            [
                new HandheldSheetItemViewModel("Remove from library", null, () => _ = RemoveFromLibraryAsync(game)) { IsDestructive = true },
                new HandheldSheetItemViewModel("Keep it", null, () => { }),
            ])
        {
            Thumbnail = CoverFor(game.Id),
        });
    }

    private async Task RemoveFromLibraryAsync(HandheldGameEntry game)
    {
        if (TitlePage?.Entry.Id == game.Id)
            CloseTitle();
        bool removed;
        try
        {
            removed = await _library.RemoveFromLibraryAsync(game);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ShowNotice($"Could not remove {game.Title}: {ex.Message}", HandheldGlyph.Alert);
            return;
        }
        if (!removed || _disposed)
            return;
        // Home and your folders drop it as the layout is reconciled; the open folder is read again without it.
        await InitializeAsync();
        await RefreshOpenFolderAsync();
    }

    /// <summary>
    /// Reads a game again after it changed (its title, cover or scraped data): home's pins and peeks, the open folder, and a
    /// Title page open on it (closed and opened again on the fresh read, so it shows the new title and cover).
    /// </summary>
    private async Task RefreshGameAsync(long gameId)
    {
        var reopen = TitlePage?.Entry.Id == gameId;
        if (reopen)
            CloseTitle();
        ReleasePeekCovers();
        await InitializeAsync();
        await RefreshOpenFolderAsync();
        if (_disposed)
            return;
        _stageGameId = -1;
        UpdateFocus();
        if (!reopen || TitlePage is not null || Panel is not null)
            return;
        IReadOnlyList<HandheldGameEntry> fresh;
        try
        {
            fresh = await _library.GetGamesByIdAsync([gameId]);
        }
        catch (Exception)
        {
            return;
        }
        if (fresh.Count == 0 || _disposed || TitlePage is not null)
        {
            _library.ReleaseGames(fresh);
            return;
        }
        OpenTitle(fresh[0]);
        // This read belongs to the page alone: it is released when the page closes.
        _releaseWithTitle.Add(fresh[0]);
    }

    // ---- The dock's slots: Open, Change app…, Clear slot (as the companion's long press does) ----

    private HandheldSheetViewModel? DockSheet(HandheldDockItemViewModel item)
    {
        if (item.Kind != HandheldDockKind.App || item.Slot is not { } slot || _companion is not { } companion)
            return null;
        var number = slot.Index + 1;
        if (slot.IsEmpty)
        {
            return new HandheldSheetViewModel($"Dock slot {number}", "Empty",
                [new HandheldSheetItemViewModel("Choose an app…", null, () => companion.EditSlotCommand.Execute(slot.Index))])
            {
                Swatch = BrushOf("#A7AEC6"),
                SwatchGlyph = HandheldGlyph.Plus,
            };
        }
        var label = slot.Label!;
        return new HandheldSheetViewModel(label, $"Dock slot {number} · Android app",
            [
                new HandheldSheetItemViewModel("Open", null, () => companion.ActivateSlotCommand.Execute(slot)),
                new HandheldSheetItemViewModel("Change app…", null, () => companion.EditSlotCommand.Execute(slot.Index)),
                new HandheldSheetItemViewModel("Clear slot", "The app stays on the device", () =>
                {
                    if (companion.ClearSlotAt(slot.Index))
                        ShowNotice($"Slot {number} cleared");
                }),
            ])
        {
            Thumbnail = slot.Icon,
        };
    }

    /// <summary>A long press on a dock button: a pinned app's sheet (the companion's hold-to-manage), else what a tap does.</summary>
    public void LongPressDockItem(HandheldDockItemViewModel item)
    {
        var index = DockItems.IndexOf(item);
        if (IsTouchBlocked || IsMenuOpen || TitlePage is not null || index < 0)
            return;
        if (OpenFolder is { } folder)
            folder.FocusedHeaderItem = null;
        Zone = HandheldZone.Dock;
        DockIndex = index;
        UpdateFocus();
        if (DockSheet(item) is { } sheet)
            ShowSheet(sheet);
        else
            Activate();
    }
}
