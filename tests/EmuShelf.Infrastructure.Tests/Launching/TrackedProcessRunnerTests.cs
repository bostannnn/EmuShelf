using System.Diagnostics;
using EmuShelf.Infrastructure.Launching;

namespace EmuShelf.Infrastructure.Tests.Launching;

public class TrackedProcessRunnerTests
{
    [Fact]
    public void RunAsync_StartsProcessOffCallingThread()
    {
        var callingThreadId = 0;
        var startThreadId = 0;
        var exitCode = -1;
        Exception? failure = null;
        var runner = new TrackedProcessRunner(startInfo =>
        {
            startThreadId = Environment.CurrentManagedThreadId;
            return Process.Start(startInfo);
        });
        var dotnetPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current .NET host path is unavailable.");

        var callingThread = new Thread(() =>
        {
            callingThreadId = Environment.CurrentManagedThreadId;
            try
            {
                exitCode = runner.RunAsync(
                        dotnetPath,
                        ["--version"],
                        Directory.GetCurrentDirectory())
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        callingThread.Start();
        callingThread.Join();

        Assert.Null(failure);
        Assert.Equal(0, exitCode);
        Assert.NotEqual(callingThreadId, startThreadId);
    }
}
