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

    /// <summary>
    /// Dolphin's User directory, which holds <c>Config/</c> and <c>Load/Textures/</c>.
    /// </summary>
    /// <remarks>
    /// Dolphin has no settings key naming its own user directory the way PCSX2 and DuckStation name
    /// their texture folder, and a launcher can point it somewhere else entirely with <c>-u</c>. A
    /// machine can therefore hold several valid-looking User directories at once. Among the ones
    /// that exist, this prefers whichever actually contains texture packs: an empty
    /// <c>Load/Textures</c> can never produce a match, so choosing it over a populated sibling is
    /// strictly worse and never what the user meant. The Settings override remains the escape hatch
    /// when the guess is wrong.
    /// </remarks>
    public static string? FindDolphin(string? installationDirectory, bool isFlatpak)
    {
        var existing = Candidates().Select(ExistingDirectory).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return existing.FirstOrDefault(HasTexturePacks) ?? existing.FirstOrDefault();

        IEnumerable<string?> Candidates()
        {
            if (isFlatpak)
            {
                yield return Flatpak("org.DolphinEmu.dolphin-emu", "data", "dolphin-emu");
                yield break;
            }

            // Dolphin uses a "User" folder beside the executable when one exists (portable builds).
            yield return Combine(installationDirectory, "User");

            // Frontends that manage their own emulator tree (ES-DE and similar) keep the binaries
            // under <root>/Emulators/... and the per-emulator data under <root>/saves/<name>/User,
            // then launch Dolphin with -u pointing there. Walk up from the executable looking for
            // that sibling rather than assuming the data sits beside the binary.
            foreach (var candidate in FrontendManagedDolphinDirectories(installationDirectory))
                yield return candidate;

            yield return Documents("Dolphin Emulator");
            yield return Home(".local", "share", "dolphin-emu");
            yield return Home("Library", "Application Support", "Dolphin");
        }
    }

    private static IEnumerable<string?> FrontendManagedDolphinDirectories(string? installationDirectory)
    {
        if (string.IsNullOrWhiteSpace(installationDirectory))
            yield break;

        DirectoryInfo? directory;
        try
        {
            directory = new DirectoryInfo(installationDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            yield break;
        }

        // Bounded walk: deep enough to clear <root>/Emulators/dolphin-emu, short enough that this
        // never turns into a filesystem crawl.
        for (var depth = 0; depth < 4 && directory is not null; depth++, directory = directory.Parent)
        {
            foreach (var name in DolphinDataDirectoryNames)
                yield return Combine(directory.FullName, "saves", name, "User");
        }
    }

    private static readonly string[] DolphinDataDirectoryNames = ["dolphin", "dolphin-emu"];

    /// <summary>Whether a User directory holds at least one texture-pack folder.</summary>
    private static bool HasTexturePacks(string userDirectory)
    {
        try
        {
            var textures = Path.Combine(userDirectory, "Load", "Textures");
            return Directory.Exists(textures) && Directory.EnumerateDirectories(textures).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
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
