namespace EmuShelf.Core.Handheld;

/// <summary>Handheld mode's five interface sounds (M45 H6d). Original tones, synthesised by <see cref="HandheldSoundSynth"/>.</summary>
public enum HandheldSound
{
    /// <summary>The selector moved (a tile, a row, a key, a page).</summary>
    Move,

    /// <summary>Something opened: a folder, a Title page, a sheet, the menu, a panel, Settings.</summary>
    Open,

    /// <summary>A folder or a Title page closed (the reverse of the zoom that opened it).</summary>
    Close,

    /// <summary>A press did something without opening anything: Play, a row chosen, a switch flipped, a key typed.</summary>
    Confirm,

    /// <summary>B stepped back out of a sheet, the menu, a panel or the dock.</summary>
    Back,
}

/// <summary>Plays Handheld mode's interface sounds. The Android head plays them; elsewhere they are silent.</summary>
public interface IHandheldSoundPlayer
{
    void Play(HandheldSound sound);
}

public sealed class SilentHandheldSoundPlayer : IHandheldSoundPlayer
{
    public static SilentHandheldSoundPlayer Instance { get; } = new();

    public void Play(HandheldSound sound)
    {
    }
}

/// <summary>
/// Synthesises the interface sounds as 16-bit mono PCM: one or two short tones each (a sine or a triangle, an 8 ms rise
/// and an exponential fall), the recipe the Handheld mockup played through Web Audio. Self-made, so there is nothing to
/// license and nothing borrowed from a console (HANDHELD-MODE.md: no Nintendo sounds).
/// </summary>
public static class HandheldSoundSynth
{
    public const int SampleRate = 44100;

    private const double Attack = 0.008;

    // How far the fall goes before the tone stops (−80 dB), as the mockup's exponentialRampToValueAtTime(.0001).
    private const double Floor = 0.0001;

    private readonly record struct Tone(double Frequency, double Start, double Duration, bool Triangle, double Volume);

    /// <summary>The tones of each sound: a high tick to move, a rising fifth to open, a falling one to close.</summary>
    private static Tone[] TonesOf(HandheldSound sound) => sound switch
    {
        HandheldSound.Move => [new(1175, 0, 0.045, true, 0.16)],
        HandheldSound.Open => [new(660, 0, 0.07, false, 0.22), new(990, 0.06, 0.10, false, 0.22)],
        HandheldSound.Close => [new(880, 0, 0.07, false, 0.22), new(587, 0.06, 0.10, false, 0.22)],
        HandheldSound.Confirm => [new(1046, 0, 0.05, true, 0.2), new(1568, 0.04, 0.09, true, 0.2)],
        HandheldSound.Back => [new(587, 0, 0.05, true, 0.2), new(440, 0.045, 0.08, true, 0.2)],
        _ => [],
    };

    /// <summary>The sound as signed 16-bit samples at <see cref="SampleRate"/>, mono.</summary>
    public static short[] Render(HandheldSound sound)
    {
        var tones = TonesOf(sound);
        var seconds = tones.Max(tone => tone.Start + tone.Duration) + 0.01;
        var samples = new double[(int)Math.Ceiling(seconds * SampleRate)];
        foreach (var tone in tones)
        {
            var first = (int)(tone.Start * SampleRate);
            var count = (int)(tone.Duration * SampleRate);
            var fall = Math.Log(Floor) / Math.Max(1e-6, tone.Duration - Attack);
            for (var i = 0; i < count && first + i < samples.Length; i++)
            {
                var t = (double)i / SampleRate;
                var envelope = t < Attack ? t / Attack : Math.Exp(fall * (t - Attack));
                var phase = (tone.Frequency * t) % 1;
                var wave = tone.Triangle ? 1 - (4 * Math.Abs(phase - 0.5)) : Math.Sin(2 * Math.PI * phase);
                samples[first + i] += wave * envelope * tone.Volume;
            }
        }
        var pcm = new short[samples.Length];
        for (var i = 0; i < samples.Length; i++)
            pcm[i] = (short)Math.Round(Math.Clamp(samples[i], -1, 1) * short.MaxValue);
        return pcm;
    }
}
