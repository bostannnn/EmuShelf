namespace EmuShelf.App.ViewModels;

/// <summary>
/// A hardware key typed while the pad is on Search's hits brings it back to the keys (M47 B14, the mock's <c>kbJump</c>).
/// </summary>
public sealed partial class HandheldSearchPanel
{
    /// <summary>A hardware key typed (the mock's <c>kbJump</c>): the ring goes to that key, off the hits if it was on them.</summary>
    internal void FocusTypedKey(HandheldKeyViewModel key)
    {
        if (IsOnHits)
        {
            IsOnHits = false;
            foreach (var hit in Hits)
                hit.IsFocused = false;
        }
        Keyboard?.FocusKey(key);
        RaiseChanged();
    }
}
