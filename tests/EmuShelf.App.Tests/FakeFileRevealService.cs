using EmuShelf.Core.Shell;

namespace EmuShelf.App.Tests;

/// <summary>Records reveal requests (and can be scripted to fail) so the context-menu flow can
/// be driven without opening a real file manager.</summary>
internal sealed class FakeFileRevealService : IFileRevealService
{
    public string? LastRevealedPath { get; private set; }
    public int RevealCount { get; private set; }
    public Exception? Failure { get; set; }

    public Task RevealAsync(string path, CancellationToken cancellationToken = default)
    {
        LastRevealedPath = path;
        RevealCount++;
        return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
    }
}
