using EmuShelf.Rendering.Gl;
using EmuShelf.Rendering.Stage;
using EmuShelf.Rendering.Stage.Backdrops;
using Silk.NET.OpenGL;

namespace EmuShelf.Rendering.Preview;

/// <summary>
/// Checks the Stage compositor's M45 H7c behaviour under a real GL context: `--stage-resume`. A lost context is played
/// as the Thor's surface plays it (the old compositor's <see cref="INativeScene.CaptureResume"/>, a new compositor, the
/// retained backdrop command replayed, then the resume), and the device's say on motion (<see cref="StageMotionCommand"/>)
/// holds the backdrop still, both from the first frame and mid-run. Prints one line per check; exits non-zero on a failure.
/// </summary>
internal static class StageResumeCheck
{
    public static int Run(GL gl)
    {
        var failures = 0;
        void Check(string what, bool ok)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok)
                failures++;
        }

        // A surfaceless context has no default framebuffer: the compositor draws into one of ours.
        var framebuffer = gl.GenFramebuffer();
        var colour = gl.GenRenderbuffer();
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, colour);
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, 480, 270);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, colour);

        var pixel = new StagePixels(1, 1, [40, 40, 40, 255]);
        var pictured = new StageBackdrop(new StageImage(pixel, 0, 0, 1920, 1080), null, null, null, System.Numerics.Vector2.Zero, 0, 0);
        IReadOnlyList<string> ids = ["runway", "nes-meta", "field"];
        var backdrop = new StageBackdropCommand(pictured, ids);

        // ---- A lost context carries the running backdrop on ----
        var first = new StageCompositor(gl, GlslDialect.Desktop, 3, 3, () => { });
        first.Apply(backdrop, 0f);
        var now = 0f;
        for (; now < 3_000f; now += BackdropCatalog.StepMilliseconds)
            Draw(gl, first, framebuffer, now);
        var captured = first.CaptureResume() as StageBackdropResumeCommand;
        Check("the old compositor captures its running backdrop", captured is { Id: not null } && ids.Contains(captured.Id));
        Check("…with the clock where it was", captured is { Clock: > 2_500f and < 3_100f, Speed: 1f });
        first.Dispose();

        var second = new StageCompositor(gl, GlslDialect.Desktop, 3, 3, () => { });
        second.Apply(backdrop.ForReplay(), now);
        var fresh = second.CaptureResume() as StageBackdropResumeCommand;
        Check("a rebuilt compositor opens on a pick of its own", fresh is { Id: not null });
        second.Apply(captured!, now);
        var resumed = second.CaptureResume() as StageBackdropResumeCommand;
        Check("…and the resume puts the old backdrop back, at its start and offset", resumed == captured);
        // The first frame after a resume moves the clock by nothing (no jump); the second moves it a step.
        Draw(gl, second, framebuffer, now + BackdropCatalog.StepMilliseconds);
        Draw(gl, second, framebuffer, now + (2 * BackdropCatalog.StepMilliseconds));
        var afterFrame = second.CaptureResume() as StageBackdropResumeCommand;
        Check("…which the next frames keep running", afterFrame is { } a && a.Id == captured!.Id && a.StartMilliseconds == captured.StartMilliseconds
            && a.Clock > captured.Clock);
        Check("the step interval is the backdrop's 30 Hz", second.NextFrameMilliseconds == BackdropCatalog.StepMilliseconds);

        // A resume naming a backdrop the replayed system no longer has is ignored.
        var stale = captured! with { Id = "ps2-sea" };
        second.Apply(stale, now);
        Check("a resume for another system's backdrop is ignored", (second.CaptureResume() as StageBackdropResumeCommand)?.Id == captured.Id);

        // A resting Stage stays at rest across the rebuild.
        var resting = captured with { Speed = 0f };
        second.Apply(resting, now);
        Check("a resting Stage is still resting after the rebuild", second.IsBackdropResting && second.NextFrameMilliseconds is null);
        second.Dispose();

        // ---- The device's say on motion ----
        var held = new StageCompositor(gl, GlslDialect.Desktop, 3, 3, () => { });
        held.Apply(new StageMotionCommand(true), 0f);
        held.Apply(backdrop, 0f);
        Draw(gl, held, framebuffer, 0f);
        Check("told before its first frame, the Stage is still from the start", held.IsBackdropResting && held.NextFrameMilliseconds is null);
        held.Apply(new StageBackdropCommand(pictured, ["ps2-sea"]), 100f); // a navigation to another system
        Draw(gl, held, framebuffer, 100f);
        Check("…and a navigation does not wake it", held.IsBackdropResting);
        held.Apply(new StageMotionCommand(false), 200f);
        Draw(gl, held, framebuffer, 200f);
        Check("lifted within the minute of a navigation, the ordinary rule brings the motion back", !held.IsBackdropResting);
        held.Dispose();

        var idle = new StageCompositor(gl, GlslDialect.Desktop, 3, 3, () => { });
        idle.Apply(new StageMotionCommand(true), 0f);
        idle.Apply(backdrop, 0f);
        Draw(gl, idle, framebuffer, 0f);
        idle.Apply(new StageMotionCommand(false), 70_000f);
        Draw(gl, idle, framebuffer, 70_000f);
        Check("lifted after a minute without navigation, it stays at rest", idle.IsBackdropResting && idle.NextFrameMilliseconds is null);
        idle.Apply(new StageBackdropCommand(pictured, ["ps2-sea"]), 70_100f);
        Draw(gl, idle, framebuffer, 70_100f);
        Check("…until the next navigation", !idle.IsBackdropResting);
        idle.Dispose();

        var running = new StageCompositor(gl, GlslDialect.Desktop, 3, 3, () => { });
        running.Apply(backdrop, 0f);
        now = 0f;
        for (; now < 1_000f; now += BackdropCatalog.StepMilliseconds)
            Draw(gl, running, framebuffer, now);
        running.Apply(new StageMotionCommand(true), now);
        var framesToRest = 0;
        for (; !running.IsBackdropResting && framesToRest < 200; framesToRest++, now += BackdropCatalog.StepMilliseconds)
            Draw(gl, running, framebuffer, now);
        Check($"told mid-run, it eases to a stop in about two seconds ({framesToRest} frames)", framesToRest is >= 55 and <= 65);
        running.Apply(new StageBackdropCommand(pictured, ["ps2-sea"]), now);
        Draw(gl, running, framebuffer, now);
        Check("…and stays still through a navigation", running.IsBackdropResting);
        running.Dispose();

        using (var stage = new StageCompositor(gl, GlslDialect.Desktop, 3, 3, () => { }))
        {
            stage.Apply(new StageMotionCommand(true, BigType: true), 0f);
            var pending = new TaskCompletionSource<StageBackdrop>();
            var picture = new Lazy<Task<StageBackdrop>>(() => pending.Task);
            var shader = new StageBackdropCommand(null, ["ps2-towers"]) { Fallback = picture };
            stage.Apply(shader, 0f);
            Draw(gl, stage, framebuffer, 0f);
            Check("a working shader leaves the fallback picture unrequested", !picture.IsValueCreated);
            stage.Apply(shader with { Animated = ["missing-test-shader"] }, 100f);
            Draw(gl, stage, framebuffer, 116f);
            Draw(gl, stage, framebuffer, 132f);
            Check("a missing shader requests the picture and keeps drawing while it is pending", picture.IsValueCreated);
            pending.SetResult(pictured);
            Draw(gl, stage, framebuffer, 150f);
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            var sample = new byte[4];
            gl.ReadPixels(240, 135, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, sample.AsSpan());
            Check("the completed fallback reaches the screen", sample.SequenceEqual(new byte[] { 40, 40, 40, 255 }));
        }

        gl.DeleteFramebuffer(framebuffer);
        gl.DeleteRenderbuffer(colour);
        Console.WriteLine(failures == 0 ? "All Stage resume checks passed." : $"{failures} Stage resume check(s) FAILED.");
        return failures == 0 ? 0 : 1;
    }

    private static void Draw(GL gl, StageCompositor compositor, uint framebuffer, float now)
    {
        // A surfaceless context has no default framebuffer: the frame is composed into ours.
        compositor.TargetFramebuffer = framebuffer;
        compositor.Draw(now, 480, 270);
        var error = gl.GetError();
        if (error != GLEnum.NoError)
            Console.WriteLine($"  GL error {error} at {now} ms");
    }
}
