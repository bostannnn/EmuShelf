using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using EmuShelf.App.Controls;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Library;

namespace EmuShelf.App.Tests;

public sealed class DecodedCoverCacheTests
{
    private static GameViewModel Game(long id) => new(new Game
    {
        Id = id, SystemId = "playstation", Path = $"/{id}.chd", Title = $"Game {id}",
        CoverPath = $"/{id}.png",
    }, "PlayStation", "PS1", "#FFFFFF");

    private static Bitmap Image() => new WriteableBitmap(new PixelSize(10, 10), new Vector(96, 96));

    [AvaloniaFact]
    public void BrowsingManyGamesStaysWithinBudgetAndEvictedCoversCanReload()
    {
        var cache = new DecodedCoverCache(800);
        using var first = Game(0);
        cache.Set(first, Image());
        for (var id = 1; id <= 100; id++)
            cache.Set(Game(id), Image());

        Assert.Equal(800, cache.RetainedBytes);
        Assert.Equal(2, cache.Count);
        Assert.Null(first.CoverImage);
        Assert.True(first.NeedsCoverLoad);
        cache.Set(first, Image());
        Assert.NotNull(first.CoverImage);
        Assert.Equal(800, cache.RetainedBytes);
    }

    [AvaloniaFact]
    public void RecentUseAndVisibleTilesArePreserved()
    {
        var cache = new DecodedCoverCache(800);
        using var first = Game(1);
        using var second = Game(2);
        using var third = Game(3);
        cache.Set(first, Image());
        cache.Set(second, Image());
        cache.Touch(first);
        cache.Set(third, Image());
        Assert.Null(second.CoverImage);
        Assert.NotNull(first.CoverImage);

        first.RetainCover();
        cache.Touch(third);
        cache.Set(second, Image());
        Assert.NotNull(first.CoverImage);
        Assert.Null(third.CoverImage);
        first.ReleaseCover();
    }

    [AvaloniaFact]
    public void MultipleVisibleConsumersPermitOverflowUntilLastLeaseIsReleased()
    {
        var cache = new DecodedCoverCache(400);
        using var first = Game(1);
        using var second = Game(2);
        first.RetainCover();
        first.RetainCover();
        second.RetainCover();
        cache.Set(first, Image());
        cache.Set(second, Image());
        Assert.Equal(800, cache.RetainedBytes);
        first.ReleaseCover();
        Assert.NotNull(first.CoverImage);
        first.ReleaseCover();
        Assert.Null(first.CoverImage);
        Assert.Equal(400, cache.RetainedBytes);
        second.ReleaseCover();
    }

    [AvaloniaFact]
    public void SameGameInDifferentScopesIsAccountedForAndDisposalRemovesEntries()
    {
        var cache = new DecodedCoverCache(800);
        using var systemGame = Game(1);
        using var allGamesGame = Game(1);
        cache.Set(systemGame, Image());
        cache.Set(allGamesGame, Image());
        Assert.Equal(800, cache.RetainedBytes);
        systemGame.ApplyCoverPath("/replacement.png");
        Assert.Equal(400, cache.RetainedBytes);
        cache.Set(allGamesGame, Image());
        Assert.Equal(400, cache.RetainedBytes);
        allGamesGame.Dispose();
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.RetainedBytes);
    }

    [AvaloniaFact]
    public void ShelfProtectsFallbackCoversAndReleasesThemOnScopeChangeAndDetach()
    {
        using var first = Game(1);
        using var second = Game(2);
        var shelf = new MediaShelf3DControl { Items = new[] { first } };
        var window = new Window { Content = shelf };
        try
        {
            Assert.Equal(0, first.CoverConsumerCount);
            window.Show();
            Assert.Equal(1, first.CoverConsumerCount);
            shelf.Items = new[] { second };
            Assert.Equal(0, first.CoverConsumerCount);
            Assert.Equal(1, second.CoverConsumerCount);
            window.Content = null;
            Assert.Equal(0, second.CoverConsumerCount);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ViewWiringBalancesRepeatedAttachRecyclingParkingAndDetach()
    {
        using var first = Game(1);
        using var second = Game(2);
        var host = new Border { DataContext = first };
        var window = new Window { Content = host };
        try
        {
            window.Show();
            GameCoverInteractions.CoverAttached(host);
            GameCoverInteractions.CoverDataContextChanged(host);
            Assert.Equal(1, first.CoverConsumerCount);
            host.DataContext = second;
            GameCoverInteractions.CoverDataContextChanged(host);
            Assert.Equal(0, first.CoverConsumerCount);
            Assert.Equal(1, second.CoverConsumerCount);
            host.DataContext = null;
            GameCoverInteractions.CoverDataContextChanged(host);
            Assert.Equal(0, second.CoverConsumerCount);
            host.DataContext = first;
            GameCoverInteractions.CoverDataContextChanged(host);
            window.Content = null;
            Assert.Equal(0, first.CoverConsumerCount);
        }
        finally { window.Close(); }
    }
}
