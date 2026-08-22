using EmuShelf.Core.Launching;

namespace EmuShelf.Integrations.Emulators;

/// <summary>
/// Builds the desktop picker from registered standalone emulators and the RetroArch cores discovered
/// on disk by the settings row. The source is intentionally pure: filesystem discovery stays in the
/// UI model where executable and Flatpak drafts are available, while this class owns the flat choice
/// shape and matching rules shared by tests and presentation.
/// </summary>
public static class DesktopEmulatorChoiceCatalog
{
    public static IReadOnlyList<EmulatorChoice> ForSystem(
        string systemId,
        IReadOnlyList<EmulatorDefinition> emulators,
        IEnumerable<string> corePaths,
        bool coreTargetConfigured,
        string? rememberedCorePath = null)
    {
        var choices = new List<EmulatorChoice>();
        var discoveredCores = corePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .DistinctBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var emulator in emulators.Where(candidate => candidate.Supports(systemId)))
        {
            if (!emulator.RequiresCorePath)
            {
                choices.Add(new EmulatorChoice(emulator.Id, emulator.Name, emulator.Id));
                continue;
            }

            var currentCore = !string.IsNullOrWhiteSpace(rememberedCorePath)
                ? rememberedCorePath.Trim()
                : null;
            if (currentCore is not null && !discoveredCores.Contains(currentCore, StringComparer.OrdinalIgnoreCase))
            {
                choices.Add(CoreChoice(emulator, currentCore, isCurrentMissingCore: true));
            }

            if (discoveredCores.Count > 0)
            {
                choices.AddRange(discoveredCores.Select(path => CoreChoice(emulator, path)));
                continue;
            }

            if (currentCore is null)
            {
                choices.Add(new EmulatorChoice(
                    $"{emulator.Id}:configure",
                    coreTargetConfigured
                        ? $"{emulator.Name} (no installed cores found)"
                        : $"{emulator.Name} (set executable to choose a core)",
                    emulator.Id));
            }
        }

        return choices;
    }

    public static EmulatorChoice? Match(
        IEnumerable<EmulatorChoice> choices,
        string? emulatorId,
        string? corePath) =>
        choices.FirstOrDefault(choice => choice.Matches(emulatorId, corePath));

    private static EmulatorChoice CoreChoice(
        EmulatorDefinition emulator,
        string path,
        bool isCurrentMissingCore = false)
    {
        var coreId = Path.GetFileNameWithoutExtension(path);
        if (coreId.EndsWith("_libretro", StringComparison.OrdinalIgnoreCase))
            coreId = coreId[..^"_libretro".Length];

        var suffix = isCurrentMissingCore ? " (current; file not found)" : string.Empty;
        return new EmulatorChoice(
            $"{emulator.Id}:{coreId}",
            $"{emulator.Name} · {coreId}{suffix}",
            emulator.Id,
            coreId,
            path);
    }
}
