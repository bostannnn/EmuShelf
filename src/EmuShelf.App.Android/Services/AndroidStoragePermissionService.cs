using System;
using Android.Content;
using Android.OS;
using Android.Provider;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Storage;
using AndroidEnvironment = Android.OS.Environment;
using Uri = Android.Net.Uri;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// The Android implementation of the all-files-access gate. The user-chosen data folder is a real
/// <c>/storage/…</c> path, which the process can only open by path once <c>MANAGE_EXTERNAL_STORAGE</c> is
/// granted — and on API 30+ that grant is a system Settings toggle, not a runtime permission dialog, so
/// it is requested by launching the all-files Settings screen and observed later via
/// <see cref="IsGranted"/>. On API &lt; 30 there is no manage-all-files concept; the legacy storage model
/// applies and the gate reports as unconditionally granted (the manifest keeps the legacy read permission).
/// </summary>
public sealed class AndroidStoragePermissionService(Func<Context?> context, IAppLogger? logger = null)
    : IStoragePermissionService
{
    // MANAGE_EXTERNAL_STORAGE exists only on Android 11 (R, API 30) and later. Below that the resolver must
    // not treat a missing grant as a reason to re-onboard.
    public bool RequiresGrant => Build.VERSION.SdkInt >= BuildVersionCodes.R;

    public bool IsGranted => !RequiresGrant || AndroidEnvironment.IsExternalStorageManager;

    public void RequestGrant()
    {
        if (!RequiresGrant)
            return;

        var ctx = context();
        if (ctx is null)
        {
            logger?.Warning(
                "All-files access requested before an Android context was available; cannot open the grant screen.");
            return;
        }

        // Prefer the package-scoped screen (drops the user straight onto EmuShelf's own toggle); fall back
        // to the global all-files list if the scoped intent cannot be resolved on this firmware.
        if (TryStart(ctx, Settings.ActionManageAppAllFilesAccessPermission,
                Uri.Parse("package:" + ctx.PackageName)))
            return;
        if (TryStart(ctx, Settings.ActionManageAllFilesAccessPermission, data: null))
            return;

        // Both failed: the user tapped "grant access" and nothing opened. Leave a diagnostic in the
        // pre-boot log (readable via adb even before a data folder is chosen) so this isn't silent.
        logger?.Warning(
            "Could not open the all-files access Settings screen: neither the package-scoped nor the " +
            "global MANAGE_ALL_FILES_ACCESS intent could be started on this firmware.");
    }

    private bool TryStart(Context ctx, string action, Uri? data)
    {
        try
        {
            using var intent = data is null ? new Intent(action) : new Intent(action, data);
            // Started from the application context (no live Activity guaranteed at onboarding time), so a
            // new task is required.
            intent.AddFlags(ActivityFlags.NewTask);
            if (intent.ResolveActivity(ctx.PackageManager!) is null)
            {
                logger?.Information($"All-files grant intent '{action}' has no activity to resolve on this firmware.");
                return false;
            }
            ctx.StartActivity(intent);
            return true;
        }
        catch (Exception ex)
        {
            logger?.Error($"Failed to start the all-files grant intent '{action}'.", ex);
            return false;
        }
    }
}
