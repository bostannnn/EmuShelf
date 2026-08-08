using EmuShelf.Core.Hotkeys;

namespace EmuShelf.Integrations.Emulators.Azahar;

/// <summary>
/// Writes the keyboard-hotkey scheme into Azahar's <c>config/qt-config.ini</c> <c>[UI]</c> section,
/// whose Qt <c>QSettings</c> shortcuts are keyed <c>Shortcuts\Main%20Window\&lt;Name&gt;\KeySeq</c> with a
/// companion <c>\KeySeq\default</c> flag. Azahar renamed several actions across versions, so each action
/// binds whichever candidate name actually exists in this machine's config (the rest are reported
/// unsupported), and each write pins <c>\KeySeq\default</c> to <c>false</c> so Azahar keeps the value.
/// Azahar has no rewind. Setting a shortcut clears any other shortcut holding the same key (an empty
/// KeySeq with default=false, rather than removing the line, so the built-in default can't reappear).
/// </summary>
public sealed class AzaharHotkeyConfigurator : HotkeyConfiguratorBase
{
    private const string Section = "UI";
    private const string ShortcutPrefix = @"Shortcuts\Main%20Window\";
    private const string RewindUnsupported = "Azahar has no rewind feature.";
    private const string NoShortcut = "This Azahar version exposes no matching shortcut.";

    /// <summary>
    /// Candidate shortcut names per action, in preference order. The first whose shortcut is registered
    /// in this config (it has a <c>\KeySeq\default</c> entry) is bound; Azahar's names differ by version
    /// (e.g. "Quick Save" vs. "Save to Oldest Slot", "Toggle Turbo Mode" vs. "Toggle Per-Application
    /// Speed").
    /// </summary>
    private static readonly IReadOnlyDictionary<HotkeyAction, IReadOnlyList<string>> ActionShortcuts =
        new Dictionary<HotkeyAction, IReadOnlyList<string>>
        {
            [HotkeyAction.CloseGame] = ["Stop Emulation"],
            [HotkeyAction.FastForward] = ["Toggle Turbo Mode", "Toggle Per-Application Speed"],
            [HotkeyAction.SaveState] = ["Quick Save", "Save to Oldest Slot", "Save to Oldest Non-Quicksave Slot"],
            [HotkeyAction.LoadState] = ["Quick Load", "Load from Newest Slot", "Load from Newest Non-Quicksave Slot"],
        };

    private readonly string _configPath;

    public AzaharHotkeyConfigurator(string userDirectory, string backupRoot, Action<string, string>? writeFile = null)
        : base(AzaharDefinition.Instance.Id, "Azahar", backupRoot, writeFile)
    {
        _configPath = Path.Combine(Path.GetFullPath(userDirectory), "config", "qt-config.ini");
    }

    public override IReadOnlyList<HotkeyActionSupport> DescribeSupport(HotkeyProfile profile) =>
        profile.Actions
            .Select(action => ActionShortcuts.ContainsKey(action)
                ? HotkeyActionSupport.Supported(action)
                : HotkeyActionSupport.Unsupported(action, RewindUnsupported))
            .ToArray();

    protected override IReadOnlyList<string> ManagedFiles =>
        File.Exists(_configPath) ? [_configPath] : [];

    private protected override HotkeyPlan BuildPlan(HotkeyProfile profile, CancellationToken cancellationToken)
    {
        var text = ReadTextOrNull(_configPath);
        if (text is null)
            return HotkeyPlan.NotFound("Azahar's qt-config.ini was not found.");

        var document = new EmulatorConfigDocument(text);
        var fileName = Path.GetFileName(_configPath);
        var bindings = new List<HotkeyBindingResult>();
        var changes = new List<HotkeyChange>();
        var targets = new List<(string KeySeqKey, string Value)>();

        foreach (var action in profile.Actions)
        {
            var label = profile[action].Label();
            if (!ActionShortcuts.TryGetValue(action, out var candidates))
            {
                bindings.Add(new HotkeyBindingResult(action, HotkeyBindingStatus.Unsupported, label, RewindUnsupported));
                continue;
            }

            var name = candidates.FirstOrDefault(candidate => ShortcutExists(document, candidate));
            if (name is null)
            {
                bindings.Add(new HotkeyBindingResult(action, HotkeyBindingStatus.Unsupported, label, NoShortcut));
                continue;
            }

            targets.Add((KeySeqKey(name), label));
            bindings.Add(new HotkeyBindingResult(action, HotkeyBindingStatus.Bound, label));
        }

        // Clear any other shortcut currently holding a key we're claiming, so two actions can't fight
        // for one key. Detect by value; skip our own targets and any non-shortcut setting.
        var targetKeys = targets.Select(target => target.KeySeqKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var value in targets.Select(target => target.Value).Distinct(StringComparer.Ordinal))
        {
            foreach (var conflicting in document.KeysWithValue(Section, value))
            {
                if (targetKeys.Contains(conflicting) || !IsShortcutKeySeq(conflicting))
                    continue;
                SetShortcut(document, fileName, conflicting, string.Empty, changes);
            }
        }

        foreach (var (keySeqKey, value) in targets)
            SetShortcut(document, fileName, keySeqKey, value, changes);

        IReadOnlyList<HotkeyFilePlan> files = document.Changed
            ? [new HotkeyFilePlan(_configPath, document.ToText())]
            : [];
        return HotkeyPlan.Edited(bindings, files, changes);
    }

    /// <summary>Sets a shortcut's KeySeq (empty clears it) and pins its default flag to false.</summary>
    private static void SetShortcut(EmulatorConfigDocument document, string fileName, string keySeqKey, string value, List<HotkeyChange> changes)
    {
        var previous = document.GetValue(Section, keySeqKey);
        if (document.SetValue(Section, keySeqKey, value))
            changes.Add(new HotkeyChange(fileName, Section, keySeqKey, previous, value.Length == 0 ? "(cleared)" : value));

        var defaultKey = keySeqKey + @"\default";
        var previousDefault = document.GetValue(Section, defaultKey);
        if (document.SetValue(Section, defaultKey, "false"))
            changes.Add(new HotkeyChange(fileName, Section, defaultKey, previousDefault, "false"));
    }

    /// <summary>A shortcut is registered when its <c>\KeySeq\default</c> flag is present, even if unbound.</summary>
    private static bool ShortcutExists(EmulatorConfigDocument document, string name) =>
        document.GetValue(Section, KeySeqKey(name) + @"\default") is not null;

    private static string KeySeqKey(string name) => $@"{ShortcutPrefix}{name.Replace(" ", "%20")}\KeySeq";

    private static bool IsShortcutKeySeq(string key) =>
        key.StartsWith(ShortcutPrefix, StringComparison.Ordinal) && key.EndsWith(@"\KeySeq", StringComparison.Ordinal);
}
