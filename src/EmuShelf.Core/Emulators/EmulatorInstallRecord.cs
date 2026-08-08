namespace EmuShelf.Core.Emulators;

/// <summary>
/// One managed emulator install, as written to the portable install manifest. Because EmuShelf is the
/// installer, the version is read from this record rather than probed out of the binary, and the
/// executable is stored as a path relative to the app's base directory so it moves with the portable
/// install.
/// </summary>
/// <param name="EmulatorId">The integration id, e.g. <c>duckstation</c>.</param>
/// <param name="InstalledVersion">The release tag/name EmuShelf installed.</param>
/// <param name="InstalledAt">When the install was written.</param>
/// <param name="ExecutableRelativePath">
/// The launchable executable's path relative to the app base directory, with <c>/</c> separators.
/// </param>
/// <param name="SourceTag">The release tag the asset came from, used to detect a newer build.</param>
public sealed record EmulatorInstallRecord(
    string EmulatorId,
    string InstalledVersion,
    DateTimeOffset InstalledAt,
    string ExecutableRelativePath,
    string SourceTag);

/// <summary>
/// Reads and writes the portable manifest of EmuShelf-managed emulator installs. The manifest is the
/// authority for "what is installed and at what version"; a managed install is only ever overwritten when
/// its emulator id already appears here.
/// </summary>
public interface IEmulatorInstallManifestStore
{
    /// <summary>The recorded managed install for an emulator, or null when none is managed.</summary>
    EmulatorInstallRecord? Get(string emulatorId);

    /// <summary>Every recorded managed install.</summary>
    IReadOnlyList<EmulatorInstallRecord> GetAll();

    /// <summary>Inserts or replaces the record for its emulator id.</summary>
    void Save(EmulatorInstallRecord record);

    /// <summary>Removes the record for an emulator id, if present.</summary>
    void Remove(string emulatorId);
}
