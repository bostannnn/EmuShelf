namespace EmuShelf.Core.Settings;

/// <summary>
/// Portable auto-update preferences. Holds no secret. Checks hit the public GitHub Releases API for
/// the EmuShelf repository; nothing about the user or their library is sent.
/// </summary>
public sealed record UpdateSettings
{
    /// <summary>
    /// Whether EmuShelf checks GitHub for a newer release shortly after launch. The manual
    /// "Check for updates" action in Settings works regardless of this toggle.
    /// </summary>
    public bool AutomaticallyCheck { get; init; } = true;

    /// <summary>When the last automatic check ran, used to throttle checks to about once a day.</summary>
    public DateTimeOffset? LastCheckUtc { get; init; }

    /// <summary>
    /// A version the user chose to skip (e.g. "1.2.3"). The launch banner stays hidden for this
    /// version; a manual check still surfaces it. Null once a newer version supersedes it.
    /// </summary>
    public string? SkippedVersion { get; init; }
}
