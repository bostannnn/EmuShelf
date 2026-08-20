using Avalonia.Controls;
using Avalonia.LogicalTree;
using EmuShelf.App.Views;

namespace EmuShelf.App.Tests;

internal static class GamepadShellTestExtensions
{
    /// <summary>
    /// Look up a named control the way these tests always did — as if the whole UI shared one
    /// namescope. Since A1 the gamepad tree lives in the extracted <see cref="GamepadShellView"/>,
    /// which is a separate namescope, so <c>Window.FindControl</c> alone can no longer see the couch
    /// controls. Search the window's own namescope first (desktop chrome) and fall back to the gamepad
    /// shell's, which reproduces the pre-extraction lookup exactly. The shell and its named controls
    /// are registered during the window's <c>InitializeComponent</c>, so this works before Show too.
    /// </summary>
    public static T? FindNamed<T>(this Control root, string name) where T : Control =>
        root.FindControl<T>(name)
        ?? root.GetLogicalDescendants().OfType<GamepadShellView>().FirstOrDefault()?.FindControl<T>(name);
}
