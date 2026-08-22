using EmuShelf.Core.SecondScreen;
using EmuShelf.Infrastructure.Settings;

namespace EmuShelf.Infrastructure.Tests.Settings;

public sealed class FileSecondScreenDockStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "EmuShelfSecondScreenDock", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "second-screen-dock.json");

    [Fact]
    public void Dock_NormalizesToFiveUniqueSlots()
    {
        var dock = new SecondScreenDock(
            [" app.one/.Main ", "app.one/.Main", null, "app.two/.Main", null, "ignored/.Sixth"]);

        Assert.Equal(SecondScreenDock.SlotCount, dock.Components.Count);
        Assert.Equal("app.one/.Main", dock[0]);
        Assert.Null(dock[1]);
        Assert.Equal("app.two/.Main", dock[3]);
    }

    [Fact]
    public void Dock_DoesNotExposeMutableBackingStorage()
    {
        var dock = SecondScreenDock.Empty.Pin(0, "app.one/.Main");

        var list = Assert.IsAssignableFrom<IList<string?>>(dock.Components);
        Assert.Throws<NotSupportedException>(() => list[0] = "mutated/.Main");
        Assert.Equal("app.one/.Main", dock[0]);
        Assert.Null(SecondScreenDock.Empty[0]);
    }

    [Fact]
    public void Pin_MovesAnExistingComponentAndClearEmptiesTheSlot()
    {
        var dock = SecondScreenDock.Empty
            .Pin(0, "app.one/.Main")
            .Pin(4, "app.one/.Main");

        Assert.Null(dock[0]);
        Assert.Equal("app.one/.Main", dock[4]);
        Assert.Null(dock.Clear(4)[4]);
    }

    [Fact]
    public void TargetResolver_PrefersRunningGameThenFocusedGame()
    {
        Assert.Equal(10, SecondScreenTargetResolver.Resolve(10, 20));
        Assert.Equal(20, SecondScreenTargetResolver.Resolve(null, 20));
        Assert.Null(SecondScreenTargetResolver.Resolve(null, null));
    }

    [Fact]
    public void Navigation_CloseOverlayRestoresRunningGameIdle()
    {
        var state = SecondScreenNavigationState.Initial
            .StartGame()
            .OpenDrawer()
            .CloseOverlay();

        Assert.Equal(SecondScreenBaseSurface.GameIdle, state.BaseSurface);
        Assert.Equal(SecondScreenOverlay.None, state.Overlay);
    }

    [Fact]
    public void Navigation_NewSessionTransitionsInvalidateOldAsyncResults()
    {
        var achievements = SecondScreenNavigationState.Initial.OpenAchievements();
        var game = achievements.StartGame();

        Assert.True(game.Revision > achievements.Revision);
        Assert.Equal(SecondScreenBaseSurface.GameIdle, game.BaseSurface);
        Assert.Equal(SecondScreenOverlay.None, game.Overlay);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAcrossInstances()
    {
        new FileSecondScreenDockStore(FilePath).Save(
            SecondScreenDock.Empty.Pin(1, "app.one/.Main").Pin(3, "app.two/.Main"));

        var loaded = new FileSecondScreenDockStore(FilePath).Load();

        Assert.Equal("app.one/.Main", loaded[1]);
        Assert.Equal("app.two/.Main", loaded[3]);
    }

    [Fact]
    public void Load_WithCorruptFile_ReturnsEmptyDock()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "{ broken json");

        Assert.All(new FileSecondScreenDockStore(FilePath).Load().Components, Assert.Null);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
