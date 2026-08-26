using System;
using System.Threading.Tasks;
using EmuShelf.Core.Diagnostics;
using Java.Lang;
using Java.Lang.Reflect;
using Exception = System.Exception;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// A thin reflection bridge to the Shizuku client library (<c>rikka.shizuku.Shizuku</c>). The Shizuku
/// modules ship inside the APK but are <em>not</em> bound to C# (see the <c>Bind="false"</c> Maven
/// references in the head's csproj), so every call here goes through <c>java.lang.reflect</c> against the
/// always-available Mono.Android reflection types. Nothing generated is required — the classes only need
/// to be present at runtime, which the manifest's <c>ShizukuProvider</c> guarantees, and the Shizuku
/// manager app hands the privileged binder to that provider when this process starts.
///
/// The one privileged operation EmuShelf needs is a <em>real</em> force-stop of a launched emulator.
/// <c>ActivityManager.killBackgroundProcesses</c> (the previous approach) is deprecated and skips any
/// process with a running foreground service — which is exactly what every emulator holds while emulating —
/// so it never actually stops them. Shizuku runs its helper at the adb-shell UID (2000), which holds
/// <c>FORCE_STOP_PACKAGES</c>, so <c>am force-stop &lt;package&gt;</c> through Shizuku is the genuine
/// Settings-style force stop, without root. On a non-rooted device the user installs Shizuku, starts it once
/// per boot (wireless debugging), and grants EmuShelf permission the first time it is asked.
/// </summary>
internal sealed class AndroidShizuku
{
    // PackageManager.PERMISSION_GRANTED. checkSelfPermission returns this int when EmuShelf is authorized.
    private const int PermissionGranted = 0;

    // Echoed back to Shizuku's (unused here) result listener; any stable value is fine.
    private const int RequestCode = 0xE3F;

    private readonly IAppLogger _logger;

    // rikka.shizuku.Shizuku, resolved once. Null when Shizuku is not installed at all (class absent), which
    // every accessor below treats as "unavailable" and returns false for.
    private readonly Class? _shizuku;

    public AndroidShizuku(IAppLogger logger)
    {
        _logger = logger;
        _shizuku = ResolveShizukuClass(logger);
    }

    private static Class? ResolveShizukuClass(IAppLogger logger)
    {
        try
        {
            return Class.ForName("rikka.shizuku.Shizuku");
        }
        catch (Exception)
        {
            // Absent from the runtime class path: Shizuku support was not packaged. Not an error — the
            // caller degrades to the best-effort fallback and guidance.
            logger.Information("Shizuku client classes are not present; rootless force-stop is unavailable.");
            return null;
        }
    }

    /// <summary>True only when the Shizuku service is running and its binder has reached this process.</summary>
    public bool IsRunning
    {
        get
        {
            if (_shizuku is null)
                return false;
            try
            {
                return CallStaticBool("pingBinder");
            }
            catch (Exception ex)
            {
                _logger.Warning("Shizuku pingBinder failed.", ex);
                return false;
            }
        }
    }

    /// <summary>True when Shizuku has granted EmuShelf permission to use its privileged APIs.</summary>
    public bool HasPermission
    {
        get
        {
            if (_shizuku is null)
                return false;
            try
            {
                return CallStaticInt("checkSelfPermission") == PermissionGranted;
            }
            catch (Exception ex)
            {
                _logger.Warning("Shizuku checkSelfPermission failed.", ex);
                return false;
            }
        }
    }

    /// <summary>
    /// Asks Shizuku to show its one-time permission dialog for EmuShelf. Safe no-op when Shizuku is not
    /// running (there is no service to ask). The grant result is picked up by a later
    /// <see cref="HasPermission"/> check rather than a listener, which keeps this bridge stateless.
    /// </summary>
    public void RequestPermission()
    {
        if (_shizuku is null || !IsRunning)
            return;
        try
        {
            var method = _shizuku.GetMethod("requestPermission", Integer.Type!);
            method?.Invoke(null, Integer.ValueOf(RequestCode));
        }
        catch (Exception ex)
        {
            _logger.Warning("Shizuku requestPermission failed.", ex);
        }
    }

    /// <summary>
    /// Runs <c>am force-stop &lt;package&gt;</c> at Shizuku's shell privilege — the genuine force stop.
    /// Caller must have confirmed <see cref="IsRunning"/> and <see cref="HasPermission"/> first.
    /// </summary>
    public bool ForceStop(string packageName) =>
        RunPrivileged(new[] { "am", "force-stop", packageName }, $"force-stop {packageName}");

    /// <summary>
    /// Drops the emulator's leftover card from the Recents/overview list. A force-stop kills the process but
    /// leaves the app's task card behind (exactly as the system's own Force Stop button does), so the closed
    /// emulator would otherwise still show in the app switcher. This reads the task id(s) for the package
    /// from <c>dumpsys activity recents</c> and removes each with <c>am stack remove</c> — all shell tools,
    /// run at Shizuku's shell privilege. Best-effort: if nothing matches, the command simply does nothing.
    /// </summary>
    public bool RemoveFromRecents(string packageName)
    {
        // One shell pipeline: find `Task{… #<id> … A=<uid>:<package>}` lines, pull the #id, remove each task.
        var script =
            "dumpsys activity recents | grep -F 'Task{' | grep -F ':" + packageName +
            "' | sed -E 's/.*#([0-9]+).*/\\1/' | while read id; do am stack remove \"$id\"; done";
        return RunPrivileged(new[] { "sh", "-c", script }, $"remove-recents {packageName}");
    }

    /// <summary>
    /// Runs <paramref name="argv"/> as a process at Shizuku's shell privilege. Returns true when the process
    /// was started (its exit code is logged once it finishes). Reflection reaches the private static
    /// <c>Shizuku.newProcess(String[], String[], String)</c>; the returned <c>ShizukuRemoteProcess</c> is
    /// likewise driven by reflection, so no Shizuku type needs a C# binding.
    /// </summary>
    private bool RunPrivileged(string[] argv, string label)
    {
        if (_shizuku is null)
            return false;
        try
        {
            var stringClass = Class.ForName("java.lang.String")!;
            // Build a real java.lang.String[] via reflection so it marshals correctly as the newProcess arg.
            var command = Java.Lang.Reflect.Array.NewInstance(stringClass, argv.Length)!;
            for (var i = 0; i < argv.Length; i++)
                Java.Lang.Reflect.Array.Set(command, i, new Java.Lang.String(argv[i]));
            var stringArrayClass = command.Class; // [Ljava.lang.String;

            var newProcess = _shizuku.GetDeclaredMethod(
                "newProcess", stringArrayClass, stringArrayClass, stringClass);
            if (newProcess is null)
                return false;
            newProcess.Accessible = true;

            // env = null, dir = null: inherit Shizuku's shell environment and working directory.
            var process = newProcess.Invoke(null, command, null, null);
            if (process is null)
                return false;

            // Reap on a background thread so the short-lived command never blocks the return frame.
            ReapAsync(process, label);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Shizuku {label} failed.", ex);
            return false;
        }
    }

    private void ReapAsync(Java.Lang.Object process, string label) => Task.Run(() =>
    {
        try
        {
            var waitFor = process.Class.GetMethod("waitFor");
            var exit = waitFor?.Invoke(process);
            _logger.Information($"Shizuku {label} exited with {exit?.ToString() ?? "?"}.");
        }
        catch (Exception ex)
        {
            _logger.Warning($"Shizuku {label}: could not read the exit code.", ex);
        }
    });

    private bool CallStaticBool(string method)
    {
        var m = _shizuku!.GetMethod(method);
        return m?.Invoke(null) is Java.Lang.Boolean b && b.BooleanValue();
    }

    private int CallStaticInt(string method)
    {
        var m = _shizuku!.GetMethod(method);
        return m?.Invoke(null) is Integer i ? i.IntValue() : -1;
    }
}
