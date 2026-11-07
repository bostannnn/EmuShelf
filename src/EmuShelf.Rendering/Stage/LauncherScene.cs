namespace EmuShelf.Rendering.Stage;

/// <summary>Which part of the Launcher a sprite belongs to: what clips it, fades it and zooms it.</summary>
public enum LauncherGroup
{
    /// <summary>Drawn first and never moves (the window's gradient).</summary>
    Background,

    /// <summary>The home pages: clipped to the page area, faded out while a folder is open.</summary>
    Home,

    /// <summary>The page dots under home (faded with it).</summary>
    Dots,

    /// <summary>An open folder's page (header and games): clipped to the folder area, zoomed as one surface.</summary>
    Folder,

    /// <summary>The dock, always on top of the pages.</summary>
    Dock,
}

/// <summary>What moves a sprite sideways: the home page track, the folder's column track, or nothing.</summary>
public enum LauncherTrack
{
    None,
    Page,
    Column,

    /// <summary>The current-page pill under the dots, riding the page track at the dots' pitch.</summary>
    Dots,
}

/// <summary>
/// One pictured piece of the Launcher. <see cref="Image"/> is where it sits at rest (track offset zero),
/// including the room around it for its shadow; a liftable sprite grows 1.13× about
/// (<see cref="PivotX"/>, <see cref="PivotY"/>) while focused, with <see cref="LiftShadow"/> fading in
/// under it.
/// </summary>
public sealed record LauncherSprite(
    string Id,
    StageImage Image,
    LauncherGroup Group,
    LauncherTrack Track,
    bool Liftable,
    float PivotX,
    float PivotY,
    StageImage? LiftShadow = null);

/// <summary>Adds, replaces (null image keeps nothing) or removes a sprite.</summary>
public sealed record LauncherSpriteCommand(string Id, LauncherSprite? Sprite) : StageCommand
{
    public override string? RetainKey => "sprite:" + Id;
    public override bool Removes => Sprite is null;
}

/// <summary>
/// Where the selector frames the focus: the focused element's ring at its lifted size, at rest on its track
/// (the track's offset is added every frame, so the frame rides a sliding page), and the pre-rendered glow
/// for that size.
/// </summary>
public sealed record LauncherSelectorTarget(float X, float Y, float Width, float Height, float Radius, LauncherTrack Track, StageImage? Glow);

/// <summary>Which sprite holds the focus (it lifts) and where the selector goes; null hides the selector.</summary>
public sealed record LauncherFocusCommand(string? SpriteId, LauncherSelectorTarget? Selector, bool Instant = false) : StageCommand
{
    public override string? RetainKey => "focus";
    public override StageCommand ForReplay() => this with { Instant = true };
}

/// <summary>The tracks' targets: the home page and the folder's first visible column; snaps land without gliding.</summary>
public sealed record LauncherTracksCommand(int Page, int FirstColumn, bool SnapPage, bool SnapColumn) : StageCommand
{
    public override string? RetainKey => "tracks";
    public override StageCommand ForReplay() => this with { SnapPage = true, SnapColumn = true };
}

/// <summary>The Launcher's fixed geometry, from the Avalonia layout.</summary>
public sealed record LauncherLayoutCommand(
    float CanvasWidth,
    float CanvasHeight,
    StageRect HomeClip,
    StageRect FolderClip,
    float PageWidth,
    float ColumnPitch,
    float DotPitch,
    float DotsShift) : StageCommand
{
    public override string? RetainKey => "layout";
}

/// <summary>Whether a folder is open (home hidden, the folder page shown) once any zoom has finished.</summary>
public sealed record LauncherFolderCommand(bool Open) : StageCommand
{
    public override string? RetainKey => "folder";
}

/// <summary>
/// The Zoom through: on open the pressed home tile grows to 2.6× and fades while the folder page grows out
/// of (<see cref="OriginX"/>, <see cref="OriginY"/>); on close the reverse. The selector hides meanwhile.
/// </summary>
public sealed record LauncherZoomCommand(bool Open, string? TileId, float OriginX, float OriginY) : StageCommand;

/// <summary>The edge bump: the selector nudges toward a wall and eases back.</summary>
public sealed record LauncherBumpCommand(int DirectionX, int DirectionY) : StageCommand;

/// <summary>An axis-aligned rectangle on the canvas.</summary>
public readonly record struct StageRect(float X, float Y, float Width, float Height);
