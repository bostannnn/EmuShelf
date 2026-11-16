using Avalonia.Threading;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// After a game played from Search (M47 B14, the mock's <c>endPlay</c>: with the keyboard under the play it lands on the hit again):
/// once the launch is over, whatever ended it (the game closed, the launch failed, the screen question was left with B), Search opens
/// again on what was typed, and the hit played takes the focus when the hits arrive. Big Type only: today's look opens a Title page.
/// </summary>
public sealed partial class HandheldShellViewModel
{
    private sealed record SearchReturn(string Query, long GameId);

    private SearchReturn? _searchReturn;

    partial void OnTitlePageChanged(HandheldTitlePageViewModel? value) => ReturnToSearchIfDue();

    partial void OnPlayingChanged(HandheldPlayViewModel? value) => ReturnToSearchIfDue();

    /// <summary>Search comes back once nothing of the launch is left (no Title page's launch state, no play).</summary>
    private void ReturnToSearchIfDue()
    {
        if (_searchReturn is not { } back || TitlePage is not null || Playing is not null)
            return;
        _searchReturn = null;
        // After the change that ended the launch has reached every view (the shelf's ground flooding back, the Stage's colour fading).
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || Panel is not null || Settings is not null || TitlePage is not null || Playing is not null)
                return;
            _lastSearch = back.Query.Trim();
            var panel = OpenSearchPanel(back.Query);
            panel.FocusOnArrival(back.GameId);
        });
    }
}
