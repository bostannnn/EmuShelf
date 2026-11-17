using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmuShelf.App.Services;
using EmuShelf.Core.Handheld;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.ViewModels;

/// <summary>Where Big Type's setup is (M47 B14): the mock's three questions, the app's own "which system", and the count.</summary>
public enum HandheldSetupStep
{
    /// <summary>"Where are your games?" (1 of 4): the folders chosen so far, and Choose a folder….</summary>
    Where,

    /// <summary>The app's own question after a folder is picked (still 1 of 4): which system its games are for (Add games' sheet).</summary>
    System,

    /// <summary>"Emulators" (2 of 4): what will run each chosen system's games, and whether it is installed.</summary>
    Emulators,

    /// <summary>"Achievements?" (3 of 4): connect RetroAchievements, or skip for now.</summary>
    Achievements,

    /// <summary>4 of 4: the folders are read and the count of games found climbs on the Stage.</summary>
    Counting,
}

/// <summary>A folder chosen in setup and the system its games are for.</summary>
public sealed record HandheldSetupFolder(string Path, string SystemId, string SystemName)
{
    /// <summary>The folder as a row names it: its last part ("PS2"), or the whole path when it has none.</summary>
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('/', '\\')) is { Length: > 0 } name ? name : Path;
}

/// <summary>
/// Setup in the Big Type look (M47 B14; the mock's <c>openPanel('wiz')</c>, <c>panelSpec</c>, <c>wizStep</c>, <c>wizScan</c> and
/// <c>panelKey</c>): a panel like the mock's achievements and Activity, "Setup" with its step as the count, each question asked in big
/// type on the Stage over the focused row (its label the line, its value the second line). A on a row answers and goes on, B goes
/// back (on the first question it closes setup). The questions are the mock's, each doing the app's work behind it: where the games
/// are is Add games' folder picker and its "which system?" (asked as a question of its own), the emulators are the ones Settings would
/// use, and connecting RetroAchievements opens Settings there once setup is done. The last step imports every folder chosen, the count
/// of games found climbing as the Stage's name, and ends on home.
/// </summary>
public sealed class HandheldSetupPanel : HandheldPanelViewModel
{
    /// <summary>The mock's steps ("1 / 4" to "4 / 4"): three questions and the count.</summary>
    public const int StepCount = 4;

    public const string WhereQuestion = "Where are your games?";
    public const string SystemQuestion = "Which system are they for?";
    public const string EmulatorsQuestion = "Emulators";
    public const string AchievementsQuestion = "Achievements?";
    public const string ChooseFolder = "Choose a folder…";
    public const string ConnectAchievements = "Connect RetroAchievements";
    public const string SkipAchievements = "Skip for now";
    public const string AllSet = "All set";

    private readonly IHandheldLibrary _library;
    private readonly TimeProvider _clock;
    private readonly List<HandheldSetupFolder> _folders = [];
    private IReadOnlyList<HandheldImportSystem> _systems = [];
    private IReadOnlyList<HandheldSetupEmulator> _emulators = [];
    private string? _picked;
    private bool _working;

    // The count (4 of 4): what the Stage shows, what has arrived, and when it last changed (ms on the panel's clock).
    private int _shownCount;
    private int _foundCount;
    private float? _countShownAt;
    private bool _countPending;
    private string _countLine = string.Empty;
    private bool _countFailed;
    private bool _anyFound;
    private readonly long _startedAt;
    private readonly CancellationTokenSource _closing = new();

    public HandheldSetupPanel(IHandheldLibrary library, TimeProvider? clock = null)
    {
        _library = library;
        _clock = clock ?? TimeProvider.System;
        _startedAt = _clock.GetTimestamp();
        ShowStep(HandheldSetupStep.Where, focus: 0);
    }

    public HandheldSetupStep Step { get; private set; }

    /// <summary>The step as the mock counts it (1 to 4).</summary>
    public int StepNumber => Step switch
    {
        HandheldSetupStep.Where or HandheldSetupStep.System => 1,
        HandheldSetupStep.Emulators => 2,
        HandheldSetupStep.Achievements => 3,
        _ => 4,
    };

    /// <summary>The folders chosen so far, in the order chosen.</summary>
    public IReadOnlyList<HandheldSetupFolder> Folders => _folders;

    /// <summary>Whether "Connect RetroAchievements" was the answer to the third question.</summary>
    public bool ConnectsAchievements { get; private set; }

    /// <summary>The count is on the Stage (4 of 4): its name is typed, as the keyboard's word is.</summary>
    public bool IsCounting => Step == HandheldSetupStep.Counting;

    /// <summary>Every folder was read and games were added: "All set" is on the line, and home is due.</summary>
    public bool IsDone { get; private set; }

    /// <summary>How long "All set" stays before home comes back (tokens.js <c>SetupCount.doneHoldMs</c>; a frame on request holds it longer).</summary>
    internal TimeSpan DoneHold { get; set; } = TimeSpan.FromMilliseconds(BigTypeSetupMotion.DoneHoldMilliseconds);

    /// <summary>The count the Stage shows now.</summary>
    public int ShownCount => _shownCount;

    /// <summary>A question changed: the way it went (Right on, Left back), for the Stage's letters and the rows' slide.</summary>
    public event Action<HandheldDirection>? StepChanged;

    /// <summary>Setup is done and its "All set" has been read: the shell closes it, with how many games were added.</summary>
    public event Action<int>? Finished;

    public override string Heading => "Setup";

    public override string? Count => FormattableString.Invariant($"{StepNumber} / {StepCount}");

    private string Where => FormattableString.Invariant($"Setup · {StepNumber} of {StepCount}");

    private string Question => Step switch
    {
        HandheldSetupStep.Where => WhereQuestion,
        HandheldSetupStep.System => SystemQuestion,
        HandheldSetupStep.Emulators => EmulatorsQuestion,
        HandheldSetupStep.Achievements => AchievementsQuestion,
        _ => HandheldText.GameCount(_shownCount),
    };

    /// <summary>The mock's panelSpec('wiz').desc: the question as the name, the focused row's label as the line, its value under it.</summary>
    public override HandheldListStageText BigTypeStage => IsCounting
        ? new HandheldListStageText(Where, Question, _countLine, string.Empty)
        : new HandheldListStageText(Where, Question, Status ?? FocusedRow?.Label ?? string.Empty,
            Step == HandheldSetupStep.System && PickedFolderName is { } picked ? $"For the games in {picked}" : FocusedRow?.Value ?? string.Empty);

    public override HandheldPanelCard Card => new(Where, Question, BigTypeStage.Line, BigTypeStage.Facts, IsWarning: _countFailed);

    public override IEnumerable<HandheldHint> Hints => Step switch
    {
        HandheldSetupStep.Counting => _countFailed || (!_working && !IsDone) ? [new HandheldHint("B", "Back")] : [],
        HandheldSetupStep.Where when FocusedRow?.Source is HandheldSetupFolder =>
            [new HandheldHint("A", "Next"), new HandheldHint("Y", "Remove"), new HandheldHint("B", "Back")],
        HandheldSetupStep.Where or HandheldSetupStep.System => [new HandheldHint("A", "Choose"), new HandheldHint("B", "Back")],
        _ => [new HandheldHint("A", "Next"), new HandheldHint("B", "Back")],
    };

    public override void Dispatch(GamepadAction action)
    {
        if (_working)
        {
            // The picker is open, the emulators are being read or folders are being read: nothing else moves until it ends.
            Bump();
            return;
        }
        switch (action)
        {
            case GamepadAction.NavigateUp:
            case GamepadAction.NavigateDown:
                var next = FocusIndex + (action == GamepadAction.NavigateDown ? 1 : -1);
                if (Rows.Count == 0 || next < 0 || next >= Rows.Count)
                {
                    Bump();
                    return;
                }
                Status = null;
                Focus(next);
                RaiseChanged();
                return;
            case GamepadAction.Confirm:
                Answer();
                return;
            case GamepadAction.Cancel:
                GoBack();
                return;
            case GamepadAction.Actions when Step == HandheldSetupStep.Where && FocusedRow?.Source is HandheldSetupFolder folder:
                _folders.Remove(folder);
                ShowStep(HandheldSetupStep.Where, focus: Math.Min(FocusIndex, _folders.Count));
                RaiseChanged();
                return;
            default:
                Bump();
                return;
        }
    }

    public override void TapRow(HandheldPanelRowViewModel row)
    {
        var index = Rows.IndexOf(row);
        if (index < 0 || _working)
            return;
        // The mock's tapPanel: a tap on the focused row is A, on another it takes the focus.
        if (index == FocusIndex)
        {
            Answer();
            return;
        }
        Status = null;
        Focus(index);
        RaiseChanged();
    }

    // ---- A and B ----

    private void Answer()
    {
        Status = null;
        switch (Step)
        {
            case HandheldSetupStep.Where when FocusedRow?.Source is HandheldSetupFolder:
                _ = GoToEmulatorsAsync();
                return;
            case HandheldSetupStep.Where:
                _ = PickAsync();
                return;
            case HandheldSetupStep.System when FocusedRow?.Source is HandheldImportSystem system && _picked is { } path:
                // A folder chosen twice is for the system chosen last.
                _folders.RemoveAll(folder => string.Equals(folder.Path, path, StringComparison.Ordinal));
                _folders.Add(new HandheldSetupFolder(path, system.Id, system.Name));
                _picked = null;
                Go(HandheldSetupStep.Where, _folders.Count - 1, HandheldDirection.Left);
                return;
            case HandheldSetupStep.Emulators:
                Go(HandheldSetupStep.Achievements, 0, HandheldDirection.Right);
                return;
            case HandheldSetupStep.Achievements:
                ConnectsAchievements = FocusedRow?.Source as string == ConnectAchievements;
                _ = CountAsync();
                return;
            case HandheldSetupStep.Counting when _countFailed || (!_working && !IsDone):
                Bump();
                return;
            default:
                Bump();
                return;
        }
    }

    private void GoBack()
    {
        Status = null;
        switch (Step)
        {
            case HandheldSetupStep.Where:
                // The mock's panelKey: B on the first question closes setup. Nothing has been imported.
                RequestClose();
                return;
            case HandheldSetupStep.System:
                _picked = null;
                Go(HandheldSetupStep.Where, _folders.Count, HandheldDirection.Left);
                return;
            case HandheldSetupStep.Emulators:
                Go(HandheldSetupStep.Where, 0, HandheldDirection.Left);
                return;
            case HandheldSetupStep.Achievements:
                Go(HandheldSetupStep.Emulators, 0, HandheldDirection.Left);
                return;
            case HandheldSetupStep.Counting when _countFailed || (!_working && !IsDone):
                // Nothing found, or a folder could not be read: back to where the games are, to choose another.
                _countFailed = false;
                Go(HandheldSetupStep.Where, 0, HandheldDirection.Left);
                return;
            default:
                Bump();
                return;
        }
    }

    private void Go(HandheldSetupStep step, int focus, HandheldDirection direction)
    {
        ShowStep(step, focus);
        StepChanged?.Invoke(direction);
        RaiseChanged();
    }

    private void ShowStep(HandheldSetupStep step, int focus)
    {
        Step = step;
        IEnumerable<HandheldPanelRowViewModel> rows = step switch
        {
            HandheldSetupStep.Where => _folders
                .Select(folder => new HandheldPanelRowViewModel(HandheldPanelRowKind.Item, folder.Name, folder) { Value = folder.SystemName })
                .Append(new HandheldPanelRowViewModel(HandheldPanelRowKind.Button, ChooseFolder)),
            HandheldSetupStep.System => _systems
                .Select(system => new HandheldPanelRowViewModel(HandheldPanelRowKind.Item, system.Name, system) { Value = system.ShortName }),
            HandheldSetupStep.Emulators => EmulatorRows(),
            HandheldSetupStep.Achievements =>
            [
                new HandheldPanelRowViewModel(HandheldPanelRowKind.Button, ConnectAchievements, ConnectAchievements) { Value = "Sign in" },
                new HandheldPanelRowViewModel(HandheldPanelRowKind.Button, SkipAchievements, SkipAchievements) { Value = "You can connect later" },
            ],
            _ => [],
        };
        SetRows(rows, focus);
    }

    // One row per emulator (Dolphin runs GameCube and Wii: one row), the mock's "Found" or "Not installed" as its value.
    private IEnumerable<HandheldPanelRowViewModel> EmulatorRows()
    {
        var rows = new List<HandheldPanelRowViewModel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var folder in _folders)
        {
            var emulator = _emulators.FirstOrDefault(candidate => candidate.SystemId == folder.SystemId);
            var name = emulator?.Name;
            if (!seen.Add(name ?? "\0" + folder.SystemId))
                continue;
            var value = name is null ? "Choose one in Settings"
                : emulator?.IsInstalled switch { true => "Found", false => "Not installed", null => folder.SystemName };
            rows.Add(new HandheldPanelRowViewModel(HandheldPanelRowKind.Item, name ?? folder.SystemName, emulator) { Value = value });
        }
        return rows;
    }

    // ---- The work behind the answers ----

    /// <summary>Choose a folder…: Add games' picker, then which system its games are for.</summary>
    private async Task PickAsync()
    {
        _working = true;
        string? path;
        try
        {
            path = await _library.PickImportFolderAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            path = null;
            Status = $"Could not open the folder picker: {ex.Message}";
        }
        finally
        {
            _working = false;
        }
        if (IsDisposed)
            return;
        if (path is null)
        {
            RaiseChanged();
            return;
        }
        _systems = _library.ImportSystems;
        if (_systems.Count == 0)
        {
            Status = "No system can take games from a folder here.";
            RaiseChanged();
            return;
        }
        _picked = path;
        Go(HandheldSetupStep.System, 0, HandheldDirection.Right);
    }

    /// <summary>The folder picked, while its system is asked for.</summary>
    public string? PickedFolderName => _picked is { } path ? new HandheldSetupFolder(path, string.Empty, string.Empty).Name : null;

    private async Task GoToEmulatorsAsync()
    {
        _working = true;
        try
        {
            _emulators = await _library.DescribeEmulatorsAsync(_folders.Select(folder => folder.SystemId).Distinct().ToList());
        }
        catch (Exception)
        {
            _emulators = [];
        }
        finally
        {
            _working = false;
        }
        if (IsDisposed)
            return;
        Go(HandheldSetupStep.Emulators, 0, HandheldDirection.Right);
    }

    /// <summary>The mock's wizScan: every folder chosen is imported, the count climbing on the Stage; "All set", then home.</summary>
    private async Task CountAsync()
    {
        _working = true;
        IsDone = false;
        _countFailed = false;
        _foundCount = 0;
        _anyFound = false;
        _shownCount = 0;
        _countShownAt = null;
        Go(HandheldSetupStep.Counting, -1, HandheldDirection.Right);
        if (_library.IsBusy)
        {
            Fail("Adding games can start once the current task finishes.");
            return;
        }
        var added = 0;
        foreach (var folder in _folders)
        {
            _countLine = $"Looking through {folder.Name}";
            RaiseChanged();
            var before = added;
            HandheldSetupImport result;
            try
            {
                result = await _library.ImportFolderCountingAsync(folder.Path, folder.SystemId, found => CountArrived(before + Math.Max(0, found)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = new HandheldSetupImport(0, ex.Message);
            }
            if (IsDisposed)
                return;
            if (result.Error is { } error)
            {
                Fail($"Could not read {folder.Name}: {error}");
                return;
            }
            added += result.Added;
            CountArrived(added);
        }
        await ShowLatestCountAsync();
        if (IsDisposed)
            return;
        _working = false;
        if (added == 0)
        {
            // Games were found but none was new (a folder added before): they are already in the library, not missing.
            var names = string.Join(", ", _folders.Select(folder => folder.Name));
            _countLine = _anyFound ? $"Nothing new in {names}: its games are already here" : $"No games found in {names}";
            RaiseChanged();
            return;
        }
        IsDone = true;
        _countLine = AllSet;
        RaiseChanged();
        try
        {
            await Task.Delay(DoneHold, _clock, _closing.Token);
        }
        catch (TaskCanceledException)
        {
        }
        if (!IsDisposed)
            Finished?.Invoke(added);
    }

    private void Fail(string reason)
    {
        _working = false;
        _countFailed = true;
        _countLine = reason;
        RaiseChanged();
    }

    private float Now => (float)_clock.GetElapsedTime(_startedAt).TotalMilliseconds;

    // A count arrived: shown now if its turn has come, else once it has (the latest then).
    private void CountArrived(int count)
    {
        _foundCount = count;
        _anyFound |= count > 0;
        if (_countPending || IsDisposed)
            return;
        var wait = BigTypeSetupMotion.WaitBeforeCount(_countShownAt, Now);
        if (wait <= 0)
        {
            ShowCount();
            return;
        }
        _countPending = true;
        _ = ShowCountLaterAsync(wait);
    }

    private async Task ShowCountLaterAsync(float wait)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(wait), _clock, _closing.Token);
        }
        catch (TaskCanceledException)
        {
        }
        _countPending = false;
        if (!IsDisposed)
            ShowCount();
    }

    // The last count waits its turn too, so "All set" never lands on a count shown a moment before.
    private async Task ShowLatestCountAsync()
    {
        if (_shownCount == _foundCount && !_countPending)
            return;
        var wait = BigTypeSetupMotion.WaitBeforeCount(_countShownAt, Now);
        if (wait > 0)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(wait), _clock, _closing.Token);
            }
            catch (TaskCanceledException)
            {
            }
        }
        if (!IsDisposed)
            ShowCount();
    }

    private void ShowCount()
    {
        if (_shownCount == _foundCount && _countShownAt is not null)
            return;
        _shownCount = _foundCount;
        _countShownAt = Now;
        RaiseChanged();
    }

    protected override void DisposeCore()
    {
        _closing.Cancel();
        _closing.Dispose();
        StepChanged = null;
        Finished = null;
    }
}
