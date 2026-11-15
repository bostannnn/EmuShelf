using System;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using EmuShelf.Rendering;
using EmuShelf.Rendering.Stage;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

// The model view on the native Stage (M47 B12): the game's medium (or its flat cover, pictured from the Avalonia Stage's BtModelCover)
// in the middle of the Stage, told once per change (it rose, it flipped into the next game, it left); the native Stage poses it every
// frame from BigTypeModelMotion. The stick's turn is passed on as it changes. The left veil lies behind the two lines, not a name.
internal sealed partial class HandheldNativeStageFeed
{
    private bool _modelSent;
    private long _modelGameSent = -1;

    /// <summary>Sends the model view's medium, or its leaving; true when it shows (the corner beside the name is then empty).</summary>
    private bool SendModel(HandheldStageViewModel stage)
    {
        var showing = stage.IsModelView && (stage.HasModelMedium || stage.HasModelCover);
        var beside = HandheldTokens.StageMediumCentre(CanvasWidth);
        var centre = HandheldTokens.StageModelCentre(CanvasWidth);
        StageModelCommand Command(StageMedium? medium, StageImage? cover, BigTypeModelChange change) =>
            new(medium, cover, change, (float)beside.X, (float)beside.Y, (float)centre.X, (float)centre.Y,
                (float)HandheldTokens.StageMediumWidth, (float)HandheldTokens.StageMediumHeight);
        if (!showing)
        {
            if (_modelSent)
            {
                _modelSent = false;
                _modelGameSent = -1;
                // After this flush's name: held still the medium goes at once and the name is there at once, so the two must reach the
                // Stage together (posted first, the Stage drew a frame with neither on the Thor).
                var leave = Command(null, null, BigTypeModelChange.Leave);
                Dispatcher.UIThread.Post(() => _host.Post(leave), DispatcherPriority.Loaded);
            }
            return false;
        }
        var change = !_modelSent ? BigTypeModelChange.Rise
            : stage.GameId == _modelGameSent ? BigTypeModelChange.None
            : _shell?.LastMoveDirection is HandheldDirection.Left or HandheldDirection.Up ? BigTypeModelChange.FlipBack
            : BigTypeModelChange.FlipForward;
        _modelSent = true;
        _modelGameSent = stage.GameId;
        if (stage.HasModelMedium && stage.Medium is { } shell)
        {
            var accent = MediaShellRenderer.ToLinear(stage.MediumAccent.R / 255f, stage.MediumAccent.G / 255f, stage.MediumAccent.B / 255f);
            var frame = HandheldTokens.StageModelFrame(CanvasWidth);
            var medium = new StageMedium(shell, accent, (float)frame.X, (float)frame.Y, (float)frame.Width, (float)frame.Height);
            // The new medium flips in only once its cover is ready, so it never shows the last game's.
            SendCover(stage.Art, then: Command(medium, null, change));
        }
        else
        {
            _host.Post(Command(null, HandheldStageShots.Shot(Find("BtModelCover"), _canvas), change));
        }
        return true;
    }

    /// <summary>The model view's properties the native Stage follows; true when the change was one of them.</summary>
    private bool OnModelProperty(HandheldStageViewModel stage, string? property)
    {
        switch (property)
        {
            case nameof(HandheldStageViewModel.IsModelView):
                // The medium rises or drops, the name leaves or lands, the veil moves to the lines or back.
                Schedule(centre: true, pop: false, chrome: true);
                return true;
            case nameof(HandheldStageViewModel.ModelHoldVersion):
                var (pitch, yaw, held) = stage.ModelHold;
                _host.Post(new StageModelHoldCommand(pitch, yaw, held));
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Where the left veil stops being flat in the model view: past the widest of the label and the two lines (there is no name to lie
    /// behind); null otherwise (the name's box decides).
    /// </summary>
    private float? ModelVeilFlatEnd(HandheldStageViewModel stage)
    {
        if (!stage.IsModelView)
            return null;
        return HandheldBigTypeVeils.LinesFlatEnd(_view, _canvas);
    }

    /// <summary>The launch's circle grows from the model view's medium, or (no case beside the name any more) from the name's middle.</summary>
    private Vector2? TakeoverOrigin()
    {
        if (_stage is not { } stage || !HandheldTokens.IsBigType)
            return null;
        if (stage.IsModelView)
        {
            var centre = HandheldTokens.StageModelCentre(CanvasWidth);
            return new Vector2((float)centre.X, (float)centre.Y);
        }
        if (stage.ShowsBigTypeName && _bigTypeName.Fitted is { } fitted)
        {
            var origin = HandheldTokens.NameTakeoverOrigin(fitted.Lines, fitted.Size);
            return new Vector2((float)origin.X, (float)origin.Y);
        }
        return null;
    }
}
