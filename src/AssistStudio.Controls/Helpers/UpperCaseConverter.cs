using Microsoft.UI.Xaml.Data;

namespace FieldCure.AssistStudio.Controls.Helpers;

/// <summary>
/// Converts a string to its upper-case form for display. Used by ModelPicker's
/// group-header template to render group keys (e.g. "Claude") in tracking-style
/// uppercase ("CLAUDE") without forcing the data layer to pre-uppercase.
/// </summary>
public sealed class UpperCaseConverter : IValueConverter
{
    /// <summary>Returns the upper-case form of <paramref name="value"/> as a string.</summary>
    public object Convert(object? value, Type targetType, object? parameter, string language)
        => value?.ToString()?.ToUpperInvariant() ?? string.Empty;

    /// <summary>Not supported — display-only conversion.</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
        => throw new NotSupportedException();
}
