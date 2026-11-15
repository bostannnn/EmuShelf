using System.Numerics;
using EmuShelf.Rendering.Gl;
using Silk.NET.OpenGL;

namespace EmuShelf.Rendering.Stage.Backdrops;

/// <summary>
/// One running backdrop: its spec, its clock, and its own low-resolution targets (a crossfade has two running
/// at once, and an LCD's ghosting, a phosphor's trail or frame feedback needs the backdrop's own previous frame).
/// </summary>
public sealed class BackdropInstance
{
    internal BackdropInstance(BackdropSpec spec, float startMilliseconds, float clockOffsetSeconds, BackdropLook look = default)
    {
        Spec = spec;
        StartMilliseconds = startMilliseconds;
        ClockOffsetSeconds = clockOffsetSeconds;
        Look = look;
        _groundFrom = _groundTo = look.Ground;
        Width = spec.Width;
        Height = spec.Height;
        Smooth = spec.Output.Smooth;
        if (look.BigType && BigTypeBackdrops.Find(spec.Id) is { } entry)
        {
            BigType = entry;
            (Width, Height) = BigTypeBackdrops.DrawSize(entry, spec);
            Smooth = entry.Upscale == BackdropUpscale.Sharp;
            if (entry.Upscale == BackdropUpscale.Crisp)
                Zoom = BigTypeBackdrops.CrispShown(Width, Height);
        }
    }

    public BackdropSpec Spec { get; }

    /// <summary>The look it is drawn for: today's (the catalog's size, no recolour) or Big Type's (M47 B13).</summary>
    public BackdropLook Look { get; }

    /// <summary>Big Type's entry for it, when it is drawn for Big Type and Big Type shows it; null otherwise.</summary>
    public BigTypeBackdrop? BigType { get; }

    /// <summary>The size its frame is drawn at: the console's own (the catalog's), or Big Type's draw size.</summary>
    public int Width { get; }

    public int Height { get; }

    /// <summary>Draw pixels per prototype pixel across (the shaders' <c>uPixel</c>): 1 unless a sharp Big Type frame is drawn larger.</summary>
    public float Pixel => Width / (float)Spec.Width;

    /// <summary>Linear upscaling onto the Stage (the console's finish; under Big Type, sharp frames only).</summary>
    internal bool Smooth { get; }

    /// <summary>The share of the frame shown across each axis (a crisp Big Type frame at a whole scale larger than the Stage).</summary>
    internal System.Numerics.Vector2 Zoom { get; } = System.Numerics.Vector2.One;

    /// <summary>How far it is recoloured toward <see cref="BackdropLook.Ground"/> (0: as it is).</summary>
    internal float Pull => BigType?.Pull ?? 0f;

    /// <summary>
    /// The colour a backdrop drawn from a colour is drawn in (M47 B13, the shaders' <c>uColour</c>): <see cref="BackdropLook.Ground"/> at
    /// the start, then eased to each new one over the Launcher's flood (<see cref="SetGround"/>), so one running backdrop changes colour.
    /// </summary>
    public System.Numerics.Vector3 GroundAt(float now)
    {
        if (now >= _groundSince + EmuShelf.Rendering.Stage.BigType.BigTypeMotion.FloodMilliseconds)
            return _groundTo;
        var p = EmuShelf.Rendering.Stage.BigType.BigTypeMotion.FloodEase(Math.Clamp((now - _groundSince) / EmuShelf.Rendering.Stage.BigType.BigTypeMotion.FloodMilliseconds, 0f, 1f));
        return System.Numerics.Vector3.Lerp(_groundFrom, _groundTo, p);
    }

    /// <summary>A new colour for a running backdrop: eased from the one on screen, or at once when the device holds still.</summary>
    public void SetGround(System.Numerics.Vector3 ground, float now, bool instantly = false)
    {
        if (ground == _groundTo)
            return;
        _groundFrom = instantly ? ground : GroundAt(now);
        _groundTo = ground;
        _groundSince = now;
    }

    private System.Numerics.Vector3 _groundFrom;
    private System.Numerics.Vector3 _groundTo;
    private float _groundSince = float.NegativeInfinity;

    /// <summary>The bloom's threshold: the console's finish, or Big Type's own where its entry sets one.</summary>
    internal float BloomThreshold => BigType?.BloomThreshold ?? Spec.Output.BloomThreshold;

    public string Id => Spec.Id;

    /// <summary>When it started, on the Stage's clock (milliseconds).</summary>
    public float StartMilliseconds { get; }

    internal float ClockOffsetSeconds { get; }

    /// <summary>Each pass's program, resolved once at the start (no lookups per step).</summary>
    internal GlProgram[] Programs { get; set; } = [];

    internal GlProgram? FinishProgram { get; set; }

    /// <summary>The output pass's program: today's, or Big Type's (zoom, interlace pitch, recolour).</summary>
    internal GlProgram? OutputProgram { get; set; }

    /// <summary>The last 30 Hz step drawn; -1 before the first.</summary>
    internal long Step { get; set; } = -1;

    /// <summary>The frame the passes draw into; two with feedback (this frame and the last one).</summary>
    internal Target[]? Scenes { get; set; }

    internal int SceneIndex { get; set; }

    internal bool HasPreviousScene { get; set; }

    internal Target? Scene => Scenes?[SceneIndex];

    internal Target? Finished { get; set; }

    internal Target[]? History { get; set; }

    internal int HistoryIndex { get; set; }

    internal bool HasHistory { get; set; }

    internal Target? BloomNear { get; set; }

    internal Target? BloomFar { get; set; }

    /// <summary>A sharp frame averaged down to its console's size before the bloom, so the glow keeps its size and softness.</summary>
    internal Target? BloomReduce { get; set; }

    /// <summary>What the output pass shows: the finished frame, its ghosted / persisted copy, or the scene.</summary>
    internal Target? Shown => History is { } history ? history[HistoryIndex] : Finished ?? Scene;
}

/// <summary>A colour texture with its framebuffer (and a depth buffer when a pass needs one).</summary>
internal sealed record Target(uint Framebuffer, uint Texture, uint Depth, uint Width, uint Height);

/// <summary>
/// Draws the Stage's animated backdrops (M45 H7) on the native Stage's render thread. A backdrop's passes
/// render into a small frame at its console's resolution, at most once per 30 Hz step; the output pass then
/// scales that frame onto the Stage every Stage frame with the console's finish (bit depth, dither, LCD/CRT
/// grid, composite blur, bloom, interlace), so a pop or a crossfade at the panel's rate costs one quad, not a
/// re-render. Programs are compiled once per backdrop and kept; a backdrop whose shaders do not compile is
/// reported as unavailable and the Stage keeps its pictured era motif.
/// </summary>
public sealed class BackdropRenderer : IDisposable
{
    private const EnableCap ProgramPointSize = (EnableCap)0x8642;

    private readonly GL _gl;
    private readonly GlslDialect _dialect;
    private readonly int _major;
    private readonly int _minor;
    private readonly uint _vao;
    private readonly Dictionary<string, GlProgram?> _programs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failed = new(StringComparer.Ordinal);
    private GlProgram? _history;
    private GlProgram? _bright;
    private GlProgram? _output;
    private GlProgram? _bigTypeOutput;

    // M47 B13: the defines a Big Type backdrop's programs are compiled with (backdrop-common.glsl, backdrop-output.frag).
    private const string SharpDefine = "BACKDROP_SHARP 1";
    private const string BigTypeDefine = "BACKDROP_BIGTYPE 1";

    public BackdropRenderer(GL gl, GlslDialect dialect, int majorVersion, int minorVersion)
    {
        _gl = gl;
        _dialect = dialect;
        _major = majorVersion;
        _minor = minorVersion;
        _vao = gl.GenVertexArray();
    }

    /// <summary>
    /// Starts a backdrop, or returns null when it is unknown or its shaders do not compile on this device (the
    /// failure is logged once and remembered).
    /// </summary>
    /// <param name="clockOffsetSeconds">Where the motion starts; by default a little way in, varied per start.</param>
    /// <param name="look">Today's look (the default) or Big Type's, which draws the animations it shows at its own size and recolours
    /// them toward the look's ground (M47 B13).</param>
    public BackdropInstance? Start(string id, float now, float? clockOffsetSeconds = null, BackdropLook look = default)
    {
        if (BackdropCatalog.Find(id) is not { } spec || _failed.Contains(id))
            return null;
        // Offset first: the instance's size (and so whether its programs are the sharp variant) does not depend on it.
        var offset = clockOffsetSeconds ?? 4f + ((uint)HashCode.Combine(id, (int)now) % 1000) / 100f;
        var instance = new BackdropInstance(spec, now, offset, look);
        var sharp = instance.Pixel > 1f;
        string[] defines = sharp ? [SharpDefine] : [];
        try
        {
            instance.OutputProgram = look.BigType
                ? _bigTypeOutput ??= Compile("fullscreen.vert", "backdrop-output.frag", [BigTypeDefine])
                : _output ??= Compile("fullscreen.vert", "backdrop-output.frag");
            if (spec.Output.Ghost > 0 || spec.Output.Persist > 0)
                _history ??= Compile("fullscreen.vert", "backdrop-history.frag");
            if (spec.Output.BloomThreshold > 0)
                _bright ??= Compile("fullscreen.vert", "backdrop-bright.frag");
            instance.Programs = spec.Passes.Select(pass => Program(pass.Vertex, pass.Fragment, defines, spec.Include)!).ToArray();
            instance.FinishProgram = spec.Finish is { } finish ? Program("fullscreen.vert", finish, defines, spec.Include) : null;
        }
        catch (Exception ex)
        {
            _failed.Add(id);
            // Standard error reaches logcat on Android (tag DOTNET), where the Thor check looks for it.
            Console.Error.WriteLine($"[EmuShelf] Backdrop '{id}' is unavailable; the era motif stays: {ex.Message}");
            return null;
        }
        // Each start begins a little way into the motion (the offset above), so a backdrop never opens on the same frame.
        return instance;
    }

    /// <summary>Renders the backdrop's next 30 Hz step if the clock has reached it. Leaves the framebuffer unbound.</summary>
    public void Update(BackdropInstance instance, float now)
    {
        var step = (long)MathF.Floor(Math.Max(0f, now - instance.StartMilliseconds) / BackdropCatalog.StepMilliseconds);
        if (step == instance.Step)
            return;
        var elapsed = instance.Step < 0 ? 1 : Math.Min(step - instance.Step, 3);
        instance.Step = step;
        var spec = instance.Spec;
        var seconds = ((step * BackdropCatalog.StepMilliseconds / 1000f) + instance.ClockOffsetSeconds) % BackdropCatalog.ClockWrapSeconds;
        EnsureTargets(instance);
        var resolution = new Vector2(spec.Width, spec.Height);

        // Frame feedback draws over the previous frame: alternate between two scene targets.
        Target? previousScene = null;
        if (spec.Feedback)
        {
            previousScene = instance.Scene;
            instance.SceneIndex ^= 1;
        }
        var scene = instance.Scene!;
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, scene.Framebuffer);
        _gl.Viewport(0, 0, scene.Width, scene.Height);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.ClearColor(0, 0, 0, 1);
        _gl.ClearDepth(1);
        _gl.DepthMask(true);
        _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
        _gl.BindVertexArray(_vao);
        if (_dialect == GlslDialect.Desktop)
            _gl.Enable(ProgramPointSize);
        if (previousScene is not null)
            Bind(0, previousScene.Texture);
        for (var index = 0; index < spec.Passes.Count; index++)
        {
            var pass = spec.Passes[index];
            var program = instance.Programs[index];
            program.Use();
            program.Set("uTime", seconds);
            program.Set("uRes", resolution);
            program.Set("uFrame", (float)(step % 1000));
            program.Set("uSteps", (float)elapsed);
            program.Set("uPrevious", 0);
            program.Set("uHasPrevious", instance.HasPreviousScene ? 1f : 0f);
            if (instance.Pixel > 1f)
            {
                program.Set("uPixel", instance.Pixel);
                program.Set("uPointRound", pass.Primitive == BackdropPrimitive.Points ? 1f : 0f);
            }
            if (instance.Look.BigType)
                program.Set("uColour", instance.GroundAt(now));
            if (pass.DepthTest)
            {
                _gl.Enable(EnableCap.DepthTest);
                _gl.DepthFunc(DepthFunction.Less);
                _gl.DepthMask(pass.DepthWrite);
            }
            else
            {
                _gl.Disable(EnableCap.DepthTest);
            }
            switch (pass.Blend)
            {
                case BackdropBlend.Additive:
                    _gl.Enable(EnableCap.Blend);
                    _gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);
                    break;
                case BackdropBlend.Alpha:
                    _gl.Enable(EnableCap.Blend);
                    _gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.Zero, BlendingFactor.One);
                    break;
                default:
                    _gl.Disable(EnableCap.Blend);
                    break;
            }
            _gl.Disable(EnableCap.CullFace);
            var primitive = pass.Primitive switch
            {
                BackdropPrimitive.Lines => PrimitiveType.Lines,
                BackdropPrimitive.Points => PrimitiveType.Points,
                _ => PrimitiveType.Triangles,
            };
            _gl.DrawArrays(primitive, pass.First, (uint)pass.VertexCount);
        }
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.CullFace);
        _gl.DepthMask(true);
        instance.HasPreviousScene = true;

        // A finishing pass over the frame (a reflection, say) that must not feed back into it.
        if (instance.FinishProgram is { } finishing && instance.Finished is { } finished)
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, finished.Framebuffer);
            finishing.Use();
            finishing.Set("uTime", seconds);
            finishing.Set("uRes", resolution);
            finishing.Set("uScene", 0);
            if (instance.Pixel > 1f)
                finishing.Set("uPixel", instance.Pixel);
            if (instance.Look.BigType)
                finishing.Set("uColour", instance.GroundAt(now));
            Bind(0, scene.Texture);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }

        // An LCD's ghosting or a phosphor's trail: blend the new frame with the backdrop's previous one.
        if (instance.History is { } history)
        {
            var source = instance.Finished ?? scene;
            var previous = history[instance.HistoryIndex];
            instance.HistoryIndex ^= 1;
            var next = history[instance.HistoryIndex];
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, next.Framebuffer);
            _gl.Viewport(0, 0, next.Width, next.Height);
            _history!.Use();
            Bind(0, source.Texture);
            Bind(1, previous.Texture);
            _history.Set("uScene", 0);
            _history.Set("uPrevious", 1);
            _history.Set("uHasPrevious", instance.HasHistory ? 1f : 0f);
            _history.Set("uGhost", spec.Output.Ghost);
            _history.Set("uPersist", spec.Output.Persist);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
            instance.HasHistory = true;
        }

        // Bloom: the bright parts at a quarter of the frame, then a twelfth (the motion lab's two glow layers).
        if (instance.BloomNear is { } near && instance.BloomFar is { } far)
        {
            var shown = instance.Shown!;
            _bright!.Use();
            if (instance.BloomReduce is { } reduce)
            {
                // A sharp frame is first averaged down to its console's size, so the glow is the one the console's frame gives.
                _bright.Set("uSource", 0);
                _bright.Set("uThreshold", -1f);
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, reduce.Framebuffer);
                _gl.Viewport(0, 0, reduce.Width, reduce.Height);
                Bind(0, shown.Texture);
                _bright.Set("uSourceSize", new Vector2(shown.Width, shown.Height));
                _bright.Set("uTargetSize", new Vector2(reduce.Width, reduce.Height));
                _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
                shown = reduce;
            }
            _bright.Set("uSource", 0);
            _bright.Set("uThreshold", instance.BloomThreshold);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, near.Framebuffer);
            _gl.Viewport(0, 0, near.Width, near.Height);
            Bind(0, shown.Texture);
            _bright.Set("uSourceSize", new Vector2(shown.Width, shown.Height));
            _bright.Set("uTargetSize", new Vector2(near.Width, near.Height));
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, far.Framebuffer);
            _gl.Viewport(0, 0, far.Width, far.Height);
            Bind(0, near.Texture);
            _bright.Set("uSourceSize", new Vector2(near.Width, near.Height));
            _bright.Set("uTargetSize", new Vector2(far.Width, far.Height));
            _bright.Set("uThreshold", -1f); // a plain downsample for the far glow
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }

        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    /// <summary>
    /// Draws the backdrop over the whole bound framebuffer at <paramref name="opacity"/> (premultiplied, for the
    /// Stage's ONE / ONE_MINUS_SRC_ALPHA blending). Changes the bound program, vertex array and textures.
    /// </summary>
    /// <param name="crop">The share of the frame's width to cut off (half each side) on a narrower screen.</param>
    /// <param name="clip">Big Type's wipe (M47 B6): a circle to show it only inside (surface pixels from the bottom-left, the radius, on
    /// when W is 1). Default: everywhere.</param>
    public void Present(BackdropInstance instance, float opacity, float crop = 0f, Vector4 clip = default)
    {
        if (instance.Shown is not { } shown || opacity <= 0f)
            return;
        var output = instance.Spec.Output;
        var program = instance.OutputProgram!;
        program.Use();
        _gl.BindVertexArray(_vao);
        Bind(0, shown.Texture);
        program.Set("uSource", 0);
        program.Set("uRes", new Vector2(shown.Width, shown.Height));
        program.Set("uOpacity", opacity);
        program.Set("uCrop", Math.Clamp(crop, 0f, 0.9f));
        program.Set("uClip", clip);
        program.Set("uSmooth", instance.Smooth ? 1f : 0f);
        if (instance.Look.BigType)
        {
            program.Set("uZoom", instance.Zoom);
            program.Set("uRowScale", instance.Height / (float)instance.Spec.Height);
            program.Set("uPull", instance.Pull);
            program.Set("uGround", instance.Look.Ground);
            program.Set("uBloomWide", output.BloomWide);
        }

        program.Set("uLevels", output.Bits > 0 ? (float)((1 << output.Bits) - 1) : 0f);
        program.Set("uDither", output.Dither);
        program.Set("uGrid", (float)output.Grid);
        program.Set("uComposite", output.Composite ? 1f : 0f);
        program.Set("uInterlace", output.Interlace);
        program.Set("uFrame", (float)(instance.Step & 1));
        var bloom = instance.BloomNear is not null ? output.BloomAmount : 0f;
        program.Set("uBloom", bloom);
        // Without bloom the glow samplers still need a texture bound; the frame itself stands in, unread.
        Bind(1, bloom > 0 ? instance.BloomNear!.Texture : shown.Texture);
        Bind(2, bloom > 0 ? instance.BloomFar!.Texture : shown.Texture);
        program.Set("uBloomNear", 1);
        program.Set("uBloomFar", 2);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.ActiveTexture(TextureUnit.Texture0);
    }

    /// <summary>Frees a backdrop's targets (its programs stay compiled for the next time it shows).</summary>
    public void Stop(BackdropInstance? instance)
    {
        if (instance is null)
            return;
        foreach (var target in instance.Scenes ?? [])
            Delete(target);
        foreach (var target in instance.History ?? [])
            Delete(target);
        Delete(instance.Finished);
        Delete(instance.BloomNear);
        Delete(instance.BloomFar);
        Delete(instance.BloomReduce);
        instance.Scenes = instance.History = null;
        instance.Finished = instance.BloomNear = instance.BloomFar = instance.BloomReduce = null;
    }

    public void Dispose()
    {
        foreach (var program in _programs.Values)
            program?.Dispose();
        _programs.Clear();
        _history?.Dispose();
        _bright?.Dispose();
        _output?.Dispose();
        _bigTypeOutput?.Dispose();
        _gl.DeleteVertexArray(_vao);
    }

    private GlProgram? Program(string vertex, string fragment, string[] defines, string? include = null)
    {
        var key = vertex + "|" + fragment + "|" + string.Join(",", defines) + "|" + include;
        if (!_programs.TryGetValue(key, out var program))
        {
            program = Compile(vertex, fragment, defines, include);
            _programs[key] = program;
        }
        return program;
    }

    private GlProgram Compile(string vertex, string fragment, string[]? defines = null, string? include = null)
    {
        // Today's backdrops include backdrop-common alone, exactly as before; a backdrop of Big Type's may add its own file (M47 B13).
        string[] includes = include is null ? ["backdrop-common"] : ["backdrop-common", include];
        return GlProgram.Create(
            _gl,
            ShaderLibrary.LoadWithDefines(vertex, _dialect, _major, _minor, defines ?? [], includes),
            ShaderLibrary.LoadWithDefines(fragment, _dialect, _major, _minor, defines ?? [], includes),
            defines is { Length: > 0 } ? fragment + " (" + string.Join(", ", defines) + ")" : fragment);
    }

    private void EnsureTargets(BackdropInstance instance)
    {
        if (instance.Scenes is not null)
            return;
        var spec = instance.Spec;
        var width = (uint)instance.Width;
        var height = (uint)instance.Height;
        var depth = spec.Passes.Any(pass => pass.DepthTest);
        instance.Scenes = spec.Feedback
            ? [CreateTarget(width, height, depth), CreateTarget(width, height, depth)]
            : [CreateTarget(width, height, depth)];
        if (spec.Finish is not null)
            instance.Finished = CreateTarget(width, height, false);
        if (spec.Output.Ghost > 0 || spec.Output.Persist > 0)
            instance.History = [CreateTarget(width, height, false), CreateTarget(width, height, false)];
        if (spec.Output.BloomThreshold > 0)
        {
            // The glow is sized from the console's own frame, whatever size this one is drawn at.
            if (width != spec.Width || height != spec.Height)
                instance.BloomReduce = CreateTarget((uint)spec.Width, (uint)spec.Height, false);
            var nearWidth = Math.Max(1u, (uint)spec.Width / 4);
            var nearHeight = Math.Max(1u, (uint)spec.Height / 4);
            instance.BloomNear = CreateTarget(nearWidth, nearHeight, false);
            instance.BloomFar = CreateTarget(Math.Max(1u, nearWidth / 3), Math.Max(1u, nearHeight / 3), false);
        }
    }

    private unsafe Target CreateTarget(uint width, uint height, bool depth)
    {
        var texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        var framebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
        uint depthBuffer = 0;
        if (depth)
        {
            depthBuffer = _gl.GenRenderbuffer();
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthBuffer);
            _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent16, width, height);
            _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depthBuffer);
        }
        _gl.ClearColor(0, 0, 0, 1);
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        return new Target(framebuffer, texture, depthBuffer, width, height);
    }

    private void Delete(Target? target)
    {
        if (target is null)
            return;
        _gl.DeleteFramebuffer(target.Framebuffer);
        _gl.DeleteTexture(target.Texture);
        if (target.Depth != 0)
            _gl.DeleteRenderbuffer(target.Depth);
    }

    private void Bind(int unit, uint texture)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(TextureTarget.Texture2D, texture);
    }
}
