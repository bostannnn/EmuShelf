using System;
using EmuShelf.Rendering.Stage;

namespace EmuShelf.App.Services;

/// <summary>
/// A platform's native Handheld Stage (M45 H4, option 5): a GPU surface with its own render thread that
/// draws the top screen from <see cref="StageCommand"/>s. Android provides one; elsewhere there is none and
/// the Avalonia Stage draws itself.
/// </summary>
public interface IHandheldNativeStage
{
    /// <summary>Shows the native surface over the Avalonia Stage, or hides it so the Avalonia Stage shows.</summary>
    void SetActive(bool active);

    /// <summary>Queues a change for the render thread. Safe from any thread.</summary>
    void Post(StageCommand command);

    /// <summary>
    /// Raised on the UI thread when the native Stage cannot draw (no GL ES 3 context, a shader that will not
    /// compile): the Avalonia Stage takes the screen back for the rest of the session.
    /// </summary>
    event Action? Failed;
}

/// <summary>Where the platform head registers its native Stage, if it has one.</summary>
public static class HandheldNativeStage
{
    /// <summary>The top screen's native surface.</summary>
    public static IHandheldNativeStage? Host { get; set; }

    /// <summary>The bottom screen's (the Launcher's) native surface.</summary>
    public static IHandheldNativeStage? Launcher { get; set; }
}
