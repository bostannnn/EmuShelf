using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Tests;

/// <summary>
/// Controller-native scraper overlay: exercises the D-pad focus model layered on the shared
/// <see cref="GameScraperViewModel"/> (navigation, toggling, connect, title-search, apply) without
/// leaving Gamepad mode. Final controller <em>feel</em> — focus hand-off to the Steam keyboard,
/// gamescope — needs real Deck acceptance and is out of scope for these headless view-model tests.
/// </summary>
public class GamepadScraperOverlayTests
{
    [Fact]
    public async Task Opens_StraightToReady_WithFirstFieldFocused()
    {
        var vm = Wrap(new StubScreenScraperPreviewService(ScraperFixtures.ReadyPreview()));

        await vm.LoadAsync();

        Assert.Equal(GameScraperState.Ready, vm.Scraper.State);
        Assert.Equal(GamepadScraperTargetKind.Field, vm.FocusedKind);
        Assert.True(vm.Scraper.Fields[0].IsFocused);
    }

    [Fact]
    public async Task Dpad_MovesAcrossFieldsBoxArtAndMedia_AndAToggles()
    {
        var vm = Wrap(new StubScreenScraperPreviewService(ScraperFixtures.ReadyPreview()));
        await vm.LoadAsync();

        // Field is focused and selected by default; A clears it.
        Assert.True(vm.Scraper.Fields[0].IsSelected);
        vm.Activate();
        Assert.False(vm.Scraper.Fields[0].IsSelected);

        vm.MoveFocus(1);
        Assert.Equal(GamepadScraperTargetKind.BoxArt, vm.FocusedKind);
        Assert.True(vm.Scraper.BoxArtRow!.IsFocused);

        vm.MoveFocus(1);
        Assert.Equal(GamepadScraperTargetKind.Media, vm.FocusedKind);
        Assert.Same(vm.Scraper.OtherMedia[0], vm.FocusedItem);
        Assert.True(vm.Scraper.OtherMedia[0].IsSelected);
        vm.Activate();
        Assert.False(vm.Scraper.OtherMedia[0].IsSelected);

        // Up from the first row clamps; it never walks out of the target list.
        vm.MoveFocus(-1);
        vm.MoveFocus(-1);
        vm.MoveFocus(-1);
        Assert.Equal(0, vm.FocusIndex);
        Assert.Equal(GamepadScraperTargetKind.Field, vm.FocusedKind);
    }

    [Fact]
    public async Task LockedRow_CannotBeToggledByA()
    {
        var existing = new GameDetails(
            1,
            [new GameMetadataValue(
                1, GameMetadataField.Title, "My Title", null,
                GameMetadataValueOrigin.User, null, null, null, DateTimeOffset.UtcNow)],
            [],
            []);
        var vm = Wrap(new StubScreenScraperPreviewService(
            ScraperFixtures.SuccessPreview(existing, [ScraperFixtures.TitleValue("Canonical")], ScraperFixtures.NoMedia())));
        await vm.LoadAsync();

        Assert.False(vm.Scraper.Fields[0].CanApply);
        Assert.False(vm.Scraper.Fields[0].IsSelected);
        vm.Activate();
        Assert.False(vm.Scraper.Fields[0].IsSelected);
    }

    [Fact]
    public async Task Apply_FromApplyTarget_SendsSelection_AndRecordsChanges()
    {
        var apply = new StubGameScrapeApplicationService
        {
            Result = new GameScrapeApplyResult(
                1, 1, 0, [new GameMediaApplyResult(GameMediaKind.BoxFront, GameMediaApplyOutcome.Imported)], true),
        };
        var vm = Wrap(new StubScreenScraperPreviewService(ScraperFixtures.ReadyPreview()), apply);
        await vm.LoadAsync();

        // Field, BoxArt, Media, Refresh, then Apply.
        for (var i = 0; i < 4; i++)
            vm.MoveFocus(1);
        Assert.Equal(GamepadScraperTargetKind.Apply, vm.FocusedKind);

        vm.Activate();

        Assert.NotNull(apply.Request);
        Assert.Equal(GameScraperState.Applied, vm.Scraper.State);
        Assert.True(vm.HasAppliedChanges);
    }

    [Fact]
    public async Task RefreshToggle_IsReachable_AndFlipsTheApplyMode()
    {
        var apply = new StubGameScrapeApplicationService();
        var vm = Wrap(new StubScreenScraperPreviewService(ScraperFixtures.ReadyPreview()), apply);
        await vm.LoadAsync();

        for (var i = 0; i < 3; i++)
            vm.MoveFocus(1);
        Assert.Equal(GamepadScraperTargetKind.RefreshToggle, vm.FocusedKind);
        Assert.False(vm.Scraper.RefreshOwnedValues);
        vm.Activate();
        Assert.True(vm.Scraper.RefreshOwnedValues);

        vm.MoveFocus(1);
        vm.Activate();
        Assert.Equal(GameMetadataApplyMode.RefreshProviderOwned, apply.Request!.Mode);
    }

    [Fact]
    public async Task NotConnected_ShowsConnectForm_AndConnectingReloadsToReady()
    {
        var preview = new StubScreenScraperPreviewService(
            ScraperFixtures.Failure(ScreenScraperPreviewStatus.NotConnected),
            ScraperFixtures.ReadyPreview());
        var account = new StubScreenScraperAccountService { ConnectResult = ScreenScraperConnectionResult.Connected };
        var vm = Wrap(preview, new StubGameScrapeApplicationService(), account);

        await vm.LoadAsync();
        Assert.Equal(GameScraperState.NotConnected, vm.Scraper.State);
        Assert.Equal(GamepadScraperTargetKind.Username, vm.FocusedKind);

        vm.Scraper.Username = "bostan";
        vm.Scraper.Password = "secret";
        vm.MoveFocus(1);
        Assert.Equal(GamepadScraperTargetKind.Password, vm.FocusedKind);
        vm.MoveFocus(1);
        Assert.Equal(GamepadScraperTargetKind.Connect, vm.FocusedKind);

        vm.Activate();

        Assert.Equal(1, account.ConnectCalls);
        Assert.Equal(GameScraperState.Ready, vm.Scraper.State);
        Assert.Equal(GamepadScraperTargetKind.Field, vm.FocusedKind);
    }

    [Fact]
    public async Task NoMatch_AutoSearches_FocusesFirstCandidate_AndAPicksIt()
    {
        var preview = new StubScreenScraperPreviewService(
            ScraperFixtures.Failure(ScreenScraperPreviewStatus.ProviderFailure, ScreenScraperRequestStatus.NotFound))
        {
            SearchCandidates = [new ScreenScraperGameMatch("777", "Some Rom Hack", "Playstation 2")],
            SelectedResult = ScraperFixtures.SuccessPreview(
                ScraperFixtures.EmptyDetails(), [ScraperFixtures.TitleValue("Some Rom Hack")], ScraperFixtures.NoMedia()),
        };
        var vm = Wrap(preview);

        await vm.LoadAsync();

        Assert.Equal(GameScraperState.NoMatch, vm.Scraper.State);
        var candidate = Assert.Single(vm.Candidates);
        Assert.Equal(GamepadScraperTargetKind.Candidate, vm.FocusedKind);
        Assert.True(candidate.IsFocused);

        vm.Activate();

        Assert.Equal("777", preview.LastSelectedProviderGameId);
        Assert.Equal(GameScraperState.Ready, vm.Scraper.State);
    }

    [Fact]
    public async Task NoMatch_ManualSearch_IsReachable_FromTheQueryField()
    {
        var preview = new StubScreenScraperPreviewService(
            ScraperFixtures.Failure(ScreenScraperPreviewStatus.ProviderFailure, ScreenScraperRequestStatus.NotFound));
        var vm = Wrap(preview);
        await vm.LoadAsync();

        // No candidates yet: focus rests on the query field, and the Search button follows it.
        Assert.Empty(vm.Candidates);
        Assert.Equal(GamepadScraperTargetKind.SearchField, vm.FocusedKind);
        vm.MoveFocus(1);
        Assert.Equal(GamepadScraperTargetKind.Search, vm.FocusedKind);

        preview.SearchCandidates = [new ScreenScraperGameMatch("9", "Late Match", "GBA")];
        vm.Activate();

        Assert.Single(vm.Candidates);
        Assert.Equal(GamepadScraperTargetKind.Candidate, vm.FocusedKind);
    }

    private static GamepadScraperViewModel Wrap(
        StubScreenScraperPreviewService preview,
        StubGameScrapeApplicationService? apply = null,
        StubScreenScraperAccountService? account = null)
    {
        var scraper = new GameScraperViewModel(
            1,
            "Game",
            preview,
            apply ?? new StubGameScrapeApplicationService(),
            account ?? new StubScreenScraperAccountService(),
            new ScreenScraperSettings { Enabled = true });
        return new GamepadScraperViewModel(scraper);
    }
}
