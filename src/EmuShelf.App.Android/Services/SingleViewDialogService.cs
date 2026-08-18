using Avalonia.Controls;
using Avalonia.Platform.Storage;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Systems;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// Walking-skeleton dialog service: every interaction that needs a picker, a modal window or a
/// gamepad-native overlay is not built yet, so each returns the "cancelled / declined / nothing"
/// answer and logs that it was reached. This keeps the shared view model fully constructible and the
/// app launchable without a keyboard, which is the point of A1.
///
/// The remaining stubs are later milestones, and each is one of the gamepad-mode "desktop escape
/// hatches" the plan enumerates: full URI-aware (SAF-backed) sources are Milestone D; cover search and
/// the settings surface are A1's remaining feature work / Milestone C's IME. <see cref="PickFolderAsync"/>
/// is implemented for the A1 gamepad-native import — the system chooser and scan-progress steps are
/// controller-native overlays in the shared view-model, so the only thing the platform must supply is
/// the folder pick itself.
/// </summary>
public sealed class SingleViewDialogService(IAppLogger logger, Func<TopLevel?> topLevel) : IDialogService
{
    private const string NotYet = "Android dialog not implemented in the A1 skeleton: ";

    public Task<IReadOnlyList<string>> PickGameFilesAsync()
    {
        logger.Information(NotYet + nameof(PickGameFilesAsync));
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    /// <summary>
    /// Opens the Android system folder picker (SAF <c>ACTION_OPEN_DOCUMENT_TREE</c>, surfaced by
    /// Avalonia through <c>TopLevel.StorageProvider</c>) and returns a real filesystem path the shared
    /// <c>FolderScanner</c> can read. Avalonia's <c>TryGetLocalPath()</c> is null for a SAF tree URI, so
    /// with all-files access held we translate an <c>externalstorage</c> tree URI to its
    /// <c>/storage/…</c> path ourselves (the plan's sanctioned all-files fast-path). If neither yields a
    /// readable local path — a provider we cannot translate, or no all-files grant — we log and return
    /// null (the shared import flow drops back to the shelf); a SAF-backed reader for that case is
    /// Milestone D.
    /// </summary>
    public async Task<string?> PickFolderAsync()
    {
        var top = topLevel();
        if (top is null)
        {
            logger.Warning("Folder pick requested before a TopLevel was available.");
            return null;
        }

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a games folder",
            AllowMultiple = false,
        });

        if (folders.Count == 0)
            return null;

        var folder = folders[0];
        var localPath = folder.TryGetLocalPath();
        if (string.IsNullOrEmpty(localPath))
            localPath = TryResolveExternalStorageTreePath(folder.Path);

        if (string.IsNullOrEmpty(localPath) || !Directory.Exists(localPath))
        {
            logger.Information(
                $"Picked folder '{folder.Path}' did not resolve to a readable local path (no all-files " +
                "access, or a provider with no local path); SAF-backed scanning is Milestone D.");
            return null;
        }

        logger.Information($"Import folder resolved to '{localPath}'.");
        return localPath;
    }

    /// <summary>
    /// Translates a SAF tree URI from the platform "external storage" documents provider into its raw
    /// filesystem path — e.g.
    /// <c>content://com.android.externalstorage.documents/tree/primary%3AEmuShelfRoms</c> →
    /// <c>/storage/emulated/0/EmuShelfRoms</c>. Only valid because EmuShelf holds all-files access, and
    /// only for that one provider; anything else returns null and routes to the Milestone D fallback.
    /// </summary>
    private static string? TryResolveExternalStorageTreePath(Uri? treeUri)
    {
        if (treeUri is null || !treeUri.Host.Equals("com.android.externalstorage.documents", StringComparison.Ordinal))
            return null;

        // The document id is the segment after "/tree/", e.g. "primary:EmuShelfRoms".
        var segments = treeUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var treeIndex = Array.IndexOf(segments, "tree");
        if (treeIndex < 0 || treeIndex + 1 >= segments.Length)
            return null;

        var documentId = Uri.UnescapeDataString(segments[treeIndex + 1]);
        var parts = documentId.Split(':', 2);
        var volume = parts[0];
        var relative = parts.Length > 1 ? parts[1] : string.Empty;

        // "primary" is the built-in shared storage; a named volume is an SD card / USB drive at
        // /storage/<id>. Reject an empty volume (the "root of all storage" pick SAF also blocks). A
        // volume id must be a single path segment — a '/' or '\' in it would itself let the root escape.
        if (string.IsNullOrEmpty(volume) || volume.AsSpan().IndexOfAny('/', '\\') >= 0)
            return null;
        var root = volume.Equals("primary", StringComparison.Ordinal)
            ? "/storage/emulated/0"
            : $"/storage/{volume}";

        if (string.IsNullOrEmpty(relative))
            return root;

        // Defense in depth: the system document picker never emits a rooted or parent-traversing
        // document id, but this translation runs with all-files access, so verify the combined path
        // stays inside the chosen volume before handing it to the scanner (a rooted 'relative' would
        // make Path.Combine discard the root, and '..' segments would climb out of it).
        var combined = Path.GetFullPath(Path.Combine(root, relative));
        return combined.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? combined
            : null;
    }

    public Task<string?> PickEmulatorExecutableAsync(string emulatorName)
    {
        logger.Information(NotYet + nameof(PickEmulatorExecutableAsync));
        return Task.FromResult<string?>(null);
    }

    public Task<string?> PickLibretroCoreAsync(string systemName)
    {
        logger.Information(NotYet + nameof(PickLibretroCoreAsync));
        return Task.FromResult<string?>(null);
    }

    public Task<string?> PickRpcs3ConfigurationDirectoryAsync()
    {
        logger.Information(NotYet + nameof(PickRpcs3ConfigurationDirectoryAsync));
        return Task.FromResult<string?>(null);
    }

    public Task<string?> PickCoverImageAsync(string gameTitle)
    {
        logger.Information(NotYet + nameof(PickCoverImageAsync));
        return Task.FromResult<string?>(null);
    }

    public Task<bool> ConfirmRemoveGameAsync(string gameTitle)
    {
        logger.Information(NotYet + nameof(ConfirmRemoveGameAsync));
        return Task.FromResult(false);
    }

    public Task<bool> ConfirmRemoveGamesAsync(int gameCount)
    {
        logger.Information(NotYet + nameof(ConfirmRemoveGamesAsync));
        return Task.FromResult(false);
    }

    public Task<MetadataConsentChoice> PromptForMetadataConsentAsync(int gameCount)
    {
        logger.Information(NotYet + nameof(PromptForMetadataConsentAsync));
        return Task.FromResult(MetadataConsentChoice.NotNow);
    }

    public Task<GameSystem?> PickSystemAsync(IReadOnlyList<GameSystem> systems, GameSystem? suggested)
    {
        logger.Information(NotYet + nameof(PickSystemAsync));
        return Task.FromResult<GameSystem?>(null);
    }

    public Task ShowEmulatorSettingsAsync(
        IReadOnlyList<GameSystem> systems,
        IReadOnlyList<EmulatorDefinition> emulators,
        IEmulatorConfigurationStore configurations,
        LibraryMaintenanceActions maintenance,
        IMetadataPreferencesService metadataPreferences,
        RetroAchievementsSettingsContext? retroAchievements = null,
        CloudSaveSyncSettingsContext? cloudSaves = null,
        TexturePackSettingsContext? texturePacks = null,
        ScreenScraperSettingsContext? screenScraper = null,
        IReadOnlyList<ThemeChoiceViewModel>? themeChoices = null,
        bool ambientThemeFromArtwork = false,
        Func<bool, Task>? setAmbientThemeFromArtwork = null,
        AppUpdateCoordinator? updates = null,
        Func<HotkeySettingsContext?>? createHotkeyContext = null)
    {
        logger.Information(NotYet + nameof(ShowEmulatorSettingsAsync));
        return Task.CompletedTask;
    }

    public Task ShowAchievementDetailsAsync(string gameTitle, int retroAchievementsGameId)
    {
        logger.Information(NotYet + nameof(ShowAchievementDetailsAsync));
        return Task.CompletedTask;
    }
}
