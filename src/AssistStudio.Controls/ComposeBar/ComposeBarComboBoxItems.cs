using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// Represents a data-backed ComboBox entry for the ComposeBar preset and profile selectors.
/// </summary>
internal sealed class ComposeBarComboBoxItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ComposeBarComboBoxItem"/> class.
    /// </summary>
    /// <param name="displayName">The text shown in the ComboBox.</param>
    /// <param name="providerPreset">The provider preset represented by this entry, if any.</param>
    /// <param name="profile">The profile represented by this entry, if any.</param>
    /// <param name="isSeparator">Whether this entry is a non-interactive separator row.</param>
    private ComposeBarComboBoxItem(string displayName, ProviderPreset? providerPreset, Profile? profile, bool isSeparator)
    {
        DisplayName = displayName;
        ProviderPreset = providerPreset;
        Profile = profile;
        IsSeparator = isSeparator;
    }

    /// <summary>
    /// Gets the text shown in the ComboBox.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the provider preset represented by this entry, if any.
    /// </summary>
    public ProviderPreset? ProviderPreset { get; }

    /// <summary>
    /// Gets the profile represented by this entry, if any.
    /// </summary>
    public Profile? Profile { get; }

    /// <summary>
    /// Gets a value indicating whether this entry is a visual separator.
    /// </summary>
    public bool IsSeparator { get; }

    /// <summary>
    /// Creates a provider preset entry.
    /// </summary>
    /// <param name="preset">The preset to store in the ComboBox entry.</param>
    /// <param name="displayName">The text shown for the preset.</param>
    /// <returns>A new provider preset ComboBox entry.</returns>
    public static ComposeBarComboBoxItem FromProviderPreset(ProviderPreset preset, string displayName)
    {
        return new ComposeBarComboBoxItem(displayName, preset, null, isSeparator: false);
    }

    /// <summary>
    /// Creates a profile entry.
    /// </summary>
    /// <param name="profile">The profile to store in the ComboBox entry.</param>
    /// <returns>A new profile ComboBox entry.</returns>
    public static ComposeBarComboBoxItem FromProfile(Profile profile)
    {
        return new ComposeBarComboBoxItem(profile.Name, null, profile, isSeparator: false);
    }

    /// <summary>
    /// Creates a separator entry.
    /// </summary>
    /// <returns>A new separator ComboBox entry.</returns>
    public static ComposeBarComboBoxItem Separator()
    {
        return new ComposeBarComboBoxItem(string.Empty, null, null, isSeparator: true);
    }
}

/// <summary>
/// Selects the visual template for ComposeBar ComboBox entries.
/// </summary>
internal sealed class ComposeBarComboBoxItemTemplateSelector : DataTemplateSelector
{
    /// <summary>
    /// Gets or sets the template used for normal text entries.
    /// </summary>
    public DataTemplate? DefaultTemplate { get; set; }

    /// <summary>
    /// Gets or sets the template used for separator entries.
    /// </summary>
    public DataTemplate? SeparatorTemplate { get; set; }

    /// <summary>
    /// Returns the correct template for the provided ComboBox entry.
    /// </summary>
    /// <param name="item">The data item being rendered.</param>
    /// <param name="container">The container requesting the template.</param>
    /// <returns>The separator template for separator entries; otherwise the default template.</returns>
    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
    {
        return item is ComposeBarComboBoxItem { IsSeparator: true }
            ? SeparatorTemplate ?? DefaultTemplate
            : DefaultTemplate;
    }
}

/// <summary>
/// Selects the item container style for ComposeBar ComboBox entries.
/// </summary>
internal sealed class ComposeBarComboBoxItemContainerStyleSelector : StyleSelector
{
    /// <summary>
    /// Gets or sets the style used for separator entries.
    /// </summary>
    public Style? SeparatorStyle { get; set; }

    /// <summary>
    /// Returns the correct item container style for the provided ComboBox entry.
    /// </summary>
    /// <param name="item">The data item being rendered.</param>
    /// <param name="container">The container requesting the style.</param>
    /// <returns>The separator style for separator entries; otherwise <c>null</c>.</returns>
    protected override Style? SelectStyleCore(object item, DependencyObject container)
    {
        return item is ComposeBarComboBoxItem { IsSeparator: true }
            ? SeparatorStyle
            : null;
    }
}
