using System;
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

    private readonly IDataLocationStore _store;
    private readonly IStoragePermissionService _permission;
    private readonly Func<TopLevel?> _topLevel;
    private readonly IAppLogger _logger;

    public AndroidDataLocationBootstrap(
        IDataLocationStore store,
        IStoragePermissionService permission,
        Func<TopLevel?> topLevel,
        DataLocationResolution resolution,
        IAppLogger? logger = null)
    {
        _store = store;
        _permission = permission;
        _topLevel = topLevel;
        _logger = logger ?? NullAppLogger.Instance;
        ResolvedBaseDirectory = resolution.BaseDirectory;
        OnboardingReason = resolution.OnboardingReason ?? DataLocationOnboardingReason.FirstRun;
    }

    public string? ResolvedBaseDirectory { get; }
    public DataLocationOnboardingReason OnboardingReason { get; }
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
