namespace EmuShelf.App.ViewModels;

/// <summary>
/// Which couch (gamepad) library layout is on screen. Replaces the older grid/spotlight boolean so a
/// third layout can exist. Persisted by name in
/// <see cref="EmuShelf.Core.Settings.LibraryViewSettings.GamepadLayout"/>; the tile order in the
/// system-menu picker (and the Left/Right stepping across it) follows the declared order here.
/// </summary>
public enum GamepadLibraryLayout
{
    /// <summary>The cover grid — the default couch layout.</summary>
    Grid,

    /// <summary>The spotlight: a scrolling game list beside a large fanart hero.</summary>
    Spotlight,

    /// <summary>The physical-media shelf: a horizontal row of games shown as the media they shipped on.
    /// See <c>docs/couch-physical-media-shelf.md</c>.</summary>
    Shelf,
}
