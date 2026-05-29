using System.Collections.ObjectModel;

namespace Lucid.Core.Extensions;

/// <summary>
/// Extension methods for collection types used across the Lucid platform.
/// </summary>
public static class CollectionExtensions
{
    /// <summary>
    /// Replaces the contents of an ObservableCollection with a new sequence
    /// in a single pass, minimising UI notifications.
    /// </summary>
    public static void ReplaceWith<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
            collection.Add(item);
    }

    /// <summary>
    /// Adds a range of items to an ObservableCollection.
    /// Fires a notification per item — prefer <see cref="ReplaceWith{T}"/> for
    /// bulk resets where a full clear is acceptable.
    /// </summary>
    public static void AddRange<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        foreach (var item in items)
            collection.Add(item);
    }
}
