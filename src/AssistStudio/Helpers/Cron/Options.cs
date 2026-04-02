// Based on cron-expression-descriptor by Brady Holt
// https://github.com/bradymholt/cron-expression-descriptor
// Licensed under MIT License

namespace AssistStudio.Helpers.Cron;

/// <summary>
/// Options for parsing and describing a Cron Expression.
/// </summary>
public class Options
{
    public Options()
    {
        ThrowExceptionOnParseError = true;
        Verbose = false;
        DayOfWeekStartIndexZero = true;
    }

    public bool ThrowExceptionOnParseError { get; set; }
    public bool Verbose { get; set; }
    public bool DayOfWeekStartIndexZero { get; set; }
    public bool? Use24HourTimeFormat { get; set; }

    /// <summary>
    /// Culture used for day/month names. Defaults to <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>.
    /// </summary>
    public System.Globalization.CultureInfo? Culture { get; set; }

    /// <summary>
    /// When <c>true</c>, places the time segment after other description parts (e.g. "매일 06:00" instead of "06:00, 매일").
    /// Useful for languages like Korean where time typically comes last.
    /// </summary>
    public bool TimeAfterDescription { get; set; }

    /// <summary>
    /// Optional function that resolves a resource name to a localized string.
    /// Return the localized value (including empty string for intentionally blank values),
    /// or <c>null</c> to fall back to English defaults.
    /// When not set, English defaults are used.
    /// </summary>
    public Func<string, string?>? StringResolver { get; set; }
}
