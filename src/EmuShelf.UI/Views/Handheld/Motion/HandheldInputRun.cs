using System.Linq;
using Avalonia;
using Avalonia.VisualTree;
using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// Whether the press behind a change came within a run of the one before (quick taps; M47 B11): the Handheld shell the control shows
/// says so (<see cref="HandheldShellViewModel.QuickChange"/>). View wiring for the Big Type motion that follows a run in a short form.
/// </summary>
internal static class HandheldInputRun
{
    public static bool Of(Visual visual) =>
        (visual as StyledElement)?.DataContext as HandheldShellViewModel is { } own ? own.QuickChange
            : visual.GetVisualAncestors().OfType<StyledElement>().Select(element => element.DataContext).OfType<HandheldShellViewModel>()
                .FirstOrDefault()?.QuickChange == true;
}
