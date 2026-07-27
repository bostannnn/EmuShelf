namespace EmuShelf.Core.Settings;

/// <summary>
/// Main-window geometry restored on the next launch. Position is nullable so a first run — and a
/// saved position that no longer lands on any connected display — falls back to centring rather
/// than opening the window off-screen.
/// </summary>
public sealed record WindowLayoutSettings
{
    public const double DefaultWidth = 1240;
    public const double DefaultHeight = 800;

    public double Width { get; init; } = DefaultWidth;

    public double Height { get; init; } = DefaultHeight;

    public int? Left { get; init; }

    public int? Top { get; init; }

    /// <summary>When true, size and position describe the window's restored (unmaximized) bounds.</summary>
    public bool IsMaximized { get; init; }
}
