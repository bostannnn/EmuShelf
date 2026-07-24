using System.Diagnostics;
using EmuShelf.Core.Launching;

namespace EmuShelf.Infrastructure.Launching;

/// <summary>Preflights Flatpak availability and game-file visibility without changing permissions.</summary>
public sealed class FlatpakLaunchTargetInspector : ILaunchTargetInspector
{
    private readonly ILaunchTargetInspector _directInspector = new DefaultLaunchTargetInspector();
    private readonly Func<ProcessStartInfo, Process?> _startProcess;
    private readonly Func<IReadOnlyList<string>, CommandResult>? _execute;

    public FlatpakLaunchTargetInspector()
        : this(Process.Start)
    {
    }

    internal FlatpakLaunchTargetInspector(Func<ProcessStartInfo, Process?> startProcess)
    {
        _startProcess = startProcess;
    }

    internal FlatpakLaunchTargetInspector(Func<IReadOnlyList<string>, CommandResult> execute)
    {
        _startProcess = Process.Start;
        _execute = execute;
    }

    public LaunchTargetInspection Inspect(
        EmulatorLaunchTarget target,
        IReadOnlyList<string> requiredPaths)
    {
        if (target is not FlatpakApplicationTarget flatpak)
            return _directInspector.Inspect(target, requiredPaths);

        // The only remaining precondition is that the application is installed. EmuShelf now grants
        // the sandbox read-only access to the required paths at launch time (see
        // EmulatorLaunchService.BuildReadOnlyFilesystemGrants), so per-file access is guaranteed by
        // the launch itself. Deliberately do NOT probe `flatpak info --file-access`: it reports only
        // the static manifest permissions and prints "hidden" for any path the ephemeral launch
        // grant will make visible, which would wrongly reject launches that actually succeed.
        var installed = Execute("info", flatpak.AppId);
        if (installed.ExitCode != 0)
            return LaunchTargetInspection.Failed($"Flatpak application '{flatpak.AppId}' is not installed.");

        return LaunchTargetInspection.Passed();
    }

    private CommandResult Execute(params string[] arguments)
    {
        if (_execute is not null)
            return _execute(arguments);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "flatpak",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = _startProcess(startInfo);
            if (process is null)
                return new CommandResult(-1, null);
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return new CommandResult(process.ExitCode, output);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new CommandResult(-1, null);
        }
    }

    internal sealed record CommandResult(int ExitCode, string? StandardOutput);
}
