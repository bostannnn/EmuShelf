namespace EmuShelf.Core.Launching;

/// <summary>Result of validating a concrete target and its required game paths.</summary>
public sealed record LaunchTargetInspection(
    bool CanLaunch,
    string? FailureMessage = null,
    string? WarningMessage = null)
{
    public static LaunchTargetInspection Passed(string? warning = null) => new(true, null, warning);
    public static LaunchTargetInspection Failed(string message) => new(false, message);
}

public interface ILaunchTargetInspector
{
    LaunchTargetInspection Inspect(
        EmulatorLaunchTarget target,
        IReadOnlyList<string> requiredPaths);
}
