using EmuShelf.Core.Storage;

namespace EmuShelf.Infrastructure.Storage;

public sealed class RelativePathResolver : IRelativePathResolver
{
    private readonly IAppPaths _appPaths;

    public RelativePathResolver(IAppPaths appPaths)
    {
        _appPaths = appPaths;
    }

    public string ToStorablePath(string absolutePath)
    {
        if (!Path.IsPathRooted(absolutePath))
            return absolutePath;

        // On Android the app base and the game live on different mounts that both root at '/', so
        // relativizing would emit a fragile '../../../storage/…' path — store the absolute path instead.
        if (!_appPaths.UsesPortableStorage)
            return absolutePath;

        var appRoot = Path.GetPathRoot(_appPaths.BaseDirectory);
        var pathRoot = Path.GetPathRoot(absolutePath);
        if (!string.Equals(appRoot, pathRoot, StringComparison.OrdinalIgnoreCase))
            return absolutePath;

        // Store the portable form with '/' regardless of OS: a library.db written on
        // Windows must still resolve on macOS/Linux (and vice-versa) when the drive moves.
        // Only the native separator is swapped, so a literal '\' in a POSIX filename survives.
        var relative = Path.GetRelativePath(_appPaths.BaseDirectory, absolutePath);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    public string ToAbsolutePath(string storedPath)
    {
        return Path.IsPathRooted(storedPath)
            ? storedPath
            : Path.GetFullPath(Path.Combine(_appPaths.BaseDirectory, storedPath));
    }
}
