using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using EmuShelf.Core.Systems;

namespace EmuShelf.App.ViewModels;

// First run in Handheld mode (M45 H6d, decision 10): the setup wizard is Settings in setup mode, laid across the two
// screens the way Settings is (the steps and their rows on the Launcher, what the focused one is on the Stage); and a
// library with no games shows where games come from on home itself. Also the dock app's Stage: its large icon on the
// colour picked from it (the dock data carries both, M45 H6d).
public sealed partial class HandheldShellViewModel
{
    private bool _libraryRead;

    /// <summary>The library has been read and has no games: home is Settings and empty slots, and A on one adds games.</summary>
    public bool IsLibraryEmpty => _libraryRead && _platforms.Count == 0;

    /// <summary>
    /// Opens the setup wizard (Settings in setup mode, over the Launcher and the Stage): on the first start, and from
    /// Settings' "Run setup again". Nothing happens if Settings is already open or cannot open now.
    /// </summary>
    public Task OpenSetupAsync() => OpenSettingsCoreAsync(_library.CreateSetupAsync);

    /// <summary>
    /// Settings' "Run setup again": Settings closes without saving, as in Gamepad mode, and the wizard opens. Posted, since
    /// the row's command runs inside Settings, which closing disposes.
    /// </summary>
    internal Task RunSetupAgainAsync()
    {
        var done = new TaskCompletionSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (Settings is { } open)
                    CloseSettings(open, saved: false);
                await OpenSetupAsync();
            }
            finally
            {
                done.TrySetResult();
            }
        });
        return done.Task;
    }

    /// <summary>Where the selector starts on an empty library: the first empty slot, which shows how to add games.</summary>
    private int FirstEmptySlotIndex()
    {
        var index = 0;
        while (index < _layout.SlotCount && !_layout.SlotAt(index).IsEmpty)
            index++;
        return index;
    }

    /// <summary>The Stage for an empty slot when there are no games: the way to add some.</summary>
    private void ShowEmptyLibraryStage()
    {
        ShowStage(HandheldStageKind.EmptySlot, "Add games", "Pick the folder your games are in");
        ApplyBigTypeEmptyLibraryText();
    }

    // ---- The dock app's Stage ----

    /// <summary>
    /// A focused dock slot: its app's launcher icon large (loaded at about 512 px for the top screen) on a sweep in the
    /// colour picked from that icon, or an empty slot's invitation to pin one. Under Big Type (M47 B14) no sweep: the mock's dock
    /// entries use the same backdrop as All apps and the menu: Lift-off at home, or the open console folder's backdrop.
    /// </summary>
    private void ShowDockAppStage(SecondScreenSlotViewModel? slot)
    {
        var label = slot?.Label;
        ShowStage(HandheldStageKind.App, label ?? "Empty dock slot", label is null ? "Pin an app from All apps" : "Android app",
            backdrop: !HandheldTokens.IsBigType && label is not null && slot?.AccentColor is { } accent ? AppSweep(accent) : null);
        Stage.AppIcon = slot?.LargeIcon ?? slot?.Icon;
    }

    /// <summary>The sweep behind a focused dock app, in its icon's colour (value equality: the same colour never crossfades).</summary>
    internal static HandheldBackdrop AppSweep(Color color) => new(color, SystemEra.Disc, Sweep: true);

    private SecondScreenViewModel? _dockSource;

    /// <summary>Follows the companion's dock slots, so an icon or colour that arrives later reaches a focused slot's Stage.</summary>
    private void WatchDockSlots(SecondScreenViewModel? companion)
    {
        if (_dockSource is { } old)
        {
            foreach (var slot in old.Dock)
                slot.PropertyChanged -= OnDockSlotChanged;
        }
        _dockSource = companion;
        if (companion is null)
            return;
        foreach (var slot in companion.Dock)
            slot.PropertyChanged += OnDockSlotChanged;
    }

    private void OnDockSlotChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed || Zone != HandheldZone.Dock || DockItems.Count <= DockIndex || !ReferenceEquals(DockItems[DockIndex].Slot, sender))
            return;
        if (e.PropertyName is nameof(SecondScreenSlotViewModel.Label) or nameof(SecondScreenSlotViewModel.Icon) or
            nameof(SecondScreenSlotViewModel.LargeIcon) or nameof(SecondScreenSlotViewModel.AccentColor))
            UpdateStage();
    }
}
