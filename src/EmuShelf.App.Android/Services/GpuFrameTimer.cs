using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using GL = Silk.NET.OpenGL.GL;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// A native scene's GPU time per frame (R1's measurement, kept by M47 B6; only while <see cref="FrameProbeProperty"/> is set): an <c>EXT_disjoint_timer_query</c> around each
/// frame's commands and draw, read back a few frames later without stalling, filed under the frame's probe tag. A frame
/// whose result the GPU marks disjoint (a clock change mid-frame) is dropped.
/// </summary>
internal sealed class GpuFrameTimer
{
    private const int TimeElapsed = 0x88BF; // GL_TIME_ELAPSED_EXT
    private const int GpuDisjoint = 0x8FBB; // GL_GPU_DISJOINT_EXT
    private const int QueryResult = 0x8866; // GL_QUERY_RESULT
    private const int QueryResultAvailable = 0x8867;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GetQueryObjectUi64(uint id, int name, out ulong value);

    private readonly GL _gl;
    private readonly GetQueryObjectUi64? _getResult;
    private readonly Queue<uint> _free = new();
    private readonly Queue<(uint Query, string? Tag)> _pending = new();
    private readonly Dictionary<string, List<float>> _collected = new(StringComparer.Ordinal);
    private uint _open;

    private GpuFrameTimer(GL gl, GetQueryObjectUi64 getResult)
    {
        _gl = gl;
        _getResult = getResult;
    }

    /// <summary>A timer for this context, or null where the extension is missing.</summary>
    public static GpuFrameTimer? Create(GL gl, Func<string, IntPtr> getProcAddress)
    {
        try
        {
            var extensions = gl.GetStringS(Silk.NET.OpenGL.StringName.Extensions) ?? string.Empty;
            if (!extensions.Contains("GL_EXT_disjoint_timer_query", StringComparison.Ordinal))
                return null;
            var address = getProcAddress("glGetQueryObjectui64vEXT");
            return address == IntPtr.Zero ? null : new GpuFrameTimer(gl, Marshal.GetDelegateForFunctionPointer<GetQueryObjectUi64>(address));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Begin()
    {
        if (_open != 0)
            return;
        var query = _free.Count > 0 ? _free.Dequeue() : _gl.GenQuery();
        _gl.BeginQuery((Silk.NET.OpenGL.QueryTarget)TimeElapsed, query);
        _open = query;
    }

    /// <summary>Closes the frame's query under <paramref name="tag"/> (null: not kept) and files any results that are in.</summary>
    public void End(string? tag)
    {
        if (_open == 0)
            return;
        // A tiler renders the frame's pass when it is flushed (otherwise at the swap, after the query closed): flush it inside.
        _gl.Flush();
        _gl.EndQuery((Silk.NET.OpenGL.QueryTarget)TimeElapsed);
        _pending.Enqueue((_open, tag));
        _open = 0;
        Collect();
    }

    private void Collect()
    {
        _gl.GetInteger((Silk.NET.OpenGL.GetPName)GpuDisjoint, out int disjoint);
        while (_pending.Count > 0)
        {
            var (query, tag) = _pending.Peek();
            _gl.GetQueryObject(query, (Silk.NET.OpenGL.QueryObjectParameterName)QueryResultAvailable, out uint available);
            if (available == 0)
                break;
            _pending.Dequeue();
            _getResult!(query, QueryResult, out var nanoseconds);
            _free.Enqueue(query);
            if (tag is null || disjoint != 0)
                continue;
            if (!_collected.TryGetValue(tag, out var list))
                _collected[tag] = list = [];
            list.Add(nanoseconds / 1_000_000f);
        }
    }

    /// <summary>One line per tag with every GPU time read back since the last drain ("gv=" ms, 0.01).</summary>
    public IEnumerable<string> Drain()
    {
        foreach (var (tag, values) in _collected)
        {
            if (values.Count == 0)
                continue;
            yield return $"gpu {tag}: n={values.Count} gv=" +
                string.Join(',', values.Select(v => v.ToString("0.00", CultureInfo.InvariantCulture)));
            values.Clear();
        }
    }
}
