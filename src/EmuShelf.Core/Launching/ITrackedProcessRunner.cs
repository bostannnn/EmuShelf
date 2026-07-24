namespace EmuShelf.Core.Launching;

/// <summary>Starts one executable directly, tracks it until exit, and never invokes a shell.</summary>
public interface ITrackedProcessRunner
{
    /// <summary>Starts a prepared shell-free process invocation.</summary>
    Task<int> RunAsync(
        ProcessStartSpec startSpec,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            startSpec.FileName,
            startSpec.Arguments,
            startSpec.WorkingDirectory,
            cancellationToken);

    Task<int> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default);
}
