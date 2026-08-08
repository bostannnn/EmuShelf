using EmuShelf.Core.Hotkeys;

namespace EmuShelf.Integrations.Emulators.RetroArch;

/// <summary>
/// Writes the keyboard-hotkey scheme into RetroArch's section-less <c>retroarch.cfg</c>. RetroArch's
/// keyboard hotkeys already default to the scheme's keys (rewind=r, fast-forward=l, save=f2, load=f4),
/// so this sets each action's <c>input_*</c> key from the profile, sets the exit key to <c>f8</c> (its
/// default is <c>escape</c>), ensures <c>rewind_enable</c>, and clears any controller hotkey buttons an
/// earlier controller-scheme version wrote (setting the <c>*_btn</c> keys back to <c>nul</c>) so the
/// keyboard keys are the only hotkeys. Keyboard keys are the same on every driver, so — unlike the old
/// raw joypad button numbers — nothing needs resolving.
/// </summary>
/// <remarks>
/// Two RetroArch defaults collide with a bare apply and are corrected here: its built-in screenshot key
/// is also <c>f8</c> (the scheme's close key), so a single F8 would screenshot as well as close —
/// <c>input_screenshot</c> is pointed at <c>nul</c> (even when absent, since RetroArch falls back to the
/// internal <c>f8</c> default) unless the user already moved it off F8. And <c>quit_press_twice</c>
/// defaults to <c>true</c>, so exit needs two presses; it is set to <c>false</c> so a single close
/// (behind a deliberate Steam Input combo) quits.
/// </remarks>
public sealed class RetroArchHotkeyConfigurator : HotkeyConfiguratorBase
{
    /// <summary>The <c>input_*</c> cfg key each action drives; the quoted key value comes from the profile.</summary>
    private static readonly IReadOnlyDictionary<HotkeyAction, string> ActionKeys = new Dictionary<HotkeyAction, string>
    {
        [HotkeyAction.CloseGame] = "input_exit_emulator",
        [HotkeyAction.Rewind] = "input_rewind",
        [HotkeyAction.FastForward] = "input_hold_fast_forward",
        [HotkeyAction.SaveState] = "input_save_state",
        [HotkeyAction.LoadState] = "input_load_state",
    };

    /// <summary>Controller hotkey buttons an earlier controller-scheme version may have set; cleared to nul.</summary>
    private static readonly IReadOnlyList<string> ControllerButtonKeys =
    [
        "input_enable_hotkey_btn",
        "input_exit_emulator_btn",
        "input_rewind_btn",
        "input_hold_fast_forward_btn",
        "input_save_state_btn",
        "input_load_state_btn",
    ];

    private readonly string _configPath;

    public RetroArchHotkeyConfigurator(string configurationDirectory, string backupRoot, Action<string, string>? writeFile = null)
        : base(RetroArchDefinition.Instance.Id, "RetroArch", backupRoot, writeFile)
    {
        _configPath = Path.Combine(Path.GetFullPath(configurationDirectory), "retroarch.cfg");
    }

    public override IReadOnlyList<HotkeyActionSupport> DescribeSupport(HotkeyProfile profile) =>
        profile.Actions.Select(HotkeyActionSupport.Supported).ToArray();

    protected override IReadOnlyList<string> ManagedFiles =>
        File.Exists(_configPath) ? [_configPath] : [];

    private protected override HotkeyPlan BuildPlan(HotkeyProfile profile, CancellationToken cancellationToken)
    {
        var text = ReadTextOrNull(_configPath);
        if (text is null)
            return HotkeyPlan.NotFound("RetroArch's retroarch.cfg was not found.");

        var document = new EmulatorConfigDocument(text);
        var fileName = Path.GetFileName(_configPath);
        var changes = new List<HotkeyChange>();
        var bindings = new List<HotkeyBindingResult>();

        foreach (var action in profile.Actions)
        {
            var key = profile[action];
            SetCfg(document, fileName, ActionKeys[action], key.Label().ToLowerInvariant(), changes);
            bindings.Add(new HotkeyBindingResult(action, HotkeyBindingStatus.Bound, key.Label()));
        }

        // RetroArch's built-in screenshot key is also f8 — the same key we use for close — so a single
        // F8 would screenshot as well as close. It is the only stock hotkey that collides (our other
        // keys are RetroArch's own defaults for the same actions), so clear it specifically rather than
        // scanning every input_* key, which would risk unbinding player game-input keys. It even falls
        // back to the internal f8 default when absent, so neutralise it unless the user moved it off F8.
        var closeValue = $"\"{profile[HotkeyAction.CloseGame].Label().ToLowerInvariant()}\"";
        var screenshot = document.GetValue(null, "input_screenshot");
        if (screenshot is null || string.Equals(screenshot, closeValue, StringComparison.Ordinal))
            SetCfg(document, fileName, "input_screenshot", "nul", changes);

        // Rewind must be enabled or its hotkey is inert.
        SetCfg(document, fileName, "rewind_enable", "true", changes);

        // Exit defaults to needing two presses; a single deliberate close should quit.
        SetCfg(document, fileName, "quit_press_twice", "false", changes);

        // Clear any controller hotkey buttons an earlier controller-scheme version wrote, so a stale
        // joypad number can't fire an action alongside the keyboard keys.
        foreach (var buttonKey in ControllerButtonKeys)
            ClearCfg(document, fileName, buttonKey, changes);

        IReadOnlyList<HotkeyFilePlan> files = document.Changed
            ? [new HotkeyFilePlan(_configPath, document.ToText())]
            : [];
        return HotkeyPlan.Edited(bindings, files, changes);
    }

    /// <summary>Sets a cfg key to a quoted value, recording the change.</summary>
    private static void SetCfg(EmulatorConfigDocument document, string fileName, string key, string value, List<HotkeyChange> changes)
    {
        var quoted = $"\"{value}\"";
        var previous = document.GetValue(null, key);
        if (document.SetValue(null, key, quoted))
            changes.Add(new HotkeyChange(fileName, null, key, previous, quoted));
    }

    /// <summary>Sets a cfg key to <c>"nul"</c> only when it exists and holds some other value.</summary>
    private static void ClearCfg(EmulatorConfigDocument document, string fileName, string key, List<HotkeyChange> changes)
    {
        var previous = document.GetValue(null, key);
        if (previous is null || previous == "\"nul\"")
            return;
        if (document.SetValue(null, key, "\"nul\""))
            changes.Add(new HotkeyChange(fileName, null, key, previous, "\"nul\""));
    }
}
