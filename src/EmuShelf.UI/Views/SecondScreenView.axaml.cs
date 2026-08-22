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
}
