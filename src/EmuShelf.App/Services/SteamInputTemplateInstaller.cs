using Avalonia.Platform;

namespace EmuShelf.App.Services;

/// <summary>The outcome of installing the bundled Steam Input template.</summary>
public enum SteamTemplateInstallStatus
{
    /// <summary>The template was copied into Steam's templates folder.</summary>
    Installed,

    /// <summary>No Steam installation with a controller-templates folder was found.</summary>
    SteamNotFound,

    /// <summary>Steam was found but the copy failed (I/O or permissions).</summary>
    Failed,
}

/// <param name="Status">What happened.</param>
/// <param name="Detail">The destination path when installed, otherwise a short reason.</param>
public sealed record SteamTemplateInstallResult(SteamTemplateInstallStatus Status, string? Detail);

/// <summary>
/// Installs EmuShelf's bundled Steam Input layout ("EmuShelf — Hotkeys for emulators") by copying it
/// into Steam's <c>controller_base/templates/</c> folder, where it appears in the Templates list of the
/// Steam controller-config UI for the user to apply per emulator. Steam exposes no clean API to
/// activate a controller config for an app, so a selectable template is the reliable install path.
/// Both dependencies — locating Steam and opening the bundled template — are injectable so the copy
/// logic is testable without a real Steam install.
/// </summary>
public sealed class SteamInputTemplateInstaller
{
    /// <summary>The file name the template is written as; also its label in Steam's Templates list.</summary>
    public const string TemplateFileName = "EmuShelf.vdf";

    private readonly Func<string?> _resolveSteamRoot;
    private readonly Func<Stream> _openBundledTemplate;

    public SteamInputTemplateInstaller(
        Func<string?>? resolveSteamRoot = null,
        Func<Stream>? openBundledTemplate = null)
    {
        _resolveSteamRoot = resolveSteamRoot ?? DefaultResolveSteamRoot;
        _openBundledTemplate = openBundledTemplate ?? DefaultOpenBundledTemplate;
    }

    public SteamTemplateInstallResult Install()
    {
        var root = _resolveSteamRoot();
        if (root is null)
            return new SteamTemplateInstallResult(SteamTemplateInstallStatus.SteamNotFound,
                "Couldn't find your Steam installation. Launch Steam once, then try again.");

        try
        {
            var templatesDirectory = Path.Combine(root, "controller_base", "templates");
            Directory.CreateDirectory(templatesDirectory);
            var destination = Path.Combine(templatesDirectory, TemplateFileName);

            using (var source = _openBundledTemplate())
            using (var target = File.Create(destination))
            {
                source.CopyTo(target);
            }

            return new SteamTemplateInstallResult(SteamTemplateInstallStatus.Installed, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SteamTemplateInstallResult(SteamTemplateInstallStatus.Failed,
                $"Steam was found but the template couldn't be copied: {ex.Message}");
        }
    }

    /// <summary>The first candidate Steam root that actually has a <c>controller_base/templates</c> folder.</summary>
    private static string? DefaultResolveSteamRoot() =>
        SteamRootCandidates().FirstOrDefault(root =>
        {
            try
            {
                return Directory.Exists(Path.Combine(root, "controller_base", "templates"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return false;
            }
        });

    private static IEnumerable<string> SteamRootCandidates()
    {
        if (OperatingSystem.IsWindows())
        {
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(programFilesX86))
                yield return Path.Combine(programFilesX86, "Steam");
            if (!string.IsNullOrEmpty(programFiles))
                yield return Path.Combine(programFiles, "Steam");
            yield break;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
            yield break;

        if (OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(home, "Library", "Application Support", "Steam");
            yield break;
        }

        // Linux / SteamOS, including the Flatpak sandbox location.
        yield return Path.Combine(home, ".steam", "steam");
        yield return Path.Combine(home, ".local", "share", "Steam");
        yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".steam", "steam");
    }

    private static Stream DefaultOpenBundledTemplate() =>
        AssetLoader.Open(new Uri($"avares://EmuShelf/Assets/SteamInput/{TemplateFileName}"));
}
