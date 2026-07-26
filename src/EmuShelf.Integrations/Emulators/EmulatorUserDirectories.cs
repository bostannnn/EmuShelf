namespace EmuShelf.Integrations.Emulators;

/// <summary>
/// Where each emulator keeps the user directory its texture and graphics settings live in, in the
/// order it would itself consult: a portable directory beside the executable first, then the
/// platform default, then the documented Flatpak location.
/// </summary>
/// <remarks>
/// Every candidate must exist to be selected. Returning a plausible-but-absent path would make a
/// texture scan report "folder missing" for an emulator that is actually installed somewhere else,
/// which reads as a broken feature rather than an unconfigured one.
/// </remarks>
public static class EmulatorUserDirectories
{
    /// <summary>DuckStation's user directory: portable beside the executable, or the platform default.</summary>
    public static string? FindDuckStation(string? installationDirectory, bool isFlatpak)
    {
        return First(Candidates());

        IEnumerable<string?> Candidates()
        {
            if (isFlatpak)
            {
                yield return Flatpak("org.duckstation.DuckStation", "config", "duckstation");
                yield break;
            }

            // DuckStation treats portable.txt, or a settings.ini beside the executable, as portable.
            if (HasAny(installationDirectory, "portable.txt", "settings.ini"))
                yield return installationDirectory;

            yield return Documents("DuckStation");
            yield return Home(".local", "share", "duckstation");
            yield return Home("Library", "Application Support", "DuckStation");
        }
    }

    /// <summary>PCSX2's configuration directory: portable beside the executable, or the platform default.</summary>
    public static string? FindPcsx2(string? installationDirectory, bool isFlatpak)
    {
        return First(Candidates());

        IEnumerable<string?> Candidates()
        {
            if (isFlatpak)
            {
                yield return Flatpak("net.pcsx2.PCSX2", "config", "PCSX2");
                yield break;
            }

            if (HasAny(installationDirectory, "portable.ini", "portable.txt") ||
                HasAny(Combine(installationDirectory, "inis"), "PCSX2.ini"))
            {
                yield return installationDirectory;
            }

            yield return Documents("PCSX2");
            yield return Home(".config", "PCSX2");
            yield return Home("Library", "Application Support", "PCSX2");
        }
    }

    /// <summary>Dolphin's User directory, which holds <c>Config/</c> and <c>Load/Textures/</c>.</summary>
    public static string? FindDolphin(string? installationDirectory, bool isFlatpak)
    {
        return First(Candidates());

        IEnumerable<string?> Candidates()
        {
            if (isFlatpak)
            {
                yield return Flatpak("org.DolphinEmu.dolphin-emu", "data", "dolphin-emu");
                yield break;
            }

            // Dolphin uses a "User" folder beside the executable when one exists (portable builds).
            yield return ExistingDirectory(Combine(installationDirectory, "User"));
            yield return Documents("Dolphin Emulator");
            yield return Home(".local", "share", "dolphin-emu");
            yield return Home("Library", "Application Support", "Dolphin");
        }
    }

    /// <summary>PPSSPP's configuration directory, which holds <c>ppsspp.ini</c>.</summary>
    public static string? FindPpssppConfiguration(string? installationDirectory, bool isFlatpak)
    {
        return First(Candidates());

        IEnumerable<string?> Candidates()
        {
            if (isFlatpak)
            {
                yield return Flatpak("org.ppsspp.PPSSPP", "config", "ppsspp", "PSP", "SYSTEM");
                yield break;
            }

            yield return Combine(installationDirectory, "memstick", "PSP", "SYSTEM");
            yield return Documents("PPSSPP", "PSP", "SYSTEM");
            yield return Home(".config", "ppsspp", "PSP", "SYSTEM");
            yield return Home("Library", "Application Support", "PPSSPP", "PSP", "SYSTEM");
        }
    }

    private static string? First(IEnumerable<string?> candidates) =>
        candidates.Select(ExistingDirectory).FirstOrDefault(path => path is not null);

    private static string? ExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            return Directory.Exists(path) ? Path.GetFullPath(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static bool HasAny(string? directory, params string[] fileNames)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return false;
        try
        {
            return fileNames.Any(name => File.Exists(Path.Combine(directory, name)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static string? Combine(string? directory, params string[] segments) =>
        string.IsNullOrWhiteSpace(directory)
            ? null
            : Path.Combine([directory, .. segments]);

    private static string? Home(params string[] segments) =>
        Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), segments);

    private static string? Documents(params string[] segments) =>
        Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), segments);

    private static string? Flatpak(string applicationId, params string[] segments) =>
        Home([".var", "app", applicationId, .. segments]);
}
