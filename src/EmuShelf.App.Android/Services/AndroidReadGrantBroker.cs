using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Launching.Android;
using EmuShelf.Core.Storage.Android;
using AndroidUri = Android.Net.Uri;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// The Android implementation of <see cref="IAndroidReadGrantBroker"/>: it makes EmuShelf hold its own
/// persisted Storage Access Framework grant to a library folder so the launcher can delegate a read grant
/// (<c>FLAG_GRANT_READ_URI_PERMISSION</c>) to any emulator, instead of relying on the emulator's own grant.
///
/// Why a folder pick is unavoidable: EmuShelf reads ROMs by real path under all-files access
/// (<c>MANAGE_EXTERNAL_STORAGE</c>), and that access is <em>not</em> delegable — only a SAF grant can be
/// passed to another app. Acquiring one is a one-time system folder pick per library folder, pre-navigated
/// to the folder in question so the user just confirms it. Avalonia's Android picker already persists the
/// grant; this also calls <c>TakePersistableUriPermission</c> explicitly so persistence does not depend on
/// that internal.
/// </summary>
public sealed class AndroidReadGrantBroker(
    Func<Context?> context,
    Func<TopLevel?> topLevel,
    IAppLogger logger) : IAndroidReadGrantBroker
{
    public bool HoldsReadGrantFor(string? romContentUri)
    {
        if (string.IsNullOrEmpty(romContentUri))
            return false;

        var ctx = context();
        if (ctx is null)
            return false;

        try
        {
            return ctx.CheckUriPermission(
                AndroidUri.Parse(romContentUri),
                global::Android.OS.Process.MyPid(),
                global::Android.OS.Process.MyUid(),
                ActivityFlags.GrantReadUriPermission) == global::Android.Content.PM.Permission.Granted;
        }
        catch (Exception ex)
        {
            logger.Warning($"Could not check the read grant for {romContentUri}.", ex);
            return false;
        }
    }

    public async Task<bool> EnsureReadGrantAsync(
        string? romContentUri,
        CancellationToken cancellationToken = default)
    {
        // RetroArch's plain path has no content URI to grant.
        if (string.IsNullOrEmpty(romContentUri))
            return false;

        // Nothing to do if we already hold a covering grant — the common case after the first launch of a
        // game in a given library folder.
        if (HoldsReadGrantFor(romContentUri))
            return true;

        var top = topLevel();
        if (top is null)
        {
            logger.Warning(
                "A read grant is needed but the folder picker is not ready (no TopLevel); launching without it.");
            return false;
        }

        // Ask for exactly the launch URI's own tree folder: it is always an ancestor-or-self of the game, so
        // granting it always covers the launch (whereas the game's import folder may not, if the URI fell back
        // to a narrower tree). Pre-navigate the picker there so the user just confirms.
        var treeFolder = AndroidExternalStorageUri.TryResolveTreePath(romContentUri);
        logger.Information(
            $"EmuShelf holds no read grant for this game; asking for access to '{treeFolder ?? "(the ROM folder)"}'.");

        IStorageFolder? suggested = null;
        if (!string.IsNullOrEmpty(treeFolder))
        {
            try
            {
                suggested = await top.StorageProvider.TryGetFolderFromPathAsync(treeFolder);
            }
            catch (Exception ex)
            {
                // Pre-navigation is a convenience; if it cannot be resolved the picker still opens un-navigated.
                logger.Information($"Could not pre-navigate the picker to '{treeFolder}': {ex.Message}");
            }
        }

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Allow EmuShelf to open games in this folder",
            AllowMultiple = false,
            SuggestedStartLocation = suggested,
        });

        if (folders.Count == 0)
        {
            logger.Information("The access grant was cancelled; launching without a delegated read grant.");
            return false;
        }

        Persist(folders[0]);

        var held = HoldsReadGrantFor(romContentUri);
        if (!held)
        {
            logger.Warning(
                "The picked folder does not cover this game, so no read grant can be delegated; " +
                "launching without it.");
        }

        return held;
    }

    // Explicitly take a persistable read permission for the picked tree so the grant survives restarts and
    // does not depend on Avalonia having taken it. Safe to call even if it was already taken.
    private void Persist(IStorageFolder folder)
    {
        var uriString = folder.Path?.ToString();
        if (string.IsNullOrEmpty(uriString))
            return;

        var uri = AndroidUri.Parse(uriString);
        var resolver = context()?.ContentResolver;
        if (uri is null || resolver is null)
            return;

        try
        {
            resolver.TakePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission);
            logger.Information($"Persisted a read grant to '{uriString}'.");
        }
        catch (Exception ex)
        {
            logger.Warning($"Could not persist the read grant to '{uriString}'.", ex);
        }
    }
}
