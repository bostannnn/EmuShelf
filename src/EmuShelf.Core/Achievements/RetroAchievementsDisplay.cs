namespace EmuShelf.Core.Achievements;

/// <summary>
/// How a game's achievement state is presented: whether the grid tile shows the trophy mark, the
/// list column text (a progress fraction or an em dash), the tooltip that explains the state, and
/// the softcore and hardcore unlock counts so the couch widget can show both at once. This is pure:
/// the same link + progress + connection always yield the same presentation, so the grid mark and
/// the list column can never disagree.
/// </summary>
/// <param name="Awarded">Softcore-inclusive unlocks (every unlock counts), or 0.</param>
/// <param name="AwardedHardcore">The hardcore subset of <paramref name="Awarded"/>, or 0.</param>
/// <param name="Total">The game's achievement count when progress is known, otherwise 0.</param>
public sealed record RetroAchievementsDisplay(
    bool ShowMark,
    string ColumnText,
    string Tooltip,
    int Awarded = 0,
    int AwardedHardcore = 0,
    int Total = 0)
{
    public const string Dash = "—";

    /// <summary>Applies the console scope before looking at a locally stored identification link.</summary>
    public static RetroAchievementsDisplay For(
        string systemId,
        bool connected,
        RetroAchievementsGameLink? link,
        RetroAchievementsProgressSnapshot? progress)
    {
        if (RetroAchievementsConsoles.ForSystem(systemId) is null)
            return Hidden("This console isn't supported by RetroAchievements.");

        return For(connected, link, progress);
    }

    public static RetroAchievementsDisplay For(
        bool connected,
        RetroAchievementsGameLink? link,
        RetroAchievementsProgressSnapshot? progress)
    {
        if (link is null || link.Status == RetroAchievementsIdentificationStatus.NotAttempted)
            return Hidden("Achievement identification is still pending.");

        switch (link.Status)
        {
            case RetroAchievementsIdentificationStatus.UnsupportedFormat:
                return Hidden("This format isn't supported for achievements yet.");
            case RetroAchievementsIdentificationStatus.InvalidMedia:
            case RetroAchievementsIdentificationStatus.Unreadable:
                return Hidden("The game image couldn't be read for achievements.");
        }

        // The game was hashed successfully from here on.
        if (link.HasAchievements == true)
        {
            if (progress is not null)
            {
                var summary = progress.Progress;
                var tooltip = summary.NumAwardedHardcore > 0
                    ? $"{summary.NumAwarded} of {summary.AchievementCount} unlocked ({summary.NumAwardedHardcore} hardcore)."
                    : $"{summary.NumAwarded} of {summary.AchievementCount} unlocked.";
                return new RetroAchievementsDisplay(
                    ShowMark: true,
                    $"{summary.NumAwarded}/{summary.AchievementCount}",
                    tooltip,
                    summary.NumAwarded,
                    summary.NumAwardedHardcore,
                    summary.AchievementCount);
            }

            return new RetroAchievementsDisplay(
                ShowMark: true,
                Dash,
                connected
                    ? "Your progress for this game hasn't loaded yet."
                    : "Connect RetroAchievements to see your progress.");
        }

        if (link.HasAchievements == false)
            return Hidden("No achievement set for this game.");

        // Hashed, but not yet resolved against a fresh catalogue (HasAchievements is null).
        return Hidden(connected
            ? "Checking the achievement catalogue…"
            : "Connect RetroAchievements to check for achievements.");
    }

    private static RetroAchievementsDisplay Hidden(string tooltip) => new(false, Dash, tooltip);
}
