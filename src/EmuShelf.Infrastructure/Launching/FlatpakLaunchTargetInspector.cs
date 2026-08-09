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
        //
        // Check installation by listing installed refs rather than `flatpak info <appId>`: when both a
        // stable and a beta/nightly branch of the same app are installed, `flatpak info <appId>` fails
        // with "Multiple branches available…" and the app looks uninstalled even though it launches
        // fine. Listing branches is unambiguous — an unpinned target passes if any branch is present,
        // and a branch-pinned target (e.g. the nightly) passes only when that exact branch is present.
        var listing = Execute("list", "--app", "--columns=application,branch");
        if (listing.ExitCode != 0)
            return LaunchTargetInspection.Failed($"Flatpak application '{flatpak.Ref}' is not installed.");

        var installed = FlatpakApplicationDiscovery.ParseInstalledRefs(listing.StandardOutput ?? string.Empty);
        var isInstalled = installed.Any(reference =>
            string.Equals(reference.AppId, flatpak.AppId, StringComparison.Ordinal) &&
            (string.IsNullOrWhiteSpace(flatpak.Branch) ||
             string.Equals(reference.Branch, flatpak.Branch, StringComparison.Ordinal)));
        if (!isInstalled)
            return LaunchTargetInspection.Failed($"Flatpak application '{flatpak.Ref}' is not installed.");

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
