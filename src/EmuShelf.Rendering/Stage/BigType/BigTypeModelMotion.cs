namespace EmuShelf.Rendering.Stage.BigType;

/// <summary>What the model view's medium does when the Stage is told about it (<see cref="StageModelCommand"/>).</summary>
public enum BigTypeModelChange
{
    /// <summary>Nothing moves: the same game again (its cover arrived, a replay), or held still.</summary>
    None,

    /// <summary>The view opened: the medium rises through the hinge, turning in.</summary>
    Rise,

    /// <summary>Another game, the next one along: one flip into its medium, turning the way you moved.</summary>
    FlipForward,

    /// <summary>Another game, the previous one: the flip turns the other way.</summary>
    FlipBack,

    /// <summary>The view closed (or the focus left the game): the medium drops out the way it came.</summary>
    Leave,
}

/// <summary>
/// Where and how the model view's medium is drawn at one moment. <see cref="OffsetX"/>, <see cref="OffsetY"/> and <see cref="Scale"/>
/// place its box against its resting box in the middle of the Stage (0, 0 and 1 at rest); <see cref="Bob"/> is the float, in canvas
/// pixels, for the medium only (its shadow stays on the floor). <see cref="Pitch"/> and <see cref="Yaw"/> are the proposal's CSS
/// angles in degrees (rotateX, rotateY); <see cref="BigTypeModelMotion.RendererYaw"/> turns them into the renderer's.
/// </summary>
/// <see cref="Kick"/> is the part of the yaw that is a turn in or a flip (a flat cover, which cannot show its back, turns by it alone).
public readonly record struct BigTypeModelPose(float OffsetX, float OffsetY, float Scale, float Opacity, float Bob, float Pitch, float Yaw,
    float Kick, float FloorScale, float FloorOpacity)
{
    /// <summary>Nothing to draw.</summary>
    public bool IsHidden => Opacity <= 0f;
}

/// <summary>
/// The model view (M47 B12, <c>docs/prototypes/handheld-big-type/proposals.html</c>, approved 2026-11-15): on LB or RB a game's medium
/// stands alone in the middle of the Stage. The numbers are tokens.js <c>stage.Model…</c> and <c>motion.ModelIn</c>, <c>ModelFlip</c>,
/// <c>ModelFloat</c>, <c>ModelShowcase</c> and <c>ModelSwing</c>; the shapes are the proposal's script and CSS (its <c>.case.big</c>
/// transition, its <c>kickr.animate</c>, its <c>showcase</c>, <c>bob</c> and <c>shade</c> keyframes, its pointer turning). Everything
/// here is a pure function of time; <see cref="BigTypeModelState"/> keeps the moments the moves started, and the native Stage and the
/// Avalonia Stage each keep one and ask it for the pose every frame.
/// </summary>
public static class BigTypeModelMotion
{
    /// <summary>tokens.js <c>stage.ModelRight</c>, <c>ModelTop</c>, <c>ModelScale</c>: the medium's box in the middle, grown.</summary>
    public const float ModelRight = 764f;
    public const float ModelTop = 216f;
    public const float ModelScale = 1.36f;

    /// <summary>
    /// tokens.js <c>stage.ModelMaxWidth</c> (the app's own): the widest a medium may be drawn at any turn; a wider one (a jewel case, a
    /// landscape cartridge) is set smaller.
    /// </summary>
    public const float MaxWidth = 720f;

    /// <summary>
    /// How much smaller than the model's size a medium is set so that at no turn is it wider than <see cref="MaxWidth"/>: its model's
    /// width and depth (<paramref name="width"/>, <paramref name="depth"/>, against its <paramref name="height"/>) drawn
    /// <paramref name="drawnHeight"/> high, turned all the way round. 1 for the cases and cards; under 1 for a landscape cartridge.
    /// </summary>
    public static float FitScale(float width, float depth, float height, float drawnHeight)
    {
        if (height <= 0f || drawnHeight <= 0f)
            return 1f;
        var widest = drawnHeight * MathF.Sqrt((width * width) + (depth * depth)) / height;
        return widest <= MaxWidth ? 1f : MaxWidth / widest;
    }

    /// <summary>The medium's drawn height in its model-view frame: the renderer fills <c>640 / 820</c> of a frame's height (MediumFrameFill).</summary>
    public const float FrameFill = 640f / 820f;

    /// <summary>tokens.js <c>stage.ModelFloor…</c>: the floor's shadow in the box's units (before the scale).</summary>
    public const float FloorLeft = 0.04f;
    public const float FloorRight = 0.10f;
    public const float FloorBottom = 56f;
    public const float FloorHeight = 44f;
    public const float FloorBlur = 20f;
    public const float FloorAlpha = 0.3f;

    /// <summary>tokens.js <c>ModelFloorColour</c>'s colour, 0–255 (its alpha is <see cref="FloorAlpha"/>).</summary>
    public const byte FloorRed = 8, FloorGreen = 8, FloorBlue = 20;

    /// <summary>tokens.js <c>type.HintKey</c>: a multi-letter hint key (RB, RS) is a rounded key, padded each side.</summary>
    public const float HintKeyPadding = 14f;
    public const float HintKeyRadius = 18f;

    /// <summary>tokens.js <c>ModelIn</c>.</summary>
    public const float InMilliseconds = 550f;
    public const float InFadeMilliseconds = 250f;
    public const float InDrop = 380f;
    public const float InFromScale = 0.8f;
    public const float InTurn = -250f;
    public const float InTurnMilliseconds = 950f;

    /// <summary>tokens.js <c>ModelFlip</c>.</summary>
    public const float FlipMilliseconds = 720f;
    public const float FlipTurn = 180f;

    /// <summary>tokens.js <c>ModelFloat</c>.</summary>
    public const float FloatEachMilliseconds = 4600f;
    public const float FloatFrom = 6f;
    public const float FloatTo = -12f;
    public const float ShadeScale = 0.9f;
    public const float ShadeOpacity = 0.7f;

    /// <summary>tokens.js <c>ModelShowcase</c>.</summary>
    public const float ShowcaseMilliseconds = 15000f;
    public const float RestPitch = 4f;
    public const float RestYaw = 32f;
    public const float DriftAt = 0.36f;
    public const float HoldPitch = 2f;
    public const float HoldYaw = 14f;
    public const float TurnAt = 0.5f;

    /// <summary>tokens.js <c>ModelSwing</c>.</summary>
    public const float SwingMilliseconds = 620f;

    /// <summary>The <c>.case.big</c> transition's curve (tokens.js <c>ModelIn.easing</c>).</summary>
    public static float InEase(float t) => Curve(0.2f, 1.4f, 0.3f, 1f, t);

    /// <summary>The proposal's <c>FLIP</c>: the turn in, the flip between games and the swing back.</summary>
    public static float FlipEase(float t) => Curve(0.2f, 1.3f, 0.3f, 1f, t);

    private static float DriftEase(float t) => Curve(0.45f, 0f, 0.3f, 1f, t);

    private static float TurnEase(float t) => Curve(0.5f, 0f, 0.25f, 1f, t);

    private static float Curve(float x1, float y1, float x2, float y2, float t) =>
        t <= 0f ? 0f : t >= 1f ? 1f : StageCurves.CubicBezier(x1, y1, x2, y2, t);

    // ---- Where it stands ----

    /// <summary>The middle of the medium's resting box in the model view, on a canvas <paramref name="canvasWidth"/> wide.</summary>
    /// <remarks>
    /// tokens.js places the box ModelRight from the right edge of the 1920 Stage, which is its middle across; on any other canvas
    /// it stays in the middle across (the narrow Stage's place waits for B11's layout).
    /// </remarks>
    public static (float X, float Y) Centre(float canvasWidth, float mediumHeight) =>
        (canvasWidth / 2f, ModelTop + (mediumHeight / 2f));

    /// <summary>
    /// Where the medium starts its rise and ends its fall, against its resting box: its old place beside the name
    /// (<paramref name="besideX"/>, <paramref name="besideY"/>, the middle of the medium's corner), <see cref="InDrop"/> lower, at
    /// <see cref="InFromScale"/> of its size there.
    /// </summary>
    public static (float X, float Y, float Scale) OffPlace(float besideX, float besideY, float modelX, float modelY) =>
        (besideX - modelX, besideY - modelY + InDrop, InFromScale / ModelScale);

    /// <summary>The proposal's showcase at <paramref name="since"/> ms into it: (pitch, yaw) in degrees; it repeats every 15 s.</summary>
    public static (float Pitch, float Yaw) Showcase(float since)
    {
        var c = since <= 0f ? 0f : since % ShowcaseMilliseconds / ShowcaseMilliseconds;
        if (c < DriftAt)
        {
            var k = DriftEase(c / DriftAt);
            return (RestPitch + ((HoldPitch - RestPitch) * k), RestYaw + ((HoldYaw - RestYaw) * k));
        }
        if (c < TurnAt)
            return (HoldPitch, HoldYaw);
        var turn = TurnEase((c - TurnAt) / (1f - TurnAt));
        return (HoldPitch + ((RestPitch - HoldPitch) * turn), HoldYaw + ((RestYaw - 360f - HoldYaw) * turn));
    }

    /// <summary>The float's lift at <paramref name="since"/> ms (the <c>bob</c> keyframes, alternate, ease-in-out), in the box's units.</summary>
    public static float Float(float since) => FloatFrom + ((FloatTo - FloatFrom) * Swing(since));

    /// <summary>The shadow's scale and opacity with the float (the <c>shade</c> keyframes).</summary>
    public static (float Scale, float Opacity) Shade(float since)
    {
        var k = Swing(since);
        return (1f + ((ShadeScale - 1f) * k), 1f + ((ShadeOpacity - 1f) * k));
    }

    // 0 → 1 over one FloatEachMilliseconds and back over the next, eased in and out each way (CSS alternate, ease-in-out).
    private static float Swing(float since)
    {
        if (since <= 0f)
            return 0f;
        var phase = since % (2f * FloatEachMilliseconds) / FloatEachMilliseconds;
        return BigTypeMotion.EaseInOut(phase <= 1f ? phase : 2f - phase);
    }

    /// <summary>A turn left from <paramref name="from"/> degrees toward 0 on the flip curve, <paramref name="since"/> ms into <paramref name="milliseconds"/>.</summary>
    public static float Kick(float from, float since, float milliseconds) =>
        since >= milliseconds ? 0f : from * (1f - FlipEase(Math.Max(0f, since) / milliseconds));

    /// <summary>
    /// The turn a flip into the next game starts from: half a turn the way you moved on top of whatever turn is still on screen (so a
    /// press mid-flip carries on and never jumps back to rest), kept within a whole turn (a whole turn looks the same).
    /// </summary>
    public static float FlipFrom(float kickOnScreen, bool forward)
    {
        var from = kickOnScreen + (forward ? FlipTurn : -FlipTurn);
        while (from > 360f)
            from -= 360f;
        while (from < -360f)
            from += 360f;
        return from;
    }

    /// <summary>The swing back's yaw start: the turned pose's yaw within half a turn of the resting yaw, so it never spins round.</summary>
    public static float NearRest(float yaw)
    {
        var d = (yaw - RestYaw) % 360f;
        if (d > 180f)
            d -= 360f;
        else if (d < -180f)
            d += 360f;
        return RestYaw + d;
    }

    /// <summary>
    /// The proposal's CSS rotateY in degrees as the renderer's yaw, in radians: the same way round (a positive turn takes the right edge
    /// away and shows the spine, as the proposal's case does at rest; checked on the native Stage's renders, --stage-model).
    /// </summary>
    public static float RendererYaw(float cssDegrees) => cssDegrees * MathF.PI / 180f;

    /// <summary>The proposal's CSS rotateX in degrees as the renderer's pitch, in radians.</summary>
    public static float RendererPitch(float cssDegrees) => -cssDegrees * MathF.PI / 180f;

    /// <summary>The renderer's yaw in radians as the proposal's CSS degrees (for the stick's turn).</summary>
    public static float CssYaw(float rendererRadians) => rendererRadians * 180f / MathF.PI;

    /// <summary>The renderer's pitch in radians as the proposal's CSS degrees.</summary>
    public static float CssPitch(float rendererRadians) => -rendererRadians * 180f / MathF.PI;

    // ---- The floor's shadow ----

    /// <summary>
    /// The floor's shadow under a medium box of <paramref name="width"/> × <paramref name="height"/> (its own units, before the scale):
    /// the rectangle (against the box's top left) the blurred ellipse is drawn into, room for the blur included.
    /// </summary>
    public static (float X, float Y, float Width, float Height) FloorRect(float width, float height)
    {
        var x0 = -FloorLeft * width;
        var x1 = width + (FloorRight * width);
        var y1 = height + FloorBottom;
        var y0 = y1 - FloorHeight;
        var pad = FloorBlur * 3f;
        return (x0 - pad, y0 - pad, x1 - x0 + (2f * pad), FloorHeight + (2f * pad));
    }

    /// <summary>
    /// The floor's shadow as premultiplied RGBA, <paramref name="pixelsWide"/> across the rectangle <see cref="FloorRect"/> gives for a
    /// box <paramref name="width"/> wide: an ellipse in the floor's colour, blurred (a Gaussian of <see cref="FloorBlur"/>, as CSS
    /// <c>blur()</c>), drawn once and stretched.
    /// </summary>
    public static StagePixels Floor(float width, float height, int pixelsWide = 160)
    {
        var rect = FloorRect(width, height);
        var scale = pixelsWide / rect.Width;
        var pixelsHigh = Math.Max(2, (int)MathF.Ceiling(rect.Height * scale));
        var a = (width * (1f + FloorLeft + FloorRight)) / 2f;
        var b = FloorHeight / 2f;
        var data = new byte[pixelsWide * pixelsHigh * 4];
        for (var row = 0; row < pixelsHigh; row++)
        {
            var y = ((row + 0.5f) / scale) - (rect.Height / 2f);
            for (var column = 0; column < pixelsWide; column++)
            {
                var x = ((column + 0.5f) / scale) - (rect.Width / 2f);
                // The signed distance to the ellipse's edge, near enough (its implicit function over its gradient).
                var f = ((x * x) / (a * a)) + ((y * y) / (b * b)) - 1f;
                var gx = 2f * x / (a * a);
                var gy = 2f * y / (b * b);
                var gradient = MathF.Sqrt((gx * gx) + (gy * gy));
                var distance = gradient > 1e-6f ? f / gradient : -b;
                var alpha = FloorAlpha * 0.5f * Erfc(distance / (FloorBlur * MathF.Sqrt(2f)));
                var i = ((row * pixelsWide) + column) * 4;
                data[i] = (byte)MathF.Round(FloorRed * alpha);
                data[i + 1] = (byte)MathF.Round(FloorGreen * alpha);
                data[i + 2] = (byte)MathF.Round(FloorBlue * alpha);
                data[i + 3] = (byte)MathF.Round(255f * alpha);
            }
        }
        return new StagePixels(pixelsWide, pixelsHigh, data, IsBgra: false);
    }

    // Abramowitz and Stegun 7.1.26 (error under 1.5e-7).
    private static float Erfc(float x)
    {
        var z = MathF.Abs(x);
        var t = 1f / (1f + (0.3275911f * z));
        var y = t * (0.254829592f + (t * (-0.284496736f + (t * (1.421413741f + (t * (-1.453152027f + (t * 1.061405429f))))))));
        var erfc = y * MathF.Exp(-z * z);
        return x >= 0f ? erfc : 2f - erfc;
    }
}

/// <summary>
/// The model view's medium over time (M47 B12): when it rose or left, its flips, the stick's hold and the swing back, and when its
/// idle motion rests. One per Stage (native and Avalonia), told about each change at its own clock's time; <see cref="Pose"/> answers
/// for any later moment. Every move starts from the pose on screen, so nothing snaps: a flip on a flip carries on, the stick takes
/// hold of the pose it finds, letting go swings back from where it was left, and a leave during the rise goes back from where it got to.
/// </summary>
public sealed class BigTypeModelState
{
    // Where it stands: 0 = its off place below the hinge, 1 = in the middle. A transition from what it was at the change.
    private bool _on;
    private float _placeFrom;
    private float _fadeFrom;
    private float _placeStart = float.NegativeInfinity;

    // The turn on top of the pose (the proposal's kickr): the rise's and the flips'.
    private float _kickFrom;
    private float _kickStart = float.NegativeInfinity;
    private float _kickMilliseconds = 1f;

    // The showcase's clock and the float's.
    private float _showcaseStart;
    private float _floatStart;

    // The stick: the pose it took hold of and how far it has turned it since; the swing back from where it was let go.
    private bool _held;
    private float _holdPitch;
    private float _holdYaw;
    private float _turnPitch;
    private float _turnYaw;
    private float _swingStart = float.NegativeInfinity;
    private float _swingPitch;
    private float _swingYaw;

    // The idle motion rests like the Stage's backdrop (StageRest): a minute after the last touch the showcase stops at the end of its
    // turn (its resting pose) and the float eases out; the next touch starts both again from there.
    private float _touched;
    private float _wake = float.NegativeInfinity;

    /// <summary>The medium is showing (or rising); false while it leaves or once it has gone.</summary>
    public bool IsOn => _on;

    /// <summary>The stick has hold of it.</summary>
    public bool IsHeld => _held;

    /// <summary>
    /// Tells it what happened at <paramref name="now"/>. Held still, or without <paramref name="animate"/> (a replay), the change is
    /// there at once.
    /// </summary>
    public void Apply(BigTypeModelChange change, float now, bool holdStill, bool animate = true)
    {
        var shown = Place(now, holdStill);
        // At once: the moves start long ago, so they have finished.
        var at = holdStill || !animate ? float.NegativeInfinity : now;
        switch (change)
        {
            case BigTypeModelChange.Rise:
                if (_on)
                    break;
                _on = true;
                _placeFrom = shown.Place;
                _fadeFrom = shown.Fade;
                _placeStart = at;
                _kickFrom = BigTypeModelMotion.InTurn + KickAt(now);
                _kickStart = at;
                _kickMilliseconds = BigTypeModelMotion.InTurnMilliseconds;
                _held = false;
                _swingStart = float.NegativeInfinity;
                _showcaseStart = _floatStart = now;
                break;
            case BigTypeModelChange.FlipForward or BigTypeModelChange.FlipBack:
                if (!_on)
                {
                    Apply(BigTypeModelChange.Rise, now, holdStill, animate);
                    return;
                }
                _kickFrom = BigTypeModelMotion.FlipFrom(KickAt(now), change == BigTypeModelChange.FlipForward);
                _kickStart = at;
                _kickMilliseconds = BigTypeModelMotion.FlipMilliseconds;
                break;
            case BigTypeModelChange.Leave:
                if (!_on)
                    break;
                _on = false;
                _placeFrom = shown.Place;
                _fadeFrom = shown.Fade;
                _placeStart = at;
                _held = false;
                break;
        }
        Touch(now);
    }

    /// <summary>The stick has turned it <paramref name="pitch"/>, <paramref name="yaw"/> degrees (CSS) since it took hold.</summary>
    public void Hold(float pitch, float yaw, float now, bool holdStill = false)
    {
        if (!_held)
        {
            // It takes hold of the pose on screen (the turn on top of it carries on by itself).
            var (p, y) = holdStill ? StillBase() : BaseAt(now);
            _holdPitch = p;
            _holdYaw = y;
            _held = true;
            _swingStart = float.NegativeInfinity;
        }
        _turnPitch = pitch;
        _turnYaw = yaw;
        Touch(now);
    }

    /// <summary>The stick let go (or R3): it swings back to rest from where it was left.</summary>
    public void Release(float now)
    {
        if (!_held)
            return;
        _held = false;
        _swingPitch = _holdPitch + _turnPitch;
        _swingYaw = BigTypeModelMotion.NearRest(_holdYaw + _turnYaw);
        _swingStart = now;
        Touch(now);
    }

    /// <summary>A press or a touch elsewhere: the idle motion's minute starts again, and a rested medium gets going.</summary>
    public void Touch(float now)
    {
        if (IsResting(now))
        {
            _showcaseStart = _floatStart = now;
            _wake = now;
        }
        _touched = now;
    }

    /// <summary>The pose at <paramref name="now"/>, placed from its off place (<paramref name="off"/>, <see cref="BigTypeModelMotion.OffPlace"/>).</summary>
    public BigTypeModelPose Pose(float now, bool holdStill, (float X, float Y, float Scale) off)
    {
        var (place, fade) = Place(now, holdStill);
        if (fade <= 0f && !_on)
            return default;
        var scale = off.Scale + ((1f - off.Scale) * place);
        var (pitch, yaw) = holdStill ? StillBase() : BaseAt(now);
        var kick = holdStill ? 0f : KickAt(now);
        yaw += kick;
        var alive = holdStill ? 0f : Alive(now);
        var bob = alive * BigTypeModelMotion.Float(now - _floatStart) * BigTypeModelMotion.ModelScale * scale;
        var shade = BigTypeModelMotion.Shade(now - _floatStart);
        return new BigTypeModelPose(off.X * (1f - place), off.Y * (1f - place), scale, fade, bob, pitch, yaw, kick,
            1f + ((shade.Scale - 1f) * alive), 1f + ((shade.Opacity - 1f) * alive));
    }

    /// <summary>Something still moves at <paramref name="now"/> (the Stage draws another frame).</summary>
    public bool IsMoving(float now, bool holdStill)
    {
        if (holdStill)
            return false;
        if (now - _placeStart < BigTypeModelMotion.InMilliseconds || now - _kickStart < _kickMilliseconds)
            return true;
        if (!_on)
            return false;
        return now - _swingStart < BigTypeModelMotion.SwingMilliseconds || Alive(now) > 0f;
    }

    /// <summary>The move under way at <paramref name="now"/>, for the frame probe: rise, leave, flip, turn (the stick), swing, float; null at rest.</summary>
    public string? Motion(float now, bool holdStill)
    {
        if (holdStill)
            return null;
        if (now - _placeStart < BigTypeModelMotion.InMilliseconds)
            return _on ? "rise" : "leave";
        if (!_on)
            return null;
        if (now - _kickStart < _kickMilliseconds)
            return "flip";
        if (_held)
            return "turn";
        if (now - _swingStart < BigTypeModelMotion.SwingMilliseconds)
            return "swing";
        var start = ShowcaseStart;
        if (now < RestAt(start) && (now - start) % BigTypeModelMotion.ShowcaseMilliseconds >= BigTypeModelMotion.TurnAt * BigTypeModelMotion.ShowcaseMilliseconds)
            return "showcase";
        return Alive(now) > 0f ? "float" : null;
    }

    /// <summary>It has gone (left and faded): the Stage can let its medium go.</summary>
    public bool IsGone(float now, bool holdStill) => !_on && Place(now, holdStill).Fade <= 0f;

    private (float Place, float Fade) Place(float now, bool holdStill)
    {
        var target = _on ? 1f : 0f;
        if (holdStill)
            return (target, target);
        var since = now - _placeStart;
        var place = _placeFrom + ((target - _placeFrom) * BigTypeModelMotion.InEase(since / BigTypeModelMotion.InMilliseconds));
        var fade = _fadeFrom + ((target - _fadeFrom) * BigTypeMotion.CssEase(since / BigTypeModelMotion.InFadeMilliseconds));
        if (since >= BigTypeModelMotion.InMilliseconds)
            place = target;
        if (since >= BigTypeModelMotion.InFadeMilliseconds)
            fade = target;
        return (place, Math.Clamp(fade, 0f, 1f));
    }

    private float KickAt(float now) => BigTypeModelMotion.Kick(_kickFrom, now - _kickStart, _kickMilliseconds);

    // Held still: the resting pose, or where the stick holds it.
    private (float Pitch, float Yaw) StillBase() => _held
        ? (_holdPitch + _turnPitch, _holdYaw + _turnYaw)
        : (BigTypeModelMotion.RestPitch, BigTypeModelMotion.RestYaw);

    // The pose under the turn: the stick's, the swing back, or the showcase (stopped at its start once resting).
    private (float Pitch, float Yaw) BaseAt(float now)
    {
        if (_held)
            return (_holdPitch + _turnPitch, _holdYaw + _turnYaw);
        var sinceSwing = now - _swingStart;
        if (sinceSwing < BigTypeModelMotion.SwingMilliseconds)
        {
            var k = BigTypeModelMotion.FlipEase(Math.Max(0f, sinceSwing) / BigTypeModelMotion.SwingMilliseconds);
            return (_swingPitch + ((BigTypeModelMotion.RestPitch - _swingPitch) * k), _swingYaw + ((BigTypeModelMotion.RestYaw - _swingYaw) * k));
        }
        var start = ShowcaseStart;
        if (now >= RestAt(start))
            return (BigTypeModelMotion.RestPitch, BigTypeModelMotion.RestYaw);
        return BigTypeModelMotion.Showcase(now - start);
    }

    // After a swing back the showcase starts again from rest.
    private float ShowcaseStart => float.IsNegativeInfinity(_swingStart)
        ? _showcaseStart
        : Math.Max(_showcaseStart, _swingStart + BigTypeModelMotion.SwingMilliseconds);

    // When the idle motion rests: the end of the first showcase turn that finishes a minute or more after the last touch.
    private float RestAt(float showcaseStart)
    {
        var due = _touched + StageRest.AfterMilliseconds;
        if (due <= showcaseStart)
            return showcaseStart + BigTypeModelMotion.ShowcaseMilliseconds;
        var turns = MathF.Ceiling((due - showcaseStart) / BigTypeModelMotion.ShowcaseMilliseconds);
        return showcaseStart + (turns * BigTypeModelMotion.ShowcaseMilliseconds);
    }

    private bool IsResting(float now) => _on && !_held && now >= RestAt(ShowcaseStart);

    // The float's strength: 1 while it is alive, easing out over StageRest.SlowMilliseconds once it rests, and in again on a touch.
    private float Alive(float now)
    {
        var wakeIn = StageRest.Eased(Math.Clamp((now - _wake) / StageRest.WakeMilliseconds, 0f, 1f));
        if (_held || now - _swingStart < BigTypeModelMotion.SwingMilliseconds)
            return wakeIn;
        var rest = RestAt(ShowcaseStart);
        var resting = now < rest ? 1f : 1f - StageRest.Eased(Math.Clamp((now - rest) / StageRest.SlowMilliseconds, 0f, 1f));
        return Math.Min(wakeIn, resting);
    }
}
