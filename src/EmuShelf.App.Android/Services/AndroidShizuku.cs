using System;
using System.Threading.Tasks;
using Android.Runtime;
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

    // The private static Shizuku.newProcess(String[], String[], String) and the java.lang.String class,
    // resolved once in the constructor rather than on every privileged call (they never change).
    private readonly Method? _newProcess;
    private readonly Class? _stringClass;

    public AndroidShizuku(IAppLogger logger)
    {
        _logger = logger;
        _shizuku = ResolveShizukuClass(logger);
        if (_shizuku is not null)
            (_stringClass, _newProcess) = ResolveNewProcess(_shizuku, logger);
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

    private static (Class?, Method?) ResolveNewProcess(Class shizuku, IAppLogger logger)
    {
        try
        {
            var stringClass = Class.ForName("java.lang.String")!;
            // The parameter type is java.lang.String[]; get its Class from a throwaway zero-length array.
            var stringArrayClass = Java.Lang.Reflect.Array.NewInstance(stringClass, 0)!.Class;
            var method = shizuku.GetDeclaredMethod("newProcess", stringArrayClass, stringArrayClass, stringClass);
            if (method is not null)
                method.Accessible = true;
            return (stringClass, method);
        }
        catch (Exception ex)
        {
            logger.Warning("Shizuku.newProcess could not be resolved; privileged commands are unavailable.", ex);
            return (null, null);
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
    /// Runs <c>am force-stop &lt;package&gt;</c> at Shizuku's shell privilege — the genuine force stop — and
    /// returns true only when the command actually exited 0. Caller must have confirmed <see cref="IsRunning"/>
    /// and <see cref="HasPermission"/> first.
    /// </summary>
    public async Task<bool> ForceStopAsync(string packageName) =>
        await RunPrivilegedAsync(new[] { "am", "force-stop", packageName }, $"force-stop {packageName}") == 0;

    /// <summary>
    /// Drops the emulator's leftover card from the Recents/overview list. A force-stop kills the process but
    /// leaves the app's task card behind (exactly as the system's own Force Stop button does), so the closed
    /// emulator would otherwise still show in the app switcher. This reads the task id(s) for the package
    /// from <c>dumpsys activity recents</c> and removes each with <c>am stack remove</c> — all shell tools,
    /// run at Shizuku's shell privilege. Best-effort and fire-and-forget: if nothing matches it does nothing.
    /// </summary>
    public void RemoveFromRecents(string packageName)
    {
        // Match the package exactly: the recents "Task{… A=<uid>:<package>}" line ends the package with '}'
        // (or a space before further attributes), so anchoring on `[ }]` stops `com.foo` matching a
        // `com.foobar` task. Dots are escaped so they cannot match arbitrary characters. `grep -oE '#[0-9]+'`
        // yields only real task ids (nothing when a line has none), rather than sed echoing the whole line.
        var pattern = "Task\\{.*A=[0-9]+:" + packageName.Replace(".", "\\.") + "[ }]";
        var script =
            "dumpsys activity recents | grep -E '" + pattern + "'" +
            " | grep -oE '#[0-9]+' | tr -d '#' | while read id; do am stack remove \"$id\"; done";
        // Fire-and-forget: the toast reports the force-stop, not this cosmetic cleanup. Exceptions are caught
        // and logged inside RunPrivilegedAsync, so the discarded task never surfaces an unobserved fault.
        _ = RunPrivilegedAsync(new[] { "sh", "-c", script }, $"remove-recents {packageName}");
    }

    /// <summary>
    /// Runs <paramref name="argv"/> as a process at Shizuku's shell privilege on a background thread and
    /// returns its exit code (-1 when the process could not be started or its result could not be read).
    /// Reflection reaches the private static <c>Shizuku.newProcess</c> (resolved once in the constructor);
    /// the returned <c>ShizukuRemoteProcess</c> is driven by reflection, so no Shizuku type needs a C#
    /// binding. Its stdout is drained before waiting so a chatty command cannot deadlock on a full pipe, and
    /// the process is always destroyed.
    /// </summary>
    private Task<int> RunPrivilegedAsync(string[] argv, string label) => Task.Run(() =>
    {
        if (_newProcess is null || _stringClass is null)
            return -1;

        Java.Lang.Object? process = null;
        try
        {
            var command = Java.Lang.Reflect.Array.NewInstance(_stringClass, argv.Length)!;
            for (var i = 0; i < argv.Length; i++)
                Java.Lang.Reflect.Array.Set(command, i, new Java.Lang.String(argv[i]));

            // env = null, dir = null: inherit Shizuku's shell environment and working directory. The two
            // trailing nulls are the reflected method's nullable String[]/String parameters (null! silences
            // the array-element nullability warning; Java reflection accepts null arguments).
            process = _newProcess.Invoke(null, new Java.Lang.Object[] { command, null!, null! });
            if (process is null)
                return -1;

            DrainStdout(process, label);
            var exit = WaitFor(process);
            _logger.Information($"Shizuku {label} exited with {exit}.");
            return exit;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Shizuku {label} failed.", ex);
            return -1;
        }
        finally
        {
            Destroy(process);
        }
    });

    // Reads the remote process's stdout to EOF so it cannot block on a full pipe buffer while we wait for it.
    // Best-effort: a stream hiccup must not turn an otherwise-successful command into a reported failure.
    private void DrainStdout(Java.Lang.Object process, string label)
    {
        try
        {
            if (process.Class.GetMethod("getInputStream")?.Invoke(process) is not { } streamObj)
                return;
            using var stream = streamObj.JavaCast<Java.IO.InputStream>();
            var buffer = new byte[4096];
            while (stream.Read(buffer) >= 0)
            {
                // Discard: EmuShelf runs only bounded-output commands (`am`), so this just clears the pipe.
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Shizuku {label}: draining output failed.", ex);
        }
    }

    private static int WaitFor(Java.Lang.Object process) =>
        process.Class.GetMethod("waitFor")?.Invoke(process) is Integer code ? code.IntValue() : -1;

    private void Destroy(Java.Lang.Object? process)
    {
        if (process is null)
            return;
        try
        {
            process.Class.GetMethod("destroy")?.Invoke(process);
        }
        catch (Exception ex)
        {
            // Cleanup only — the command has already run; a failed destroy is not worth surfacing.
            _logger.Warning("Shizuku: could not destroy a finished remote process.", ex);
        }
    }

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
