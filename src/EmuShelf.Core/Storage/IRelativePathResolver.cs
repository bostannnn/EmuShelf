namespace EmuShelf.Core.Storage;

/// <summary>
/// Converts between absolute file-system paths and the form stored in the
/// database, so the library survives the app, emulators, and games being
/// moved together to a new drive letter or mount point.
/// </summary>
public interface IRelativePathResolver
{
    /// <summary>Absolute path to persist: relative to the app directory when on the same volume, absolute otherwise.</summary>
    string ToStorablePath(string absolutePath);

    /// <summary>Resolves a stored path (relative or absolute) back to an absolute path.</summary>
    string ToAbsolutePath(string storedPath);
}
