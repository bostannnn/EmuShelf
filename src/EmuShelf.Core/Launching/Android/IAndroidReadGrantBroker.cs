using System.Threading;
using System.Threading.Tasks;

namespace EmuShelf.Core.Launching.Android;

/// <summary>
/// Ensures EmuShelf itself holds a <em>delegable</em> read grant for a game's ROM before it is handed to an
/// emulator, so the launcher can attach <c>FLAG_GRANT_READ_URI_PERMISSION</c> and the emulator reads through
/// EmuShelf's grant rather than its own. This removes the dependency on each emulator holding a matching
/// persisted SAF grant — the gap that makes Azahar (and any similarly-behaved emulator) fall back to prompting
/// the user for media/storage permission when it is handed a <c>content://</c> URI it cannot otherwise read.
///
/// EmuShelf's all-files access (<c>MANAGE_EXTERNAL_STORAGE</c>) is <em>not</em> delegable — only a Storage
/// Access Framework grant can be passed to another app — so acquiring one requires a one-time folder pick per
/// library folder. Implemented in the Android head over SAF; the desktop build uses <see cref="Unavailable"/>.
/// </summary>
public interface IAndroidReadGrantBroker
{
    /// <summary>
    /// True when EmuShelf already holds a persisted SAF grant that covers <paramref name="romContentUri"/>,
    /// so a read grant can be delegated to the emulator without prompting.
    /// </summary>
    bool HoldsReadGrantFor(string? romContentUri);

    /// <summary>
    /// Ensures EmuShelf holds a delegable grant covering <paramref name="romContentUri"/>: a no-op returning
    /// true when one is already held, otherwise a one-time system folder pick — pre-navigated to the launch
    /// URI's own <c>/tree/</c> folder, which is always an ancestor-or-self of the game, so granting it always
    /// covers the launch — whose grant is persisted. Returns false, <em>without failing the launch</em>, when
    /// no context is available, the user cancels, or the pick does not cover the game; the caller proceeds to
    /// launch anyway (today's behaviour, where the emulator may prompt for its own access).
    /// </summary>
    Task<bool> EnsureReadGrantAsync(
        string? romContentUri,
        CancellationToken cancellationToken = default);

    /// <summary>The inert broker for platforms without SAF (desktop): never holds a grant, never prompts.</summary>
    public static IAndroidReadGrantBroker Unavailable { get; } = new UnavailableReadGrantBroker();

    private sealed class UnavailableReadGrantBroker : IAndroidReadGrantBroker
    {
        public bool HoldsReadGrantFor(string? romContentUri) => false;

        public Task<bool> EnsureReadGrantAsync(
            string? romContentUri,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
