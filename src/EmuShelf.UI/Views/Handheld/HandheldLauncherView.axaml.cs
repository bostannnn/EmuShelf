using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views.Handheld;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.Views;

/// <summary>
/// Handheld mode's bottom screen. A pure view onto <c>HandheldShellViewModel</c>; its motion (the travelling
/// selector, tracks, folder zoom, launch lift) is <see cref="HandheldLauncherMotion"/>. Where the platform has a
/// native Launcher (<see cref="HandheldNativeStage.Launcher"/>), <see cref="HandheldNativeLauncherFeed"/> pictures
/// this view's pieces for it. Touch is wired here and handed to the shell: tap selects and a second tap opens,
/// a swipe turns the page, a long press lifts a home tile and a drag carries it (the native Launcher passes
/// touches through to this view, which stays hit-testable while invisible).
/// </summary>
public partial class HandheldLauncherView : UserControl, IHandheldSelectorHost
{
    private static readonly TimeSpan LongPress = TimeSpan.FromMilliseconds(480);
    private const double SwipeDistance = 80;
    private const double DragSlop = 24;

    private readonly HandheldLauncherMotion _motion;
    private readonly HandheldNativeLauncherFeed? _feed;
    private readonly DispatcherTimer _longPress;
    private HandheldShellViewModel? _shell;
    private Point? _pressAt;
    private (int Page, int Slot)? _pressedSlot;
    // Move mode started by this touch's long press: letting go drops. A tap during a pad-driven move does not.
    private bool _carrying;

    public HandheldLauncherView()
    {
        InitializeComponent();
        // View wiring only: the motion follows whichever shell the view shows.
        _motion = new HandheldLauncherMotion(this);
        // On a platform with a native Launcher this view stays laid out but invisible, and lends it pictures.
        _feed = HandheldNativeStage.Launcher is { } host ? new HandheldNativeLauncherFeed(this, _motion, host) : null;
        _longPress = new DispatcherTimer { Interval = LongPress };
        _longPress.Tick += (_, _) => OnLongPress();
        DataContextChanged += (_, _) =>
        {
            _shell = DataContext as HandheldShellViewModel;
            _motion.Attach(_shell);
            _feed?.Attach(_shell);
        };
        AddHandler(PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    void IHandheldSelectorHost.OnAnchorChanged(Control anchor, bool isFocused)
    {
        _motion.OnAnchorChanged(anchor, isFocused);
        _feed?.OnFocusChanged();
    }

    // ---- Touch ----

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_shell is not { } shell)
            return;
        var source = e.Source as Visual;
        // The overlays take taps on their own rows; a tap on the sheet's scrim closes it.
        if (shell.IsSheetOpen)
        {
            if (Find<HandheldSheetItemViewModel>(source) is { } row)
                shell.RunSheetItem(row);
            else if (!IsInside(source, "SheetCard"))
                shell.DispatchGamepadAction(GamepadAction.Cancel);
            e.Handled = true;
            return;
        }
        if (shell.IsNaming)
        {
            if (Find<HandheldColorChipViewModel>(source) is { } chip && shell.Naming is { } naming)
            {
                shell.ChooseNamingColor(naming.Colors.IndexOf(chip));
                e.Handled = true;
            }
            else if (Find<HandheldKeyViewModel>(source) is { } key)
            {
                shell.PressNamingKey(key);
                e.Handled = true;
            }
            // A press on the field itself is left to the field (a hardware keyboard types there).
            return;
        }
        _pressAt = e.GetPosition(this);
        _pressedSlot = SlotAt(source);
        if (_pressedSlot is not null && !shell.IsMoving)
            _longPress.Start();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_shell is not { } shell || _pressAt is not { } start)
            return;
        var delta = e.GetPosition(this) - start;
        if (shell.IsMoving && _carrying)
        {
            if (SlotAt(this.GetInputElementsAt(e.GetPosition(this)).OfType<Visual>().FirstOrDefault()) is { } over)
                shell.CarryTo((over.Page * HandheldHomeLayout.SlotsPerPage) + over.Slot);
            return;
        }
        // Moving off the tile before the long press fires is a swipe, not a press.
        if (Math.Abs(delta.X) > DragSlop || Math.Abs(delta.Y) > DragSlop)
            _longPress.Stop();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _longPress.Stop();
        if (_shell is not { } shell || _pressAt is not { } start)
            return;
        _pressAt = null;
        var delta = e.GetPosition(this) - start;
        if (shell.IsMoving)
        {
            // Lifted by this touch's long press and dragged: let go to drop.
            if (_carrying)
                shell.Drop();
            _carrying = false;
            return;
        }
        if (Math.Abs(delta.X) > SwipeDistance && Math.Abs(delta.X) > Math.Abs(delta.Y))
        {
            shell.SwipePage(delta.X < 0 ? 1 : -1);
            return;
        }
        if (Math.Abs(delta.X) > DragSlop || Math.Abs(delta.Y) > DragSlop)
            return;
        var source = e.Source as Visual;
        if (_pressedSlot is { } slot)
            shell.TapHomeSlot(slot.Page, slot.Slot);
        else if (Find<HandheldGameTileViewModel>(source) is { } tile)
            shell.TapFolderTile(tile.Index);
    }

    private void OnLongPress()
    {
        _longPress.Stop();
        if (_shell is { } shell && _pressedSlot is { } slot)
        {
            shell.LongPressHomeSlot(slot.Page, slot.Slot);
            _carrying = shell.IsMoving;
        }
    }

    /// <summary>The home slot (page, slot) a visual belongs to, if any.</summary>
    private static (int Page, int Slot)? SlotAt(Visual? visual)
    {
        HandheldHomeSlotViewModel? slot = null;
        for (var current = visual; current is not null; current = current.GetVisualParent())
        {
            if (current is not StyledElement element)
                continue;
            if (slot is null && element.DataContext is HandheldHomeSlotViewModel found)
                slot = found;
            else if (slot is not null && element.DataContext is HandheldHomePageViewModel page)
                return (page.Index, slot.Index);
        }
        return null;
    }

    private static T? Find<T>(Visual? visual) where T : class
    {
        for (var current = visual; current is not null; current = current.GetVisualParent())
        {
            if (current is StyledElement { DataContext: T found })
                return found;
        }
        return null;
    }

    private static bool IsInside(Visual? visual, string name) =>
        visual?.GetSelfAndVisualAncestors().OfType<Control>().Any(control => control.Name == name) == true;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
