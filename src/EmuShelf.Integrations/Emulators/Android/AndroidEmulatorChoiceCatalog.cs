using EmuShelf.Core.Launching;

namespace EmuShelf.Integrations.Emulators.Android;

/// <summary>
/// Android's fixed per-system emulator choices. Standalone applications contribute one item and
/// RetroArch contributes one item per compatible core because Android cannot enumerate its private
/// core directory. Ordering follows the maintained-first launch-profile order, so the first item is
/// also the launch default when a system has no saved choice.
/// </summary>
public static class AndroidEmulatorChoiceCatalog
{
    public static IReadOnlyDictionary<string, IReadOnlyList<EmulatorChoice>> BySystem { get; } =
        AndroidEmulatorLaunchProfiles.All
            .SelectMany(profile => profile.SupportedSystemIds)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                systemId => systemId,
                systemId => (IReadOnlyList<EmulatorChoice>)Build(systemId),
                StringComparer.Ordinal);

    public static IReadOnlyList<EmulatorChoice> ForSystem(string systemId) =>
        BySystem.GetValueOrDefault(systemId) ?? [];

    public static EmulatorChoice? Match(string systemId, string? emulatorId, string? corePath) =>
        ForSystem(systemId).FirstOrDefault(choice => choice.Matches(emulatorId, corePath));

    private static List<EmulatorChoice> Build(string systemId)
    {
        var choices = new List<EmulatorChoice>();
        foreach (var profile in AndroidEmulatorLaunchProfiles.ForSystem(systemId))
        {
            if (!string.Equals(
                    profile.SelectionId,
                    AndroidEmulatorLaunchProfiles.RetroArch.SelectionId,
                    StringComparison.Ordinal))
            {
                choices.Add(new EmulatorChoice(
                    profile.SelectionId,
                    profile.DisplayName,
                    profile.SelectionId));
                continue;
            }

            if (!AndroidRetroArchCoreCatalog.BySystem.TryGetValue(systemId, out var cores))
                continue;

            choices.AddRange(cores.Select(core => new EmulatorChoice(
                $"{profile.SelectionId}:{core.CoreId}",
                $"{profile.DisplayName} · {core.DisplayName}",
                profile.SelectionId,
                core.CoreId,
                core.Path)));
        }

        return choices;
    }
}
