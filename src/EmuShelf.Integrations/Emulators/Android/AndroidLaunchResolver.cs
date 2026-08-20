using EmuShelf.Core.Launching.Android;
using EmuShelf.Core.Storage.Android;

namespace EmuShelf.Integrations.Emulators.Android;

/// <summary>The outcome of resolving a game into a launchable Android intent.</summary>
/// <param name="Intent">The intent to fire, when <paramref name="Profile"/> is set.</param>
/// <param name="Profile">The chosen emulator profile, or null on failure.</param>
/// <param name="FailureReason">A user-facing reason when no intent could be built.</param>
public sealed record AndroidLaunchResolution(
    AndroidIntentRequest? Intent,
    AndroidLaunchProfile? Profile,
    string? FailureReason)
{
    public bool Success => Intent is not null;

    public static AndroidLaunchResolution Failed(string reason) => new(null, null, reason);
}

/// <summary>
/// Turns "launch this game on this system" into a concrete <see cref="AndroidIntentRequest"/>: picks the
/// emulator profile, converts the game's path into the reference shape that emulator wants, and builds the
/// intent — all as a pure function so the whole selection is exercised in the desktop suite. The Android
/// head only has to fire the resulting intent.
///
/// Tree-selection: the launch URI is scoped to <paramref name="emulatorGrantRoot"/> — the folder the
/// target emulator holds a persisted SAF grant to (the output of the per-emulator setup checklist). On the
/// Thor the working tree was exactly that grant folder, e.g. the whole <c>roms/psx</c> tree, with the game
/// (even a nested multi-disc <c>.m3u</c>) as the document beneath it. When no grant root is supplied the
/// resolver falls back to the game file's own parent directory; that is a <b>best-effort default that is
/// not yet on-device verified</b> for a game nested below the grant folder, because whether a prefix grant
/// to an ancestor authorises a deeper tree URI depends on Android's prefix-match semantics — see the
/// caveat in <c>docs/android-port-plan.md</c>. Prefer passing the real grant root.
/// </summary>
public static class AndroidLaunchResolver
{
    public static AndroidLaunchResolution Resolve(
        string systemId,
        string absoluteGamePath,
        string? preferredEmulatorId = null,
        string? retroArchCorePath = null,
        string? emulatorGrantRoot = null)
    {
        var candidates = AndroidEmulatorLaunchProfiles.ForSystem(systemId);
        if (candidates.Count == 0)
            return AndroidLaunchResolution.Failed($"No Android emulator is configured for {systemId}.");

        var profile = candidates.FirstOrDefault(p =>
                          string.Equals(p.Id, preferredEmulatorId, StringComparison.Ordinal))
                      ?? candidates[0];

        // RetroArch is the plain-path exception: it holds all-files, takes the raw path, and needs a core.
        if (profile.PayloadSlot == AndroidRomPayloadSlot.RetroArchCore)
        {
            if (string.IsNullOrEmpty(retroArchCorePath))
                return AndroidLaunchResolution.Failed($"Select an installed {profile.DisplayName} core first.");

            return new AndroidLaunchResolution(
                AndroidIntentFactory.Build(profile, absoluteGamePath, retroArchCorePath),
                profile,
                null);
        }

        if (!AndroidExternalStorageUri.TrySplitLocalPath(absoluteGamePath, out var volume, out var relative))
        {
            // App-private or an unaddressable mount: EmuShelf's own reads work, but no shared-storage
            // document URI can be built for the emulator. A SAF-backed handoff is the fallback (Milestone D).
            return AndroidLaunchResolution.Failed(
                $"{absoluteGamePath} is not on shared storage, so it cannot be handed to {profile.DisplayName}.");
        }

        var treeRelative = ResolveTreeRelative(volume, relative, emulatorGrantRoot);
        var romUri = AndroidExternalStorageUri.BuildTreeDocumentUri(volume, treeRelative, relative);
        return new AndroidLaunchResolution(AndroidIntentFactory.Build(profile, romUri), profile, null);
    }

    // The tree the launch URI is scoped to: the emulator's grant root when it is a same-volume ancestor of
    // the game (the on-device-verified case), otherwise the game's own parent directory (best-effort).
    private static string ResolveTreeRelative(string volume, string gameRelative, string? emulatorGrantRoot)
    {
        if (!string.IsNullOrEmpty(emulatorGrantRoot) &&
            AndroidExternalStorageUri.TrySplitLocalPath(emulatorGrantRoot, out var grantVolume, out var grantRelative) &&
            string.Equals(grantVolume, volume, StringComparison.Ordinal) &&
            IsAncestorOrSelf(grantRelative, gameRelative))
        {
            return grantRelative;
        }

        return ParentRelative(gameRelative);
    }

    // True when 'ancestor' is the same as, or a directory prefix of, 'path' (both volume-relative, '/').
    private static bool IsAncestorOrSelf(string ancestor, string path)
    {
        if (ancestor.Length == 0)
            return true;
        return path.Equals(ancestor, StringComparison.Ordinal) ||
               path.StartsWith(ancestor + "/", StringComparison.Ordinal);
    }

    // The directory portion of a volume-relative, forward-slashed path; empty for a file at the volume root.
    private static string ParentRelative(string relative)
    {
        var lastSlash = relative.LastIndexOf('/');
        return lastSlash <= 0 ? string.Empty : relative[..lastSlash];
    }
}
