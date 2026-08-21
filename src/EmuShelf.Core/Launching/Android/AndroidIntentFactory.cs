namespace EmuShelf.Core.Launching.Android;

/// <summary>
/// Turns an <see cref="AndroidLaunchProfile"/> plus an already-resolved ROM reference into a concrete
/// <see cref="AndroidIntentRequest"/>. Pure and deterministic so the exact intent shapes measured on the
/// Thor are asserted in the desktop test suite rather than discovered at deploy time.
///
/// Deliberately <em>not</em> this class's job: deciding what the ROM reference is. The caller passes a
/// tree-scoped <c>content://</c> URI (built by <see cref="Storage.Android.AndroidExternalStorageUri"/>)
/// for the scoped-storage emulators, or a plain filesystem path for RetroArch. Keeping "which URI" in the
/// storage layer and "which intent shape" here is what lets both be tested in isolation.
/// </summary>
public static class AndroidIntentFactory
{
    /// <summary>Android's boolean extra DuckStation reads to boot-and-exit rather than return to its list.</summary>
    public const string OneShotExtra = "isOneShot";

    /// <summary>RetroArch's ROM-path extra.</summary>
    public const string RetroArchRomExtra = "ROM";

    /// <summary>RetroArch's libretro-core-path extra.</summary>
    public const string RetroArchCoreExtra = "LIBRETRO";

    /// <summary>
    /// RetroArch's config-file extra. The load-bearing one for user settings: it names the absolute path of
    /// <c>retroarch.cfg</c>, and without it <c>RetroActivityFuture</c> starts with a default config — the
    /// user's hotkeys, gamepad autoconfig and settings never load. Confirmed on the Thor: with only
    /// <c>ROM</c>+<c>LIBRETRO</c> RetroArch's intent-parse log emits no "Config file" line; adding
    /// <c>CONFIGFILE</c> makes it load <c>…/files/retroarch.cfg</c>.
    /// </summary>
    public const string RetroArchConfigExtra = "CONFIGFILE";

    /// <summary>RetroArch's app-data-dir extra; seeds the assets/autoconfig/core roots.</summary>
    public const string RetroArchDataDirExtra = "DATADIR";

    /// <summary>RetroArch's internal-storage-root extra; seeds the default save/state/system folders.</summary>
    public const string RetroArchSdcardExtra = "SDCARD";

    /// <summary>RetroArch's external-files-dir extra (the app's <c>Android/data/&lt;pkg&gt;/files</c>).</summary>
    public const string RetroArchExternalExtra = "EXTERNAL";

    /// <summary>
    /// Builds the launch intent for <paramref name="profile"/> handing it <paramref name="romReference"/>
    /// (a <c>content://</c> URI for the scoped-storage emulators, a plain path for RetroArch).
    /// <paramref name="retroArchCorePath"/> is required for, and only used by, the
    /// <see cref="AndroidRomPayloadSlot.RetroArchCore"/> slot.
    /// </summary>
    public static AndroidIntentRequest Build(
        AndroidLaunchProfile profile,
        string romReference,
        string? retroArchCorePath = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrEmpty(romReference);

        var stringExtras = new Dictionary<string, string>(StringComparer.Ordinal);
        var boolExtras = new Dictionary<string, bool>(StringComparer.Ordinal);
        string? dataUri = null;
        var grantRead = false;

        switch (profile.PayloadSlot)
        {
            case AndroidRomPayloadSlot.DataUri:
                dataUri = romReference;
                grantRead = true;
                break;

            case AndroidRomPayloadSlot.ExtraUri:
                if (string.IsNullOrEmpty(profile.PayloadExtraName))
                {
                    throw new ArgumentException(
                        $"Android launch profile '{profile.Id}' uses ExtraUri but names no extra.",
                        nameof(profile));
                }

                stringExtras[profile.PayloadExtraName] = romReference;
                if (profile.BootOneShot)
                    boolExtras[OneShotExtra] = true;
                grantRead = true;
                break;

            case AndroidRomPayloadSlot.RetroArchCore:
                if (string.IsNullOrEmpty(retroArchCorePath))
                {
                    throw new ArgumentException(
                        $"Android launch profile '{profile.Id}' is RetroArch-shaped and needs a core path.",
                        nameof(retroArchCorePath));
                }

                stringExtras[RetroArchRomExtra] = romReference;
                stringExtras[RetroArchCoreExtra] = retroArchCorePath;

                // The environment extras RetroArch's own launcher (and every working frontend — Cocoon,
                // NeoStation) sends alongside ROM/LIBRETRO. Derived from the target package the way those
                // frontends derive them, from fixed Android conventions: internal storage is /storage/
                // emulated/0, the app's external files live under Android/data/<pkg>/files, its default
                // config is retroarch.cfg there, and its data dir is /data/user/0/<pkg>. Omitting these
                // makes RetroActivityFuture ignore the user's config (see RetroArchConfigExtra). APK and IME
                // (also sent by those frontends) are install/device-specific and not load-bearing here — a
                // Thor launch with just these four loaded the config and the correct save/system folders.
                var externalFiles = $"/storage/emulated/0/Android/data/{profile.PackageName}/files";
                stringExtras[RetroArchConfigExtra] = $"{externalFiles}/retroarch.cfg";
                stringExtras[RetroArchDataDirExtra] = $"/data/user/0/{profile.PackageName}";
                stringExtras[RetroArchSdcardExtra] = "/storage/emulated/0";
                stringExtras[RetroArchExternalExtra] = externalFiles;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(profile));
        }

        return new AndroidIntentRequest(
            profile.PackageName,
            profile.ActivityName,
            profile.Action,
            dataUri,
            stringExtras,
            boolExtras,
            // Launches target an explicit component, which bypasses intent-filter matching, so no
            // category is needed even for the action-carrying (VIEW) shapes.
            [],
            grantRead);
    }
}
