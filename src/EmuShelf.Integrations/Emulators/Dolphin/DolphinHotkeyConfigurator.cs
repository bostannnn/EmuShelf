using EmuShelf.Core.Hotkeys;

namespace EmuShelf.Integrations.Emulators.Dolphin;

/// <summary>
/// Writes the keyboard-hotkey scheme into Dolphin's <c>Config/Hotkeys.ini</c> <c>[Hotkeys]</c> as
/// fully-qualified keyboard tokens — <c>`&lt;KeyboardDevice&gt;:&lt;Key&gt;`</c> — which resolve
/// regardless of the section's <c>Device =</c> line (that names the controller). The keyboard device
/// backend is platform-specific (see <see cref="KeyboardDevice"/>). Dolphin has no rewind or true
/// fast-forward, so rewind is reported unsupported and fast-forward maps to "Disable Emulation Speed
/// Limit" (hold-to-uncap).
/// </summary>
public sealed class DolphinHotkeyConfigurator : HotkeyConfiguratorBase
{
    private const string Section = "Hotkeys";

    /// <summary>
    /// Dolphin's keyboard input device, which the fully-qualified token binds against. The backend is
    /// platform-specific: DInput on Windows (verified from the user's <c>GCKeyNew.ini</c>), Quartz on
    /// macOS, and XInput2 on Linux/X11. The Windows value is verified against a real config; the
    /// macOS/Linux values match Dolphin's documented device names but are not yet config-verified there.
    /// </summary>
    public static string KeyboardDevice { get; } =
        OperatingSystem.IsWindows() ? "DInput/0/Keyboard Mouse"
        : OperatingSystem.IsMacOS() ? "Quartz/0/Keyboard Mouse"
        : "XInput2/0/Virtual core pointer";

    private static readonly IReadOnlyDictionary<HotkeyAction, string?> ActionKeys = new Dictionary<HotkeyAction, string?>
    {
        [HotkeyAction.CloseGame] = "General/Exit",
        [HotkeyAction.Rewind] = null,
        [HotkeyAction.FastForward] = "Emulation Speed/Disable Emulation Speed Limit",
        [HotkeyAction.SaveState] = "Save State/Save to Selected Slot",
        [HotkeyAction.LoadState] = "Load State/Load from Selected Slot",
    };

    private readonly string _hotkeysPath;

    public DolphinHotkeyConfigurator(string userDirectory, string backupRoot, Action<string, string>? writeFile = null)
        : base(DolphinDefinition.Instance.Id, "Dolphin", backupRoot, writeFile)
    {
        _hotkeysPath = Path.Combine(Path.GetFullPath(userDirectory), "Config", "Hotkeys.ini");
    }

    public override IReadOnlyList<HotkeyActionSupport> DescribeSupport(HotkeyProfile profile) =>
        profile.Actions
            .Select(action => ActionKeys[action] is null
                ? HotkeyActionSupport.Unsupported(action, "Dolphin has no rewind feature.")
                : HotkeyActionSupport.Supported(action))
            .ToArray();

    protected override IReadOnlyList<string> ManagedFiles =>
        File.Exists(_hotkeysPath) ? [_hotkeysPath] : [];

    private protected override HotkeyPlan BuildPlan(HotkeyProfile profile, CancellationToken cancellationToken)
    {
        var text = ReadTextOrNull(_hotkeysPath);
        if (text is null)
            return HotkeyPlan.NotFound("Dolphin's Hotkeys.ini was not found.");

        var document = new EmulatorConfigDocument(text);
        var fileName = Path.GetFileName(_hotkeysPath);
        var (bindings, changes) = ApplyKeySection(
            document,
            fileName,
            Section,
            profile,
            action => ActionKeys[action],
            _ => "Dolphin has no rewind feature.",
            action => $"`{KeyboardDevice}:{profile[action].Label()}`");

        // Dolphin's built-in slot hotkeys are bareword keys (e.g. `Load State Slot 2 = F2`) that resolve
        // to the same physical key as our fully-qualified token but don't match it by value, so the
        // base's value-based clearing misses them. Clear any that hold a bareword key we're claiming, so
        // pressing the key can't also trigger a slot action (only barewords — `@(Shift+F2)` is a
        // different input and stays).
        foreach (var action in profile.Actions)
        {
            if (ActionKeys[action] is null)
                continue;
            var bareword = profile[action].Label();
            foreach (var conflicting in document.KeysWithValue(Section, bareword))
            {
                if (document.RemoveKey(Section, conflicting))
                    changes.Add(new HotkeyChange(fileName, Section, conflicting, bareword, "(unbound — conflicted with EmuShelf's scheme)"));
            }
        }

        IReadOnlyList<HotkeyFilePlan> files = document.Changed
            ? [new HotkeyFilePlan(_hotkeysPath, document.ToText())]
            : [];
        return HotkeyPlan.Edited(bindings, files, changes);
    }
}
