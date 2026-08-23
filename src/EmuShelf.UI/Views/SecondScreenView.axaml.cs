using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Views;

/// <summary>
/// The Thor companion surface, hosted as an embedded Avalonia top level on the second display so it
/// inherits the app's theme, fonts, and controls. Nearly all behaviour is data-bound to
/// <see cref="SecondScreenViewModel"/>; the one exception is the dock's long-press-to-manage gesture,
/// which Avalonia surfaces as a routed <see cref="InputElement.HoldingEvent"/> rather than a command.
/// </summary>
public partial class SecondScreenView : UserControl
{
    public SecondScreenView()
    {
        InitializeComponent();
        AddHandler(InputElement.HoldingEvent, OnHolding, RoutingStrategies.Bubble);
    }

    private SecondScreenViewModel? _boundModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_boundModel is not null)
            _boundModel.SelectionScrolledTo -= OnSelectionScrolledTo;
        _boundModel = DataContext as SecondScreenViewModel;
        if (_boundModel is not null)
            _boundModel.SelectionScrolledTo += OnSelectionScrolledTo;
    }

    // Gamepad navigation moves the selection on the view model; bring the newly selected badge's row into
    // view so the highlight never runs off-screen. The ListBox items are rows, so scroll the row that holds
    // the flat badge index.
    private void OnSelectionScrolledTo(int badgeIndex)
    {
        if (this.FindControl<ListBox>("AchievementsBadgeList") is not { } list ||
            DataContext is not SecondScreenViewModel viewModel)
            return;

        var columns = Math.Max(1, viewModel.AchievementColumnCount);
        var rowIndex = badgeIndex / columns;
        if (rowIndex >= 0 && rowIndex < list.ItemCount)
            list.ScrollIntoView(rowIndex);
    }

    private void OnHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState != HoldingState.Started || DataContext is not SecondScreenViewModel viewModel)
            return;

        // Walk up from whatever was held to the dock slot it belongs to, and open that slot's picker
        // (where a filled slot can be re-pinned or cleared). Tapping still launches; this is the manage
        // gesture, so it needs no permanent on-screen affordance.
        if (e.Source is Visual source &&
            source.GetSelfAndVisualAncestors()
                .OfType<Control>()
                .Select(control => control.DataContext)
                .OfType<SecondScreenSlotViewModel>()
                .FirstOrDefault() is { } slot)
        {
            viewModel.EditSlotCommand.Execute(slot.Index);
            e.Handled = true;
        }
    }

    // The target pitch (tile edge + both margins) the column count is derived from. Matches the pre-redesign
    // dense grid — a 70px tile with a 5px margin each side — so the Thor's panel lands on ~6 columns again
    // instead of the 3 the oversized (118px) tiles gave. Keep TileMargin in sync with the tile Margin in
    // the AXAML.
    private const double TargetBadgePitch = 80;
    private const double TileMargin = 5;
    // Headroom for the auto vertical scrollbar. Column count is derived from the FULL width (so a set that
    // fits without a scrollbar still gets the full column count), but the tile size fills width MINUS this,
    // so when the scrollbar does show the rightmost tile isn't clipped by it (horizontal scroll is off).
    private const double ScrollbarAllowance = 14;

    private void OnAchievementsBadgeListSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is not SecondScreenViewModel viewModel || e.NewSize.Width <= 0)
            return;

        var columns = Math.Max(1, (int)(e.NewSize.Width / TargetBadgePitch));
        // Grow each tile to fill the row exactly for that column count, so no strip is left on the right.
        var fillWidth = Math.Max(TargetBadgePitch, e.NewSize.Width - ScrollbarAllowance);
        var tileSize = Math.Max(1, (fillWidth / columns) - (2 * TileMargin));
        viewModel.AchievementTileSize = tileSize;
        viewModel.AchievementColumnCount = columns;
    }

    // Touch has no hover, so a tapped badge selects itself: the view model moves the accent ring to it and
    // shows its title/subtitle in the detail strip below the grid.
    private void OnAchievementBadgeTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: AchievementRowViewModel row } &&
            DataContext is SecondScreenViewModel viewModel)
            viewModel.SelectAchievement(row);
    }

    // Deferred badge loading: each tile requests its badge only once it attaches to the visual tree (or is
    // recycled onto a new row), so the virtualized grid never loads more badges than are on screen.
    private void OnAchievementBadgeAttached(object? sender, VisualTreeAttachmentEventArgs e) =>
        RequestAchievementBadge(sender);

    private void OnAchievementBadgeDataContextChanged(object? sender, EventArgs e) =>
        RequestAchievementBadge(sender);

    private static void RequestAchievementBadge(object? sender)
    {
        if (sender is Control { DataContext: AchievementRowViewModel row } && row.Badge is null)
            _ = row.LoadBadgeAsync(row.BadgeName);
    }
}
