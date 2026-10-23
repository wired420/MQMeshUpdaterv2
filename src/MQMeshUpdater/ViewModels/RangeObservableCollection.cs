using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace MQMeshUpdater.ViewModels;

/// <summary>
/// An observable collection that can swap all its items with one change
/// notification, so a grouped view rebuilds once instead of per item.
/// </summary>
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();

        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
