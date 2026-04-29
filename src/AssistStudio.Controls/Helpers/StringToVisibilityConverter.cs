using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace FieldCure.AssistStudio.Controls.Helpers;

/// <summary>
/// Converts a string to a <see cref="Visibility"/>: returns
/// <see cref="Visibility.Collapsed"/> when the string is null, empty, or
/// whitespace; otherwise <see cref="Visibility.Visible"/>. Used by the
/// <see cref="ModelPicker"/> item template to hide empty Description rows.
/// </summary>
internal sealed class StringToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Converts an input value (typically a string) into <see cref="Visibility"/>.
    /// </summary>
    /// <param name="value">The source value; may be null.</param>
    /// <param name="targetType">Unused; provided by the binding framework.</param>
    /// <param name="parameter">Unused.</param>
    /// <param name="language">Unused.</param>
    /// <returns><see cref="Visibility.Visible"/> when the string is non-empty; <see cref="Visibility.Collapsed"/> otherwise.</returns>
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var s = value as string;
        return string.IsNullOrWhiteSpace(s) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Not supported — visibility cannot be converted back to a meaningful string.
    /// </summary>
    /// <param name="value">Unused.</param>
    /// <param name="targetType">Unused.</param>
    /// <param name="parameter">Unused.</param>
    /// <param name="language">Unused.</param>
    /// <returns>Always throws <see cref="NotSupportedException"/>.</returns>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
