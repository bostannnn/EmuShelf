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

    public Task<GameEntrySelection> ScanAsync(
        string folderPath,
        GameSystem system,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var candidates = new List<string>();
            if (!Directory.Exists(folderPath))
                return GameEntrySelection.Empty;

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
            };

            string? lastDirectory = null;
            foreach (var file in Directory.EnumerateFiles(folderPath, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!_rules.IsFolderCandidate(file, system))
                    continue;

                candidates.Add(file);

                var directory = Path.GetDirectoryName(file);
                if (progress is not null && directory != lastDirectory)
                {
                    lastDirectory = directory;
                    progress.Report(new ScanProgress(candidates.Count, directory));
                }
            }

            var selection = _rules.SelectGameEntries(candidates, system);
            progress?.Report(new ScanProgress(selection.EntryPaths.Count, null));
            return selection;
        }, cancellationToken);
    }
}
