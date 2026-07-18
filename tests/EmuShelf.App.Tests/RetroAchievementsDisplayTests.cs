using EmuShelf.Core.Achievements;

namespace EmuShelf.App.Tests;

public class RetroAchievementsDisplayTests
{
    [Fact]
    public void NullLink_IsPendingDashWithoutMark()
    {
        var display = RetroAchievementsDisplay.For(connected: true, link: null, progress: null);

        Assert.False(display.ShowMark);
        Assert.Equal(RetroAchievementsDisplay.Dash, display.ColumnText);
        Assert.Contains("pending", display.Tooltip);
    }

    [Theory]
    [InlineData(RetroAchievementsIdentificationStatus.NotAttempted, "pending")]
    [InlineData(RetroAchievementsIdentificationStatus.UnsupportedFormat, "isn't supported")]
    [InlineData(RetroAchievementsIdentificationStatus.InvalidMedia, "couldn't be read")]
    [InlineData(RetroAchievementsIdentificationStatus.Unreadable, "couldn't be read")]
    public void NonHashedStatuses_ShowDashNoMark(
        RetroAchievementsIdentificationStatus status,
        string tooltipFragment)
    {
        var display = RetroAchievementsDisplay.For(connected: true, Link(status), progress: null);

        Assert.False(display.ShowMark);
        Assert.Equal(RetroAchievementsDisplay.Dash, display.ColumnText);
        Assert.Contains(tooltipFragment, display.Tooltip);
    }

    [Fact]
    public void Matched_WithProgress_ShowsMarkAndFraction()
    {
        var link = Link(RetroAchievementsIdentificationStatus.Hashed, hasAchievements: true);
        var display = RetroAchievementsDisplay.For(connected: true, link, Progress(total: 40, awarded: 12));

        Assert.True(display.ShowMark);
        Assert.Equal("12/40", display.ColumnText);
        Assert.Equal("12 of 40 unlocked.", display.Tooltip);
    }

    [Fact]
    public void Matched_WithHardcoreUnlocks_NotesHardcoreInTooltip()
    {
        var link = Link(RetroAchievementsIdentificationStatus.Hashed, hasAchievements: true);
        var display = RetroAchievementsDisplay.For(
            connected: true, link, Progress(total: 40, awarded: 12, hardcore: 3));

        Assert.Equal("12/40", display.ColumnText);
        Assert.Contains("3 hardcore", display.Tooltip);
    }

    [Fact]
    public void Matched_NoProgressYet_Connected_ShowsMarkAndDash()
    {
        var link = Link(RetroAchievementsIdentificationStatus.Hashed, hasAchievements: true);
        var display = RetroAchievementsDisplay.For(connected: true, link, progress: null);

        Assert.True(display.ShowMark); // the mark is account-independent
        Assert.Equal(RetroAchievementsDisplay.Dash, display.ColumnText);
        Assert.Contains("hasn't loaded", display.Tooltip);
    }

    [Fact]
    public void Matched_NoProgress_NotConnected_InvitesConnection()
    {
        var link = Link(RetroAchievementsIdentificationStatus.Hashed, hasAchievements: true);
        var display = RetroAchievementsDisplay.For(connected: false, link, progress: null);

        Assert.True(display.ShowMark);
        Assert.Contains("Connect RetroAchievements to see your progress", display.Tooltip);
    }

    [Fact]
    public void FreshMiss_NoSet_ShowsDashNoMark()
    {
        var link = Link(RetroAchievementsIdentificationStatus.Hashed, hasAchievements: false);
        var display = RetroAchievementsDisplay.For(connected: true, link, progress: null);

        Assert.False(display.ShowMark);
        Assert.Equal(RetroAchievementsDisplay.Dash, display.ColumnText);
        Assert.Contains("No achievement set", display.Tooltip);
    }

    [Theory]
    [InlineData(true, "Checking the achievement catalogue")]
    [InlineData(false, "Connect RetroAchievements to check")]
    public void HashedButUnresolved_DependsOnConnection(bool connected, string tooltipFragment)
    {
        var link = Link(RetroAchievementsIdentificationStatus.Hashed, hasAchievements: null);
        var display = RetroAchievementsDisplay.For(connected, link, progress: null);

        Assert.False(display.ShowMark);
        Assert.Equal(RetroAchievementsDisplay.Dash, display.ColumnText);
        Assert.Contains(tooltipFragment, display.Tooltip);
    }

    private static RetroAchievementsGameLink Link(
        RetroAchievementsIdentificationStatus status,
        bool? hasAchievements = null,
        int? raGameId = null) =>
        new(1, status, "hash", "algo", "fingerprint", raGameId, hasAchievements, DateTimeOffset.UtcNow, null);

    private static RetroAchievementsProgressSnapshot Progress(int total, int awarded, int hardcore = 0) =>
        new(new RetroAchievementsGameProgress(1234, total, awarded, hardcore), DateTimeOffset.UtcNow);
}
