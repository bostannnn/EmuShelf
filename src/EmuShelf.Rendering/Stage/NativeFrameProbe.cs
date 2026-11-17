using System.Globalization;
using System.Text;

namespace EmuShelf.Rendering.Stage;

/// <summary>
/// Frame times of a native scene while a named motion runs (R1's measurements, kept by M47 B6 for the owner's on the Thor): the time between one
/// frame's start and the next (what the panel's 120 Hz asks for is 8.33 ms; a missed vsync shows as about 16.7), the
/// render thread's own CPU time applying commands and drawing ("work"), and the wall time it took ("wall", which also holds
/// any wait for a free buffer: the driver blocks the first draw of a frame while the queue is full). A run of frames under one tag ends when the tag
/// changes or the scene stops drawing; <see cref="Frame"/> then hands back one log line for it.
/// </summary>
/// <remarks>Not thread-safe: the render thread calls it once per frame.</remarks>
public sealed class NativeFrameProbe
{
    /// <summary>A gap this long between frames means the scene rested: the run ends there.</summary>
    public const float RestGapMilliseconds = 100f;

    /// <summary>An interval past this missed a 120 Hz vsync (1.5 frames, so jitter does not count).</summary>
    public const float MissedMilliseconds = 1000f / 120f * 1.5f;

    private readonly List<float> _intervals = [];
    private readonly List<float> _cpu = [];
    private readonly List<float> _wall = [];
    private string? _tag;
    private float _runStart;
    private float _last = float.NaN;

    /// <summary>
    /// Records a frame that started at <paramref name="start"/> (ms) and took <paramref name="cpuMilliseconds"/> on the
    /// render thread, under <paramref name="tag"/> (null: no measured motion). Returns the summary of the run this frame
    /// ended, if it ended one.
    /// </summary>
    public string? Frame(float start, float cpuMilliseconds, string? tag, float wallMilliseconds = float.NaN)
    {
        string? ended = null;
        var gap = float.IsNaN(_last) ? float.PositiveInfinity : start - _last;
        if (_tag is not null && (tag != _tag || gap > RestGapMilliseconds))
            ended = Close();
        if (tag is not null)
        {
            if (_tag is null)
            {
                _tag = tag;
                _runStart = start;
            }
            else if (gap <= RestGapMilliseconds)
            {
                _intervals.Add(gap);
            }
            _cpu.Add(cpuMilliseconds);
            if (!float.IsNaN(wallMilliseconds))
                _wall.Add(wallMilliseconds);
        }
        _last = start;
        return ended;
    }

    /// <summary>Ends the current run (the scene stopped drawing) and returns its summary, if there was one.</summary>
    public string? Flush() => _tag is null ? null : Close();

    private string Close()
    {
        var line = Summarise(_tag!, _last - _runStart, _intervals, _cpu, _wall);
        _tag = null;
        _intervals.Clear();
        _cpu.Clear();
        _wall.Clear();
        return line;
    }

    /// <summary>One run as a log line: counts, percentiles and every interval (to 0.1 ms) for offline aggregation.</summary>
    public static string Summarise(string tag, float duration, IReadOnlyList<float> intervals, IReadOnlyList<float> cpu,
        IReadOnlyList<float>? wall = null)
    {
        var c = CultureInfo.InvariantCulture;
        var builder = new StringBuilder();
        builder.Append(c, $"probe {tag}: frames={cpu.Count} ms={duration:0} ");
        if (intervals.Count > 0)
        {
            var sorted = intervals.Order().ToArray();
            builder.Append(c, $"int p50={Percentile(sorted, 0.5f):0.0} p95={Percentile(sorted, 0.95f):0.0} ");
            builder.Append(c, $"p99={Percentile(sorted, 0.99f):0.0} max={sorted[^1]:0.0} missed={intervals.Count(i => i > MissedMilliseconds)} ");
        }
        if (cpu.Count > 0)
        {
            var sorted = cpu.Order().ToArray();
            builder.Append(c, $"work mean={cpu.Average():0.00} p95={Percentile(sorted, 0.95f):0.00} max={sorted[^1]:0.00} ");
        }
        if (wall is { Count: > 0 })
        {
            var sorted = wall.Order().ToArray();
            builder.Append(c, $"wall mean={wall.Average():0.00} p95={Percentile(sorted, 0.95f):0.00} max={sorted[^1]:0.00} ");
        }
        builder.Append("iv=");
        builder.AppendJoin(',', intervals.Select(i => i.ToString("0.0", c)));
        builder.Append(" work=");
        builder.AppendJoin(',', cpu.Select(i => i.ToString("0.00", c)));
        return builder.ToString();
    }

    /// <summary>Nearest-rank percentile of sorted values.</summary>
    public static float Percentile(IReadOnlyList<float> sorted, float p)
    {
        if (sorted.Count == 0)
            return float.NaN;
        var rank = (int)MathF.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }
}
