using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmuShelf.App.Controls;
using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Views;

/// <summary>
/// Handheld mode's companion (M45 H6b), a pure view onto <see cref="HandheldCompanionViewModel"/>. The code here is
/// view wiring only: taps and holds handed to the view model, the badge grid and All apps kept scrolled to the selection,
/// badges loaded as their tiles appear, and the photo viewer's gestures.
/// </summary>
public partial class HandheldCompanionView : UserControl
{
    public HandheldCompanionView()
        : this(wide: false)
    {
    }

    /// <summary>A companion drawn for the 16:9 screen's 1920-wide canvas when <paramref name="wide"/>, else the 1240-wide one.</summary>
    public HandheldCompanionView(bool wide)
    {
        IsWide = wide;
        InitializeComponent();
        var width = wide ? HandheldShellViewModel.WideCanvasWidth : HandheldShellViewModel.NarrowCanvasWidth;
        this.FindControl<Grid>("Canvas")!.Width = width;
        this.FindControl<Panel>("RestCanvas")!.Width = width;
        // Every touch keeps the companion lit; the tunnel pass with handled events too, so a tap a button takes still counts.
        AddHandler(PointerPressedEvent, OnAnyPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        DataContextChanged += (_, _) => Bind(DataContext as HandheldCompanionViewModel);
    }

    /// <summary>Drawn for the 16:9 screen.</summary>
    public bool IsWide { get; }

    private HandheldCompanionViewModel? _bound;

    private void Bind(HandheldCompanionViewModel? companion)
    {
        if (_bound is { } old)
        {
            old.PropertyChanged -= OnCompanionChanged;
            old.Model.SelectionScrolledTo -= OnSelectionScrolledTo;
        }
        _bound = companion;
        if (companion is null)
            return;
        companion.PropertyChanged += OnCompanionChanged;
        companion.Model.SelectionScrolledTo += OnSelectionScrolledTo;
    }

    private HandheldCompanionViewModel? Companion => DataContext as HandheldCompanionViewModel;

    private void OnCompanionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HandheldCompanionViewModel.DrawerIndex))
            Dispatcher.UIThread.Post(BringDrawerSelectionIntoView, DispatcherPriority.Loaded);
    }

    private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e) => Companion?.NoteTouch();

    // ---- The body ----

    private void OnAchievementsTabTapped(object? sender, TappedEventArgs e) => Companion?.SelectTab(HandheldCompanionTab.Achievements);

    private void OnPhotosTabTapped(object? sender, TappedEventArgs e) => Companion?.SelectTab(HandheldCompanionTab.Photos);

    private Point? _panelPress;
    private bool _panelSwiped;

    private void OnPanelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _panelPress = e.GetPosition(this);
        _panelSwiped = false;
    }

    // A swipe across the Photos panel pages through the photos; a tap (no swipe) opens the panel.
    private void OnPanelPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_panelPress is not { } start || Companion is not { } companion)
            return;
        _panelPress = null;
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) >= 24 && Math.Abs(delta.X) > Math.Abs(delta.Y) * 1.5)
        {
            _panelSwiped = true;
            companion.SwipePanel(delta.X < 0 ? 1 : -1);
        }
    }

    private void OnPanelTapped(object? sender, TappedEventArgs e)
    {
        if (_panelSwiped)
        {
            _panelSwiped = false;
            return;
        }
        Companion?.OpenPanel();
    }

    private DateTimeOffset _lastHoldAt = DateTimeOffset.MinValue;

    private void OnDockItemTapped(object? sender, TappedEventArgs e)
    {
        // Some input stacks still send a tap as a long press ends; that one must not also open the slot.
        if (DateTimeOffset.UtcNow - _lastHoldAt < TimeSpan.FromMilliseconds(500))
            return;
        if (sender is Control { DataContext: HandheldCompanionDockItemViewModel item })
            Companion?.TapDockItem(item);
    }

    private void OnDockItemHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState != HoldingState.Started || sender is not Control { DataContext: HandheldCompanionDockItemViewModel item })
            return;
        _lastHoldAt = DateTimeOffset.UtcNow;
        Companion?.HoldDockItem(item);
        e.Handled = true;
    }

    private void OnRestTapped(object? sender, TappedEventArgs e)
    {
        Companion?.Wake();
        e.Handled = true;
    }

    // ---- Every badge ----

    private void OnAllFilterTapped(object? sender, TappedEventArgs e) => Companion?.SelectFilter(AchievementDisplayFilter.All);

    private void OnLockedFilterTapped(object? sender, TappedEventArgs e) => Companion?.SelectFilter(AchievementDisplayFilter.Locked);

    private void OnUnlockedFilterTapped(object? sender, TappedEventArgs e) => Companion?.SelectFilter(AchievementDisplayFilter.Unlocked);

    private void OnRefreshTapped(object? sender, TappedEventArgs e)
    {
        if (Companion?.Model is { CanRefresh: true, IsAchievementsRefreshing: false } model)
            model.RefreshCommand.Execute(null);
    }

    private void OnCloseTapped(object? sender, TappedEventArgs e) => Companion?.CloseLayer();

    private void OnBadgeTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: AchievementRowViewModel row })
            Companion?.Model.SelectAchievement(row);
    }

    // A badge loads once its tile is on screen (or recycled onto a new row), so the grid never loads more than it shows.
    private void OnBadgeAttached(object? sender, VisualTreeAttachmentEventArgs e) => RequestBadge(sender);

    private void OnBadgeDataContextChanged(object? sender, EventArgs e) => RequestBadge(sender);

    private static void RequestBadge(object? sender)
    {
        if (sender is Control { DataContext: AchievementRowViewModel row } && row.Badge is null)
            _ = row.LoadBadgeAsync(row.BadgeName);
    }

    // The D-pad (or a new set, or a filter) moved the selection: bring its row into view.
    private void OnSelectionScrolledTo(int badgeIndex)
    {
        if (this.FindControl<ListBox>("BadgeList") is not { } list || Companion is not { } companion)
            return;
        var row = badgeIndex / Math.Max(1, companion.Model.AchievementColumnCount);
        if (row == 0 && list.Scroll is ScrollViewer scroller)
            scroller.Offset = scroller.Offset.WithY(0);
        else if (row >= 0 && row < list.ItemCount)
            list.ScrollIntoView(row);
    }

    // ---- All apps ----

    private void OnDrawerAppTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: HandheldDrawerAppViewModel app })
            Companion?.TapDrawerApp(app);
    }

    private void OnClearSlotTapped(object? sender, TappedEventArgs e)
    {
        if (Companion?.Model is { CanClearSlot: true } model)
            model.ClearSlotCommand.Execute(null);
    }

    private void BringDrawerSelectionIntoView()
    {
        if (Companion is not { } companion || this.FindControl<ItemsControl>("DrawerApps") is not { } apps ||
            companion.DrawerIndex >= companion.DrawerApps.Count)
            return;
        apps.ContainerFromIndex(companion.DrawerIndex)?.BringIntoView();
    }

    // ---- The photo viewer ----

    private Point? _viewerPress;

    private void OnViewerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _viewerPress = null;
        // The native video view owns its own touches; buttons take theirs.
        if (Companion?.Model is not { } model || model.IsVideoPlaying)
            return;
        if (e.Source is Visual visual &&
            visual.GetSelfAndVisualAncestors().OfType<Control>().Any(control => control.Classes.Contains("viewer-button")))
            return;
        _viewerPress = e.GetPosition(this);
    }

    private void OnViewerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_viewerPress is not { } start || Companion?.Model is not { } model)
            return;
        _viewerPress = null;
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < 8 && Math.Abs(delta.Y) < 8)
            model.TapMedia();
        else
            model.SwipeMedia(delta.X, delta.Y);
    }

    private void OnPlayTapped(object? sender, TappedEventArgs e)
    {
        if (Companion?.Model is { ShowMediaPlay: true } model)
            model.PlayMediaCommand.Execute(null);
        e.Handled = true;
    }

    private void OnVideoAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not CompanionVideoHost host)
            return;
        host.PlaybackFailed -= OnVideoFailed;
        host.PlaybackFailed += OnVideoFailed;
        host.Swiped -= OnVideoSwiped;
        host.Swiped += OnVideoSwiped;
    }

    private void OnVideoFailed(string message)
    {
        if (Companion?.Model is not { } model)
            return;
        model.StopVideoCommand.Execute(null);
        model.MediaStatus = message;
    }

    private void OnVideoSwiped(double x, double y) => Companion?.Model.SwipeMedia(x, y);

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
