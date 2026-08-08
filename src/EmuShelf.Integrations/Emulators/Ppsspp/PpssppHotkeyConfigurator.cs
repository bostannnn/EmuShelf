using EmuShelf.Core.Hotkeys;

namespace EmuShelf.Integrations.Emulators.Ppsspp;

/// <summary>
/// Writes the keyboard-hotkey scheme into PPSSPP's <c>controls.ini</c> <c>[ControlMapping]</c>. PPSSPP
/// encodes a binding as <c>&lt;deviceEnum&gt;-&lt;NKCODE&gt;</c>; device <c>1</c> is the keyboard and the
/// codes are Android <c>NKCODE</c>s (R=46, L=40, F2=132, F4=134, F8=138). These are single keys, not
/// combos, so no <c>AllowMappingCombos</c> flag is needed. PPSSPP's close-game control is literally
/// named <c>Exit App</c>.
/// </summary>
public sealed class PpssppHotkeyConfigurator : HotkeyConfiguratorBase
{
    private const string MappingSection = "ControlMapping";

    /// <summary>Android NKCODEs for the scheme's keys (device 1 = keyboard).</summary>
    private static readonly IReadOnlyDictionary<HotkeyKey, int> NkCodes = new Dictionary<HotkeyKey, int>
    {
        [HotkeyKey.R] = 46,
        [HotkeyKey.L] = 40,
        [HotkeyKey.F2] = 132,
        [HotkeyKey.F4] = 134,
        [HotkeyKey.F8] = 138,
    };

    private static readonly IReadOnlyDictionary<HotkeyAction, string> ActionKeys = new Dictionary<HotkeyAction, string>
    {
        [HotkeyAction.CloseGame] = "Exit App",
        [HotkeyAction.Rewind] = "Rewind",
        [HotkeyAction.FastForward] = "Fast-forward",
        [HotkeyAction.SaveState] = "Save State",
        [HotkeyAction.LoadState] = "Load State",
    };

    private readonly string _controlsPath;

    public PpssppHotkeyConfigurator(string configurationDirectory, string backupRoot, Action<string, string>? writeFile = null)
        : base(PpssppDefinition.Instance.Id, "PPSSPP", backupRoot, writeFile)
    {
        _controlsPath = Path.Combine(Path.GetFullPath(configurationDirectory), "controls.ini");
    }

    public override IReadOnlyList<HotkeyActionSupport> DescribeSupport(HotkeyProfile profile) =>
        profile.Actions.Select(HotkeyActionSupport.Supported).ToArray();

    protected override IReadOnlyList<string> ManagedFiles =>
        File.Exists(_controlsPath) ? [_controlsPath] : [];

    private protected override HotkeyPlan BuildPlan(HotkeyProfile profile, CancellationToken cancellationToken)
    {
        var text = ReadTextOrNull(_controlsPath);
        if (text is null)
            return HotkeyPlan.NotFound("PPSSPP's controls.ini was not found.");

        var controls = new EmulatorConfigDocument(text);
        var (bindings, changes) = ApplyKeySection(
            controls,
            Path.GetFileName(_controlsPath),
            MappingSection,
            profile,
            action => ActionKeys[action],
            _ => "This emulator has no such feature.",
            action => $"1-{NkCodes[profile[action]]}");

        IReadOnlyList<HotkeyFilePlan> files = controls.Changed
            ? [new HotkeyFilePlan(_controlsPath, controls.ToText())]
            : [];
        return HotkeyPlan.Edited(bindings, files, changes);
    }
}
