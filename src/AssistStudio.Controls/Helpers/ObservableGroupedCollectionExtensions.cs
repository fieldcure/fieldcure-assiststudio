// Ported from ZStudio (sibling project, MinTech). Pure LINQ wrappers over CommunityToolkit.Mvvm's ObservableGroupedCollection.
using CommunityToolkit.Mvvm.Collections;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldCure.AssistStudio.Controls.Helpers;

/// <summary>
/// Extension methods for ObservableGroupedCollection to enable LINQ operations.
/// </summary>
internal static class ObservableGroupedCollectionExtensions
{
    /// <summary>
    /// Returns the first element of a sequence, or a default value if the sequence contains no elements.
    /// </summary>
    /// <typeparam name="TKey">The type of the group key.</typeparam>
    /// <typeparam name="TElement">The type of elements in the group.</typeparam>
    /// <param name="source">The ObservableGroupedCollection to return the first element of.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>default(ObservableGroup) if source is empty or if no element passes the test specified by predicate; otherwise, the first element in source that passes the test specified by predicate.</returns>
    public static ObservableGroup<TKey, TElement>? FirstOrDefault<TKey, TElement>(
        this ObservableGroupedCollection<TKey, TElement> source,
        Func<ObservableGroup<TKey, TElement>, bool> predicate)
        where TKey : notnull
    {
        return ((IEnumerable<ObservableGroup<TKey, TElement>>)source).FirstOrDefault(predicate);
    }

    /// <summary>
    /// Returns the first element of a sequence, or a default value if the sequence contains no elements.
    /// </summary>
    /// <typeparam name="TKey">The type of the group key.</typeparam>
    /// <typeparam name="TElement">The type of elements in the group.</typeparam>
    /// <param name="source">The ObservableGroupedCollection to return the first element of.</param>
    /// <returns>default(ObservableGroup) if source is empty; otherwise, the first element in source.</returns>
    public static ObservableGroup<TKey, TElement>? FirstOrDefault<TKey, TElement>(
        this ObservableGroupedCollection<TKey, TElement> source)
        where TKey : notnull
    {
        return ((IEnumerable<ObservableGroup<TKey, TElement>>)source).FirstOrDefault();
    }

    /// <summary>
    /// Returns the first element of a sequence.
    /// </summary>
    /// <typeparam name="TKey">The type of the group key.</typeparam>
    /// <typeparam name="TElement">The type of elements in the group.</typeparam>
    /// <param name="source">The ObservableGroupedCollection to return the first element of.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>The first element in the sequence that passes the test in the specified predicate function.</returns>
    public static ObservableGroup<TKey, TElement> First<TKey, TElement>(
        this ObservableGroupedCollection<TKey, TElement> source,
        Func<ObservableGroup<TKey, TElement>, bool> predicate)
        where TKey : notnull
    {
        return ((IEnumerable<ObservableGroup<TKey, TElement>>)source).First(predicate);
    }

    /// <summary>
    /// Returns the first element of a sequence.
    /// </summary>
    /// <typeparam name="TKey">The type of the group key.</typeparam>
    /// <typeparam name="TElement">The type of elements in the group.</typeparam>
    /// <param name="source">The ObservableGroupedCollection to return the first element of.</param>
    /// <returns>The first element in the specified sequence.</returns>
    public static ObservableGroup<TKey, TElement> First<TKey, TElement>(
        this ObservableGroupedCollection<TKey, TElement> source)
        where TKey : notnull
    {
        return ((IEnumerable<ObservableGroup<TKey, TElement>>)source).First();
    }

    /// <summary>
    /// Determines whether a sequence contains any elements.
    /// </summary>
    /// <typeparam name="TKey">The type of the group key.</typeparam>
    /// <typeparam name="TElement">The type of elements in the group.</typeparam>
    /// <param name="source">The ObservableGroupedCollection to check for emptiness.</param>
    /// <returns>true if the source sequence contains any elements; otherwise, false.</returns>
    public static bool Any<TKey, TElement>(
        this ObservableGroupedCollection<TKey, TElement> source)
        where TKey : notnull
    {
        return ((IEnumerable<ObservableGroup<TKey, TElement>>)source).Any();
    }

    /// <summary>
    /// Determines whether any element of a sequence satisfies a condition.
    /// </summary>
    /// <typeparam name="TKey">The type of the group key.</typeparam>
    /// <typeparam name="TElement">The type of elements in the group.</typeparam>
    /// <param name="source">An ObservableGroupedCollection whose elements to apply the predicate to.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>true if any elements in the source sequence pass the test in the specified predicate; otherwise, false.</returns>
    public static bool Any<TKey, TElement>(
        this ObservableGroupedCollection<TKey, TElement> source,
        Func<ObservableGroup<TKey, TElement>, bool> predicate)
        where TKey : notnull
    {
        return ((IEnumerable<ObservableGroup<TKey, TElement>>)source).Any(predicate);
    }

    /// <summary>
    /// Projects each element of a sequence into a new form.
    /// </summary>
    /// <typeparam name="TKey">The type of the group key.</typeparam>
    /// <typeparam name="TElement">The type of elements in the group.</typeparam>
    /// <typeparam name="TResult">The type of the value returned by selector.</typeparam>
    /// <param name="source">A sequence of values to invoke a transform function on.</param>
    /// <param name="selector">A transform function to apply to each element.</param>
    /// <returns>An IEnumerable whose elements are the result of invoking the transform function on each element of source.</returns>
    public static IEnumerable<TResult> Select<TKey, TElement, TResult>(
        this ObservableGroupedCollection<TKey, TElement> source,
        Func<ObservableGroup<TKey, TElement>, TResult> selector)
        where TKey : notnull
    {
        return ((IEnumerable<ObservableGroup<TKey, TElement>>)source).Select(selector);
    }

    /// <summary>
    /// Filters a sequence of values based on a predicate.
    /// </summary>
    /// <typeparam name="TKey">The type of the group key.</typeparam>
    /// <typeparam name="TElement">The type of elements in the group.</typeparam>
    /// <param name="source">An ObservableGroupedCollection to filter.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>An IEnumerable that contains elements from the input sequence that satisfy the condition.</returns>
    public static IEnumerable<ObservableGroup<TKey, TElement>> Where<TKey, TElement>(
        this ObservableGroupedCollection<TKey, TElement> source,
        Func<ObservableGroup<TKey, TElement>, bool> predicate)
        where TKey : notnull
    {
        return ((IEnumerable<ObservableGroup<TKey, TElement>>)source).Where(predicate);
    }

    /// <summary>
    /// Sorts the elements of a sequence in ascending order according to a key.
    /// </summary>
    /// <typeparam name="TKey">The type of the group key.</typeparam>
    /// <typeparam name="TElement">The type of elements in the group.</typeparam>
    /// <typeparam name="TOrderKey">The type of the key returned by keySelector.</typeparam>
    /// <param name="source">A sequence of values to order.</param>
    /// <param name="keySelector">A function to extract a key from an element.</param>
    /// <returns>An IOrderedEnumerable whose elements are sorted according to a key.</returns>
    public static IOrderedEnumerable<ObservableGroup<TKey, TElement>> OrderBy<TKey, TElement, TOrderKey>(
        this ObservableGroupedCollection<TKey, TElement> source,
        Func<ObservableGroup<TKey, TElement>, TOrderKey> keySelector)
        where TKey : notnull
    {
        return ((IEnumerable<ObservableGroup<TKey, TElement>>)source).OrderBy(keySelector);
    }

    /// <summary>
    /// Gets the group with the specified key.
    /// </summary>
    /// <typeparam name="TKey">The type of the group key.</typeparam>
    /// <typeparam name="TElement">The type of elements in the group.</typeparam>
    /// <param name="source">The ObservableGroupedCollection to search.</param>
    /// <param name="key">The key of the group to find.</param>
    /// <returns>The group with the specified key, or null if not found.</returns>
    public static ObservableGroup<TKey, TElement>? GetGroup<TKey, TElement>(
        this ObservableGroupedCollection<TKey, TElement> source,
        TKey key)
        where TKey : notnull
    {
        return source.FirstOrDefault(g => EqualityComparer<TKey>.Default.Equals(g.Key, key));
    }

    /// <summary>
    /// Converts the ObservableGroupedCollection to a standard IEnumerable for LINQ operations.
    /// </summary>
    /// <typeparam name="TKey">The type of the group key.</typeparam>
    /// <typeparam name="TElement">The type of elements in the group.</typeparam>
    /// <param name="source">The ObservableGroupedCollection to convert.</param>
    /// <returns>An IEnumerable view of the collection.</returns>
    public static IEnumerable<ObservableGroup<TKey, TElement>> AsEnumerable<TKey, TElement>(
        this ObservableGroupedCollection<TKey, TElement> source)
        where TKey : notnull
    {
        return source;
    }
}
