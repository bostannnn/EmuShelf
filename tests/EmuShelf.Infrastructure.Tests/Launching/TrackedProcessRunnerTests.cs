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
        // A process guaranteed to start and exit 0 on every host, without depending on the
        // .NET host layout. Environment.ProcessPath is not portable here: under `dotnet test`
        // the Windows testhost runs the test inside testhost.exe, so ProcessPath points at an
        // apphost that cannot self-launch (it has no runtimeconfig beside it) and
        // `<apphost> --version` returns a host error instead of 0.
        var shell = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
        string[] shellArguments = OperatingSystem.IsWindows()
            ? ["/c", "exit", "0"]
            : ["-c", "exit 0"];

        var callingThread = new Thread(() =>
        {
            callingThreadId = Environment.CurrentManagedThreadId;
            try
            {
                exitCode = runner.RunAsync(
                        shell,
                        shellArguments,
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
