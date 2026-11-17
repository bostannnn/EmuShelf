using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using EmuShelf.App.ViewModels;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

// The Big Type keyboard's motion on the Avalonia Launcher (M47 B9, the mock's openKb, kbSearch and closeKb), from BigTypeTypingMotion:
// the layer under it goes, its heading's letters land, its keys rise in by row and along each row, and 260 ms after it opened the ring
// is on the first key; Search's hits come down from above each time the set changes; closing, the keys and hits drop, and 290 ms later
// the layer under comes back (the native Launcher takes the screen back only then). Each key's "arrive" part is posed, so the ring,
// anchored to the key's resting slot, is never dragged by it. Held still, everything is simply there.
internal sealed partial class HandheldLauncherMotion
{
    private long? _keysStart;
    private bool _keysOpening;
    private bool _keysRingWaits;
    private int _hitsVersion = -1;
    private long? _hitsStart;
    private bool _hitsArriving;

    // A keyboard leaving: which, and when it set off (stamped on the next frame).
    private sealed class KeysExit(HandheldTypingViewModel typing)
    {
        public HandheldTypingViewModel Typing { get; } = typing;
        public long? Start { get; set; }
    }

    private KeysExit? _keysExit;

    /// <summary>How much longer a keyboard that closed keeps the screen while its keys drop (0 when none is leaving).</summary>
    public float KeyboardExitRemaining => _keysExit is not { } exit ? 0f
        : Math.Max(0f, BigTypeTypingMotion.KeysOutHold(Still) - (exit.Start is { } start ? SinceTicks(start) : 0f));

    /// <summary>The keyboard opened (or another took its place): its heading lands, its keys rise in, the ring waits.</summary>
    private void OpenKeyboard(HandheldBigTypeLists lists)
    {
        lists.KeyboardTitle.Arrive();
        _hitsVersion = -1;
        _keysStart = null;
        _keysOpening = !Still && _onScreen;
        _keysRingWaits = _keysOpening;
        PoseKeys(0f);
        RequestTick();
    }

    /// <summary>Each frame: the keys rising in, the hits arriving, the keys leaving. True while any moves.</summary>
    private bool StepKeyboard()
    {
        var moving = false;
        if (_keysOpening)
        {
            _keysStart ??= Stopwatch.GetTimestamp();
            var since = SinceTicks(_keysStart.Value);
            PoseKeys(since);
            if (_keysRingWaits && BigTypeTypingMotion.RingShows(since, Still))
            {
                // The mock's sleep(260): the ring is on the first key, landing (not travelling) there.
                _keysRingWaits = false;
                _jump = true;
            }
            _keysOpening = since < KeysInTotal() && !Still;
            if (!_keysOpening)
                PoseKeys(float.MaxValue);
            moving = true;
        }
        moving |= StepHits();
        moving |= StepKeysExit();
        return moving;
    }

    /// <summary>The ring waits, hidden, until the keys have had their 260 ms.</summary>
    private bool KeyboardRingWaits => _keysRingWaits;

    private float KeysInTotal()
    {
        if (ListsShown.Typing is not { } typing || typing.KeyRows.Count == 0)
            return 0f;
        var lastRow = typing.KeyRows.Count - 1;
        var widest = typing.KeyRows.Max(row => row.Keys.Count) - 1;
        return BigTypeTypingMotion.KeysInTotal(lastRow, widest);
    }

    // Every key's arriving part, by its row and its place along the row (the mock's r * 50 + i * 12); a rename's colours rise with
    // the row above the letters.
    private void PoseKeys(float since)
    {
        if (_lists is not { } lists || ListsShown.Typing is not { } typing)
            return;
        var still = Still;
        foreach (var part in Arrives(lists.KeyboardKeys))
        {
            if (part.DataContext is not HandheldKeyViewModel key)
                continue;
            var row = RowOf(typing, key);
            var index = row < 0 ? 0 : typing.KeyRows[row].Keys.ToList().IndexOf(key);
            var (rise, opacity) = BigTypeTypingMotion.KeyIn(Math.Max(0, row), Math.Max(0, index), since, still);
            Pose(part, 0, rise, opacity);
        }
        var chip = 0;
        foreach (var part in Arrives(lists.KeyboardColours))
        {
            var (rise, opacity) = BigTypeTypingMotion.KeyIn(0, chip++, since, still);
            Pose(part, 0, rise, opacity);
        }
    }

    private static int RowOf(HandheldTypingViewModel typing, HandheldKeyViewModel key)
    {
        for (var row = 0; row < typing.KeyRows.Count; row++)
        {
            if (typing.KeyRows[row].Keys.Contains(key))
                return row;
        }
        return -1;
    }

    // Search's hits come down from above (the mock's kbSearch), each time a new set of games is shown.
    private bool StepHits()
    {
        if (_lists is not { } lists || ListsShown.Typing?.Search is not { } search)
            return false;
        if (search.HitsVersion != _hitsVersion)
        {
            _hitsVersion = search.HitsVersion;
            _hitsStart = null;
            _hitsArriving = !Still && _onScreen && search.Hits.Count > 0;
        }
        if (!_hitsArriving)
            return false;
        _hitsStart ??= Stopwatch.GetTimestamp();
        var since = SinceTicks(_hitsStart.Value);
        var parts = Arrives(lists.KeyboardHits);
        for (var i = 0; i < parts.Count; i++)
        {
            var (y, scale, opacity) = BigTypeTypingMotion.ResultIn(i, since, Still);
            PoseCover(parts[i], y, scale, opacity);
        }
        _hitsArriving = since < BigTypeTypingMotion.ResultsInTotal(parts.Count);
        return true;
    }

    /// <summary>
    /// Whether a keyboard that closed is still leaving (the lists then wait). The shell's keyboard closing (or another taking its
    /// place) starts it; the same keyboard coming back cancels it.
    /// </summary>
    private bool KeyboardLeaving(HandheldShellViewModel shell)
    {
        if (_keysExit is { } exit)
        {
            if (!ReferenceEquals(shell.Typing, exit.Typing))
                return true;
            CancelKeysExit();
            return false;
        }
        if (ListsShown.Typing is { } shown && !ReferenceEquals(shell.Typing, shown) && _onScreen && !Still)
        {
            _keysExit = new KeysExit(shown);
            _keysOpening = false;
            _keysRingWaits = false;
            _hitsArriving = false;
            RequestTick();
            return true;
        }
        return false;
    }

    private void CancelKeysExit()
    {
        _keysExit = null;
        PoseKeys(float.MaxValue);
        if (_lists is { } lists)
        {
            foreach (var part in Arrives(lists.KeyboardHits))
                PoseCover(part, 0, 1, 1);
        }
    }

    // Closing (the mock's closeKb): the keys and hits drop, 8 ms apart counted every ten; after the hold the lists follow the shell.
    private bool StepKeysExit()
    {
        if (_keysExit is not { } exit || _lists is not { } lists || _shell is not { } shell)
            return false;
        exit.Start ??= Stopwatch.GetTimestamp();
        var since = SinceTicks(exit.Start.Value);
        var still = Still;
        var index = 0;
        foreach (var part in Arrives(lists.KeyboardKeys).Concat(Arrives(lists.KeyboardColours)).Concat(Arrives(lists.KeyboardHits)))
        {
            var (drop, opacity) = BigTypeTypingMotion.KeyOut(index++, since, still);
            Pose(part, 0, drop, opacity);
        }
        if (since < BigTypeTypingMotion.KeysOutHold(still))
            return true;
        _keysExit = null;
        // Put back for the next keyboard (the view's parts are reused), then the layer under comes back (or the next keyboard shows:
        // the one that left is no longer shown, so it does not leave again).
        PoseKeys(float.MaxValue);
        foreach (var part in Arrives(lists.KeyboardHits))
            PoseCover(part, 0, 1, 1);
        ListsShown.Typing = shell.Typing;
        UpdateLists();
        KeyboardExitEnded?.Invoke();
        return false;
    }

    /// <summary>A leaving keyboard has gone; the Launcher may hand the screen back to the native one.</summary>
    public event Action? KeyboardExitEnded;

    // A keyboard part's arriving pieces, in drawing order.
    private static List<Control> Arrives(Control root) =>
        root.GetVisualDescendants().OfType<Panel>().Where(panel => panel.Classes.Contains("arrive")).Cast<Control>().ToList();
}
