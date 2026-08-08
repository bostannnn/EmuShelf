using EmuShelf.Core.Hotkeys;

namespace EmuShelf.Integrations.Emulators.Rpcs3;

/// <summary>
/// Writes the keyboard-hotkey scheme into RPCS3's <c>GuiConfigs/CurrentSettings.ini</c>. RPCS3 exposes
/// only a close-game hotkey to the scheme: its GUI shortcuts serialize under the <c>[Shortcuts]</c>
/// group, where <c>game_window_stop</c> is the "Stop/Exit Game" action (unbound by default). Save state
/// is Ctrl+S / a suspend-resume model with no single hotkey, and there is no load-state, rewind, or
/// fast-forward hotkey, so those actions are reported unsupported.
/// </summary>
public sealed class Rpcs3HotkeyConfigurator : HotkeyConfiguratorBase
{
    private const string Section = "Shortcuts";
    private const string StopShortcut = "game_window_stop";
    private const string Unsupported = "RPCS3 exposes no keyboard hotkey for this action.";

    private readonly string _configPath;

    public Rpcs3HotkeyConfigurator(string installationDirectory, string backupRoot, Action<string, string>? writeFile = null)
        : base(Rpcs3Definition.Instance.Id, "RPCS3", backupRoot, writeFile)
    {
        _configPath = Path.Combine(Path.GetFullPath(installationDirectory), "GuiConfigs", "CurrentSettings.ini");
    }

    public override IReadOnlyList<HotkeyActionSupport> DescribeSupport(HotkeyProfile profile) =>
        profile.Actions
            .Select(action => action == HotkeyAction.CloseGame
                ? HotkeyActionSupport.Supported(action)
                : HotkeyActionSupport.Unsupported(action, Unsupported))
            .ToArray();

    protected override IReadOnlyList<string> ManagedFiles =>
        File.Exists(_configPath) ? [_configPath] : [];

    private protected override HotkeyPlan BuildPlan(HotkeyProfile profile, CancellationToken cancellationToken)
    {
        var text = ReadTextOrNull(_configPath);
        if (text is null)
            return HotkeyPlan.NotFound("RPCS3's CurrentSettings.ini was not found.");

        var document = new EmulatorConfigDocument(text);
        var fileName = Path.GetFileName(_configPath);
        var changes = new List<HotkeyChange>();
        var bindings = new List<HotkeyBindingResult>();

        foreach (var action in profile.Actions)
        {
            var label = profile[action].Label();
            if (action != HotkeyAction.CloseGame)
            {
                bindings.Add(new HotkeyBindingResult(action, HotkeyBindingStatus.Unsupported, label, Unsupported));
                continue;
            }

            var previous = document.GetValue(Section, StopShortcut);
            if (document.SetValue(Section, StopShortcut, label))
                changes.Add(new HotkeyChange(fileName, Section, StopShortcut, previous, label));
            bindings.Add(new HotkeyBindingResult(action, HotkeyBindingStatus.Bound, label));
        }

        IReadOnlyList<HotkeyFilePlan> files = document.Changed
            ? [new HotkeyFilePlan(_configPath, document.ToText())]
            : [];
        return HotkeyPlan.Edited(bindings, files, changes);
    }
}
