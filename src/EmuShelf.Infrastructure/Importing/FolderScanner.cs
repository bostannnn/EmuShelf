using EmuShelf.Core.Importing;
using EmuShelf.Core.Systems;

namespace EmuShelf.Infrastructure.Importing;

/// <summary>
/// Shared recursive scanner. Runs the walk on a background thread, skips folders it
/// can't read, reports progress, and filters candidates through <see cref="IGameImportRules"/>.
/// </summary>
public sealed class FolderScanner : IFolderScanner
{
    private readonly IGameImportRules _rules;

    public FolderScanner(IGameImportRules rules)
    {
        _rules = rules;
    }

    public Task<IReadOnlyList<string>> ScanAsync(
        string folderPath,
        GameSystem system,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            var candidates = new List<string>();
            if (!Directory.Exists(folderPath))
                return candidates;

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
            };

            string? lastDirectory = null;
            foreach (var file in Directory.EnumerateFiles(folderPath, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!_rules.IsCandidate(file, system))
                    continue;

                candidates.Add(file);

                var directory = Path.GetDirectoryName(file);
                if (progress is not null && directory != lastDirectory)
                {
                    lastDirectory = directory;
                    progress.Report(new ScanProgress(candidates.Count, directory));
                }
            }

            progress?.Report(new ScanProgress(candidates.Count, null));
            return candidates;
        }, cancellationToken);
    }
}
