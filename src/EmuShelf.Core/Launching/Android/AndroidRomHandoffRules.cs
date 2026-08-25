namespace EmuShelf.Core.Launching.Android;

/// <summary>
/// The pure decisions behind how the Android head hands a ROM to an emulator under the FileProvider model
/// (DECISIONS 2026-08-25). EmuShelf reads ROMs by real path under all-files access, then re-exposes each one
/// through its <em>own</em> FileProvider so it can delegate a read grant to any emulator — no per-launch SAF
/// folder pick, and no dependence on the emulator holding its own <c>roms/&lt;system&gt;</c> grant. The one
/// exception is a multi-file disc descriptor handed to an emulator that resolves its sibling tracks by
/// relative path (DuckStation): a FileProvider URI hides the base directory, so that case falls back to a
/// real <c>file://</c> path. This class is where "which shape" is decided, kept pure so it is asserted in the
/// desktop suite rather than discovered on device.
/// </summary>
public static class AndroidRomHandoffRules
{
    // Descriptor formats that reference sibling track files by relative name. A .cue points at its .bin(s),
    // a .gdi/.m3u at their tracks/discs. .chd/.pbp/.iso are self-contained and never appear here.
    private static readonly string[] MultiFileDescriptorExtensions = [".cue", ".gdi", ".m3u"];

    /// <summary>
    /// True when <paramref name="romPath"/> is a multi-file disc descriptor whose contents name sibling
    /// tracks by relative path (<c>.cue</c>/<c>.gdi</c>/<c>.m3u</c>). Case-insensitive on the extension.
    /// </summary>
    public static bool IsMultiFileDescriptor(string romPath)
    {
        if (string.IsNullOrEmpty(romPath))
            return false;

        foreach (var extension in MultiFileDescriptorExtensions)
        {
            if (romPath.EndsWith(extension, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when the launcher must hand <paramref name="profile"/> a real <c>file://</c> path for
    /// <paramref name="romPath"/> instead of a FileProvider <c>content://</c> URI — i.e. the emulator
    /// resolves relative sibling tracks (<see cref="AndroidLaunchProfile.NeedsRealPathForMultiFile"/>) and
    /// the ROM is a multi-file descriptor. Single-file ROMs, and emulators that read the descriptor as a
    /// document, always take the FileProvider URI.
    /// </summary>
    public static bool PrefersRealPath(AndroidLaunchProfile profile, string romPath)
    {
        System.ArgumentNullException.ThrowIfNull(profile);
        return profile.NeedsRealPathForMultiFile && IsMultiFileDescriptor(romPath);
    }
}
