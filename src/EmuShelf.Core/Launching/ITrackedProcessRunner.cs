namespace EmuShelf.Core.Launching;

/// <summary>Starts one executable directly, tracks it until exit, and never invokes a shell.</summary>
public interface ITrackedProcessRunner
{
    Task<int> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default);
}
