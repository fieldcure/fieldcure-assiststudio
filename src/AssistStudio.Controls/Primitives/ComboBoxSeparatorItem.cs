using Microsoft.UI.Xaml.Controls;

namespace FieldCure.AssistStudio.Controls.Primitives;

/// <summary>
/// A non-interactive separator item for use in a <see cref="ComboBox"/>.
/// Visuals are provided by the implicit style in <c>Themes/Shared.xaml</c>,
/// which draws a theme-aware divider line via <c>{ThemeResource DividerStrokeColorDefaultBrush}</c>.
/// </summary>
public sealed partial class ComboBoxSeparatorItem : ComboBoxItem
{
    /// <summary>Initializes a new instance of the <see cref="ComboBoxSeparatorItem"/> class.</summary>
    public ComboBoxSeparatorItem()
    {
        DefaultStyleKey = typeof(ComboBoxSeparatorItem);
    }
}
