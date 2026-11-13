using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using EmuShelf.App.Services;
using EmuShelf.Core.Achievements;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// The play log laid across Handheld mode's two screens (M46 P4), over the Activity view model Gamepad mode and the Desktop
/// window use. The Launcher lists the current page's rows: the week's sessions (each with its day), the ranking, or one
/// game's sessions; while a session is edited, Save, Delete and Cancel. The Stage's card is the detail: the focused
/// session's day (its total and the games played), or the focused game's stats (total, times played, average per session,
/// first and last played). Every press goes to the view model, so both modes behave the same.
/// </summary>
public sealed class HandheldActivityPanel : HandheldPanelViewModel
{
    /// <summary>The day card names this many games before "and N more".</summary>
    private const int DayCardGames = 4;

    private bool _rowsStale = true;
    private (ActivityPage Page, bool Editing) _shape;
    private int _chip;

    public HandheldActivityPanel(ActivityViewModel activity)
    {
        Activity = activity;
        activity.PropertyChanged += OnActivityChanged;
        activity.Days.CollectionChanged += OnRowsChanged;
        activity.RankItems.CollectionChanged += OnRowsChanged;
        activity.GameSessions.CollectionChanged += OnRowsChanged;
        Sync();
    }

    public ActivityViewModel Activity { get; }

    public override string Heading => Activity.IsGamePage ? Activity.GameTitle : HandheldActivityText.Name;

    public override string Subheading => Activity.Page switch
    {
        ActivityPage.Ranking => $"Ranked by {Activity.RankingMetricLabel.ToLowerInvariant()}",
        ActivityPage.Game => "Play history",
        _ => Activity.WeekLabel,
    };

    public override void Dispatch(GamepadAction action)
    {
        if (!Activity.Dispatch(action))
        {
            RequestClose();
            return;
        }
        Sync();
        RaiseChanged();
    }

    public override void TapRow(HandheldPanelRowViewModel row)
    {
        var index = Rows.IndexOf(row);
        if (index < 0)
            return;
        // A tap selects; a second tap on the selected row does what A does (Save, Delete, Cancel at once in the editor).
        if (Activity.IsEditing)
        {
            Activity.FocusEdit(index);
        }
        else if (index != FocusIndex)
        {
            Activity.FocusRow(index);
            Sync();
            RaiseChanged();
            return;
        }
        Dispatch(GamepadAction.Confirm);
    }

    public override void TapChip(HandheldPanelChipViewModel chip)
    {
        var index = Chips.IndexOf(chip);
        if (index == 0)
            Activity.ShowTimelineCommand.Execute(null);
        else if (index == 1)
            Activity.ShowRankingCommand.Execute(null);
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e) => _rowsStale = true;

    private void OnActivityChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsDisposed)
            return;
        Sync();
        RaiseChanged();
    }

    /// <summary>Brings the rows, the chips, the focus and the status line in line with the view model.</summary>
    private void Sync()
    {
        var shape = (Activity.Page, Activity.IsEditing);
        if (_rowsStale || shape != _shape)
        {
            _rowsStale = false;
            _shape = shape;
            SetRows(BuildRows(), Activity.IsEditing ? Activity.EditFocus : Activity.FocusedIndex);
        }
        else if (Activity.IsEditing)
        {
            UpdateEditRows();
            Focus(Activity.EditFocus);
        }
        else
        {
            Focus(Activity.FocusedIndex);
        }

        // The chips say which list you are on; a game's page keeps the one it was opened from.
        _chip = Activity.Page switch
        {
            ActivityPage.Timeline => 0,
            ActivityPage.Ranking => 1,
            _ => _chip,
        };
        if (Chips.Count == 0)
            SetChips(["Timeline", "Ranking"], _chip);
        for (var i = 0; i < Chips.Count; i++)
            Chips[i].IsSelected = i == _chip;

        IsBusy = Activity.IsLoading;
        Status = Activity.IsEditing
            ? null
            : Activity.Page switch
            {
                ActivityPage.Timeline => $"{Activity.WeekTotalText} · left and right for other weeks",
                ActivityPage.Ranking => "Left and right change the order",
                _ => null,
            };
        EmptyText = Activity.IsLoading
            ? "Reading the play log…"
            : Activity.Page switch
            {
                ActivityPage.Ranking => "No games played yet",
                ActivityPage.Game => "No sessions in the play log yet",
                _ => $"Nothing played {WeekPhrase()}",
            };
        OnPropertyChanged(nameof(HasEmptyText));
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(Subheading));
        OnPropertyChanged(nameof(HasSubheading));
    }

    private string WeekPhrase() => Activity.WeekLabel switch
    {
        "This week" => "this week",
        "Last week" => "last week",
        var label => $"between {label}",
    };

    private IEnumerable<HandheldPanelRowViewModel> BuildRows()
    {
        if (Activity.EditingSession is not null)
        {
            yield return new HandheldPanelRowViewModel(HandheldPanelRowKind.Button, "Save") { IsPrimary = true, Value = Activity.EditLengthText };
            yield return new HandheldPanelRowViewModel(HandheldPanelRowKind.Button, DeleteLabel());
            yield return new HandheldPanelRowViewModel(HandheldPanelRowKind.Button, "Cancel");
            yield break;
        }
        switch (Activity.Page)
        {
            case ActivityPage.Ranking:
                foreach (var rank in Activity.RankItems)
                {
                    yield return new HandheldPanelRowViewModel(HandheldPanelRowKind.Item, $"{rank.Place}. {rank.Title}", rank)
                    {
                        Value = rank.ValueText,
                        Detail = rank.DetailText,
                    };
                }
                break;
            case ActivityPage.Game:
                foreach (var session in Activity.GameSessions)
                {
                    yield return new HandheldPanelRowViewModel(HandheldPanelRowKind.Item, session.FullWhenText, session)
                    {
                        Value = session.LengthText,
                        Detail = session.HasNote ? session.NoteText : null,
                    };
                }
                break;
            default:
                // A row per session, newest day first; the day is in each row's line, and its total on the Stage.
                foreach (var day in Activity.Days)
                {
                    foreach (var session in day.Sessions)
                    {
                        var when = $"{day.Label} · {session.WhenText}";
                        yield return new HandheldPanelRowViewModel(HandheldPanelRowKind.Item, session.GameTitle, session)
                        {
                            Value = session.LengthText,
                            Detail = session.HasNote ? $"{when} · {session.NoteText}" : when,
                        };
                    }
                }
                break;
        }
    }

    private string DeleteLabel() => Activity.IsConfirmingDelete ? "Press A again to delete" : "Delete session";

    private void UpdateEditRows()
    {
        if (Rows.Count < 2)
            return;
        Rows[0].Value = Activity.EditLengthText;
        Rows[1].Label = DeleteLabel();
    }

    private ActivityDayViewModel? DayOf(ActivitySessionViewModel session) =>
        Activity.Days.FirstOrDefault(day => day.Sessions.Contains(session));

    public override HandheldPanelCard Card
    {
        get
        {
            if (Activity.EditingSession is { } editing)
            {
                return new HandheldPanelCard("Edit session", editing.GameTitle, Activity.EditLengthText,
                    $"{editing.FullWhenText}. Left and right change it by a minute, LB and RB by ten.",
                    Activity.IsConfirmingDelete ? "Deleting takes this session's time off the game's total." : null,
                    IsWarning: Activity.IsConfirmingDelete, Glyph: HandheldGlyph.Activity);
            }
            return Activity.Page switch
            {
                ActivityPage.Ranking => RankingCard(),
                ActivityPage.Game => GameCard(),
                _ => DayCard(),
            };
        }
    }

    // The focused session's day: its total, and what was played for how long.
    private HandheldPanelCard DayCard()
    {
        var eyebrow = $"{HandheldActivityText.Name} · {Activity.WeekLabel}";
        if (Activity.FocusedRow is not ActivitySessionViewModel session || DayOf(session) is not { } day)
        {
            return new HandheldPanelCard(eyebrow, Activity.IsLoading ? "Reading the play log…" : $"Nothing played {WeekPhrase()}",
                Description: Activity.IsLoading ? null : "Left for earlier weeks; the ranking is on RB.", Glyph: HandheldGlyph.Activity);
        }
        var games = day.Sessions
            .GroupBy(entry => entry.GameTitle, StringComparer.Ordinal)
            .Select(group => (Title: group.Key, Total: TimeSpan.FromSeconds(group.Sum(entry => entry.Session.Duration?.TotalSeconds ?? 0))))
            .OrderByDescending(game => game.Total)
            .ToList();
        var named = games.Take(DayCardGames).Select(game => $"{game.Title} {ActivityText.Duration(game.Total, zeroText: "?")}");
        var description = string.Join(" · ", named) + (games.Count > DayCardGames ? $" · and {games.Count - DayCardGames} more" : string.Empty);
        var plays = day.Sessions.Count == 1 ? "1 session" : $"{day.Sessions.Count} sessions";
        var status = $"{session.GameTitle} at {session.WhenText} · {session.LengthText}" + (session.HasNote ? $" · {session.NoteText}" : string.Empty);
        return new HandheldPanelCard(eyebrow, day.Label, $"{day.TotalText} · {plays}", description, status, Glyph: HandheldGlyph.Activity);
    }

    // The focused game's place and stats.
    private HandheldPanelCard RankingCard()
    {
        if (Activity.FocusedRow is not ActivityRankItemViewModel rank)
        {
            return new HandheldPanelCard(HandheldActivityText.Name, Activity.IsLoading ? "Reading the play log…" : "No games played yet",
                Glyph: HandheldGlyph.Activity);
        }
        var ranking = rank.Ranking;
        var plays = ranking.Plays == 1 ? "played once" : $"played {ranking.Plays} times";
        var description = ranking.Plays == 0
            ? $"{ActivityText.Duration(ranking.Total)} in all"
            : $"{ActivityText.Duration(ranking.Total)} in all · {plays} · {ActivityText.Duration(ranking.Average)} a session";
        var lastPlayed = ranking.Game.LastPlayedAt is { } last ? $"Last played {ActivityText.Date(last, Activity.Zone)}" : null;
        return new HandheldPanelCard($"#{rank.Place} by {Activity.RankingMetricLabel.ToLowerInvariant()}", rank.Title, rank.ValueText,
            description, lastPlayed, Glyph: HandheldGlyph.Activity);
    }

    // The game's stats; the focused session's date and length under them.
    private HandheldPanelCard GameCard()
    {
        var average = Activity.GameAverageText == RetroAchievementsDisplay.Dash ? null : $" · {Activity.GameAverageText} a session";
        var description = $"Played {Activity.GamePlaysText}{average}\n" +
            $"First played {Activity.GameFirstPlayedText} · last played {Activity.GameLastPlayedText}";
        if (Activity.HasGameEarlierPlay)
            description += $"\n{Activity.GameEarlierText}";
        var status = Activity.FocusedRow is ActivitySessionViewModel session
            ? $"{session.FullWhenText} · {session.LengthText}" + (session.HasNote ? $" · {session.NoteText}" : string.Empty)
            : null;
        return new HandheldPanelCard(HandheldActivityText.Name, Activity.GameTitle, Activity.GameTotalText, description, status,
            Glyph: HandheldGlyph.Activity);
    }

    public override IEnumerable<HandheldHint> Hints
    {
        get
        {
            if (Activity.IsEditing)
            {
                yield return new HandheldHint("A", "Select");
                yield return new HandheldHint("RB", "+10 min");
                yield return new HandheldHint("B", "Cancel");
                yield break;
            }
            switch (Activity.FocusedRow)
            {
                case ActivityRankItemViewModel:
                    yield return new HandheldHint("A", "Open");
                    break;
                case ActivitySessionViewModel session:
                    if (!Activity.IsGamePage && !session.IsGameRemoved)
                        yield return new HandheldHint("A", "Open game");
                    yield return new HandheldHint("Y", "Edit");
                    break;
            }
            if (!Activity.IsGamePage)
                yield return new HandheldHint("RB", Activity.IsRanking ? "Timeline" : "Ranking");
            yield return new HandheldHint("B", Activity.IsGamePage ? "Back" : "Close");
        }
    }

    protected override void DisposeCore()
    {
        Activity.PropertyChanged -= OnActivityChanged;
        Activity.Days.CollectionChanged -= OnRowsChanged;
        Activity.RankItems.CollectionChanged -= OnRowsChanged;
        Activity.GameSessions.CollectionChanged -= OnRowsChanged;
    }
}
