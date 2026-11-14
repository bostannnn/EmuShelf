using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// Where the Big Type Stage's next name arrives from (M47 B6, the mock's <c>setWord</c> directions): the way the last press moved,
/// into a folder (from the hinge) or out of one, or, for anything else (a first show, the idle drift, a touch), rising in.
/// </summary>
internal static class HandheldStageEntrance
{
    public static BigTypeEntrance Of(HandheldShellViewModel shell)
    {
        if (shell.Stage.IsDrift)
            return BigTypeEntrance.Rise;
        if (shell.LastFolderOpened is { } opened)
            return opened ? BigTypeEntrance.Open : BigTypeEntrance.Back;
        return shell.LastMoveDirection switch
        {
            HandheldDirection.Left => BigTypeEntrance.Left,
            HandheldDirection.Right => BigTypeEntrance.Right,
            HandheldDirection.Up => BigTypeEntrance.Up,
            HandheldDirection.Down => BigTypeEntrance.Down,
            _ => BigTypeEntrance.Rise,
        };
    }
}
