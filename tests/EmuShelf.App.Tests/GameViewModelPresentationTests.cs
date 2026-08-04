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
    public void GamepadCoverHeight_IsUniformAcrossPlatforms_WhileDesktopHeightFollowsAspect()
    {
        // A square PS1 cover and a portrait PS2 cover: the desktop grid keeps each platform's true
        // frame, but the gamepad grid draws both into one uniform frame so a mixed view is an even
        // grid with no void above the shorter cover.
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

        square.ApplyCoverLayout(200, shelfCoverHeight: 300);
        portrait.ApplyCoverLayout(200, shelfCoverHeight: 300);

        Assert.NotEqual(square.CoverHeight, portrait.CoverHeight);
        Assert.Equal(square.GamepadCoverHeight, portrait.GamepadCoverHeight);
        Assert.True(square.GamepadCoverHeight > 0);
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
