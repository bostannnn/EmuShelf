using Android.Media;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// Plays Handheld mode's interface sounds (M45 H6d): each sound is synthesised once (<see cref="HandheldSoundSynth"/>) into
/// a static <see cref="AudioTrack"/>, which a press replays from the start. Games' usage, so the media volume (the one the
/// volume keys move in an app) sets how loud they are. Played off the UI thread: a press never waits on the audio service.
/// </summary>
internal sealed class AndroidHandheldSoundPlayer(IAppLogger logger) : IHandheldSoundPlayer, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<HandheldSound, AudioTrack> _tracks = [];
    private bool _failed;
    private bool _disposed;

    public void Play(HandheldSound sound)
    {
        if (_failed || _disposed)
            return;
        Task.Run(() =>
        {
            lock (_gate)
            {
                if (_failed || _disposed)
                    return;
                try
                {
                    var track = TrackFor(sound);
                    // A static track plays its buffer once; to play it again it is stopped and rewound.
                    if (track.PlayState != PlayState.Stopped)
                        track.Stop();
                    track.ReloadStaticData();
                    track.Play();
                }
                catch (Exception ex)
                {
                    // No audio output (or a device that refuses static tracks): stay silent from now on, once logged.
                    _failed = true;
                    logger.Warning("Handheld sounds are off: the interface sound could not play.", ex);
                }
            }
        });
    }

    private AudioTrack TrackFor(HandheldSound sound)
    {
        if (_tracks.TryGetValue(sound, out var track))
            return track;
        var samples = HandheldSoundSynth.Render(sound);
        var attributes = new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.Game)!
            .SetContentType(AudioContentType.Sonification)!
            .Build()!;
        var format = new AudioFormat.Builder()
            .SetEncoding(Encoding.Pcm16bit)!
            .SetSampleRate(HandheldSoundSynth.SampleRate)!
            .SetChannelMask(ChannelOut.Mono)!
            .Build()!;
        track = new AudioTrack.Builder()
            .SetAudioAttributes(attributes)!
            .SetAudioFormat(format)!
            .SetBufferSizeInBytes(samples.Length * sizeof(short))!
            .SetTransferMode(AudioTrackMode.Static)!
            .Build()!;
        track.Write(samples, 0, samples.Length);
        _tracks[sound] = track;
        return track;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var track in _tracks.Values)
            {
                track.Release();
                track.Dispose();
            }
            _tracks.Clear();
        }
    }
}
