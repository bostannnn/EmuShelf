using EmuShelf.Core.Settings;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// Android runs the couch modes only: Gamepad, and Handheld (M45) on a dual-screen handheld with a
/// presentation display. There is no desktop window shell, so Desktop is never offered and a request for
/// it is ignored. The choice between Gamepad and Handheld is persisted in <see cref="AppSettings"/>; a saved
/// Handheld choice falls back to Gamepad when the second screen is not there at startup.
/// </summary>
public sealed class AndroidInterfaceModeService : IInterfaceModeService
{
    private readonly ISettingsService _settings;
    private readonly Func<bool> _hasPresentationDisplay;

    public AndroidInterfaceModeService(ISettingsService settings, Func<bool> hasPresentationDisplay)
    {
        _settings = settings;
        _hasPresentationDisplay = hasPresentationDisplay;
        Current = settings.Load().InterfaceMode == InterfaceMode.Handheld && hasPresentationDisplay()
            ? InterfaceMode.Handheld
            : InterfaceMode.Gamepad;
        global::Android.Util.Log.Info(LogTag, $"Interface mode at start: {Current} (Handheld available: {hasPresentationDisplay()})");
    }

    private const string LogTag = "EmuShelfBoot";

    public InterfaceMode Current { get; private set; }

    // Nothing on the command line forces the mode here; the shared UI hides the Desktop affordances through
    // SupportsDesktopMode instead.
    public bool IsCommandLineOverride => false;

    // Android has no window shell at all, so Desktop mode is absent rather than merely locked.
    public bool SupportsDesktopMode => false;

    public bool SupportsHandheldMode => _hasPresentationDisplay();

    public event EventHandler<InterfaceMode>? ModeChanged;

    public async Task SetModeAsync(InterfaceMode mode, CancellationToken cancellationToken = default)
    {
        if (mode == InterfaceMode.Desktop || mode == Current)
            return;
        if (mode == InterfaceMode.Handheld && !SupportsHandheldMode)
            return;

        Current = mode;
        global::Android.Util.Log.Info(LogTag, $"Interface mode switched to {mode}");
        ModeChanged?.Invoke(this, mode);
        await Task.Run(() => _settings.Update(latest => latest with { InterfaceMode = mode }), cancellationToken);
    }
}
