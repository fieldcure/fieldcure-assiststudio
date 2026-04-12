using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FieldCure.AssistStudio.Controls;

/// <summary>Selects between short inline and long collapsible parameter templates.</summary>
public sealed class ToolParameterTemplateSelector : DataTemplateSelector
{
    /// <summary>Template for short, inline parameter values.</summary>
    public DataTemplate? ShortTemplate { get; set; }

    /// <summary>Template for long, collapsible parameter values.</summary>
    public DataTemplate? LongTemplate { get; set; }

    /// <inheritdoc/>
    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
    {
        if (item is ToolParameterItem { IsLong: true } && LongTemplate is not null)
            return LongTemplate;
        return ShortTemplate ?? base.SelectTemplateCore(item, container);
    }
}
