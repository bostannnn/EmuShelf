namespace EmuShelf.Rendering.Stage.BigType;

/// <summary>
/// One letter of the Stage's name (M47 B6): where its cell sits on the Stage canvas at rest, and where that cell is in the name's
/// atlas (atlas pixels; the cell is the same size in both). <paramref name="Order"/> is its place in reading order, spaces not
/// counted: the stagger's index.
/// </summary>
public readonly record struct BigTypeLetter(float X, float Y, float Width, float Height, float AtlasX, float AtlasY, int Order);

/// <summary>
/// The focused item's name as the native Stage draws it (R1's route, made real by M47 B6): one atlas holding each distinct letter
/// once, in white, in its own ink-tight cell, and every letter placed where the fitted name has it at rest. <paramref name="Size"/>
/// is the size the name was fitted at (an em, for the motion). <paramref name="Opacity"/> dims a missing game's name.
/// </summary>
public sealed record BigTypeLetters(string Text, StagePixels Atlas, IReadOnlyList<BigTypeLetter> Letters, float Size, float Opacity = 1f);

/// <summary>
/// The Stage's name under the Big Type look (M47 B6); null takes it away. With <paramref name="Animate"/> the old name's letters
/// leave and the new one's arrive from <paramref name="Entrance"/>; without it (a replay, a resend) it is simply there. Drawn in the
/// left zone's ink, like the rest of the left side's text.
/// </summary>
public sealed record StageNameCommand(BigTypeLetters? Name, BigTypeEntrance Entrance, bool Animate = true) : StageCommand
{
    public override string? RetainKey => "bigtype-name";
    public override StageCommand ForReplay() => this with { Animate = false };

    /// <summary>The name is a word being typed (M47 B9): how it changed; null for any other name.</summary>
    public BigTypeTyped? Typed { get; init; }
}

/// <summary>
/// A press (any pad press or touch, the shell's <c>InputArrived</c>) under the Big Type look (M47 B6): the name's letters hop on the
/// same schedule as the Launcher's tiles (<see cref="BigTypeMotion.HopStart"/>). A moment, never retained; it does not wake a resting
/// backdrop on its own.
/// </summary>
public sealed record StageInputCommand : StageCommand;

/// <summary>
/// How a typed word changed (M47 B9, the mock's typeWord): the letters from <paramref name="FreshFrom"/> on (in reading order) drop in;
/// the letter with order −1, if any, is the one deleted, falling off the end; the word was fitted at <paramref name="FromSize"/> before
/// (0: it was not shown) and eases to its new size about (<paramref name="AnchorX"/>, <paramref name="AnchorY"/>), its box's bottom left
/// on the canvas. <paramref name="Begins"/>: typing began with this word, and the name that was there leaves upward.
/// </summary>
public sealed record BigTypeTyped(int FreshFrom, float FromSize, float AnchorX, float AnchorY, bool Begins);
