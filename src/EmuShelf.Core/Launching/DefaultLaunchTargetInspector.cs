namespace EmuShelf.Core.Launching;

/// <summary>Local, dependency-free validation for direct launcher targets.</summary>
public sealed class DefaultLaunchTargetInspector : ILaunchTargetInspector
{
    public LaunchTargetInspection Inspect(
        EmulatorLaunchTarget target,
        IReadOnlyList<string> requiredPaths)
    {
        if (target is FlatpakApplicationTarget flatpak)
        {
            return LaunchTargetInspection.Failed(
                $"Flatpak target '{flatpak.AppId}' cannot be inspected on this platform.");
        }

        var direct = (DirectExecutableTarget)target;
        if (!File.Exists(direct.Path))
            return LaunchTargetInspection.Failed("The configured emulator executable was not found.");

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                var mode = File.GetUnixFileMode(direct.Path);
                if ((mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0)
                    return LaunchTargetInspection.Failed("The configured emulator executable is not marked executable.");
            }
            catch (PlatformNotSupportedException)
            {
                // Some platforms do not expose Unix modes. File existence is still useful.
            }
        }

        return LaunchTargetInspection.Passed();
    }
}
