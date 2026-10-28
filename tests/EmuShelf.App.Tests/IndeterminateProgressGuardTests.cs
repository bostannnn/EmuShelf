using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmuShelf.App.Controls;

namespace EmuShelf.App.Tests;

/// <summary>
/// The Fluent indeterminate sweep must not keep running once nobody can see the bar. Unguarded it does —
/// Avalonia only pauses/ends an animation for a Visual target, and the sweep targets a TranslateTransform —
/// which kept the Android couch's UI thread ticking at 60 Hz forever at rest (DECISIONS 2026-10-28).
/// These tests watch the real theme animation move (or not) across rendered frames.
/// </summary>
public sealed class IndeterminateProgressGuardTests
{
    [AvaloniaFact]
    public async Task ShownBar_Sweeps()
    {
        var (window, _, bar) = ShowBar();
        try
        {
            Assert.True(IndeterminateProgressGuard.IsSweepActive(bar));
            Assert.True(await IsSweeping(SweepTransform(bar)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task HidingTheBarsAncestor_StopsTheSweep_AndShowingItAgainRestartsIt()
    {
        var (window, host, bar) = ShowBar();
        try
        {
            var sweep = SweepTransform(bar);
            Assert.True(await IsSweeping(sweep));

            host.IsVisible = false;
            Assert.False(IndeterminateProgressGuard.IsSweepActive(bar));
            Assert.False(await IsSweeping(sweep));
            // IsIndeterminate is the caller's; only the animation switch is touched.
            Assert.True(bar.IsIndeterminate);

            host.IsVisible = true;
            Assert.True(IndeterminateProgressGuard.IsSweepActive(bar));
            Assert.True(await IsSweeping(SweepTransform(bar)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task HidingTheBarItself_StopsTheSweep()
    {
        var (window, _, bar) = ShowBar();
        try
        {
            var sweep = SweepTransform(bar);
            Assert.True(await IsSweeping(sweep));

            bar.IsVisible = false;
            Assert.False(await IsSweeping(sweep));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SwappingTheBarsViewOut_StopsTheSweep()
    {
        // The startup screen's shape: shown while the app boots, then replaced wholesale by the shell.
        var (window, _, bar) = ShowBar();
        try
        {
            var sweep = SweepTransform(bar);
            Assert.True(await IsSweeping(sweep));

            window.Content = new TextBlock { Text = "shell" };
            await Frame();
            Assert.False(bar.IsAttachedToVisualTree());
            Assert.False(await IsSweeping(sweep));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task StartupScreenHandOff_StopsTheBootSweep()
    {
        // The measured culprit (Thor + emulator, 2026-10-28): App.BuildInitialAndroidView hosts StartupView
        // in a ContentControl and, once the library is ready, replaces host.Content with the shell. The
        // detached boot bar's sweep kept Avalonia's 16 ms animation timer alive for the whole session.
        var startup = new EmuShelf.App.ViewModels.StartupViewModel(
            () => Task.CompletedTask, () => { }, () => Task.FromResult<string?>(null), _ => { });
        var startupView = new EmuShelf.App.Views.StartupView { DataContext = startup };
        var host = new ContentControl { Content = startupView };
        var window = new Window { Width = 480, Height = 360, Content = host };
        window.Show();
        try
        {
            await Frame();
            var bar = startupView.GetVisualDescendants().OfType<ProgressBar>().Single();
            var sweep = SweepTransform(bar);
            Assert.True(await IsSweeping(sweep));

            host.Content = new TextBlock { Text = "shell" };
            await Frame();
            Assert.False(bar.IsAttachedToVisualTree());
            // Leaving the logical tree withdraws the style that enabled the guard, and on device that
            // happens while the bar is still visually attached. The guard must not take that as "off" and
            // hand :indeterminate back — that is exactly how the boot sweep survived the first fix.
            Assert.False(IndeterminateProgressGuard.IsSweepActive(bar));
            Assert.False(await IsSweeping(sweep));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ReSettingIsIndeterminate_WhileHidden_DoesNotRestartTheSweep()
    {
        var (window, host, bar) = ShowBar();
        try
        {
            var sweep = SweepTransform(bar);
            host.IsVisible = false;
            bar.IsIndeterminate = false;
            bar.IsIndeterminate = true;
            Assert.False(IndeterminateProgressGuard.IsSweepActive(bar));
            Assert.False(await IsSweeping(sweep));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DeterminateBar_IsUntouched()
    {
        var (window, _, bar) = ShowBar(indeterminate: false);
        try
        {
            Assert.False(IndeterminateProgressGuard.IsSweepActive(bar));
            Assert.True(IndeterminateProgressGuard.GetIsEnabled(bar));
        }
        finally
        {
            window.Close();
        }
    }

    private static (Window Window, Border Host, ProgressBar Bar) ShowBar(bool indeterminate = true)
    {
        var bar = new ProgressBar { IsIndeterminate = indeterminate, Width = 200, Height = 4 };
        var host = new Border { Child = new StackPanel { Children = { bar } } };
        var window = new Window { Width = 320, Height = 120, Content = host };
        window.Show();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        // The app-wide style (EmuShelfStyles, included by TestApp) must have switched the guard on.
        Assert.True(IndeterminateProgressGuard.GetIsEnabled(bar));
        return (window, host, bar);
    }

    // The theme's sweep animates TranslateTransform.X inside both indicators' render transforms. Each
    // indicator HOLDS for part of the 2 s cycle (the first from 1.5 s, the second until 0.75 s), but never
    // at the same time, so watching both means a short sample always sees movement while it runs.
    private static TranslateTransform[] SweepTransform(ProgressBar bar) =>
        new[] { "IndeterminateProgressBarIndicator", "IndeterminateProgressBarIndicator2" }
            .Select(name => bar.GetVisualDescendants().OfType<Border>().Single(border => border.Name == name))
            .Select(indicator => indicator.RenderTransform switch
            {
                TranslateTransform translate => translate,
                TransformGroup group => group.Children.OfType<TranslateTransform>().Single(),
                var other => throw new Xunit.Sdk.XunitException(
                    $"The Fluent sweep is no longer a TranslateTransform ({other?.GetType().Name ?? "none"}); revisit IndeterminateProgressGuard."),
            })
            .ToArray();

    // Lets real time pass across several frames and reports whether either indicator moved meanwhile.
    private static async Task<bool> IsSweeping(TranslateTransform[] sweep)
    {
        await Frame();
        var start = sweep.Select(transform => transform.X).ToArray();
        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(40);
            await Frame();
            if (sweep.Where((transform, index) => transform.X != start[index]).Any())
                return true;
        }

        return false;
    }

    private static async Task Frame()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }
}
