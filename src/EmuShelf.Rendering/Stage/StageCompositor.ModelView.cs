using System.Numerics;
using EmuShelf.Rendering.Shells;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.Rendering.Stage;

// The model view on the native Stage under the Big Type look (M47 B12, proposals.html): the game's medium alone in the middle of the
// Stage, from the maths the Avalonia Stage shares (BigTypeModelMotion): it rises through the hinge turning in, floats with its shadow
// breathing under it, shows its back and spine every fifteen seconds, flips into the next game's medium, turns under the stick and
// swings back; leaving, it drops out the way it came. Its pose is worked out here every frame and rendered into the medium's texture
// only when it changed, so the shell posts nothing while it moves.
public sealed partial class StageCompositor
{
    private BigTypeModelState? _model;
    private StageModelCommand? _modelShown;
    private Layer? _modelCover;
    private Layer? _modelFloor;
    private (float Width, float Height) _modelFloorBox;

    // The medium the model view draws: it owns _medium from its first command until it has left, so a centre with no medium (the name
    // view's) does not take it away mid-leave.
    private bool _modelOwnsMedium;

    private void ApplyModel(StageModelCommand command, float now)
    {
        var model = _model ??= new BigTypeModelState();
        var animate = command.Animate && !_holdStill;
        if (command.Change == BigTypeModelChange.Leave)
        {
            if (_modelShown is null)
                return;
            model.Apply(BigTypeModelChange.Leave, now, _holdStill, animate);
            return;
        }
        _modelShown = command;
        if (command.Medium is { } medium)
        {
            _modelOwnsMedium = true;
            SetMedium(medium);
        }
        else if (_modelOwnsMedium)
        {
            _modelOwnsMedium = false;
            SetMedium(null);
        }
        Replace(ref _modelCover, command.Cover, repeat: false);
        var change = command.Change;
        // A replay (or a medium told again before it was ever shown) is simply there.
        if (!model.IsOn && change == BigTypeModelChange.None)
            change = BigTypeModelChange.Rise;
        model.Apply(change, now, _holdStill, animate, command.Quick);
    }

    private void ApplyModelHold(StageModelHoldCommand command, float now)
    {
        if (_model is not { IsOn: true } model)
            return;
        if (command.Held)
            model.Hold(command.Pitch, command.Yaw, now, _holdStill);
        else
            model.Release(now);
    }

    /// <summary>
    /// The centre's medium (today's look, and Big Type's cover search): while the model view holds the medium a centre with none
    /// leaves it alone; a centre with one of its own ends the model view at once.
    /// </summary>
    private void SetCentreMedium(StageMedium? medium)
    {
        if (_modelOwnsMedium)
        {
            if (medium is null)
                return;
            EndModel();
        }
        SetMedium(medium);
    }

    private void EndModel()
    {
        if (_modelOwnsMedium)
            SetMedium(null);
        _modelOwnsMedium = false;
        _modelShown = null;
        _model = null;
        Replace(ref _modelCover, null, repeat: false);
    }

    /// <summary>Before the medium is rendered: the model view's pose for this frame, rendered again only if it changed.</summary>
    private void PoseModel(float now)
    {
        if (_modelShown is not { } shown || _model is not { } model || !_modelOwnsMedium)
            return;
        var pose = model.Pose(now, _holdStill, Off(shown), shown.Scale);
        if (pose.IsHidden)
            return;
        var yaw = BigTypeModelMotion.RendererYaw(pose.Yaw);
        var pitch = BigTypeModelMotion.RendererPitch(pose.Pitch);
        if (yaw == _yaw && pitch == _pitch)
            return;
        _yaw = yaw;
        _pitch = pitch;
        _mediumDirty = true;
    }

    private static (float X, float Y, float Scale) Off(StageModelCommand shown) =>
        BigTypeModelMotion.OffPlace(shown.BesideX, shown.BesideY, shown.CentreX, shown.CentreY, shown.Scale);

    /// <summary>Draws the model view (its floor's shadow, then the medium or the cover); false when it is not showing.</summary>
    private bool DrawModel(float now, ref bool animating)
    {
        if (_modelShown is not { } shown || _model is not { } model)
            return false;
        if (model.IsGone(now, _holdStill))
        {
            EndModel();
            return false;
        }
        animating |= model.IsMoving(now, _holdStill);
        var pose = model.Pose(now, _holdStill, Off(shown), shown.Scale);
        if (pose.IsHidden)
            return true;
        // The launch bumps it as the tile arrives (M47 B8), from either view's place. A landscape medium is set smaller to keep clear
        // of the corners (ModelMaxWidth).
        var (bump, rise) = CaseBump(now, ref animating);
        bump *= _modelOwnsMedium && _medium is { } fitted ? ModelFit(fitted) : 1f;
        var scale = shown.Scale * pose.Scale * bump;
        var centreX = shown.CentreX + pose.OffsetX;
        var centreY = shown.CentreY + pose.OffsetY - rise;
        DrawModelFloor(shown, pose, centreX, centreY, scale);
        if (_modelOwnsMedium && _medium is { } medium && _mediumTexture != 0)
        {
            // The medium's frame was laid at the model's size; it is scaled from there. The FBO's first row is the picture's bottom.
            var k = pose.Scale * bump;
            var width = medium.Width * k;
            var height = medium.Height * k;
            var x = centreX + ((medium.X + (medium.Width / 2f) - shown.CentreX) * k) - (width / 2f);
            var y = centreY + pose.Bob + ((medium.Y + (medium.Height / 2f) - shown.CentreY) * k) - (height / 2f);
            DrawQuad(_mediumTexture, x, y, width, height, pose.Opacity, 1f, 0f, 0f, new Vector4(0, 1, 1, 0));
        }
        else if (_modelCover is { } cover)
        {
            // A flat cover cannot show its back: it turns in and flips by narrowing, and stands still otherwise.
            var image = cover.Image;
            var k = pose.Scale * bump;
            var width = image.Width * k * MathF.Abs(MathF.Cos(pose.Kick * MathF.PI / 180f));
            var height = image.Height * k;
            var x = centreX + ((image.X + (image.Width / 2f) - shown.CentreX) * k) - (width / 2f);
            var y = centreY + pose.Bob + ((image.Y + (image.Height / 2f) - shown.CentreY) * k) - (height / 2f);
            DrawQuad(cover.Texture, x, y, width, height, pose.Opacity, 1f, 0f, 0f, new Vector4(0, 0, 1, 1));
        }
        return true;
    }

    // The medium's fit (BigTypeModelMotion.FitScale) from its model's bounds, once its shell is prepared (1 until then).
    private MediaShell? _modelFitShell;
    private float _modelFit = 1f;

    private float ModelFit(StageMedium medium)
    {
        if (_modelFitShell == medium.Shell)
            return _modelFit;
        var prepared = MediaShellCatalog.PrepareAsync(medium.Shell);
        if (!prepared.IsCompletedSuccessfully)
            return 1f;
        var size = prepared.Result.Size;
        _modelFitShell = medium.Shell;
        _modelFit = BigTypeModelMotion.FitScale(size.X, size.Z, size.Y, medium.Height * BigTypeModelMotion.FrameFill);
        return _modelFit;
    }

    private void DrawModelFloor(StageModelCommand shown, BigTypeModelPose pose, float centreX, float centreY, float scale)
    {
        if (_modelFloor is null || _modelFloorBox != (shown.BoxWidth, shown.BoxHeight))
        {
            Replace(ref _modelFloor, null, repeat: false);
            var pixels = BigTypeModelMotion.Floor(shown.BoxWidth, shown.BoxHeight);
            var box = BigTypeModelMotion.FloorRect(shown.BoxWidth, shown.BoxHeight);
            Replace(ref _modelFloor, new StageImage(pixels, box.X, box.Y, box.Width, box.Height), repeat: false);
            _modelFloorBox = (shown.BoxWidth, shown.BoxHeight);
        }
        var floor = _modelFloor!;
        var rect = floor.Image;
        // The rectangle is against the box's top left in the box's units; the shadow breathes about its own middle.
        var left = centreX - (shown.BoxWidth * scale / 2f);
        var top = centreY - (shown.BoxHeight * scale / 2f);
        var middleX = left + ((rect.X + (rect.Width / 2f)) * scale);
        var middleY = top + ((rect.Y + (rect.Height / 2f)) * scale);
        var width = rect.Width * scale * pose.FloorScale;
        var height = rect.Height * scale * pose.FloorScale;
        DrawQuad(floor.Texture, middleX - (width / 2f), middleY - (height / 2f), width, height, pose.Opacity * pose.FloorOpacity, 1f, 0f, 0f,
            new Vector4(0, 0, 1, 1));
    }

    /// <summary>The frame probe's tag with the model view's move added ("model-rise", "model-flip", …) while one runs.</summary>
    private string? ModelProbeTag(string? tag, float now)
    {
        if (_model?.Motion(now, _holdStill) is not { } motion || _modelShown is null)
            return tag;
        return tag is null ? "model-" + motion : tag + "+model-" + motion;
    }

    private void DisposeModel()
    {
        EndModel();
        Replace(ref _modelFloor, null, repeat: false);
    }
}
