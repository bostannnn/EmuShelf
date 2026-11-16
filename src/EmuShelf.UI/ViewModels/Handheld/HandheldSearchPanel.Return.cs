namespace EmuShelf.App.ViewModels;

/// <summary>
/// Search coming back after a game (M47 B14, the mock's <c>endPlay</c> on the keyboard: <c>focusRes</c>): opened again on what was typed,
/// its hits arrive and the one played takes the focus.
/// </summary>
public sealed partial class HandheldSearchPanel
{
    private long? _focusOnArrival;

    /// <summary>The hit for <paramref name="gameId"/> takes the focus: now if the hits are in (a search that answered at once), else when they arrive.</summary>
    internal void FocusOnArrival(long gameId)
    {
        _focusOnArrival = gameId;
        if (Hits.Count == 0)
            return;
        TakeArrivalFocus();
        RaiseChanged();
    }

    // Called once new hits are in place.
    private void TakeArrivalFocus()
    {
        if (_focusOnArrival is not { } id)
            return;
        _focusOnArrival = null;
        for (var i = 0; i < Hits.Count; i++)
        {
            if (Hits[i].Entry.Id == id)
            {
                FocusHit(i);
                return;
            }
        }
    }
}
