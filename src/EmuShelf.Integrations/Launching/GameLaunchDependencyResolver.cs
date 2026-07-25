using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Integrations.Importing;

namespace EmuShelf.Integrations.Launching;

/// <summary>Resolves disc-image descriptor trees before a sandboxed launch.</summary>
public sealed class GameLaunchDependencyResolver : IGameLaunchDependencyResolver
{
    public GameLaunchDependencies Resolve(Game game)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            ResolvePath(Path.GetFullPath(game.Path), paths, visiting);
            return new GameLaunchDependencies(true, paths.ToArray());
        }
        catch (DependencyResolutionException exception)
        {
            return new GameLaunchDependencies(false, paths.ToArray(), exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new GameLaunchDependencies(false, paths.ToArray(), "the game descriptor could not be read.");
        }
    }

    private static void ResolvePath(
        string path,
        ISet<string> paths,
        ISet<string> visiting)
    {
        if (!File.Exists(path))
            throw new DependencyResolutionException($"required file '{path}' was not found.");

        paths.Add(path);
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".m3u", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".cue", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".gdi", StringComparison.OrdinalIgnoreCase))
            return;

        if (!visiting.Add(path))
            throw new DependencyResolutionException($"descriptor cycle detected at '{path}'.");

        try
        {
            var references = extension.ToLowerInvariant() switch
            {
                ".m3u" => ReferencedFileParser.ParseM3u(path),
                ".cue" => ReferencedFileParser.ParseCue(path),
                ".gdi" => DreamcastGdiReader.GetReferencedFiles(path),
                _ => [],
            };
            if (references.Count == 0)
                throw new DependencyResolutionException($"descriptor '{path}' has no readable file references.");

            foreach (var reference in references)
                ResolvePath(reference, paths, visiting);
        }
        finally
        {
            visiting.Remove(path);
        }
    }

    private sealed class DependencyResolutionException(string message) : Exception(message);
}
