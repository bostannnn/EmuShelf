using Android.Media;
using Android.Views;
using Android.Widget;
using Avalonia.Android;
using Avalonia.Platform;
using EmuShelf.App.Controls;
using EmuShelf.Core.SecondScreen;

namespace EmuShelf.App.Android.Services;

internal sealed class AndroidCompanionVideo : ICompanionVideoPlayer
{
    private readonly VideoView _view;
    private bool _disposed;
    private bool _paused;
    private readonly PreparedListener _prepared;
    private float _startX, _startY;

    private AndroidCompanionVideo(VideoView view, Action<string> failed, Action<double, double> swipe)
    {
        _view = view;
        _prepared = new PreparedListener(player =>
        {
            // Mute unconditionally: this is the only place volume is ever set, so it must never be
            // skipped — otherwise a later Start() could play the trailer at full device volume on the
            // second screen. Only the auto-start is gated on the surface still being live.
            player?.SetVolume(0, 0);
            if (_disposed || !view.IsShown) return;
            if (!_paused) view.Start();
        });
        view.SetOnPreparedListener(_prepared);
        view.Error += (_, e) =>
        {
            e.Handled = true;
            if (!_disposed) failed("This video could not be played.");
        };
        view.Completion += (_, _) => { if (!_disposed) { _paused = true; view.SeekTo(0); } };
        view.Touch += (_, e) =>
        {
            if (e.Event is not { } motion) return;
            if (motion.Action == MotionEventActions.Down) { _startX = motion.GetX(); _startY = motion.GetY(); }
            if (motion.Action == MotionEventActions.Up)
            {
                var density = view.Resources?.DisplayMetrics?.Density ?? 1;
                var x = (motion.GetX() - _startX) / density;
                var y = (motion.GetY() - _startY) / density;
                if ((Math.Abs(x) >= 48 && Math.Abs(x) > Math.Abs(y) * 1.5) ||
                    (y >= 64 && y > Math.Abs(x) * 1.5)) swipe(x, y);
                else
                {
                    // Remember a pause even while the decoder is still preparing the first frame.
                    _paused = !_paused;
                    if (_paused) view.Pause(); else view.Start();
                }
            }
            e.Handled = true;
        };
    }

    public static EmbeddedCompanionVideo Create(IPlatformHandle parent, Action<string> failed, Action<double, double> swipe)
    {
        var context = (parent as AndroidViewControlHandle)?.View.Context ?? global::Android.App.Application.Context;
        var video = new VideoView(context);
        var container = new FrameLayout(context);
        container.AddView(video, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent, GravityFlags.Center));
        return new(new AndroidViewControlHandle(container), new AndroidCompanionVideo(video, failed, swipe));
    }

    private sealed class PreparedListener(Action<MediaPlayer?> prepared) : Java.Lang.Object, MediaPlayer.IOnPreparedListener
    {
        public void OnPrepared(MediaPlayer? player) => prepared(player);
    }

    public void Play(string localPath)
    {
        if (_disposed) return;
        _view.StopPlayback();
        _paused = false;
        _view.SetVideoPath(localPath);
    }
    public void Stop() { if (!_disposed) _view.StopPlayback(); }
    public void Dispose() { if (_disposed) return; Stop(); _disposed = true; _view.SetOnPreparedListener(null); _prepared.Dispose(); _view.Dispose(); }
}
