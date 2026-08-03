using EmuShelf.Core.Launching;

namespace EmuShelf.Integrations.Emulators.Azahar;

/// <summary>
/// Azahar (the maintained Citra successor) launches the Nintendo 3DS import profile with the game
/// path as one argv entry. Every recognized 3DS container — cartridge dump, single title,
/// installable archive, homebrew, and the seekable-Zstandard compressed variants — is handed over
/// the same way, so no container needs launch-argument handling of its own.
/// </summary>
public static class AzaharDefinition
{
    public static EmulatorDefinition Instance { get; } = new(
        "azahar",
        "Azahar",
        ["3ds"],
        "\"{GamePath}\"");
}
