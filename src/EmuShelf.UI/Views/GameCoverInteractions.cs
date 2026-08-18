using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Views;

/// <summary>
/// Cover-tile view wiring shared by every surface that hosts game tiles — the desktop grid/list in
/// <c>MainWindow</c> and the gamepad shelf/grid in <c>GamepadShellView</c>. The behaviour is identical
/// (virtualization owns realization; the game command owns the async cover load), so it lives here
/// once and each surface's XAML event handlers delegate to it, rather than the logic being duplicated
/// on two code-behinds. Pure functions over the <c>sender</c> control — no window or surface state.
/// </summary>
internal static class GameCoverInteractions
{
    /// <summary>
    /// Double-tap (or the desktop grid/list equivalent) launches the tile's game. View wiring only:
    /// the game view model owns the launch command and whether it can run.
    /// </summary>
    public static void HandleDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: GameViewModel game }
            && game.LaunchCommand.CanExecute(game))
        {
            game.LaunchCommand.Execute(game);
            e.Handled = true;
        }
    }

    /// <summary>
    /// A cover control was realized: virtualization decides when; the game command owns the
    /// asynchronous load, caching, and stale-result handling.
    /// </summary>
    public static void CoverAttached(object? sender) => RequestCover(sender);

    /// <summary>
    /// A recycled tile stays attached while the virtualizing list hands it a different game (desktop
    /// ItemsRepeater, or a gamepad row scrolling in). Cut the crossfade and load the new game's cover.
    /// </summary>
    public static void CoverDataContextChanged(object? sender)
    {
        SnapCoverLayers(sender);
        RequestCover(sender);
    }

    /// <summary>
    /// Cuts the cover crossfade for one recycling.
    ///
    /// The fade exists to soften a cover arriving from disk. Recycling is not that: the control is
    /// being handed a different game, and animating it dissolves the previous game's artwork over
    /// the incoming tile. During fast scrolling or a run of LB/RB that reads as the wrong art
    /// briefly appearing. Dropping the transitions here lets the class bindings that follow this
    /// event apply as a straight cut; they are restored once that has happened, so the next real
    /// cover load still fades.
    /// </summary>
    public static void SnapCoverLayers(object? sender)
    {
        if (sender is not Control root)
            return;

        foreach (var layer in root.GetSelfAndVisualDescendants().OfType<Control>())
        {
            if (!layer.Classes.Contains("cover-image") && !layer.Classes.Contains("cover-placeholder"))
                continue;

            var transitions = layer.Transitions;
            if (transitions is null)
                continue;

            layer.Transitions = null;
            // Loaded runs after the bindings this event precedes, so the opacity change lands
            // while the transitions are detached and is applied instantly.
            Dispatcher.UIThread.Post(
                () => layer.Transitions ??= transitions,
                DispatcherPriority.Loaded);
        }
    }

    private static void RequestCover(object? sender)
    {
        if (sender is Control { DataContext: GameViewModel game } control &&
            control.IsAttachedToVisualTree() &&
            game.LoadCoverCommand.CanExecute(game))
        {
            game.LoadCoverCommand.Execute(game);
        }
    }
}
