using Avalonia.Media;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Systems;
using EmuShelf.Integrations.Systems;
using EmuShelf.Rendering.Gl;
using EmuShelf.Rendering.Stage;
using EmuShelf.Rendering.Stage.Backdrops;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

/// <summary>
/// The Thor's animated Stage backdrops (M45 H7a): every system names backdrops the catalog can draw, and
/// every backdrop's shaders ship in the assembly. Whether they compile and look right is checked with
/// <c>tools/EmuShelf.Rendering.Preview --backdrops</c> (a GL context), not here.
/// </summary>
public class StageBackdropCatalogTests
{
    [Fact]
    public void EverySystem_HasAnAnimatedBackdrop_TheCatalogCanDraw()
    {
        foreach (var system in KnownSystems.All)
        {
            Assert.NotNull(system.Backdrops);
            Assert.NotEmpty(system.Backdrops!);
            foreach (var id in system.Backdrops!)
                Assert.True(BackdropCatalog.Find(id) is not null, $"{system.Id} names '{id}', which the catalog does not have.");
        }
    }

    [Fact]
    public void EveryCatalogBackdrop_BelongsToASystem_AndIdsAreUnique()
    {
        var used = KnownSystems.All.SelectMany(system => system.Backdrops ?? []).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(BackdropCatalog.All.Count, BackdropCatalog.All.Select(spec => spec.Id).Distinct(StringComparer.Ordinal).Count());
        foreach (var spec in BackdropCatalog.All)
            Assert.Contains(spec.Id, used);
    }

    [Fact]
    public void EveryPass_ShipsItsShaders_ForTheThorsGlEs()
    {
        foreach (var name in new[] { "backdrop-output.frag", "backdrop-history.frag", "backdrop-bright.frag" })
            Assert.Contains("#version 300 es", ShaderLibrary.Load(name, GlslDialect.Es300, 3, 0, "backdrop-common"));
        foreach (var spec in BackdropCatalog.All)
        {
            Assert.NotEmpty(spec.Passes);
            foreach (var pass in spec.Passes)
            {
                Assert.True(pass.VertexCount > 0, $"{spec.Id}: a pass draws nothing.");
                Assert.Contains("void main()", ShaderLibrary.Load(pass.Vertex, GlslDialect.Es300, 3, 0, "backdrop-common"));
                Assert.Contains("void main()", ShaderLibrary.Load(pass.Fragment, GlslDialect.Es300, 3, 0, "backdrop-common"));
            }
        }
    }

    [Fact]
    public void Backdrops_DrawAtTheirConsolesResolution_In16By9()
    {
        foreach (var spec in BackdropCatalog.All)
        {
            Assert.InRange(spec.Width, 160, 480);
            Assert.InRange(spec.Width / (double)spec.Height, 16.0 / 9.0 - 0.02, 16.0 / 9.0 + 0.02);
        }
    }

    [Fact]
    public void Backdrops_StepAt30Hz_AndTheStageCommandCarriesTheList()
    {
        Assert.Equal(1000f / 30f, BackdropCatalog.StepMilliseconds, 3);
        IReadOnlyList<string> animated = ["runway"];
        var command = new StageBackdropCommand(null, animated);
        Assert.Same(animated, command.Animated);
        Assert.Equal("backdrop", command.RetainKey);
        Assert.Null(new StageBackdropCommand(null).Animated);
    }

    [Fact]
    public void Rotation_NeverPicksTheBackdropShowingNow_AndReachesEveryOther()
    {
        IReadOnlyList<string> ids = ["a", "b", "c", "d"];
        var random = new Random(1);
        var seen = new HashSet<string>();
        var current = BackdropRotation.Pick(ids, null, random);
        for (var i = 0; i < 400; i++)
        {
            var next = BackdropRotation.Pick(ids, current, random);
            Assert.NotEqual(current, next);
            Assert.Contains(next, ids);
            seen.Add(next);
            current = next;
        }
        Assert.Equal(ids.Count, seen.Count);
    }

    [Fact]
    public void Rotation_WithOneBackdrop_KeepsIt_AndRestartsItRarely()
    {
        Assert.Equal("psp-field-v2", BackdropRotation.Pick(["psp-field-v2"], "psp-field-v2", new Random(1)));
        Assert.Equal(12_000f, BackdropRotation.DwellMilliseconds(4));
        // A lone backdrop restarts itself well before its clock wraps, so the wrap never shows.
        Assert.True(BackdropRotation.DwellMilliseconds(1) < BackdropCatalog.ClockWrapSeconds * 1000f);
        Assert.True(BackdropRotation.FadeMilliseconds < BackdropRotation.DwellMilliseconds(2));
    }

    [Fact]
    public void Rest_EasesTheMotionToAStopAfterAMinute_AndBackQuickly()
    {
        Assert.Equal(60_000f, StageRest.AfterMilliseconds);
        var speed = 1f;
        var steps = 0;
        while (speed > 0f && steps++ < 1000)
            speed = StageRest.Next(speed, 1000f / 30f, resting: true);
        // About two seconds of 30 Hz frames to stop, then the Stage stops drawing.
        Assert.InRange(steps, 55, 65);
        Assert.Equal(0f, StageRest.Eased(0f));
        Assert.Equal(1f, StageRest.Eased(1f));
        Assert.True(StageRest.Eased(0.1f) < 0.1f); // eases in: no lurch
        Assert.Equal(1f, StageRest.Next(0f, StageRest.WakeMilliseconds, resting: false));
    }

    [Fact]
    public void EverySystem_RotatesThroughTheOwnersPicks()
    {
        Assert.Equal(53, KnownSystems.All.Sum(system => system.Backdrops!.Count));
        Assert.Equal(53, BackdropCatalog.All.Count);
    }
}

public partial class HandheldShellViewModelTests
{
    [Fact]
    public async Task StageBackdrop_CarriesTheSystemsAnimatedBackdrops_TheSameListWithinASystem()
    {
        IReadOnlyList<string> animated = ["runway"];
        var library = new FakeLibrary();
        library.Platforms.Add(new HandheldPlatform("snes", "Super Nintendo", "SNES", "#8D66C4", 2, SystemEra.Cartridge, animated));
        library.Games["snes"] = [Entry(1, "snes"), Entry(2, "snes")];
        var shell = await CreateAsync(library);

        Assert.Equal(new HandheldBackdrop(Color.Parse("#8D66C4"), SystemEra.Cartridge, animated), shell.Stage.Backdrop);
        // The list instance is the system's own, so the native Stage keeps the running backdrop between moves.
        Assert.Same(animated, shell.Stage.Backdrop!.Animated);
    }
}
