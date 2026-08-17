using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// Observable collection that can replace its contents with one reset notification.
/// Large library reloads and searches should not force Avalonia to process thousands
/// of individual remove/add events when the view only needs the final snapshot.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }
}
