namespace EmuShelf.Core.Launching;

/// <summary>Describes an already-installed emulator without invoking a shell.</summary>
public abstract record EmulatorLaunchTarget;

/// <summary>A native executable, including an AppImage on Linux.</summary>
public sealed record DirectExecutableTarget(string Path) : EmulatorLaunchTarget;

/// <summary>
/// An installed Flatpak application addressed by its stable application id, optionally pinned to a
/// specific branch. Stable and beta/nightly builds of the same emulator share one application id and
/// differ only by branch (e.g. PCSX2's nightly is <c>net.pcsx2.PCSX2//beta</c>), so a branch is what
/// distinguishes them. A null branch means "whichever branch flatpak treats as current".
/// </summary>
public sealed record FlatpakApplicationTarget(string AppId, string? Branch = null) : EmulatorLaunchTarget
{
    /// <summary>
    /// The flatpak ref used on the command line and by <c>flatpak info</c>/<c>flatpak run</c>: the app
    /// id alone, or <c>appId//branch</c> (arch omitted) when a branch is pinned. Pinning the branch is
    /// what makes those commands unambiguous when more than one branch of the same app is installed.
    /// </summary>
    public string Ref => string.IsNullOrWhiteSpace(Branch) ? AppId : AppId + "//" + Branch;

    /// <summary>
    /// Parses an <c>appId</c> or <c>appId//branch</c> reference back into a target. EmuShelf never emits
    /// an explicit arch segment, so only the branch-only (two-slash) form is recognized; anything after
    /// the first <c>//</c> is taken as the branch.
    /// </summary>
    public static FlatpakApplicationTarget Parse(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        var trimmed = reference.Trim();
        var separator = trimmed.IndexOf("//", StringComparison.Ordinal);
        if (separator < 0)
            return new FlatpakApplicationTarget(trimmed);

        var appId = trimmed[..separator].Trim();
        var branch = trimmed[(separator + 2)..].Trim();
        return new FlatpakApplicationTarget(appId, branch.Length == 0 ? null : branch);
    }
}
