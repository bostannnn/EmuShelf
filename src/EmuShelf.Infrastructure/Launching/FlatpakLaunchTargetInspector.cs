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

        var installed = Execute("info", flatpak.AppId);
        if (installed.ExitCode != 0)
            return LaunchTargetInspection.Failed($"Flatpak application '{flatpak.AppId}' is not installed.");

        foreach (var path in requiredPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var access = Execute("info", $"--file-access={path}", flatpak.AppId);
            if (access.ExitCode != 0 || string.IsNullOrWhiteSpace(access.StandardOutput))
            {
                return new LaunchTargetInspection(
                    true,
                    WarningMessage: $"Could not determine Flatpak access to '{path}'. Launch will be attempted.");
            }

            var level = access.StandardOutput.Trim();
            if (level.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return LaunchTargetInspection.Failed(
                    $"Flatpak application '{flatpak.AppId}' cannot access '{path}'.");
            }

            if (!level.Equals("read", StringComparison.OrdinalIgnoreCase) &&
                !level.Equals("read-write", StringComparison.OrdinalIgnoreCase))
            {
                return new LaunchTargetInspection(
                    true,
                    WarningMessage: $"Flatpak returned an unknown access state for '{path}'. Launch will be attempted.");
            }
        }

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
