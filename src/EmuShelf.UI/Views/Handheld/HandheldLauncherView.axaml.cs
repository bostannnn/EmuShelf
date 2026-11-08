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
    private HandheldGameTileViewModel? _pressedTile;
    // Move mode started by this touch's long press: letting go drops. A tap during a pad-driven move does not.
    private bool _carrying;

    /// <summary>
    /// Raised once the native Launcher has everything this view shows: after it was shown again, or moved to the
    /// other screen's shape (M45 H5), so the platform can bring that screen into view.
    /// </summary>
    public event System.Action? NativeCaughtUp;

    public HandheldLauncherView()
        : this(HandheldNativeStage.Launcher, pinnedWide: null)
    {
    }

    /// <summary>
    /// A Launcher drawn by <paramref name="native"/> (if any). <paramref name="pinnedWide"/> keeps it on one screen's
    /// shape (the 16:9 screen's when true): the Android head keeps one Launcher per display, so a swap only changes
    /// which one is in front (M45 H5). Null follows the swap (one Launcher that moves).
    /// </summary>
    public HandheldLauncherView(IHandheldNativeStage? native, bool? pinnedWide)
    {
        PinnedWide = pinnedWide;
        InitializeComponent();
        _homeLayer = this.FindControl<Control>("HomeLayer")!;
        _canvas = this.FindControl<Control>("Canvas")!;
        _selectorLayer = this.FindControl<Control>("SelectorLayer")!;
        _settingsCanvas = this.FindControl<Control>("SettingsCanvas")!;
        _settingsRows = this.FindControl<ItemsControl>("SettingsRows")!;
        _settingsSections = this.FindControl<ItemsControl>("SettingsSections")!;
        _settingsThemes = this.FindControl<ItemsControl>("SettingsThemes")!;
        _settingsChoices = this.FindControl<ItemsControl>("SettingsChoices")!;
        _panelCanvas = this.FindControl<Control>("PanelCanvas")!;
        _sheetRows = this.FindControl<ItemsControl>("SheetRows")!;
        // View wiring only: the motion follows whichever shell the view shows.
        _motion = new HandheldLauncherMotion(this);
        // On a platform with a native Launcher this view stays laid out but invisible, and lends it pictures.
        _feed = native is { } host ? new HandheldNativeLauncherFeed(this, _motion, host) : null;
        if (_feed is not null)
            _feed.CaughtUp += () => NativeCaughtUp?.Invoke();
        _longPress = new DispatcherTimer { Interval = LongPress };
        _longPress.Tick += (_, _) => OnLongPress();
        DataContextChanged += (_, _) =>
        {
            if (_shell is { } old)
                old.PropertyChanged -= OnShellChanged;
            _shell = DataContext as HandheldShellViewModel;
            if (_shell is { } shell)
                shell.PropertyChanged += OnShellChanged;
            WatchSettings(_shell?.Settings);
            WatchSheet(_shell?.Sheet);
            _motion.Attach(_shell);
            _feed?.Attach(_shell);
            ApplyScreen();
        };
        AddHandler(PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>The screen shape this Launcher is kept on, or null when it follows the swap.</summary>
    public bool? PinnedWide { get; }

    /// <summary>Home as this Launcher draws it: its pinned screen's shape, or the shape in use.</summary>
    public HandheldHomeScreenViewModel? Screen => _shell is not { } shell ? null : PinnedWide is { } wide ? shell.HomeFor(wide) : shell.Home;

    private readonly Control _homeLayer;
    private readonly Control _canvas;
    private readonly Control _selectorLayer;
    private readonly Control _settingsCanvas;
    private readonly ItemsControl _settingsRows;
    private readonly ItemsControl _settingsSections;
    private readonly ItemsControl _settingsThemes;
    private readonly ItemsControl _settingsChoices;
    private readonly Control _panelCanvas;
    private readonly ItemsControl _sheetRows;
    private HandheldSheetViewModel? _sheet;
    private HandheldHomeScreenViewModel? _screen;
    private HandheldSettingsViewModel? _settings;
    private (Point At, GamepadSettingsRowViewModel Row)? _settingsPress;

    private void OnShellChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HandheldShellViewModel.IsSwapped) && PinnedWide is null)
            ApplyScreen();
        else if (e.PropertyName == nameof(HandheldShellViewModel.Settings))
            WatchSettings(_shell?.Settings);
        else if (e.PropertyName == nameof(HandheldShellViewModel.Sheet))
            WatchSheet(_shell?.Sheet);
    }

    // ---- The actions sheet (M45 H6c): a long one scrolls, keeping its focused row in view ----

    private void WatchSheet(HandheldSheetViewModel? sheet)
    {
        if (ReferenceEquals(sheet, _sheet))
            return;
        if (_sheet is { } old)
            old.PropertyChanged -= OnSheetChanged;
        _sheet = sheet;
        if (sheet is null)
            return;
        sheet.PropertyChanged += OnSheetChanged;
        Dispatcher.UIThread.Post(RevealSheetFocus, DispatcherPriority.Loaded);
    }

    private void OnSheetChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HandheldSheetViewModel.Index))
            Dispatcher.UIThread.Post(RevealSheetFocus, DispatcherPriority.Loaded);
    }

    private void RevealSheetFocus()
    {
        if (_sheet is { } sheet && ReferenceEquals(sheet, _shell?.Sheet))
            Reveal(_sheetRows, sheet.Index);
    }

    // ---- Settings (M45 H6a): keep what has the focus in view ----

    private void WatchSettings(HandheldSettingsViewModel? settings)
    {
        if (ReferenceEquals(settings, _settings))
            return;
        if (_settings is { } old)
            old.Changed -= OnSettingsChanged;
        _settings = settings;
        _settingsPress = null;
        if (settings is null)
            return;
        settings.Changed += OnSettingsChanged;
        OnSettingsChanged();
    }

    private void OnSettingsChanged() =>
        // After layout, so a rebuilt list has its containers.
        Dispatcher.UIThread.Post(RevealSettingsFocus, DispatcherPriority.Loaded);

    private void RevealSettingsFocus()
    {
        if (_settings is not { } settings || !IsVisible)
            return;
        var projection = settings.Projection;
        if (projection.IsChoicePickerOpen)
            Reveal(_settingsChoices, projection.FocusedChoiceIndex);
        else if (projection.IsThemesSection)
            Reveal(_settingsThemes, projection.FocusedThemeIndex);
        else
            Reveal(_settingsRows, projection.FocusedRowIndex);
        Reveal(_settingsSections, settings.Sections.ToList().FindIndex(section => section.IsSelected));
    }

    private static void Reveal(ItemsControl items, int index)
    {
        if (index >= 0 && items.ContainerFromIndex(index) is Control container)
            container.BringIntoView();
    }

    // View wiring: the home layer, the canvas and the selector layer take this Launcher's screen.
    private void ApplyScreen()
    {
        var screen = Screen;
        if (ReferenceEquals(screen, _screen))
            return;
        _screen = screen;
        _homeLayer.DataContext = screen;
        var width = screen?.CanvasWidth ?? HandheldShellViewModel.NarrowCanvasWidth;
        _canvas.Width = width;
        _selectorLayer.Width = width;
        _settingsCanvas.Width = width;
        _panelCanvas.Width = width;
        _motion.SetScreen(screen);
        _feed?.SetScreen(screen);
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
        if (shell.Settings is { } settings)
        {
            OnSettingsPressed(settings, source, e);
            return;
        }
        if (shell.Panel is { } panel)
        {
            OnPanelPressed(shell, panel, source, e);
            return;
        }
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
        // The menu and the Title page's sheets take taps on their rows; a tap on their scrim closes them.
        if (shell.IsMenuOpen)
        {
            if (Find<HandheldMenuItemViewModel>(source) is { } menuItem)
                shell.TapMenuItem(menuItem);
            else if (!IsInside(source, "MenuCard"))
                shell.TapOutside();
            e.Handled = true;
            return;
        }
        if (shell.TitlePage is { } title)
        {
            if (title.IsOptionList)
            {
                if (Find<HandheldTitleOptionViewModel>(source) is { } option)
                    shell.TapTitleOption(option);
                else if (!IsInside(source, "OptionCard"))
                    shell.TapOutside();
            }
            else if (title.IsScreenChooser)
            {
                if (IsInside(source, "ScreenTopShot"))
                    shell.TapScreenChoice(HandheldTitlePageViewModel.ScreenTopIndex);
                else if (IsInside(source, "ScreenBottomShot"))
                    shell.TapScreenChoice(HandheldTitlePageViewModel.ScreenBottomIndex);
                else if (IsInside(source, "RememberShot"))
                    shell.TapScreenChoice(HandheldTitlePageViewModel.ScreenRememberIndex);
                else if (!IsInside(source, "ScreenCard"))
                    shell.TapOutside();
            }
            else if (Find<HandheldTitleButtonViewModel>(source) is { } button)
            {
                shell.TapTitleButton(button);
            }
            e.Handled = true;
            return;
        }
        // The folder header's chips and the dock act on the first tap.
        if (IsInside(source, "BackShot") || IsInside(source, "SortShot"))
        {
            shell.TapHeader(IsInside(source, "SortShot") ? HandheldHeaderItem.Sort : HandheldHeaderItem.Back);
            e.Handled = true;
            return;
        }
        if (Find<HandheldDockItemViewModel>(source) is { } dockItem)
        {
            _dockPress = dockItem;
            _dockLongPressed = false;
            _pressAt = e.GetPosition(this);
            _longPress.Start();
            e.Handled = true;
            return;
        }
        // The edge hints turn the page.
        if (IsInside(source, "EdgeLeft") || IsInside(source, "EdgeRight"))
        {
            shell.SwipePage(IsInside(source, "EdgeRight") ? 1 : -1);
            e.Handled = true;
            return;
        }
        _pressAt = e.GetPosition(this);
        _pressedSlot = SlotAt(source);
        _pressedTile = _pressedSlot is null && shell.IsFolderOpen ? Find<HandheldGameTileViewModel>(source) : null;
        if ((_pressedSlot is not null || _pressedTile is not null) && !shell.IsMoving)
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
                shell.CarryTo((Screen ?? shell.Home).Grid.IndexOf(new HandheldHomePosition(over.Page, over.Slot)));
            return;
        }
        // Moving off the tile before the long press fires is a swipe, not a press.
        if (Math.Abs(delta.X) > DragSlop || Math.Abs(delta.Y) > DragSlop)
            _longPress.Stop();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _longPress.Stop();
        if (_settingsPress is { } press)
        {
            // A row is pressed on release, and only if the finger stayed put: a drag scrolls the list instead.
            _settingsPress = null;
            var moved = e.GetPosition(this) - press.At;
            if (Math.Abs(moved.X) <= DragSlop && Math.Abs(moved.Y) <= DragSlop)
                _shell?.Settings?.TapRow(press.Row);
            return;
        }
        if (_panelPress is { } panelPress)
        {
            // The same for a panel's rows (a long list of achievements scrolls under the finger).
            _panelPress = null;
            var moved = e.GetPosition(this) - panelPress.At;
            if (Math.Abs(moved.X) <= DragSlop && Math.Abs(moved.Y) <= DragSlop)
                _shell?.TapPanelRow(panelPress.Row);
            return;
        }
        if (_dockPress is { } dockPress)
        {
            // A dock button acts on release, so a hold can open its sheet instead (the companion's hold-to-manage).
            _dockPress = null;
            var dockMoved = _pressAt is { } dockStart ? e.GetPosition(this) - dockStart : default;
            // A finger that slid away is not a tap.
            if (_shell is { } tapped && !_dockLongPressed && Math.Abs(dockMoved.X) <= DragSlop && Math.Abs(dockMoved.Y) <= DragSlop)
                tapped.TapDockItem(dockPress);
            _dockLongPressed = false;
            _pressAt = null;
            return;
        }
        if (_shell is not { } shell || _pressAt is not { } start)
            return;
        _pressAt = null;
        var delta = e.GetPosition(this) - start;
        if (shell.IsMoving)
        {
            // Lifted by this touch's long press: let go to drop where it was carried, or, if it never left its
            // slot, to put it back and open its actions sheet instead.
            if (_carrying)
            {
                if (shell.HasCarriedAway)
                    shell.Drop();
                else
                    shell.ReleaseLongPressInPlace();
            }
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
        if (_shell is not { } shell)
            return;
        if (_dockPress is { } dockItem)
        {
            _dockLongPressed = true;
            shell.LongPressDockItem(dockItem);
            return;
        }
        if (_pressedSlot is { } slot)
        {
            shell.LongPressHomeSlot(slot.Page, slot.Slot);
            _carrying = shell.IsMoving;
        }
        else if (_pressedTile is { } tile)
        {
            // The release that follows must not also count as a tap on the tile.
            _pressAt = null;
            shell.LongPressFolderTile(tile.Index);
        }
    }

    // A panel takes its own taps: Back, a filter chip, a key of its keyboard, or a row (on release, so the list can scroll).
    private void OnPanelPressed(HandheldShellViewModel shell, HandheldPanelViewModel panel, Visual? source, PointerPressedEventArgs e)
    {
        _panelPress = null;
        if (IsInside(source, "PanelBack"))
        {
            shell.TapPanelBack();
            e.Handled = true;
            return;
        }
        if (panel.IsTyping)
        {
            if (Find<HandheldKeyViewModel>(source) is { } key)
            {
                shell.TapPanelKey(key);
                e.Handled = true;
            }
            // A press on the field itself is left to the field (a hardware keyboard types there).
            return;
        }
        if (Find<HandheldPanelChipViewModel>(source) is { } chip)
        {
            shell.TapPanelChip(chip);
            e.Handled = true;
        }
        else if (Find<HandheldPanelRowViewModel>(source) is { } row)
        {
            _panelPress = (e.GetPosition(this), row);
        }
    }

    private (Point At, HandheldPanelRowViewModel Row)? _panelPress;
    private HandheldDockItemViewModel? _dockPress;
    private bool _dockLongPressed;

    // Settings takes its own taps: a section shows it, a row does what A does (on release, so the list can scroll), a
    // choice or an answer is picked, and a tap outside an open chooser closes it.
    private void OnSettingsPressed(HandheldSettingsViewModel settings, Visual? source, PointerPressedEventArgs e)
    {
        _settingsPress = null;
        if (settings.IsTyping)
        {
            if (Find<HandheldKeyViewModel>(source) is { } key)
            {
                settings.PressKey(key);
                e.Handled = true;
            }
            // A press on the field itself is left to the field (a hardware keyboard types there).
            return;
        }
        if (settings.IsChoicePickerOpen)
        {
            if (Find<GamepadChoiceOptionViewModel>(source) is { } option)
                settings.TapChoice(option);
            else if (!IsInside(source, "SettingsChoiceCard"))
                settings.TapOutside();
            e.Handled = true;
            return;
        }
        if (settings.IsConfirmationOpen)
        {
            if (IsInside(source, "SettingsConfirmGo"))
                settings.TapConfirmation(confirm: true);
            else if (IsInside(source, "SettingsConfirmCancel") || !IsInside(source, "SettingsConfirmCard"))
                settings.TapConfirmation(confirm: false);
            e.Handled = true;
            return;
        }
        if (IsInside(source, "SettingsBack"))
        {
            settings.Back();
            e.Handled = true;
        }
        else if (Find<HandheldSettingsSectionViewModel>(source) is { } section)
        {
            settings.TapSection(section);
            e.Handled = true;
        }
        else if (Find<ThemeChoiceViewModel>(source) is { } theme)
        {
            settings.TapTheme(theme);
            e.Handled = true;
        }
        else if (Find<GamepadSettingsRowViewModel>(source) is { } row)
        {
            _settingsPress = (e.GetPosition(this), row);
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
