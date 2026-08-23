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

    // The badge tile is 70px wide with a 5px margin each side (an 80px pitch). Derive the column count
    // from the viewport width so the row stride the VM slices to matches what the grid renders.
    private const double AchievementBadgePitch = 80;

    private void OnAchievementsBadgeListSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is SecondScreenViewModel viewModel && e.NewSize.Width > 0)
            viewModel.AchievementColumnCount = Math.Max(1, (int)(e.NewSize.Width / AchievementBadgePitch));
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
