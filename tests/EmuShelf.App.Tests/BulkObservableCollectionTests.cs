using System.Collections.Specialized;
using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Tests;

public class BulkObservableCollectionTests
{
    [Fact]
    public void ReplaceAll_EmitsOneResetForTheFinalSnapshot()
    {
        var collection = new BulkObservableCollection<int> { 1, 2 };
        var events = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, args) => events.Add(args);

        collection.ReplaceAll(Enumerable.Range(10, 1_000));

        Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Reset, events[0].Action);
        Assert.Equal(1_000, collection.Count);
        Assert.Equal(10, collection[0]);
        Assert.Equal(1_009, collection[^1]);
    }
}
