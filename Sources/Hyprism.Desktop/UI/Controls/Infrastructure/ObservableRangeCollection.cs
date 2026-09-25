// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Hyprism.Desktop.Controls;

internal sealed class ObservableRangeCollection<T> : ObservableCollection<T>
{
    public void AddRange(IReadOnlyList<T> items)
    {
        if (items.Count == 0)
            return;

        var startIndex = Count;
        foreach (var item in items)
            Items.Add(item);

        RaisePropertiesChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add,
            items.ToList(),
            startIndex));
    }

    public void RemoveRange(int startIndex, int count)
    {
        if (count <= 0)
            return;

        var removed = new List<T>(count);
        for (var index = 0; index < count; index++)
        {
            removed.Add(Items[startIndex]);
            Items.RemoveAt(startIndex);
        }

        RaisePropertiesChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Remove,
            removed,
            startIndex));
    }

    public void ReplaceRange(IEnumerable<T> items)
    {
        var replacement = items as IReadOnlyList<T> ?? items.ToList();
        Items.Clear();
        foreach (var item in replacement)
            Items.Add(item);

        RaisePropertiesChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }

    private void RaisePropertiesChanged()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
    }
}
