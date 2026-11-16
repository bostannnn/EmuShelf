namespace EmuShelf.Rendering.Stage.BigType;

/// <summary>
/// The idle artwork's round window on the native Stage (M47 B15, proposals-screens.html option 12, the Porthole): <paramref name="Art"/> is
/// the window at rest, pictured round (the artwork cropped to fill it, transparent outside the circle), placed where the window sits on
/// the canvas. A first picture rises through the hinge; another one takes the window through a circle growing from its bottom; null drops
/// it back through the hinge. Without <paramref name="Animate"/> (a replay, holding still) it is simply there or gone.
/// </summary>
public sealed record StagePortholeCommand(StageImage? Art, bool Animate = true) : StageCommand
{
    public override string? RetainKey => "bigtype-porthole";
    public override StageCommand ForReplay() => this with { Animate = false };
}
