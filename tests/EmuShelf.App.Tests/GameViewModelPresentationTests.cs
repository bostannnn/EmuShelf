using Avalonia.Media;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Library;

namespace EmuShelf.App.Tests;

public sealed class GameViewModelPresentationTests
{
    [Fact]
    public void AchievementProgress_ProjectsCompactCountAndBarRatio()
    {
        var viewModel = CreateGame();

        viewModel.ApplyAchievementsDisplay(new RetroAchievementsDisplay(
            ShowMark: true,
            ColumnText: "3/62",
            Tooltip: "3 of 62 unlocked."));

        Assert.Equal("3/62", viewModel.GamepadAchievementCountText);
        Assert.Equal(3d / 62d, viewModel.GamepadAchievementProgressRatio, 8);
        Assert.Equal("sample.chd", viewModel.GamepadSubtitle);
    }

    [Theory]
    [InlineData("—")]
    [InlineData("invalid")]
    [InlineData("0/0")]
    public void AchievementProgress_UsesHonestUnavailableStateWhenCountsAreMissing(string columnText)
    {
        var viewModel = CreateGame();

        viewModel.ApplyAchievementsDisplay(new RetroAchievementsDisplay(
            ShowMark: true,
            ColumnText: columnText,
            Tooltip: "Progress has not loaded."));

        Assert.Equal("—/—", viewModel.GamepadAchievementCountText);
        Assert.Equal(0, viewModel.GamepadAchievementProgressRatio);
    }

    [Fact]
    public void SpotlightDisplayTitle_PrefersTheCanonicalName_ButFallsBackToTitleAndTracksRenames()
    {
        var viewModel = CreateGame();

        // No canonical name scraped yet → the game's own title, and it follows an in-place rename.
        Assert.Equal("Sample Game", viewModel.SpotlightDisplayTitle);
        viewModel.CompleteTitleEdit("Renamed Game");
        Assert.Equal("Renamed Game", viewModel.SpotlightDisplayTitle);

        // A scraped canonical name wins over the (filename-derived) title.
        viewModel.ApplySpotlightTitle("Canonical Name");
        Assert.Equal("Canonical Name", viewModel.SpotlightDisplayTitle);

        // A blank canonical name clears the override and reverts to the current title.
        viewModel.ApplySpotlightTitle("   ");
        Assert.Equal("Renamed Game", viewModel.SpotlightDisplayTitle);
    }

    [Fact]
    public void SpotlightFacts_AreEmpty_UntilScrapedDetailsResolve()
    {
        var viewModel = CreateGame();

        // Before details resolve there are no chips (the filename shows on its own as the caption).
        Assert.Empty(viewModel.SpotlightFacts);

        // Scraped facts become chips, one per entry.
        viewModel.ApplySpotlightDetails(null, null, null, ["Beat 'em up", "1994", "2 players"]);
        Assert.Equal(["Beat 'em up", "1994", "2 players"], viewModel.SpotlightFacts);

        // No scraped facts → back to no chips.
        viewModel.ApplySpotlightDetails(null, null, null, []);
        Assert.Empty(viewModel.SpotlightFacts);
    }

    [Fact]
    public void SpotlightFacts_SplitFirstThreeOntoTheirOwnRow()
    {
        var viewModel = CreateGame();

        // Full set: genre/year/players stay on the primary row, developer/publisher spill to the second.
        viewModel.ApplySpotlightDetails(null, null, null,
            ["Role-Playing", "2008", "1 player", "Atlus", "Square Enix"]);
        Assert.Equal(["Role-Playing", "2008", "1 player"], viewModel.SpotlightFactsPrimary);
        Assert.Equal(["Atlus", "Square Enix"], viewModel.SpotlightFactsSecondary);
        Assert.True(viewModel.HasSpotlightSecondaryFacts);

        // Three or fewer facts: everything stays on the primary row, no second row.
        viewModel.ApplySpotlightDetails(null, null, null, ["Sports", "1996"]);
        Assert.Equal(["Sports", "1996"], viewModel.SpotlightFactsPrimary);
        Assert.Empty(viewModel.SpotlightFactsSecondary);
        Assert.False(viewModel.HasSpotlightSecondaryFacts);

        // None: both rows empty.
        viewModel.ApplySpotlightDetails(null, null, null, []);
        Assert.Empty(viewModel.SpotlightFactsPrimary);
        Assert.Empty(viewModel.SpotlightFactsSecondary);
        Assert.False(viewModel.HasSpotlightSecondaryFacts);
    }

    [Fact]
    public void ShowSpotlightTitleFallback_OnlyWhenResolvedDetailsConfirmNoLogoArt()
    {
        var viewModel = CreateGame();

        // Before details resolve we don't yet know whether a logo exists, so the title stays hidden
        // (rather than flashing in the gap before a logo bitmap could decode).
        Assert.False(viewModel.ShowSpotlightTitleFallback);

        // Details resolved with a logo path → the logo carries the identity, no fallback title.
        viewModel.ApplySpotlightDetails(null, "/covers/logo.png", null, []);
        Assert.False(viewModel.ShowSpotlightTitleFallback);

        // Details resolved with no logo art → the title stands in for the missing logo.
        viewModel.ApplySpotlightDetails(null, null, null, []);
        Assert.True(viewModel.ShowSpotlightTitleFallback);
    }

    [Fact]
    public void GamepadSubtitle_TracksTheSourceSelectedForMultiDiscLaunch()
    {
        var disc1 = CreateModel(1, "/games/Sample Game (Disc 1).chd");
        var disc2 = CreateModel(2, "/games/Sample Game (Disc 2).chd");
        var viewModel = new GameViewModel(
            disc1,
            "PlayStation 2",
            "PS2",
            "#4657D7",
            platformArtwork: new DrawingImage(),
            discs: [new GameDisc(1, disc1), new GameDisc(2, disc2)],
            selectedDisc: new GameDisc(1, disc1));

        viewModel.SetSelectedDisc(new GameDisc(2, disc2));

        Assert.Equal("Sample Game (Disc 2).chd", viewModel.GamepadSubtitle);
    }

    [Fact]
    public void GamepadCoverHeight_DefaultsToTheTrueFrame_ButHonorsAnExplicitUniformHeight()
    {
        var square = new GameViewModel(
            CreateModel(1, "/games/square.cue"),
            "PlayStation", "PS1", "#8A8FA3",
            platformArtwork: new DrawingImage(),
            coverAspectRatio: 1.0);
        var portrait = new GameViewModel(
            CreateModel(2, "/games/portrait.chd"),
            "PlayStation 2", "PS2", "#4657D7",
            platformArtwork: new DrawingImage(),
            coverAspectRatio: 0.708);

        // No gamepad height passed (a single-platform view): each tile keeps its own platform frame,
        // so covers fill the frame with no letterbox bars.
        square.ApplyCoverLayout(200, shelfCoverHeight: 300);
        portrait.ApplyCoverLayout(200, shelfCoverHeight: 300);
        Assert.Equal(square.CoverHeight, square.GamepadCoverHeight);
        Assert.NotEqual(square.GamepadCoverHeight, portrait.GamepadCoverHeight);

        // A mixed view passes one uniform height to every tile so the grid is even.
        square.ApplyCoverLayout(200, shelfCoverHeight: 300, gamepadCoverHeight: 275);
        portrait.ApplyCoverLayout(200, shelfCoverHeight: 300, gamepadCoverHeight: 275);
        Assert.Equal(275, square.GamepadCoverHeight);
        Assert.Equal(square.GamepadCoverHeight, portrait.GamepadCoverHeight);
    }

    [Fact]
    public void GamepadCoverHeightFor_UsesTheTrueHeightForOnePlatform_AndAUniformHeightForAMix()
    {
        static GameViewModel Tile(long id, string systemId, double ratio) => new(
            new Game
            {
                Id = id,
                SystemId = systemId,
                Path = $"/games/{systemId}-{id}.bin",
                Title = $"Game {id}",
                IsAvailable = true,
                DateAdded = DateTimeOffset.UtcNow,
            },
            systemId, systemId, "#4657D7",
            platformArtwork: new DrawingImage(),
            coverAspectRatio: ratio);

        const double width = 200;
        var squareA = Tile(1, "playstation", 1.0);
        var squareB = Tile(2, "playstation", 1.0);
        var portrait = Tile(3, "playstation2", 0.708);

        // One platform → that platform's true height (square: 200/1.0), so covers fill with no bars.
        Assert.Equal(
            Math.Round(width / 1.0),
            MainViewModel.GamepadCoverHeightFor(new[] { squareA, squareB }, width));

        // Mixed → the uniform mixed frame, regardless of the members' own ratios.
        Assert.Equal(
            Math.Round(width / GameViewModel.GamepadMixedCoverAspectRatio),
            MainViewModel.GamepadCoverHeightFor(new[] { squareA, portrait }, width));
    }

    private static GameViewModel CreateGame() => new(
        CreateModel(1, "/games/sample.chd"),
        "PlayStation 2",
        "PS2",
        "#4657D7",
        platformArtwork: new DrawingImage());

    private static Game CreateModel(long id, string path) =>
        new()
        {
            Id = id,
            SystemId = "playstation2",
            Path = path,
            Title = "Sample Game",
            IsAvailable = true,
            DateAdded = DateTimeOffset.UtcNow,
        };
}
