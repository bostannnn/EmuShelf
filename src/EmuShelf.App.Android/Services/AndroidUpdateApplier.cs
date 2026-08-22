using System;
using System.IO;
using Android.Content;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Updates;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// The Android in-place update applier. Unlike the desktop appliers there is no file-swap: an app cannot
/// overwrite its own installed APK. Instead this hands the downloaded, checksum-verified APK to the system
/// package installer, which shows the user a confirmation and (on accept) updates EmuShelf in place. The
/// shared <c>AppUpdateCoordinator</c> still drives the check + verified download; only this final
/// step is Android-specific, which is why it is injected through <c>App.UpdateApplierFactoryOverride</c>.
///
/// Two conditions must hold for the install to actually succeed, both outside this class's control:
/// the new APK must be signed with the SAME key as the running build (Android refuses an update whose
/// signature differs — the CI release keystore, DECISIONS 2026-08-20), and the user must allow EmuShelf to
/// "install unknown apps" (the installer itself prompts for this the first time). A signature mismatch or a
/// declined prompt surfaces to the user as the installer's own error, not a silent failure.
/// </summary>
public sealed class AndroidUpdateApplier : IUpdateApplier
{
    // Matches the <provider> authority declared in AndroidManifest.xml. A content:// URI (not a file://
    // one) is mandatory for the installer since API 24, and only a FileProvider can mint one the installer
    // is granted to read.
    private const string FileProviderAuthority = "com.emushelf.app.updateprovider";

    // Under the app-internal cache dir, matching <cache-path path="updates/"> in Resources/xml. The APK is
    // copied here from wherever the shared downloader staged it (which can be on a microSD or USB data
    // folder a FileProvider cannot map) so the provider path is always valid regardless of data location.
    private const string StagedApkRelativeDir = "updates";
    private const string StagedApkFileName = "EmuShelf-update.apk";

    private readonly Func<Context?> _context;
    private readonly IAppLogger _logger;

    public AndroidUpdateApplier(Func<Context?> context, IAppLogger logger)
    {
        _context = context;
        _logger = logger;
    }

    public bool CanApply(out string? reason)
    {
        if (_context() is null)
        {
            reason = "EmuShelf can't reach the Android installer right now.";
            return false;
        }

        reason = null;
        return true;
    }

    public void ApplyAndRelaunch(StagedUpdate staged)
    {
        ArgumentNullException.ThrowIfNull(staged);

        var context = _context()
            ?? throw new InvalidOperationException("No Android context is available to install the update.");

        var apk = CopyIntoProviderCache(context, staged.PayloadPath);
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, FileProviderAuthority, apk);

        // ACTION_VIEW on the package-archive type is the portable "open this APK in the installer" intent.
        // The read grant lets the (separate) installer process read our content URI; NEW_TASK is required
        // because we start it from the application context. The installer runs as its own task, so the
        // coordinator's follow-up exit does not tear it down.
        using var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(uri, "application/vnd.android.package-archive");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);

        context.StartActivity(intent);
        _logger.Information($"Handed update {staged.Version} to the Android package installer.");
    }

    // Copies the staged APK into the FileProvider-mapped cache dir, returning the destination file. A stale
    // copy from an interrupted attempt is overwritten (FileMode.Create) so the installer never reads a
    // truncated payload.
    private static Java.IO.File CopyIntoProviderCache(Context context, string stagedApkPath)
    {
        var cacheRoot = context.CacheDir?.AbsolutePath
            ?? throw new InvalidOperationException("The Android cache directory is unavailable.");
        var destinationDir = Path.Combine(cacheRoot, StagedApkRelativeDir);
        Directory.CreateDirectory(destinationDir);
        var destination = Path.Combine(destinationDir, StagedApkFileName);

        using (var source = new FileStream(stagedApkPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            source.CopyTo(target);
        }

        return new Java.IO.File(destination);
    }
}
