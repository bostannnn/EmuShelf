using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

// The Big Type lists' motion on the Avalonia Launcher (M47 B7, the mock's openSheet, openSettings and openPanel): when a list opens
// (or a new one takes its place) its rows slide in and its heading's letters land, the ring jumps to its first row instead of
// travelling there, and the grid under a sheet or the menu steps back (under Settings and a panel it goes). When a Settings
// section changes, its rows rise in from the way you moved. All of it from BigTypeListMotion, on this view's frame clock, set on each
// row's "arrive" part (so the ring, anchored to the row's resting slot, is not dragged by it); held still, everything is in place.
internal sealed partial class HandheldLauncherMotion
{
    private HandheldBigTypeLists? _lists;
    private ListKey _listKey;
    private readonly List<Arrival> _arrivals = [];
    private bool _jump;

    // The grid stepping back: where it went from, where it goes, and when it set off (stamped on the next frame).
    private (float Scale, float Opacity) _underFrom = (1f, 1f);
    private (float Scale, float Opacity) _underTo = (1f, 1f);
    private (float Scale, float Opacity) _underNow = (1f, 1f);
    private long? _underStart;
    private bool _underPending;

    private enum ListKind
    {
        None,
        Menu,
        Sheet,
        Settings,
        SettingsChoice,
        SettingsConfirm,
        Panel,
    }

    /// <summary>Which list is open and which instance: a new one arrives; the same one with another section rises in.</summary>
    private readonly record struct ListKey(ListKind Kind, object? Instance, int Section);

    /// <summary>One list's rows arriving: from where (along x, or along y for a section), since when (stamped on the next frame).</summary>
    private sealed class Arrival(Control root, float slide, bool section, int columns = 0)
    {
        public Control Root { get; } = root;
        public float Slide { get; } = slide;
        public bool Section { get; } = section;

        /// <summary>A grid of covers this many to a row (0: a list).</summary>
        public int Columns { get; } = columns;
        public long? Start { get; set; }
        public bool Done { get; set; }
    }

    /// <summary>The Big Type lists this view draws (M47 B7), given once the view is built.</summary>
    public void AttachLists(HandheldBigTypeLists lists)
    {
        _lists = lists;
        // After each layout (rows made for a list that just opened), before that frame is drawn: every arriving row takes its pose, so
        // a row never shows once at rest before it slides in.
        lists.LayoutUpdated += (_, _) =>
        {
            UpdateLists();
            ApplyArrivals();
        };
    }

    private static readonly double StepBackTicksPerMillisecond = Stopwatch.Frequency / 1000d;

    private static float SinceTicks(long start) => (float)((Stopwatch.GetTimestamp() - start) / StepBackTicksPerMillisecond);

    private ListKey CurrentListKey()
    {
        if (_shell is not { } shell)
            return default;
        if (shell.Settings is { } settings)
        {
            if (settings.IsTyping)
                return default;
            if (settings.IsChoicePickerOpen)
                return new ListKey(ListKind.SettingsChoice, settings, -1);
            if (settings.IsConfirmationOpen)
                return new ListKey(ListKind.SettingsConfirm, settings, -1);
            return new ListKey(ListKind.Settings, settings, settings.SelectedSectionIndex);
        }
        if (shell.Panel is { IsListing: true } panel)
            return new ListKey(ListKind.Panel, panel, -1);
        if (shell.IsMenuOpen)
            return new ListKey(ListKind.Menu, null, -1);
        if (shell.Sheet is { } sheet)
            return new ListKey(ListKind.Sheet, sheet, -1);
        return default;
    }

    /// <summary>Notices a list opening, changing or closing (called on every shell change and after every layout).</summary>
    private void UpdateLists()
    {
        if (!HandheldTokens.IsBigType || _lists is not { } lists)
            return;
        var key = CurrentListKey();
        if (key == _listKey)
            return;
        var was = _listKey;
        _listKey = key;
        StepBackTo(key.Kind);
        if (key.Kind == ListKind.None)
        {
            _arrivals.Clear();
            // The ring lands on what was under the list (the mock's 'back': no glide).
            _jump = true;
            RequestTick();
            return;
        }
        // The same Settings with another section on show: its rows rise in from the way the move went.
        if (key.Kind == ListKind.Settings && was.Kind == ListKind.Settings && ReferenceEquals(key.Instance, was.Instance))
        {
            if (key.Section != was.Section && was.Section >= 0)
            {
                var rise = _shell?.LastMoveDirection == HandheldDirection.Up ? -BigTypeListMotion.SectionInRise : BigTypeListMotion.SectionInRise;
                Arrive(lists.SettingsRows, rise, section: true);
                Arrive(lists.SettingsThemes, rise, section: true);
            }
            RequestTick();
            return;
        }
        _jump = true;
        switch (key.Kind)
        {
            case ListKind.Menu:
                Arrive(lists.MenuRows, BigTypeListMotion.RowsInSlide);
                lists.MenuTitle.Arrive();
                break;
            case ListKind.Sheet:
                Arrive(lists.SheetRows, BigTypeListMotion.RowsInSlide);
                lists.SheetTitle.Arrive();
                break;
            case ListKind.Settings:
                Arrive(lists.Rail, BigTypeListMotion.RowsInRailSlide);
                Arrive(lists.SettingsRows, BigTypeListMotion.RowsInSlide);
                Arrive(lists.SettingsThemes, BigTypeListMotion.RowsInSlide);
                // Back from a choice or a confirmation the heading is already there.
                if (was.Kind is not (ListKind.SettingsChoice or ListKind.SettingsConfirm))
                    lists.SettingsTitle.Arrive();
                break;
            case ListKind.SettingsChoice:
                Arrive(lists.ChoiceRows, BigTypeListMotion.RowsInSlide);
                lists.ChoiceTitle.Arrive();
                break;
            case ListKind.SettingsConfirm:
                Arrive(lists.ConfirmRows, BigTypeListMotion.RowsInSlide);
                lists.ConfirmTitle.Arrive();
                break;
            case ListKind.Panel:
                Arrive(lists.PanelRows, BigTypeListMotion.RowsInSlide);
                ArriveCovers(lists.PanelGrid, HandheldTokens.CoverColumns);
                lists.PanelTitle.Arrive();
                break;
        }
        RequestTick();
    }

    private void Arrive(Control root, float slide, bool section = false)
    {
        _arrivals.RemoveAll(arrival => ReferenceEquals(arrival.Root, root));
        if (Still)
            return;
        var arrival = new Arrival(root, slide, section);
        _arrivals.Add(arrival);
        Apply(arrival, 0f);
    }

    private void ArriveCovers(Control root, int columns)
    {
        _arrivals.RemoveAll(arrival => ReferenceEquals(arrival.Root, root));
        if (Still)
            return;
        var arrival = new Arrival(root, 0, section: false, columns);
        _arrivals.Add(arrival);
        Apply(arrival, 0f);
    }

    /// <summary>Each frame (and after each layout): every arriving row takes its pose; true while any still moves.</summary>
    private bool ApplyArrivals()
    {
        if (_arrivals.Count == 0)
            return false;
        var moving = false;
        foreach (var arrival in _arrivals)
        {
            var since = arrival.Start is { } start ? SinceTicks(start) : 0f;
            Apply(arrival, since);
            moving |= !arrival.Done;
        }
        _arrivals.RemoveAll(arrival => arrival.Done);
        return moving;
    }

    private bool StepArrivals()
    {
        foreach (var arrival in _arrivals)
            arrival.Start ??= Stopwatch.GetTimestamp();
        return ApplyArrivals();
    }

    private void Apply(Arrival arrival, float since)
    {
        var rows = Rows(arrival.Root);
        var still = Still;
        if (arrival.Columns > 0)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var (rise, scale, fade) = BigTypeListMotion.CoverIn(i % arrival.Columns, i / arrival.Columns, since, still);
                PoseCover(rows[i], rise, scale, fade);
            }
            var last = rows.Count == 0 ? 0 : ((rows.Count - 1) % arrival.Columns) + ((rows.Count - 1) / arrival.Columns);
            arrival.Done = still || (arrival.Start is not null && since >= BigTypeListMotion.CoversInTotal(last + 1));
            return;
        }
        var total = arrival.Section ? BigTypeListMotion.SectionInTotal(rows.Count) : BigTypeListMotion.RowsInTotal(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var (offset, opacity) = arrival.Section
                ? BigTypeListMotion.SectionRowIn(i, since, arrival.Slide, still)
                : BigTypeListMotion.RowIn(i, since, arrival.Slide, still);
            Pose(rows[i], arrival.Section ? 0 : offset, arrival.Section ? offset : 0, opacity);
        }
        arrival.Done = still || (arrival.Start is not null && since >= total);
    }

    private static void Pose(Control row, double x, double y, double opacity)
    {
        var shift = row.RenderTransform as TranslateTransform;
        if (x == 0 && y == 0)
        {
            if (shift is not null)
                row.RenderTransform = null;
        }
        else if (shift is null)
        {
            row.RenderTransform = new TranslateTransform(x, y);
        }
        else
        {
            shift.X = x;
            shift.Y = y;
        }
        var rounded = Math.Round(opacity, 3);
        if (row.Opacity != rounded)
            row.Opacity = rounded;
    }

    private static void PoseCover(Control tile, double rise, double scale, double opacity)
    {
        if (rise == 0 && scale == 1)
        {
            if (tile.RenderTransform is not null)
                tile.RenderTransform = null;
        }
        else
        {
            // Scaled about its middle, risen from below (the mock's translateY then scale).
            tile.RenderTransform = new MatrixTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(0, rise));
        }
        var rounded = Math.Round(opacity, 3);
        if (tile.Opacity != rounded)
            tile.Opacity = rounded;
    }

    // A list's rows in order: each row's arriving part (an items control's, or a static list's).
    private static List<Control> Rows(Control root) =>
        root.GetVisualDescendants().OfType<Panel>().Where(panel => panel.Classes.Contains("arrive")).Cast<Control>().ToList();

    // ---- The grid under the list ----

    private void StepBackTo(ListKind kind)
    {
        var to = kind switch
        {
            ListKind.Menu or ListKind.Sheet => ((float)HandheldTokens.UnderSheetScale, (float)HandheldTokens.UnderSheetOpacity),
            ListKind.None => (1f, 1f),
            _ => (1f, 0f),
        };
        PlaceCorner(to.Item2 == 0f);
        if (to == _underTo)
            return;
        _underFrom = _underNow;
        _underTo = to;
        _underStart = null;
        // Closing puts the grid back at once: the native Launcher takes the screen back and pictures it at rest, and no picture may
        // be placed from a grid still scaled down.
        _underPending = kind != ListKind.None && !Still;
        if (!_underPending)
            PlaceUnder(to);
        RequestTick();
    }

    private bool StepUnder()
    {
        if (!_underPending)
            return false;
        _underStart ??= Stopwatch.GetTimestamp();
        var since = SinceTicks(_underStart.Value);
        var (scale, opacity) = BigTypeListMotion.StepBack(since, _underFrom.Scale, _underFrom.Opacity, _underTo.Scale, _underTo.Opacity, Still);
        PlaceUnder((scale, opacity));
        if (since < BigTypeListMotion.StepBackMilliseconds)
            return true;
        _underPending = false;
        return false;
    }

    private void PlaceUnder((float Scale, float Opacity) state)
    {
        _underNow = state;
        foreach (var layer in UnderLayers())
        {
            layer.Opacity = Math.Round(state.Opacity, 3);
            if (state.Scale == 1f)
            {
                layer.RenderTransform = null;
                continue;
            }
            // About the middle of the screen, as the mock's layer scales.
            var middle = _canvas.TranslatePoint(new Point(_canvas.Bounds.Width / 2, _canvas.Bounds.Height / 2), layer) ?? default;
            layer.RenderTransformOrigin = new RelativePoint(middle, RelativeUnit.Absolute);
            layer.RenderTransform = new ScaleTransform(state.Scale, state.Scale);
        }
    }

    // What a list covers: home's grid, an open folder's page and the Title page (the dock and its corners stay, as in the mock).
    private IEnumerable<Control> UnderLayers()
    {
        if (_view.FindControl<Control>("HomeClip") is { } home)
            yield return home;
        if (_folderPage is Panel folder)
        {
            foreach (var child in folder.Children)
                yield return child;
        }
        yield return _titleLayer;
    }

    /// <summary>The dock row's right corner (the page, a folder's sort) says nothing of a list that takes the screen (the mock's Settings and panels).</summary>
    private void PlaceCorner(bool gone)
    {
        foreach (var name in CornerNames)
        {
            if (_view.FindControl<Control>(name) is not { } corner)
                continue;
            // Each keeps its own opacity at rest (the page is set at the corners' opacity).
            if (!_cornerOpacity.ContainsKey(name))
                _cornerOpacity[name] = corner.Opacity;
            corner.Opacity = gone ? 0 : _cornerOpacity[name];
        }
    }

    private readonly Dictionary<string, double> _cornerOpacity = new(StringComparer.Ordinal);

    private static readonly string[] CornerNames = ["BtPage", "BtSortCorner"];

    /// <summary>The ring goes to a list's first row, or back from a list, without travelling (the mock's 'open' and 'back').</summary>
    private bool TakeJump()
    {
        var jump = _jump;
        _jump = false;
        return jump;
    }
}
