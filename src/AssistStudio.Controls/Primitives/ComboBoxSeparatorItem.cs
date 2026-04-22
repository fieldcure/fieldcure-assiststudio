using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FieldCure.AssistStudio.Controls.Primitives;

/// <summary>
/// A non-interactive separator item for use in a <see cref="ComboBox"/>.
/// </summary>
public sealed partial class ComboBoxSeparatorItem : ComboBoxItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ComboBoxSeparatorItem"/> class
    /// with a horizontal divider line and all interaction disabled.
    /// </summary>
    public ComboBoxSeparatorItem()
    {
        IsEnabled = false;
        IsHitTestVisible = false;
        MinHeight = 0;
        Height = 9;
        Padding = new Thickness(0);
        Content = new Border
        {
            Height = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
        };
    }
}
