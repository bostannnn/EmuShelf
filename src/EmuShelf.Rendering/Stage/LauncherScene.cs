using System.Numerics;
using EmuShelf.Rendering.Stage.BigType;

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

    /// <summary>A game's Title page: covers everything else while it is up (its own background is opaque).</summary>
    Title,
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
/// including the room around it for its shadow; a liftable sprite grows about (<see cref="PivotX"/>,
/// <see cref="PivotY"/>) while focused, to <see cref="Lift"/> (0: today's 1.13, <see cref="LauncherMotion.LiftScale"/>),
/// with <see cref="LiftShadow"/> fading in under it.
/// </summary>
/// <remarks>
/// Big Type (M47 B5): <see cref="Cell"/> is a grid tile's place, so its neighbours make room around it and it hops; <see cref="Text"/>
/// is where a pictured text's characters sit, so a new picture of it rolls the characters that changed.
/// </remarks>
public sealed record LauncherSprite(
    string Id,
    StageImage Image,
    LauncherGroup Group,
    LauncherTrack Track,
    bool Liftable,
    float PivotX,
    float PivotY,
    StageImage? LiftShadow = null,
    FolderArt? Folder = null,
    int Z = 0,
    float Lift = 0f,
    LauncherCell? Cell = null,
    StageText? Text = null)
{
    /// <summary>The scale this sprite lifts to while focused.</summary>
    public float LiftTo => Lift > 0f ? Lift : LauncherMotion.LiftScale;
    public bool CacheFace { get; init; }
}

/// <summary>A home folder's flap: closed, peeking (focused) or open (its folder zooming open).</summary>
public sealed record LauncherFolderStateCommand(string Id, FolderArtState State, bool Animate) : StageCommand
{
    public override string? RetainKey => "fstate:" + Id;
    public override StageCommand ForReplay() => this with { Animate = false };
}

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
public sealed record LauncherSelectorTarget(float X, float Y, float Width, float Height, float Radius, LauncherTrack Track, StageImage? Glow,
    LauncherRingBands? Bands = null);

/// <summary>
/// The selector's bands, from its outer edge in: the first band runs from the edge to <see cref="Inset1"/>, the second to
/// <see cref="Inset2"/>, the third to <see cref="Inset3"/>, each in its colour (premultiplied RGBA). A band whose insets are
/// equal is not drawn. Today's look is <see cref="Classic"/> (a white keyline, the violet frame, a white keyline); the Big
/// Type look draws one white band (M47 B3).
/// </summary>
public sealed record LauncherRingBands(float Inset1, float Inset2, float Inset3, Vector4 Band1, Vector4 Band2, Vector4 Band3)
{
    /// <summary>4 px white, 14 px violet (91, 88, 217), 4 px white: the nested Borders today's Avalonia selector draws.</summary>
    public static LauncherRingBands Classic { get; } = new(4f, 18f, 22f, Vector4.One,
        new Vector4(91f / 255f, 88f / 255f, 217f / 255f, 1f), Vector4.One);

    /// <summary>One band of <paramref name="stroke"/> pixels in an opaque colour (premultiplied: alpha 1).</summary>
    public static LauncherRingBands One(float stroke, float r, float g, float b) =>
        new(stroke, stroke, stroke, new Vector4(r, g, b, 1f), Vector4.Zero, Vector4.Zero);
}

/// <summary>
/// The Launcher's ground (M47 B3, the Big Type look): the screen is cleared to this flat colour. With <paramref name="Flood"/> (M47 B6,
/// move 3) the new colour grows as a circle from the focused tile over the old (<see cref="BigType.BigTypeMotion.FloodRadiusAt"/>);
/// without it, or held still, it is there at once. Never sent in today's look, whose ground is a pictured gradient.
/// </summary>
/// <remarks><paramref name="FromMiddle"/> (M47 B8): the wave grows from the middle of the screen, not the focus (the shelf coming back
/// after a game, the mock's <c>endPlay</c>).</remarks>
public sealed record LauncherGroundCommand(float R, float G, float B, bool Flood = false, bool FromMiddle = false) : StageCommand
{
    public override string? RetainKey => "ground";
    public override StageCommand ForReplay() => this with { Flood = false, FromMiddle = false };
}

/// <summary>
/// A game launched under the Big Type look (M47 B8): with <paramref name="Playing"/> the grid goes as the Stage's takeover starts
/// (<see cref="BigTypeLaunchMotion.GridOpacity"/>) and the ring hides; without it the shelf comes back and the tiles pop in around the
/// focused one. <paramref name="Animate"/> false (a replay, holding still) just sets where it is.
/// </summary>
public sealed record LauncherPlayCommand(bool Playing, bool Animate = true) : StageCommand
{
    public override string? RetainKey => "play";
    public override StageCommand ForReplay() => this with { Animate = false };
}

/// <summary>
/// A pictured text's letters under the Big Type look (M47 B8: a folder's name, the mock's <c>lettersIn(fname, .9)</c>): while they land
/// (tokens.js <c>HeadingIn</c>, from <paramref name="FromEm"/>) they are drawn one quad each in place of the sprite
/// <paramref name="SpriteId"/>, which shows again once they have. During a folder's opening they wait for it to show. Null takes them away.
/// </summary>
public sealed record LauncherLettersCommand(string SpriteId, BigTypeLetters? Letters, float FromEm, bool Animate = true) : StageCommand
{
    public override string? RetainKey => "letters:" + SpriteId;
    public override StageCommand ForReplay() => this with { Animate = false };
}

/// <summary>Which sprite holds the focus (it lifts) and where the selector goes; null hides the selector.</summary>
/// <remarks><paramref name="Pop"/> (Big Type, M47 B5): the focus moved onto another home page, so the ring lands there and pops in
/// rather than stretching across the screen.</remarks>
public sealed record LauncherFocusCommand(string? SpriteId, LauncherSelectorTarget? Selector, bool Instant = false, bool Pop = false) : StageCommand
{
    public override string? RetainKey => "focus";
    public override StageCommand ForReplay() => this with { Instant = true, Pop = false };
}

/// <summary>
/// How the Launcher moves (M47 B5): <paramref name="BigType"/> turns on the Big Type look's motion (the ring's stretch and breath,
/// the landing lift, neighbours making room, the bob, the idle hop, rolling text); <paramref name="HoldStill"/> is the device asking
/// for stillness (battery saver, reduced motion: M45 H7c), under which none of that animates. Today's look keeps its motion as it is.
/// </summary>
public sealed record LauncherMotionCommand(bool BigType, bool HoldStill) : StageCommand
{
    public override string? RetainKey => "motion";
}

/// <summary>A press or a touch (M47 B5): the resting motion wakes for another minute and the idle hop waits again.</summary>
public sealed record LauncherInputCommand : StageCommand;

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

/// <summary>The launching panel's indeterminate bar, at <paramref name="Track"/>; null removes it.</summary>
public sealed record LauncherProgressCommand(StageRect? Track) : StageCommand
{
    public override string? RetainKey => "progress";
}

/// <summary>The launch: the sprite (the Title page's cover) presses in, pops and lifts off toward the top screen.</summary>
/// <remarks><paramref name="Downward"/>: the Launcher is the top screen (the screens swapped, M45 H5), so the cover
/// leaves through its bottom edge toward the Stage below.</remarks>
/// <remarks><paramref name="BigType"/> (M47 B8): the tile flies up and out through the hinge on the mock's curve
/// (<see cref="BigTypeLaunchMotion.Flight"/>) and stays gone until the shelf comes back.</remarks>
public sealed record LauncherLaunchCommand(string SpriteId, bool Downward = false, bool BigType = false) : StageCommand;

/// <summary>The edge bump: the selector nudges toward a wall and eases back.</summary>
public sealed record LauncherBumpCommand(int DirectionX, int DirectionY) : StageCommand;

/// <summary>An axis-aligned rectangle on the canvas.</summary>
public readonly record struct StageRect(float X, float Y, float Width, float Height);
