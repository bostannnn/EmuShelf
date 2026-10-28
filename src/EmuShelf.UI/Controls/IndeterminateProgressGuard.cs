using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace EmuShelf.App.Controls;

/// <summary>
/// Stops a Fluent indeterminate <see cref="ProgressBar"/> from animating while nobody can see it — hidden
/// (by itself or any ancestor) or removed from the visual tree. Switched on for every ProgressBar by one
/// style in <c>EmuShelfStyles.axaml</c>, so a bar added later is covered without anyone remembering to.
/// </summary>
/// <remarks>
/// Why this exists: the Fluent bar's busy sweep is two <c>IterationCount="Infinite"</c> style animations
/// on <c>TranslateTransform.X</c>. Avalonia pauses an animation on an invisible control, and ends it when
/// the control leaves the tree, only when the animation's target is a <c>Visual</c>; a transform is not,
/// so neither safeguard applies. Once a bar has been shown (its template applied), hiding it leaves the
/// sweep running forever, and on Android so does swapping its view out (the startup screen's hand-off to
/// the shell). Each frame it changes a value nothing draws, which keeps Avalonia's 16 ms animation timer
/// ticking on the UI thread while no frame is ever rendered: ~24% of a core at rest on the Thor, from the
/// startup screen's bar alone, on every launch (DECISIONS 2026-10-28).
///
/// The fix drives the one switch the theme's animation styles listen to: the <c>:indeterminate</c>
/// pseudo-class. It is cleared while the bar is not shown, which deactivates those styles and disposes
/// their animations properly; it comes back when the bar is shown again, restarting the sweep.
/// <see cref="ProgressBar.IsIndeterminate"/> itself is never touched, so bindings and layout are unchanged.
/// </remarks>
public sealed class IndeterminateProgressGuard
{
    private const string IndeterminatePseudoClass = ":indeterminate";

    /// <summary>
    /// Turns the guard on for a <see cref="ProgressBar"/>; set app-wide by EmuShelfStyles. Once on it stays on
    /// for that bar (see the static constructor for why).
    /// </summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<IndeterminateProgressGuard, ProgressBar, bool>("IsEnabled");

    private static readonly ConditionalWeakTable<ProgressBar, IndeterminateProgressGuard> Guards = new();

    private readonly ProgressBar _bar;
    // The bar's ancestors while it is attached; any of them collapsing hides the bar too.
    private readonly List<Visual> _ancestors = new();

    static IndeterminateProgressGuard()
    {
        // Enable-only, on purpose. The app turns the guard on with a style setter, and a style's values are
        // withdrawn the moment the bar leaves the logical tree — which on Android happens BEFORE it leaves
        // the visual tree when a view is swapped out (the startup screen's hand-off). Treating that
        // withdrawal as "disable" handed :indeterminate straight back and the detached sweep ran on, so a
        // guard once attached stays for the bar's lifetime and simply re-evaluates on every change.
        IsEnabledProperty.Changed.AddClassHandler<ProgressBar>((bar, e) =>
        {
            if (e.NewValue is true)
                Enable(bar);
            else if (Guards.TryGetValue(bar, out var guard))
                guard.Update();
        });
    }

    private IndeterminateProgressGuard(ProgressBar bar) => _bar = bar;

    public static bool GetIsEnabled(ProgressBar bar) => bar.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(ProgressBar bar, bool value) => bar.SetValue(IsEnabledProperty, value);

    /// <summary>Whether the bar's busy sweep is allowed to run right now. Exposed for tests.</summary>
    internal static bool IsSweepActive(ProgressBar bar) =>
        ((IPseudoClasses)bar.Classes).Contains(IndeterminatePseudoClass);

    private static void Enable(ProgressBar bar)
    {
        if (Guards.TryGetValue(bar, out var existing))
        {
            existing.Update();
            return;
        }
        var guard = new IndeterminateProgressGuard(bar);
        Guards.Add(bar, guard);
        bar.AttachedToVisualTree += guard.OnAttachedToVisualTree;
        bar.DetachedFromVisualTree += guard.OnDetachedFromVisualTree;
        bar.AttachedToLogicalTree += guard.OnLogicalTreeChanged;
        bar.DetachedFromLogicalTree += guard.OnLogicalTreeChanged;
        bar.PropertyChanged += guard.OnBarPropertyChanged;
        if (bar.IsAttachedToVisualTree())
            guard.HookAncestors();
        guard.Update();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        HookAncestors();
        Update();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        UnhookAncestors();
        Update();
    }

    private void OnLogicalTreeChanged(object? sender, LogicalTreeAttachmentEventArgs e) => Update();

    // Runs after ProgressBar.OnPropertyChanged, so a re-set IsIndeterminate (which makes the bar restore
    // its own :indeterminate) is corrected here in the same change.
    private void OnBarPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ProgressBar.IsIndeterminateProperty || e.Property == Visual.IsVisibleProperty)
            Update();
    }

    private void OnAncestorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty)
            Update();
    }

    private void HookAncestors()
    {
        UnhookAncestors();
        foreach (var ancestor in _bar.GetVisualAncestors())
        {
            _ancestors.Add(ancestor);
            ancestor.PropertyChanged += OnAncestorPropertyChanged;
        }
    }

    private void UnhookAncestors()
    {
        foreach (var ancestor in _ancestors)
            ancestor.PropertyChanged -= OnAncestorPropertyChanged;
        _ancestors.Clear();
    }

    private void Update() => SetSweep(_bar, _bar.IsIndeterminate && IsShown());

    // Shown = in both trees and visible all the way up. The logical check is what catches a swapped-out
    // view early (see the static constructor). Visibility is walked from the hooked ancestors rather than
    // read from IsEffectivelyVisible, whose own update is not ordered against these PropertyChanged callbacks.
    private bool IsShown()
    {
        if (!_bar.IsVisible || !_bar.IsAttachedToVisualTree() || !((ILogical)_bar).IsAttachedToLogicalTree)
            return false;
        foreach (var ancestor in _ancestors)
        {
            if (!ancestor.IsVisible)
                return false;
        }

        return true;
    }

    private static void SetSweep(ProgressBar bar, bool active)
    {
        var pseudoClasses = (IPseudoClasses)bar.Classes;
        if (active)
            pseudoClasses.Add(IndeterminatePseudoClass);
        else
            pseudoClasses.Remove(IndeterminatePseudoClass);
    }
}
