using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;

namespace EmuShelf.App.Tests;

/// <summary>
/// The Android head builds a FRESH <see cref="GamepadShellView"/> for every activity — the supported
/// <c>IActivityApplicationLifetime.MainViewFactory</c> hosting the single-view head now uses (see
/// <c>SingleViewShell</c>). So a shell that leaves the tree for good has to drop its
/// <see cref="MainViewModel"/> subscription on <c>DetachedFromVisualTree</c>; otherwise the one
/// long-lived view model keeps firing <c>PropertyChanged</c> into — and retaining — every dead view
/// across recreations, a leak the old single-instance reuse never had.
/// </summary>
/// <remarks>
/// The cleanup cannot lean on <c>DataContextChanged</c>: the DataContext lives on a PARENT (MainView on
/// Android, MainWindow on desktop) that keeps it when the subtree is torn out, so the shell's inherited
/// DataContext does not change on detach and that event never re-fires. These tests reproduce that exact
/// shape — a host that retains its DataContext while its child subtree is removed — which is why
/// <c>DetachedFromVisualTree</c> is the only hook that can unwire the subscription.
/// </remarks>
public sealed class GamepadShellViewLifetimeTests
{
    [AvaloniaFact]
    public void AttachingTheShell_WiresTheViewModelSubscription()
    {
        var viewModel = new MainViewModel { IsGamepadMode = true };
        var shell = new GamepadShellView();
        var window = new Window { Content = shell, DataContext = viewModel };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Same(viewModel, shell.DataContext);
        Assert.Same(viewModel, ObservedViewModel(shell));

        window.Close();
    }

    [AvaloniaFact]
    public void DetachingTheShell_UnwiresTheSubscription_ThoughTheParentKeepsDataContext()
    {
        var viewModel = new MainViewModel { IsGamepadMode = true };
        var shell = new GamepadShellView();
        // The DataContext lives on the host and STAYS there when the subtree detaches — the real
        // MainView/MainWindow shape — so the shell keeps inheriting it and DataContextChanged does not
        // fire on detach. Only DetachedFromVisualTree can unwire the subscription here.
        var host = new ContentControl { Content = shell, DataContext = viewModel };
        var window = new Window { Content = host };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Same(viewModel, ObservedViewModel(shell));

        // Tear the subtree out of the tree for good — the activity-teardown path on Android.
        window.Content = null;
        Dispatcher.UIThread.RunJobs();

        // The shell STILL inherits the same DataContext (the host kept it), proving no DataContextChanged
        // fired — yet the subscription is gone, so the unwiring came from DetachedFromVisualTree.
        Assert.Same(viewModel, shell.DataContext);
        Assert.Null(ObservedViewModel(shell));

        window.Close();
    }

    // The shell's live view-model subscription is private (an implementation detail of the attach/detach
    // wiring), so read it reflectively rather than widening the control's surface just for the test. It
    // mirrors the subscription one-to-one — set alongside `+= OnGamepadViewModelPropertyChanged`, cleared
    // alongside the matching `-=` — so null means unsubscribed. A rename is a deliberate refactor and this
    // fails loudly, pointing straight at the field.
    private static MainViewModel? ObservedViewModel(GamepadShellView shell) =>
        (MainViewModel?)typeof(GamepadShellView)
            .GetField("_gamepadViewModel", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(shell);
}
