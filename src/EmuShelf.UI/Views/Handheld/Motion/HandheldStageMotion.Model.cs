using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using EmuShelf.App.Controls;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

// The model view on the Avalonia Stage (M47 B12), from the maths the native Stage shares (BigTypeModelMotion): the medium (or the flat
// cover) rises through the hinge turning in, floats over its breathing shadow, shows its back every fifteen seconds, flips into the next
// game's medium, turns under the stick and swings back, and drops out the way it came. Each frame on this view's clock while it shows;
// held still it stands at rest. While the native Stage draws, this view is only pictured: the cover stays at rest for its picture.
internal sealed partial class HandheldStageMotion
{
    private static readonly long ModelEpoch = Stopwatch.GetTimestamp();

    private readonly BigTypeModelState _modelState = new();
    private HandheldStageViewModel? _modelStage;
    private bool _modelTold;
    private long _modelGame = -1;
    private bool _modelTicking;
    private WriteableBitmap? _modelFloor;

    private static float ModelNow => (float)Stopwatch.GetElapsedTime(ModelEpoch).TotalMilliseconds;

    /// <summary>Follows the shell's Stage for the model view (called as the shell is attached).</summary>
    private void AttachModel(HandheldShellViewModel? old, HandheldShellViewModel? shell)
    {
        if (old is not null)
            old.StageChanged -= OnModelStageChanged;
        if (_modelStage is { } oldStage)
            oldStage.PropertyChanged -= OnModelStageProperty;
        _modelStage = shell?.Stage;
        if (shell is null || !HandheldTokens.IsBigType)
            return;
        shell.StageChanged += OnModelStageChanged;
        shell.Stage.PropertyChanged += OnModelStageProperty;
    }

    private void OnModelStageChanged()
    {
        TellModel();
        // Another game's lines are another width: the model view's veil follows them.
        if (_modelStage is { IsModelView: true })
            Dispatcher.UIThread.Post(UpdateModelVeil, DispatcherPriority.Loaded);
    }

    private void OnModelStageProperty(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HandheldStageViewModel.IsModelView):
            case nameof(HandheldStageViewModel.HasModelMedium):
            case nameof(HandheldStageViewModel.HasModelCover):
                TellModel();
                // The veil moves to the lines (or back to the name) once they are laid out.
                Dispatcher.UIThread.Post(UpdateModelVeil, DispatcherPriority.Loaded);
                break;
            case nameof(HandheldStageViewModel.ModelHoldVersion) when _modelStage is { } stage:
                var (pitch, yaw, held) = stage.ModelHold;
                if (held)
                    _modelState.Hold(pitch, yaw, ModelNow, ModelStill);
                else
                    _modelState.Release(ModelNow);
                RequestModelFrame();
                break;
        }
    }

    // What changed for the model view since it was last told: it rose, it flipped into another game, it left (the native feed tells its
    // Stage the same, HandheldNativeStageFeed.SendModel).
    private void TellModel()
    {
        if (_modelStage is not { } stage)
            return;
        var showing = stage.IsModelView && (stage.HasModelMedium || stage.HasModelCover);
        BigTypeModelChange change;
        if (!showing)
        {
            if (!_modelTold)
                return;
            _modelTold = false;
            _modelGame = -1;
            change = BigTypeModelChange.Leave;
        }
        else
        {
            change = !_modelTold ? BigTypeModelChange.Rise
                : stage.GameId == _modelGame ? BigTypeModelChange.None
                : _shell?.LastMoveDirection is HandheldDirection.Left or HandheldDirection.Up ? BigTypeModelChange.FlipBack
                : BigTypeModelChange.FlipForward;
            _modelTold = true;
            _modelGame = stage.GameId;
        }
        // Off screen (the native Stage draws) the pieces are only pictured: at rest, at once.
        _modelState.Apply(change, ModelNow, holdStill: ModelStill);
        RequestModelFrame();
    }

    private bool ModelStill => _shell?.HoldsStill == true || !_onScreen;

    private void UpdateModelVeil()
    {
        if (_view.FindControl<HandheldBigTypeVeils>("BtVeils") is not { } veils)
            return;
        veils.FlatEnd = _modelStage is { IsModelView: true } && _view.FindControl<Control>("StageCanvas") is { } canvas
            ? HandheldBigTypeVeils.LinesFlatEnd(_view, canvas)
            : double.NaN;
    }

    private void RequestModelFrame()
    {
        if (_modelTicking || TopLevel.GetTopLevel(_view) is not { } top)
        {
            if (!_modelTicking)
                StepModel();
            return;
        }
        _modelTicking = true;
        top.RequestAnimationFrame(_ =>
        {
            _modelTicking = false;
            if (StepModel())
                RequestModelFrame();
        });
    }

    /// <summary>Poses the model view's pieces for now; true while they still move.</summary>
    private bool StepModel()
    {
        var medium = _view.FindControl<Media3DControl>("BtModelMedium");
        var cover = _view.FindControl<Control>("BtModelCover");
        var floor = _view.FindControl<Image>("BtModelFloor");
        if (medium is null || cover is null || floor is null || _modelStage is not { } stage)
            return false;
        var now = ModelNow;
        var still = ModelStill;
        var beside = HandheldTokens.StageMediumCentre(CanvasWidthBigType);
        var centre = HandheldTokens.StageModelCentre(CanvasWidthBigType);
        var off = BigTypeModelMotion.OffPlace((float)beside.X, (float)beside.Y, (float)centre.X, (float)centre.Y);
        var gone = _modelState.IsGone(now, still);
        var pose = gone ? default : _modelState.Pose(now, still, off);
        var visible = !gone && !pose.IsHidden;
        // The 3D medium draws only on screen (the native Stage renders its own); the cover shows whenever it is the model's, so it can be
        // pictured, and at rest while the native Stage draws.
        medium.IsVisible = visible && _onScreen && (stage.HasModelMedium || (!stage.IsModelView && stage.Medium is not null));
        floor.IsVisible = visible && _onScreen;
        if (!visible)
        {
            cover.RenderTransform = null;
            cover.Opacity = 1;
            return false;
        }
        var (bump, rise) = _shell?.Playing is { } play ? BigTypeLaunchMotion.CaseBump(Since(play.Started), still) : (1f, 0f);
        // A landscape medium is set smaller to keep clear of the corners (ModelMaxWidth), as on the native Stage.
        if (medium.IsVisible && stage.Medium is { } shell &&
            EmuShelf.Rendering.Shells.MediaShellCatalog.PrepareAsync(shell) is { IsCompletedSuccessfully: true } prepared)
        {
            var size = prepared.Result.Size;
            bump *= BigTypeModelMotion.FitScale(size.X, size.Z, size.Y, (float)HandheldTokens.StageModelFrameHeight * BigTypeModelMotion.FrameFill);
        }
        var k = pose.Scale * bump;
        var dx = pose.OffsetX;
        var dy = pose.OffsetY - rise;
        if (medium.IsVisible)
        {
            medium.Yaw = BigTypeModelMotion.RendererYaw(pose.Yaw);
            medium.Pitch = BigTypeModelMotion.RendererPitch(pose.Pitch);
            medium.Opacity = pose.Opacity;
            medium.RenderTransform = Place(k, 1f, dx, dy + pose.Bob);
        }
        if (_onScreen)
        {
            cover.Opacity = pose.Opacity;
            cover.RenderTransform = Place(k, MathF.Abs(MathF.Cos(pose.Kick * MathF.PI / 180f)), dx, dy + pose.Bob);
        }
        else
        {
            cover.Opacity = 1;
            cover.RenderTransform = null;
        }
        if (floor.IsVisible)
            PlaceFloor(floor, centre, pose, k, dx, dy);
        return _modelState.IsMoving(now, still) || (_shell?.Playing is { } playing && Since(playing.Started) < BigTypeLaunchMotion.BumpAt + BigTypeLaunchMotion.BumpMilliseconds);
    }

    private double CanvasWidthBigType => HandheldShellViewModel.WideCanvasWidth;

    // A piece laid out at the model's rest place, scaled about its own middle (across by scaleX more) and moved.
    private static ITransform Place(float scale, float scaleX, float dx, float dy) => new MatrixTransform(
        Matrix.CreateScale(scale * scaleX, scale) * Matrix.CreateTranslation(dx, dy));

    private void PlaceFloor(Image floor, Point centre, BigTypeModelPose pose, float k, float dx, float dy)
    {
        var boxWidth = (float)HandheldTokens.StageMediumWidth;
        var boxHeight = (float)HandheldTokens.StageMediumHeight;
        var rect = BigTypeModelMotion.FloorRect(boxWidth, boxHeight);
        if (_modelFloor is null)
        {
            var pixels = BigTypeModelMotion.Floor(boxWidth, boxHeight);
            _modelFloor = new WriteableBitmap(new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul);
            using (var buffer = _modelFloor.Lock())
            {
                for (var row = 0; row < pixels.Height; row++)
                    Marshal.Copy(pixels.Data, row * pixels.Width * 4, buffer.Address + (row * buffer.RowBytes), pixels.Width * 4);
            }
            floor.Source = _modelFloor;
        }
        // Laid out at rest (the box grown ModelScale about the model's middle); the pose scales it about the box's middle, then the
        // shadow breathes about its own.
        var scale = BigTypeModelMotion.ModelScale;
        var left = centre.X - (boxWidth * scale / 2);
        var top = centre.Y - (boxHeight * scale / 2);
        floor.Width = rect.Width * scale;
        floor.Height = rect.Height * scale;
        floor.Margin = new Thickness(left + (rect.X * scale), top + (rect.Y * scale), 0, 0);
        var floorMiddle = new Point(left + ((rect.X + (rect.Width / 2)) * scale), top + ((rect.Y + (rect.Height / 2)) * scale));
        var fromBox = new Point(floorMiddle.X - centre.X, floorMiddle.Y - centre.Y);
        // Where the shadow's middle goes when the box is scaled by k about its middle and moved.
        var shiftX = (fromBox.X * k) - fromBox.X + dx;
        var shiftY = (fromBox.Y * k) - fromBox.Y + dy;
        floor.RenderTransformOrigin = RelativePoint.Center;
        floor.RenderTransform = new MatrixTransform(Matrix.CreateScale(k * pose.FloorScale, k * pose.FloorScale) * Matrix.CreateTranslation(shiftX, shiftY));
        floor.Opacity = pose.Opacity * pose.FloorOpacity;
    }

    /// <summary>Where the launch's colour grows from on this Stage (the native feed works it out the same way).</summary>
    private Point TakeoverOrigin()
    {
        if (_modelStage is { IsModelView: true })
            return HandheldTokens.StageModelCentre(CanvasWidthBigType);
        if (_modelStage is { ShowsBigTypeName: true } && _view.FindControl<HandheldBigTypeName>("BtName")?.Fitted is { } fitted)
            return HandheldTokens.NameTakeoverOrigin(fitted.Lines, fitted.Size);
        return new Point(BigTypeLaunchMotion.TakeoverX, BigTypeLaunchMotion.TakeoverY);
    }
}
