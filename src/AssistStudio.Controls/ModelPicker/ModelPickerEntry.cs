using System;
using System.Diagnostics.CodeAnalysis;

namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// Thin view model entry consumed by <see cref="ModelPicker"/>. The picker
/// is provider/model-agnostic; hosts adapt their domain types
/// (ProviderModel, AnthropicClient.Models.List, etc.) into these entries
/// via host-side adapter code.
/// </summary>
public sealed class ModelPickerEntry : IEquatable<ModelPickerEntry>
{
    /// <summary>
    /// Initializes a new <see cref="ModelPickerEntry"/>. The parameterless
    /// form exists only so the WinUI XAML type-info generator can list the
    /// type; in practice <see cref="ModelId"/> must be supplied via an
    /// object initializer.
    /// </summary>
    [SetsRequiredMembers]
    public ModelPickerEntry()
    {
        ModelId = string.Empty;
    }

    /// <summary>Stable model identifier. Falls back as the displayed label.</summary>
    public required string ModelId { get; init; }

    /// <summary>Human-readable label. Falls back to <see cref="ModelId"/>.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Optional one-line subtitle (e.g., "200K context · tools").</summary>
    public string? Description { get; init; }

    /// <summary>Group key for grouping; null means flat list.</summary>
    public string? GroupKey { get; init; }

    /// <summary>Header text for the group; falls back to <see cref="GroupKey"/>.</summary>
    public string? GroupDisplayName { get; init; }

    /// <summary>Host-specific opaque payload (e.g., ProviderModel).</summary>
    public object? Tag { get; init; }

    /// <summary>
    /// Equality is by (GroupKey, ModelId) using ordinal string comparison.
    /// This makes <see cref="ModelPicker.SelectedItem"/> stable across
    /// <see cref="ModelPicker.ItemsSource"/> reassignments — a freshly
    /// rebuilt entry with the same identity matches the previously
    /// selected one.
    /// </summary>
    /// <param name="other">The other entry to compare to, or null.</param>
    /// <returns>True if both entries refer to the same (GroupKey, ModelId) pair.</returns>
    public bool Equals(ModelPickerEntry? other) =>
        other is not null
        && string.Equals(ModelId, other.ModelId, StringComparison.Ordinal)
        && string.Equals(GroupKey, other.GroupKey, StringComparison.Ordinal);

    /// <summary>
    /// Compares this entry to an arbitrary object using
    /// <see cref="Equals(ModelPickerEntry?)"/>.
    /// </summary>
    /// <param name="obj">The other object.</param>
    /// <returns>True if <paramref name="obj"/> is a <see cref="ModelPickerEntry"/> with the same identity.</returns>
    public override bool Equals(object? obj) => Equals(obj as ModelPickerEntry);

    /// <summary>
    /// Returns a hash code consistent with <see cref="Equals(ModelPickerEntry?)"/>.
    /// </summary>
    /// <returns>A hash combining <see cref="GroupKey"/> and <see cref="ModelId"/>.</returns>
    public override int GetHashCode() => HashCode.Combine(ModelId, GroupKey);
}
