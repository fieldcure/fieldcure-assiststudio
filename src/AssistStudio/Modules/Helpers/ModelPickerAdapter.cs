using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AssistStudio.Helpers;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Controls;

namespace AssistStudio.Modules.Helpers;

/// <summary>
/// Host-side adapter that projects <see cref="ProviderModel"/> domain types into
/// <see cref="ModelPickerEntry"/> view-model entries consumed by the
/// <see cref="ModelPicker"/> control. Lives in the host app because the projection
/// resolves <c>Custom_*</c> provider display names via <see cref="AppSettings"/>,
/// which the SDK-facing <c>FieldCure.AssistStudio.Controls</c> package does not
/// know about.
/// </summary>
internal static class ModelPickerAdapter
{
    /// <summary>
    /// Builds <see cref="ModelPickerEntry"/> entries from a flat sequence of
    /// <see cref="ProviderModel"/> instances. Group key is <see cref="ProviderModel.ProviderType"/>;
    /// group display name follows the existing labeling convention
    /// (Mock → "Demo", Custom_* → user-defined display name, otherwise the
    /// provider type literal).
    /// </summary>
    /// <param name="models">Source models.</param>
    /// <param name="filter">Optional predicate; entries are excluded when this returns false.</param>
    /// <returns>A list of <see cref="ModelPickerEntry"/> ready to assign to
    /// <see cref="ModelPicker.ItemsSource"/>.</returns>
    public static IList<ModelPickerEntry> BuildEntries(
        IEnumerable<ProviderModel> models,
        Func<ProviderModel, bool>? filter = null)
    {
        ArgumentNullException.ThrowIfNull(models);
        var source = filter is null ? models : models.Where(filter);
        return [.. source.Select(BuildEntry)];
    }

    /// <summary>
    /// Builds <see cref="ModelPickerEntry"/> entries from the ordered
    /// <see cref="System.Collections.ArrayList"/> produced by
    /// <see cref="AppSettings.BuildOrderedModelItems"/>. The list interleaves
    /// <see cref="ProviderModel"/> entries with literal <c>"-"</c> separator strings
    /// for visual grouping in <see cref="Microsoft.UI.Xaml.Controls.ComboBox"/> hosts.
    /// The separators are silently dropped here because <see cref="ModelPicker"/>
    /// performs its own grouping by <see cref="ModelPickerEntry.GroupKey"/>.
    /// </summary>
    /// <param name="ordered">The ordered ArrayList of models and "-" separators.</param>
    /// <param name="filter">Optional predicate to exclude models (e.g., Mock or
    /// cloud providers without an API key).</param>
    /// <returns>A list of <see cref="ModelPickerEntry"/> ready to assign to
    /// <see cref="ModelPicker.ItemsSource"/>.</returns>
    public static IList<ModelPickerEntry> BuildEntriesFromOrderedItems(
        IList ordered,
        Func<ProviderModel, bool>? filter = null)
    {
        ArgumentNullException.ThrowIfNull(ordered);

        var result = new List<ModelPickerEntry>(ordered.Count);
        foreach (var obj in ordered)
        {
            if (obj is not ProviderModel model) continue;        // drop "-" separators
            if (filter is not null && !filter(model)) continue;
            result.Add(BuildEntry(model));
        }
        return result;
    }

    /// <summary>
    /// Projects a single <see cref="ProviderModel"/> into a
    /// <see cref="ModelPickerEntry"/>.
    /// </summary>
    /// <param name="model">The source model.</param>
    /// <returns>The projected entry. <see cref="ModelPickerEntry.Tag"/> carries
    /// the original <see cref="ProviderModel"/> reference so callers can recover
    /// it on selection.</returns>
    private static ModelPickerEntry BuildEntry(ProviderModel model) => new()
    {
        ModelId = model.ModelId,
        DisplayName = model.ModelId,
        GroupKey = model.ProviderType,
        GroupDisplayName = ResolveGroupDisplayName(model.ProviderType),
        Tag = model,
    };

    /// <summary>
    /// Resolves the human-readable group header for a given provider-type literal.
    /// Mock → "Demo", Custom_* → resolved via
    /// <see cref="AppSettings.LoadCustomProviders"/>, otherwise the literal itself.
    /// </summary>
    /// <param name="providerType">The <see cref="ProviderModel.ProviderType"/> value.</param>
    /// <returns>The group display name.</returns>
    public static string ResolveGroupDisplayName(string providerType)
    {
        if (providerType == "Mock") return "demo";
        if (providerType.StartsWith("Custom_", StringComparison.Ordinal))
        {
            var configId = providerType["Custom_".Length..];
            var customs = AppSettings.LoadCustomProviders();
            var match = customs.FirstOrDefault(c => c.Id == configId);
            return match is null ? providerType : match.DisplayName;
        }
        return providerType;
    }
}
