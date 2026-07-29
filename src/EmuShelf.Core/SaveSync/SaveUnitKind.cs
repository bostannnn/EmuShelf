namespace EmuShelf.Core.SaveSync;

/// <summary>How a save unit is laid out on disk.</summary>
public enum SaveUnitKind
{
    /// <summary>A single file, e.g. a PCSX2 file memory card (<c>Mcd001.ps2</c>).</summary>
    File,

    /// <summary>A directory, e.g. one per-game subfolder inside a PCSX2 folder memory card.</summary>
    Folder,

    /// <summary>
    /// An explicitly allow-listed set of sibling files, e.g. every GCI belonging to one
    /// GameCube game inside a shared Dolphin card directory.
    /// </summary>
    FileSet,
}
