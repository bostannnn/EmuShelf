using System.Diagnostics;
using EmuShelf.Core.Launching;

namespace EmuShelf.Infrastructure.Launching;

public sealed class TrackedProcessRunner : ITrackedProcessRunner
{
    private readonly Func<ProcessStartInfo, Process?> _startProcess;

    public TrackedProcessRunner()
        : this(Process.Start)
    {
    }

    internal TrackedProcessRunner(Func<ProcessStartInfo, Process?> startProcess)
    {
        _startProcess = startProcess;
    }

    public async Task<int> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = await Task.Run(
            () => _startProcess(startInfo),
            cancellationToken)
            ?? throw new InvalidOperationException("The operating system did not start the emulator process.");
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

}
