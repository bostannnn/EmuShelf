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
