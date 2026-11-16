namespace EmuShelf.Rendering.Stage.BigType;

/// <summary>
/// The model view's medium (M47 B12): the game's <paramref name="Medium"/> in its box in the middle of the Stage at the model's size, or
/// for a platform with no authored medium its flat <paramref name="Cover"/> pictured in the same place, and what it does
/// (<paramref name="Change"/>). <paramref name="BesideX"/>, <paramref name="BesideY"/> are the middle of the medium's old corner beside
/// the name (where it rises from), <paramref name="CentreX"/>, <paramref name="CentreY"/> the middle of its resting box, and
/// <paramref name="BoxWidth"/> × <paramref name="BoxHeight"/> that box's size before the model's scale (its floor's shadow is laid
/// from it), <paramref name="Scale"/> the model's scale on this canvas (<see cref="BigTypeModelMotion.ScaleFor"/>). Without
/// <paramref name="Animate"/> (a replay) it is simply there.
/// </summary>
public sealed record StageModelCommand(StageMedium? Medium, StageImage? Cover, BigTypeModelChange Change, float BesideX, float BesideY,
    float CentreX, float CentreY, float BoxWidth, float BoxHeight, bool Animate = true, float Scale = BigTypeModelMotion.ModelScale) : StageCommand
{
    public override string? RetainKey => "bigtype-model";
    public override StageCommand ForReplay() => this with { Animate = false };

    /// <summary>The press that brought another game came within a run of the one before (M47 B11): its flip is the short form.</summary>
    public bool Quick { get; init; }
}

/// <summary>
/// The right stick on the model view's medium (M47 B12): it has turned it <paramref name="Pitch"/>, <paramref name="Yaw"/> degrees (the
/// proposal's CSS rotateX / rotateY) since it took hold; <paramref name="Held"/> false is letting go (or R3), and it swings back.
/// </summary>
public sealed record StageModelHoldCommand(float Pitch, float Yaw, bool Held) : StageCommand;
