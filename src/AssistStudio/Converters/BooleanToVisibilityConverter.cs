using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace AssistStudio.Converters;

/// <summary>
/// Converts a boolean value to Visibility.
/// </summary>
public sealed partial class BooleanToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Gets or sets whether to invert the boolean value.
    /// </summary>
    public bool IsInverted { get; set; } = false;

    /// <summary>
    /// Converts a boolean or non-null value to Visibility.
    /// </summary>
    /// <param name="value">The value to convert. Can be boolean or any object (non-null treated as true).</param>
    /// <param name="targetType">The target type (not used).</param>
    /// <param name="parameter">Optional parameter (not used).</param>
    /// <param name="language">The language (not used).</param>
    /// <returns>Visibility.Visible if true (or non-null), Visibility.Collapsed if false (or null). Result is inverted if IsInverted is true.</returns>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var boolValue = false;

        if (value is bool b)
            boolValue = b;
        else if (value != null)
            boolValue = true;

        if (IsInverted)
            boolValue = !boolValue;

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Converts a Visibility value back to boolean.
    /// </summary>
    /// <param name="value">The Visibility value to convert back.</param>
    /// <param name="targetType">The target type (not used).</param>
    /// <param name="parameter">Optional parameter (not used).</param>
    /// <param name="language">The language (not used).</param>
    /// <returns>True if Visibility.Visible, false if Visibility.Collapsed. Result is inverted if IsInverted is true.</returns>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is Visibility visibility)
        {
            var result = visibility == Visibility.Visible;
            return IsInverted ? !result : result;
        }
        return false;
    }
}
