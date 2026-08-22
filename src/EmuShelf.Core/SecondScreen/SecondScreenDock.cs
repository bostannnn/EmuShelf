namespace EmuShelf.Core.SecondScreen;

/// <summary>
/// The five launchable Android components pinned to the Thor companion-screen dock. Component names
/// are stored in Android's flattened <c>package/class</c> form, but Core deliberately treats them as
/// opaque strings so dock persistence and mutation remain desktop-testable.
/// </summary>
public sealed record SecondScreenDock
{
    public const int SlotCount = 5;

    public SecondScreenDock(IReadOnlyList<string?> components)
    {
        ArgumentNullException.ThrowIfNull(components);

        var normalized = new string?[SlotCount];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < Math.Min(components.Count, SlotCount); index++)
        {
            var component = Normalize(components[index]);
            if (component is not null && seen.Add(component))
                normalized[index] = component;
        }

        // Do not expose the backing array through the IReadOnlyList interface: callers could cast it
        // back to string?[] and mutate even the shared Empty instance.
        Components = Array.AsReadOnly(normalized);
    }

    public IReadOnlyList<string?> Components { get; }

    public string? this[int slot] => Components[ValidateSlot(slot)];

    public static SecondScreenDock Empty { get; } = new(new string?[SlotCount]);

    /// <summary>
    /// Pins one component. An app may occupy only one slot; moving it clears its old slot first.
    /// </summary>
    public SecondScreenDock Pin(int slot, string component)
    {
        ValidateSlot(slot);
        var normalized = Normalize(component)
            ?? throw new ArgumentException("A dock component cannot be empty.", nameof(component));
        var next = Components.ToArray();
        for (var index = 0; index < next.Length; index++)
        {
            if (string.Equals(next[index], normalized, StringComparison.Ordinal))
                next[index] = null;
        }

        next[slot] = normalized;
        return new SecondScreenDock(next);
    }

    public SecondScreenDock Clear(int slot)
    {
        ValidateSlot(slot);
        var next = Components.ToArray();
        next[slot] = null;
        return new SecondScreenDock(next);
    }

    private static int ValidateSlot(int slot)
    {
        if (slot is < 0 or >= SlotCount)
            throw new ArgumentOutOfRangeException(nameof(slot));
        return slot;
    }

    private static string? Normalize(string? component) =>
        string.IsNullOrWhiteSpace(component) ? null : component.Trim();
}

/// <summary>Portable persistence boundary for <see cref="SecondScreenDock"/>.</summary>
public interface ISecondScreenDockStore
{
    SecondScreenDock Load();

    void Save(SecondScreenDock dock);
}

/// <summary>
/// Resolves the companion panel's context. A currently running game always wins over a library
/// selection; once play ends, the focused library game becomes the achievements target.
/// </summary>
public static class SecondScreenTargetResolver
{
    public static long? Resolve(long? runningGameId, long? focusedGameId) =>
        runningGameId is > 0 ? runningGameId : focusedGameId is > 0 ? focusedGameId : null;
}
