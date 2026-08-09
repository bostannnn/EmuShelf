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
///
/// RetroArch also has a single hotkey-enable *gate* shared by keyboard and controller
/// (<c>input_enable_hotkey</c> / <c>_btn</c>). The keyboard scheme needs that gate OFF (unset ⇒ hotkeys
/// are always active) so a bare Steam-Input key like <c>f2</c> fires without a modifier — but "off" also
/// un-gates any *controller* button RetroArch has bound as a hotkey, so a stock pad autoconfig that maps
/// save-state-slot ± to the D-pad (or screenshot/pause/fps/runahead to the face buttons) makes those fire
/// during play — e.g. D-pad left/right silently changing the save slot. Since the gate can't be "keyboard
/// only", the controller hotkey bindings are cleared instead: this nul's the <c>_btn</c> and <c>_axis</c>
/// bindings of the scheme's own actions plus those common autoconfig hotkeys, so the pad is game-input
/// only and the keyboard (via Steam Input) is the sole hotkey path. All of it is backed up and revertible.
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

    /// <summary>
    /// Hotkey controls whose controller bindings (<c>&lt;control&gt;_btn</c> and <c>&lt;control&gt;_axis</c>)
    /// are cleared to <c>nul</c> on apply so an always-on hotkey can't fire from a bare pad press (see the
    /// remarks on the class). Covers the scheme's own actions — a leftover trigger-axis bind for rewind or
    /// fast-forward would otherwise fire alongside the keyboard key — plus <c>input_enable_hotkey</c> (nul
    /// keeps the gate off) and the hotkeys a stock autoconfig commonly lands on the D-pad / face buttons.
    /// Game inputs (<c>input_playerN_*</c>) are never touched, so the pad still plays.
    /// </summary>
    private static readonly IReadOnlyList<string> ClearedControllerHotkeys =
    [
        "input_enable_hotkey",
        "input_exit_emulator",
        "input_rewind",
        "input_hold_fast_forward",
        "input_toggle_fast_forward",
        "input_save_state",
        "input_load_state",
        "input_state_slot_increase",
        "input_state_slot_decrease",
        "input_screenshot",
        "input_pause_toggle",
        "input_fps_toggle",
        "input_runahead_toggle",
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
        if (profile.Actions.Contains(HotkeyAction.CloseGame))
        {
            var closeValue = $"\"{profile[HotkeyAction.CloseGame].Label().ToLowerInvariant()}\"";
            var screenshot = document.GetValue(null, "input_screenshot");
            if (screenshot is null || string.Equals(screenshot, closeValue, StringComparison.Ordinal))
                SetCfg(document, fileName, "input_screenshot", "nul", changes);
        }

        // Rewind must be enabled or its hotkey is inert.
        SetCfg(document, fileName, "rewind_enable", "true", changes);

        // Exit defaults to needing two presses; a single deliberate close should quit.
        SetCfg(document, fileName, "quit_press_twice", "false", changes);

        // Clear the controller (joypad + axis) bindings of the scheme's actions and the hotkeys a stock
        // autoconfig lands on game-facing buttons, so RetroArch's always-on hotkey mode can't fire one
        // from a bare pad press (e.g. D-pad left/right changing the save-state slot mid-game).
        foreach (var control in ClearedControllerHotkeys)
        {
            ClearCfg(document, fileName, $"{control}_btn", changes);
            ClearCfg(document, fileName, $"{control}_axis", changes);
        }

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
