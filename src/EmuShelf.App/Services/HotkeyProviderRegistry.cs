using EmuShelf.Core.Hotkeys;
using EmuShelf.Integrations.Emulators;
using EmuShelf.Integrations.Emulators.Azahar;
using EmuShelf.Integrations.Emulators.Dolphin;
using EmuShelf.Integrations.Emulators.DuckStation;
using EmuShelf.Integrations.Emulators.Pcsx2;
using EmuShelf.Integrations.Emulators.Ppsspp;
using EmuShelf.Integrations.Emulators.RetroArch;
using EmuShelf.Integrations.Emulators.Rpcs3;

namespace EmuShelf.App.Services;

/// <summary>What a descriptor needs to build a configurator for one installed emulator.</summary>
/// <param name="EmulatorDirectory">The directory of the configured executable, or null.</param>
/// <param name="IsFlatpak">Whether the configured installation is a Flatpak target.</param>
/// <param name="BackupRoot">EmuShelf's portable backup directory for hotkey configs.</param>
/// <param name="WriteFile">The durable file writer (AtomicFile in the app, a plain write in tests).</param>
public sealed record HotkeyInstallationContext(
    string? EmulatorDirectory,
    bool IsFlatpak,
    string BackupRoot,
    Action<string, string> WriteFile);

/// <summary>One supported emulator's hotkey configurator factory.</summary>
public sealed record HotkeyEmulatorDescriptor(
    string EmulatorId,
    string DisplayName,
    Func<HotkeyInstallationContext, IEmulatorHotkeyConfigurator?> Create);

/// <summary>
/// The emulators EmuShelf can write a keyboard-hotkey scheme for, in the order Settings shows them.
/// Each descriptor resolves the emulator's config directory (reusing <see cref="EmulatorUserDirectories"/>)
/// and builds its configurator, or returns null when nothing is installed here. This is the single
/// place that names an emulator; the coordinator and view model stay generic.
/// </summary>
public static class HotkeyProviderRegistry
{
    public static IReadOnlyList<HotkeyEmulatorDescriptor> All { get; } =
    [
        new(DuckStationDefinition.Instance.Id, "DuckStation", context => Build(
            EmulatorUserDirectories.FindDuckStation(context.EmulatorDirectory, context.IsFlatpak),
            directory => new DuckStationHotkeyConfigurator(directory, context.BackupRoot, context.WriteFile))),

        new(Pcsx2Definition.Instance.Id, "PCSX2", context => Build(
            EmulatorUserDirectories.FindPcsx2(context.EmulatorDirectory, context.IsFlatpak),
            directory => new Pcsx2HotkeyConfigurator(directory, context.BackupRoot, context.WriteFile))),

        new(DolphinDefinition.Instance.Id, "Dolphin", context => Build(
            EmulatorUserDirectories.FindDolphin(context.EmulatorDirectory, context.IsFlatpak),
            directory => new DolphinHotkeyConfigurator(directory, context.BackupRoot, context.WriteFile))),

        new(PpssppDefinition.Instance.Id, "PPSSPP", context => Build(
            EmulatorUserDirectories.FindPpssppConfiguration(context.EmulatorDirectory, context.IsFlatpak),
            directory => new PpssppHotkeyConfigurator(directory, context.BackupRoot, context.WriteFile))),

        new(RetroArchDefinition.Instance.Id, "RetroArch", context => Build(
            EmulatorUserDirectories.FindRetroArch(context.EmulatorDirectory, context.IsFlatpak),
            directory => new RetroArchHotkeyConfigurator(directory, context.BackupRoot, context.WriteFile))),

        new(AzaharDefinition.Instance.Id, "Azahar", context => Build(
            EmulatorUserDirectories.FindAzahar(context.EmulatorDirectory, context.IsFlatpak),
            directory => new AzaharHotkeyConfigurator(directory, context.BackupRoot, context.WriteFile))),

        // RPCS3 keeps its GUI config in the install directory's GuiConfigs/, not a platform user dir.
        new(Rpcs3Definition.Instance.Id, "RPCS3", context => Build(
            context.EmulatorDirectory,
            directory => new Rpcs3HotkeyConfigurator(directory, context.BackupRoot, context.WriteFile))),
    ];

    public static HotkeyEmulatorDescriptor? Find(string emulatorId) =>
        All.FirstOrDefault(descriptor => string.Equals(descriptor.EmulatorId, emulatorId, StringComparison.Ordinal));

    private static IEmulatorHotkeyConfigurator? Build(string? directory, Func<string, IEmulatorHotkeyConfigurator> create) =>
        directory is null ? null : create(directory);
}
