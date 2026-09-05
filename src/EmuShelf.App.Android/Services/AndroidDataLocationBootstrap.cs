using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using EmuShelf.App.Services;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Storage;
using EmuShelf.Core.Storage.Android;
using EmuShelf.Infrastructure.Storage;
using AndroidEnvironment = Android.OS.Environment;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// The Android head's <see cref="IDataLocationBootstrap"/>: it turns the shared onboarding view-model's
/// requests into the real platform actions — grant all-files access, run the SAF folder picker, translate
/// the picked tree URI to a real <c>/storage/…</c> path (the sanctioned all-files fast path, shared with
/// <see cref="SingleViewDialogService"/>), reject off-limits targets, create the <c>EmuShelf</c> subfolder,
/// and persist the pointer. The pure resolve/validate policy lives in <c>DataLocationResolver</c>; this
/// class is only the device side.
/// </summary>
public sealed class AndroidDataLocationBootstrap : IDataLocationBootstrap
{
    /// <summary>The subfolder created inside the user's pick so EmuShelf never scatters files into it.</summary>
    private const string DataFolderName = "EmuShelf";

    // logcat tag for the resolve verdict. The pre-boot FileAppLogger lives in app-private storage, which a
    // Release build (not debuggable) makes unreadable over adb, so the one line that explains "why did I get
    // onboarding" is also written where `adb logcat -s EmuShelfBoot` can see it.
    private const string BootLogTag = "EmuShelfBoot";

    private readonly IDataLocationStore _store;
    private readonly IStoragePermissionService _permission;
    private readonly Func<TopLevel?> _topLevel;
    private readonly Func<DataLocationResolution> _resolve;
    private readonly IAppLogger _logger;

    public AndroidDataLocationBootstrap(
        IDataLocationStore store,
        IStoragePermissionService permission,
        Func<TopLevel?> topLevel,
        Func<DataLocationResolution> resolve,
        IAppLogger? logger = null)
    {
        _store = store;
        _permission = permission;
        _topLevel = topLevel;
        _resolve = resolve;
        _logger = logger ?? NullAppLogger.Instance;
    }

    /// <summary>
    /// Runs the resolver afresh (pointer, grant, write probe) and logs the verdict. Called by the shared
    /// composition root when an Activity asks for its first view, and by onboarding on every foreground
    /// return — never at process creation, where a headless start would freeze a stale verdict.
    /// </summary>
    public DataLocationResolution Resolve()
    {
        var resolution = _resolve();
        var verdict = resolution.IsResolved
            ? $"resolved '{resolution.BaseDirectory}'"
            : $"onboarding ({resolution.OnboardingReason})";
        _logger.Information($"Data location: {verdict}.");
        global::Android.Util.Log.Info(BootLogTag, $"Data location: {verdict}");
        return resolution;
    }
    public bool RequiresStoragePermission => _permission.RequiresGrant;
    public bool IsStoragePermissionGranted => _permission.IsGranted;

    /// <summary>
    /// The recommended folder EmuShelf can create by itself under internal shared storage —
    /// <c>&lt;primary&gt;/EmuShelf</c>. Because we hold all-files access we write it by path directly, with
    /// no document picker, which is what avoids the picker's refusal of Download/Documents/root that made the
    /// first-run pick feel like a dead end.
    /// </summary>
    public string? RecommendedBaseDirectory
    {
        get
        {
            var primary = AndroidEnvironment.ExternalStorageDirectory?.AbsolutePath;
            return string.IsNullOrEmpty(primary) ? null : Path.Combine(primary, DataFolderName);
        }
    }

    public event Action? StoragePermissionMaybeChanged;

    /// <summary>
    /// Called by the head when EmuShelf returns to the foreground (its <c>OnTopResumedActivityChanged</c>
    /// signal), so the onboarding view-model re-checks the grant after the user leaves the Settings toggle.
    /// </summary>
    public void NotifyForegroundReturned() => StoragePermissionMaybeChanged?.Invoke();

    public void RequestStoragePermission() => _permission.RequestGrant();

    /// <summary>
    /// Offer the second-screen-return step only on a device that actually has a companion display (the Thor).
    /// Probed via the display manager rather than assumed, so a single-screen phone never sees the step.
    /// </summary>
    public bool ShowSecondScreenReturnStep
    {
        get
        {
            try
            {
                var context = global::Android.App.Application.Context;
                if (context.GetSystemService(global::Android.Content.Context.DisplayService)
                    is not global::Android.Hardware.Display.DisplayManager manager)
                {
                    return false;
                }

                var displays = manager.GetDisplays(
                    global::Android.Hardware.Display.DisplayManager.DisplayCategoryPresentation);
                return displays is { Length: > 0 };
            }
            catch (Exception ex)
            {
                _logger.Warning("Could not probe for a second display during onboarding.", ex);
                return false;
            }
        }
    }

    /// <summary>
    /// Whether the <c>SecondScreenReturnWatcher</c> accessibility service is enabled, read from the system's
    /// enabled-services list so it is authoritative regardless of whether the service has bound yet.
    /// </summary>
    public bool IsSecondScreenReturnEnabled
    {
        get
        {
            try
            {
                var context = global::Android.App.Application.Context;
                var enabled = global::Android.Provider.Settings.Secure.GetString(
                    context.ContentResolver,
                    global::Android.Provider.Settings.Secure.EnabledAccessibilityServices);
                return !string.IsNullOrEmpty(enabled)
                    && enabled.Contains("SecondScreenReturnWatcher", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }
    }

    public void RequestSecondScreenReturn()
    {
        try
        {
            using var intent = new global::Android.Content.Intent(
                global::Android.Provider.Settings.ActionAccessibilitySettings);
            intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            global::Android.App.Application.Context.StartActivity(intent);
        }
        catch (Exception ex)
        {
            _logger.Warning("Could not open the accessibility settings screen.", ex);
        }
    }

    /// <summary>
    /// Looks for a data folder left by a previous install: <c>Data/library.db</c> under <c>EmuShelf</c> at
    /// the root or one level down of primary shared storage and of every mounted volume (the Thor keeps
    /// its library at <c>/storage/emulated/0/User/EmuShelf</c>). Read-only and shallow, so it costs a
    /// handful of stats. Null before the all-files grant, when shared storage is not readable.
    /// </summary>
    public string? FindExistingDataFolder()
    {
        if (!_permission.IsGranted)
            return null;

        try
        {
            // More than one can exist (an earlier install's abandoned folder next to the live one); the
            // library written most recently is the one the user was actually using.
            string? best = null;
            var bestWrite = DateTime.MinValue;
            foreach (var root in CandidateRoots())
            {
                foreach (var candidate in Candidates(root))
                {
                    var database = LibraryDatabase(candidate);
                    if (!File.Exists(database))
                        continue;
                    var written = File.GetLastWriteTimeUtc(database);
                    if (written > bestWrite)
                    {
                        best = candidate;
                        bestWrite = written;
                    }
                }
            }
            return best;
        }
        catch (Exception ex)
        {
            _logger.Warning("Could not look for an existing data folder.", ex);
            return null;
        }
    }

    private static IEnumerable<string> Candidates(string root)
    {
        yield return Path.Combine(root, DataFolderName);
        foreach (var child in SafeSubdirectories(root))
            yield return Path.Combine(child, DataFolderName);
    }

    private static string LibraryDatabase(string directory) => Path.Combine(directory, "Data", "library.db");

    public Task<DataLocationPickResult> UseExistingFolderAsync(string baseDirectory)
    {
        if (!IsDataFolder(baseDirectory))
            return Task.FromResult(DataLocationPickResult.Failed("That folder no longer holds an EmuShelf library."));

        if (!DirectoryWritability.IsWritable(baseDirectory))
        {
            _logger.Warning($"Existing data folder '{baseDirectory}' is not writable.");
            return Task.FromResult(DataLocationPickResult.Failed(
                "EmuShelf can't write to that folder. Make sure all-files access is allowed, then try again."));
        }

        _store.Write(new DataLocation(baseDirectory, null, DateTimeOffset.UtcNow));
        _logger.Information($"Data folder set to existing library '{baseDirectory}'.");
        return Task.FromResult(DataLocationPickResult.Success(baseDirectory));
    }

    private static bool IsDataFolder(string directory) => File.Exists(LibraryDatabase(directory));

    private static IEnumerable<string> CandidateRoots()
    {
        var primary = AndroidEnvironment.ExternalStorageDirectory?.AbsolutePath;
        if (!string.IsNullOrEmpty(primary))
            yield return primary;

        // Removable volumes mount as /storage/XXXX-XXXX; "emulated" is the primary volume's tree and
        // "self" a symlink to it, so both are skipped.
        foreach (var volume in SafeSubdirectories("/storage"))
        {
            var name = Path.GetFileName(volume);
            if (name is "emulated" or "self")
                continue;
            yield return volume;
        }
    }

    private static IEnumerable<string> SafeSubdirectories(string directory)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.GetDirectories(directory) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public Task<DataLocationPickResult> UseRecommendedFolderAsync()
    {
        var baseDirectory = RecommendedBaseDirectory;
        if (string.IsNullOrEmpty(baseDirectory))
            return Task.FromResult(DataLocationPickResult.Failed("No internal storage is available."));

        if (!DirectoryWritability.IsWritable(baseDirectory))
        {
            _logger.Warning($"Recommended data folder '{baseDirectory}' is not writable.");
            return Task.FromResult(DataLocationPickResult.Failed(
                "EmuShelf can't write there yet. Make sure all-files access is granted, then try again."));
        }

        _store.Write(new DataLocation(baseDirectory, null, DateTimeOffset.UtcNow));
        _logger.Information($"Data folder set to recommended location '{baseDirectory}'.");
        return Task.FromResult(DataLocationPickResult.Success(baseDirectory));
    }

    public async Task<DataLocationPickResult> PickFolderAsync()
    {
        var top = _topLevel();
        if (top is null)
        {
            _logger.Warning("Data-folder pick requested before a TopLevel was available.");
            return DataLocationPickResult.Failed("The picker isn't ready yet. Please try again.");
        }

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose where EmuShelf keeps its data",
            AllowMultiple = false,
        });

        _logger.Information($"Folder picker returned {folders.Count} folder(s).");
        if (folders.Count == 0)
            return DataLocationPickResult.Cancelled();

        var folder = folders[0];
        var localPath = folder.TryGetLocalPath();
        if (string.IsNullOrEmpty(localPath))
            localPath = AndroidExternalStorageUri.TryResolveLocalPath(folder.Path);
        _logger.Information($"Picked folder path='{folder.Path}' resolved local='{localPath}'.");

        if (string.IsNullOrEmpty(localPath))
        {
            return DataLocationPickResult.Failed(
                "That folder can't be reached by EmuShelf. Pick a folder on your SD card or internal shared storage.");
        }

        // An app's Android/data or Android/obb subtree is off-limits: on Android 12+ the process cannot
        // File.Open there even with all-files access, so a data folder placed inside one would break SQLite.
        if (IsUnderAndroidPrivateData(localPath))
        {
            return DataLocationPickResult.Failed(
                "That's an app's private folder. Choose a normal folder — for example a new folder on your SD card.");
        }

        // Don't nest EmuShelf/EmuShelf when the user already picked (or made) a folder named EmuShelf.
        var baseDirectory = string.Equals(
                Path.GetFileName(localPath.TrimEnd('/')), DataFolderName, StringComparison.OrdinalIgnoreCase)
            ? localPath
            : Path.Combine(localPath, DataFolderName);
        if (!DirectoryWritability.IsWritable(baseDirectory))
        {
            _logger.Warning($"Chosen data folder '{baseDirectory}' is not writable.");
            return DataLocationPickResult.Failed(
                "EmuShelf can't write to that folder. Make sure all-files access is granted, then try another folder.");
        }

        _store.Write(new DataLocation(baseDirectory, folder.Path?.ToString(), DateTimeOffset.UtcNow));
        _logger.Information($"Data folder set to '{baseDirectory}'.");
        return DataLocationPickResult.Success(baseDirectory);
    }

    // True for any path inside a per-app external-data tree (…/Android/data/… or …/Android/obb/…), matched
    // case-insensitively on normalised separators.
    private static bool IsUnderAndroidPrivateData(string path)
    {
        var normalised = path.Replace('\\', '/');
        return normalised.Contains("/Android/data/", StringComparison.OrdinalIgnoreCase)
            || normalised.Contains("/Android/obb/", StringComparison.OrdinalIgnoreCase);
    }
}
