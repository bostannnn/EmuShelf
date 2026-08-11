using EmuShelf.Core.Hotkeys;

namespace EmuShelf.Integrations.Emulators.Dolphin;

/// <summary>
/// Writes the keyboard-hotkey scheme into Dolphin's <c>Config/Hotkeys.ini</c> <c>[Hotkeys]</c> as
/// fully-qualified keyboard tokens — <c>`&lt;KeyboardDevice&gt;:&lt;Key&gt;`</c> — which resolve
/// regardless of the section's <c>Device =</c> line (that names the controller). The keyboard device
/// backend is platform-specific (see <see cref="KeyboardDevice"/>). Dolphin has no rewind or true
/// fast-forward, so rewind is reported unsupported and fast-forward maps to "Disable Emulation Speed
/// Limit" (hold-to-uncap). Dolphin only writes <c>Config/Hotkeys.ini</c> once a hotkey is customised in
/// its UI, so even a long-used install may have none; rather than refuse, this creates the file with a
/// <c>[Hotkeys]</c> section that Dolphin reads on its next launch — but only when the resolved folder is
/// really Dolphin's config directory (it has a <c>Dolphin.ini</c>). If neither file is there we resolved
/// the wrong folder, so it reports that instead of writing a file Dolphin will never read. The config
/// directory is passed in already resolved (see <see cref="EmulatorUserDirectories.FindDolphinConfigDirectory"/>),
/// because on Linux it is a separate XDG tree, not a <c>Config/</c> subfolder of the user directory.
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

    private readonly string _configDirectory;
    private readonly string _hotkeysPath;
    private readonly string _mainConfigPath;

    public DolphinHotkeyConfigurator(string configDirectory, string backupRoot, Action<string, string>? writeFile = null)
        : base(DolphinDefinition.Instance.Id, "Dolphin", backupRoot, writeFile)
    {
        _configDirectory = Path.GetFullPath(configDirectory);
        _hotkeysPath = Path.Combine(_configDirectory, "Hotkeys.ini");
        // Dolphin always writes Dolphin.ini on first run, so it marks a real Dolphin config directory —
        // used to tell "customised no hotkeys yet" apart from "we resolved the wrong folder".
        _mainConfigPath = Path.Combine(_configDirectory, "Dolphin.ini");
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
        if (text is null && !File.Exists(_mainConfigPath))
        {
            // No Hotkeys.ini *and* no Dolphin.ini in this folder: it isn't Dolphin's config directory (we
            // resolved the wrong place), so report that instead of writing a file Dolphin will never read.
            // Point at what the user can actually do — there is no config-folder picker in Settings, but
            // launching Dolphin writes Dolphin.ini here, and the emulator path drives resolution.
            return HotkeyPlan.NotFound(
                $"{_configDirectory} has no Dolphin.ini, so it isn't Dolphin's config folder. Launch Dolphin once so it writes its configuration, or check the Dolphin path in Settings.");
        }

        // A missing Hotkeys.ini in a real Dolphin user dir is not an error: Dolphin writes it lazily
        // (only once a hotkey is customised), so start from an empty document and let ApplyKeySection
        // create the [Hotkeys] section and our bindings.
        var document = new EmulatorConfigDocument(text ?? string.Empty);
        var fileName = Path.GetFileName(_hotkeysPath);
        var (bindings, changes) = ApplyKeySection(
            document,
            fileName,
            Section,
            profile,
            action => ActionKeys[action],
            _ => "Dolphin has no rewind feature.",
            action => $"`{KeyboardDevice}:{profile[action].Label()}`");

        // Dolphin's built-in slot hotkeys are plain keys (e.g. `Load State Slot 2 = F2`) that resolve to
        // the same physical key as our fully-qualified token but don't match it by value, so the base's
        // value-based clearing misses them. Dolphin serializes these inconsistently — some bare (`F3`),
        // some backtick-quoted (`` `F2` ``) — and both forms bind the same key, so clear either that holds
        // a key we're claiming; otherwise it keeps firing alongside our binding (a Slot-1 save on the same
        // press as Save-to-Selected-Slot = save state twice). Only these two literal forms — `@(Shift+F2)`
        // is a different input and stays.
        foreach (var action in profile.Actions)
        {
            if (ActionKeys[action] is null)
                continue;
            var bareword = profile[action].Label();
            foreach (var value in new[] { bareword, $"`{bareword}`" })
            {
                foreach (var conflicting in document.KeysWithValue(Section, value))
                {
                    if (document.RemoveKey(Section, conflicting))
                        changes.Add(new HotkeyChange(fileName, Section, conflicting, value, "(unbound — conflicted with EmuShelf's scheme)"));
                }
            }
        }

        IReadOnlyList<HotkeyFilePlan> files = document.Changed
            ? [new HotkeyFilePlan(_hotkeysPath, document.ToText())]
            : [];
        return HotkeyPlan.Edited(bindings, files, changes);
    }
}
