using System;
using Android.App;
using Android.Content;
using EmuShelf.Core.Diagnostics;

namespace EmuShelf.App.Android.Services;

/// <summary>What <see cref="AndroidEmulatorProcessTerminator.CloseEmulator"/> did, so the shell can toast it.</summary>
public enum EmulatorCloseOutcome
{
    /// <summary>Nothing was attempted (no package, our own package, or no context).</summary>
    NotAttempted,

    /// <summary>The emulator was force-stopped via Shizuku (and its Recents card removed).</summary>
    Closed,

    /// <summary>Shizuku is running but unauthorized; its permission dialog was shown. The next return closes it.</summary>
    PermissionRequested,

    /// <summary>Shizuku is not installed/running, so only the (usually no-op) background kill was tried.</summary>
    ShizukuUnavailable,
}

/// <summary>
/// Closes a launched emulator's process once EmuShelf is back in the foreground, so a heavy emulator does
/// not linger in the background draining the battery.
///
/// The honest constraint: Android gives an ordinary third-party app <em>no</em> way to truly force-stop
/// another app. <c>ActivityManager.killBackgroundProcesses</c> (the original approach) is deprecated and,
/// by design, skips any process that owns a foreground service — which is exactly what an emulator holds
/// while emulating — so it never actually stops them. The only rootless mechanism that works is
/// <b>Shizuku</b>: its helper runs at the adb-shell UID (2000), which holds <c>FORCE_STOP_PACKAGES</c>, so
/// <c>am force-stop &lt;package&gt;</c> routed through Shizuku is the genuine Settings-style force stop. See
/// <see cref="AndroidShizuku"/> and DECISIONS 2026-08-26.
///
/// Deliberately best-effort and never fatal: if Shizuku is not installed/running/authorized we fall back to
/// the (mostly no-op) background kill and log guidance, and every path is caught so returning to EmuShelf
/// can never turn into a crash.
/// </summary>
public sealed class AndroidEmulatorProcessTerminator
{
    private readonly Func<Context?> _context;
    private readonly IAppLogger _logger;
    private readonly AndroidShizuku _shizuku;

    public AndroidEmulatorProcessTerminator(Func<Context?> context, IAppLogger logger)
    {
        _context = context;
        _logger = logger;
        _shizuku = new AndroidShizuku(logger);
    }

    /// <summary>
    /// Closes <paramref name="packageName"/> and returns what happened, so the caller can toast the right
    /// message. No-ops (<see cref="EmulatorCloseOutcome.NotAttempted"/>) when the package is EmuShelf itself,
    /// is empty, or there is no context. Prefers a real Shizuku force-stop (and then drops the emulator's
    /// leftover Recents card); when Shizuku is present but not yet authorized it triggers Shizuku's one-time
    /// permission dialog (this round closes nothing, the next return does); otherwise it degrades to the
    /// deprecated background kill and reports Shizuku as unavailable.
    /// </summary>
    public EmulatorCloseOutcome CloseEmulator(string? packageName)
    {
        if (string.IsNullOrEmpty(packageName))
            return EmulatorCloseOutcome.NotAttempted;

        var ctx = _context();
        if (ctx is null)
        {
            _logger.Warning($"Cannot close {packageName}: no Android context is available.");
            return EmulatorCloseOutcome.NotAttempted;
        }

        // Never kill ourselves: the return handler runs inside EmuShelf, and our package can legitimately be
        // the target of a bad record. Force-stopping our own package would only fight the OS.
        if (string.Equals(packageName, ctx.PackageName, StringComparison.Ordinal))
            return EmulatorCloseOutcome.NotAttempted;

        // Preferred path: a real force-stop via Shizuku. This is the only rootless mechanism that actually
        // stops an emulator holding a foreground service.
        if (_shizuku.IsRunning)
        {
            if (_shizuku.HasPermission)
            {
                if (_shizuku.ForceStop(packageName))
                {
                    _logger.Information($"Asked Shizuku to force-stop the emulator {packageName}.");
                    // Also drop the emulator's leftover Recents card so it disappears from the app switcher,
                    // not just the process list. Best-effort and independent of the stop's success.
                    _shizuku.RemoveFromRecents(packageName);
                    return EmulatorCloseOutcome.Closed;
                }
                // Shizuku is authorized but the call itself failed — fall through to the best-effort kill.
            }
            else
            {
                // Shizuku is running but has not authorized EmuShelf yet. Pop its one-time permission dialog;
                // nothing is stopped this round, and the next return will force-stop. This doubles as the
                // opt-in onboarding trigger, so no separate setup screen is needed for the common case.
                _logger.Information(
                    "Shizuku is running but EmuShelf is not authorized yet; requesting permission now. " +
                    "Grant it in the Shizuku prompt and the emulator will be closed on the next return.");
                _shizuku.RequestPermission();
                return EmulatorCloseOutcome.PermissionRequested;
            }
        }
        else
        {
            _logger.Information(
                $"Shizuku is not available, so {packageName} cannot be truly force-stopped. Install Shizuku, " +
                "start it (once per boot), and grant EmuShelf permission to enable close-on-return.");
        }

        // Fallback: the deprecated killBackgroundProcesses. It cannot stop a foreground-service emulator, so
        // this is usually a no-op, but it costs nothing and reclaims whatever the OS still allows on devices
        // without Shizuku.
        TryKillBackground(ctx, packageName);
        return EmulatorCloseOutcome.ShizukuUnavailable;
    }

    /// <summary>
    /// Called when the user turns the close-on-return setting on, so the Shizuku permission is requested up
    /// front rather than only on the first return. Returns a short status to show the user, or null when
    /// everything is already in place (Shizuku running and authorized) and nothing needs saying.
    /// </summary>
    public string? PreparePrivilege()
    {
        if (!_shizuku.IsRunning)
        {
            return "To close emulators when you return, install Shizuku (a free companion app), start it, " +
                "then grant EmuShelf permission.";
        }

        if (!_shizuku.HasPermission)
        {
            _shizuku.RequestPermission();
            return "Grant EmuShelf permission in the Shizuku prompt so it can close emulators on return.";
        }

        return null;
    }

    private bool TryKillBackground(Context ctx, string packageName)
    {
        try
        {
            if (ctx.GetSystemService(Context.ActivityService) is not ActivityManager manager)
            {
                _logger.Warning($"Cannot close {packageName}: the ActivityManager is unavailable.");
                return false;
            }

            manager.KillBackgroundProcesses(packageName);
            _logger.Information($"Fell back to killBackgroundProcesses for {packageName} (best-effort).");
            return true;
        }
        catch (Exception ex)
        {
            // Best-effort: the emulator stays open, but returning to EmuShelf must still succeed.
            _logger.Warning($"Could not close the emulator process {packageName}.", ex);
            return false;
        }
    }
}
