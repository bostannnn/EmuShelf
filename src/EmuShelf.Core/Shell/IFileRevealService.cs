namespace EmuShelf.Core.Shell;

/// <summary>
/// Opens the desktop file manager showing a game's file in its containing folder, with the
/// file selected. Platform-specific reveal mechanics live behind this interface (see the rule
/// in CLAUDE.md that platform behavior goes behind Core interfaces).
/// </summary>
public interface IFileRevealService
{
    /// <summary>
    /// Reveals <paramref name="path"/> in the OS file manager, selecting it inside its
    /// containing folder. When the item itself is gone but its folder still exists, the folder
    /// is opened instead. Throws when neither the item nor its folder can be found, or when the
    /// file manager could not be started.
    /// </summary>
    Task RevealAsync(string path, CancellationToken cancellationToken = default);
}
