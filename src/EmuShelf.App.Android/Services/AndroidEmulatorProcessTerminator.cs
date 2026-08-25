using System;
using Android.App;
using Android.Content;
using EmuShelf.Core.Diagnostics;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// Asks Android to close a launched emulator's process once EmuShelf is back in the foreground, so a heavy
/// emulator does not linger in the background draining the battery. This is the one piece a third-party app
/// on Android actually can do: <c>ActivityManager.killBackgroundProcesses</c> (guarded by the manifest's
/// <c>KILL_BACKGROUND_PROCESSES</c> permission) can terminate another package's <em>background</em>
/// processes. A foreground app cannot be force-stopped without root — but by the time this runs EmuShelf has
/// regained the top-resumed slot, which means the emulator is already backgrounded and is a legal target.
///
/// Deliberately best-effort: some OEM firmware ignores the request, and an emulator holding a sticky
/// foreground service can survive it. A failure must never turn returning to EmuShelf into a crash, so every
/// path here is caught and logged, and the caller treats "could not close it" as a no-op.
/// </summary>
public sealed class AndroidEmulatorProcessTerminator(Func<Context?> context, IAppLogger logger)
{
    /// <summary>
    /// Requests that Android terminate <paramref name="packageName"/>'s background processes. No-ops (returns
    /// false) when the package is EmuShelf itself — killing our own background processes here would be
    /// pointless and risky — when it is empty, or when there is no context to reach the service.
    /// </summary>
    public bool CloseEmulator(string? packageName)
    {
        if (string.IsNullOrEmpty(packageName))
            return false;

        var ctx = context();
        if (ctx is null)
        {
            logger.Warning($"Cannot close {packageName}: no Android context is available.");
            return false;
        }

        // Never kill ourselves: the return handler runs inside EmuShelf, and our package can legitimately be
        // the target of a bad record. killBackgroundProcesses on our own package would only fight the OS.
        if (string.Equals(packageName, ctx.PackageName, StringComparison.Ordinal))
            return false;

        try
        {
            if (ctx.GetSystemService(Context.ActivityService) is not ActivityManager manager)
            {
                logger.Warning($"Cannot close {packageName}: the ActivityManager is unavailable.");
                return false;
            }

            manager.KillBackgroundProcesses(packageName);
            logger.Information($"Asked Android to close the emulator process {packageName}.");
            return true;
        }
        catch (Exception ex)
        {
            // Best-effort: the emulator stays open, but returning to EmuShelf must still succeed.
            logger.Warning($"Could not close the emulator process {packageName}.", ex);
            return false;
        }
    }
}
