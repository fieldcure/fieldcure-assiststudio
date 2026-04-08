// Based on cron-expression-descriptor by Brady Holt
// https://github.com/bradymholt/cron-expression-descriptor
// Licensed under MIT License

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AssistStudio.Helpers.Cron;

/// <summary>
/// Describes the type of cron expression description to generate.
/// </summary>
public enum DescriptionTypeEnum
{
    FULL,
    TIMEOFDAY,
    SECONDS,
    MINUTES,
    HOURS,
    DAYOFWEEK,
    MONTH,
    DAYOFMONTH,
    YEAR
}

/// <summary>
/// Converts a Cron Expression into a human readable string.
/// Resource strings are loaded from WinUI ResourceLoader with <c>Cron_</c> prefix,
/// falling back to English defaults when unavailable.
/// </summary>
public class ExpressionDescriptor
{
    #region Fields

    /// <summary>
    /// English fallback strings, keyed by resource name.
    /// Used when <see cref="Options.StringResolver"/> is not set or returns <c>null</c>.
    /// </summary>
    private static readonly Dictionary<string, string> EnglishDefaults = new()
    {
        ["EveryMinute"] = "every minute",
        ["EveryHour"] = "every hour",
        ["EverySecond"] = "every second",
        ["AnErrorOccurredWhenGeneratingTheExpressionD"] = "An error occurred when generating the expression description. Check the cron expression syntax.",
        ["At"] = "At",
        ["AtSpace"] = "At ",
        ["AtX0"] = "at {0}",
        ["AtX0MinutesPastTheHour"] = "at {0} minutes past the hour",
        ["AtX0SecondsPastTheMinute"] = "at {0} seconds past the minute",
        ["BetweenX0AndX1"] = "between {0} and {1}",
        ["EveryMinuteBetweenX0AndX1"] = "Every minute between {0} and {1}",
        ["EveryX0Seconds"] = "every {0} seconds",
        ["EveryX0Minutes"] = "every {0} minutes",
        ["EveryX0Hours"] = "every {0} hours",
        ["SecondsX0ThroughX1PastTheMinute"] = "seconds {0} through {1} past the minute",
        ["MinutesX0ThroughX1PastTheHour"] = "minutes {0} through {1} past the hour",
        ["ComaEveryDay"] = ", every day",
        ["ComaEveryMinute"] = ", every minute",
        ["ComaEveryHour"] = ", every hour",
        ["ComaEveryX0Days"] = ", every {0} days",
        ["ComaEveryX0DaysOfTheWeek"] = ", every {0} days of the week",
        ["ComaEveryX0Months"] = ", every {0} months",
        ["ComaEveryX0Years"] = ", every {0} years",
        ["ComaOnDayX0OfTheMonth"] = ", on day {0} of the month",
        ["ComaOnThe"] = ", on the ",
        ["ComaOnTheX0OfTheMonth"] = ", on the {0} of the month",
        ["ComaOnTheLastDayOfTheMonth"] = ", on the last day of the month",
        ["ComaOnTheLastWeekdayOfTheMonth"] = ", on the last weekday of the month",
        ["ComaOnTheLastX0OfTheMonth"] = ", on the last {0} of the month",
        ["ComaBetweenDayX0AndX1OfTheMonth"] = ", between day {0} and {1} of the month",
        ["CommaDaysBeforeTheLastDayOfTheMonth"] = ", {0} days before the last day of the month",
        ["ComaOnlyOnX0"] = ", only on {0}",
        ["ComaOnlyInX0"] = ", only in {0}",
        ["ComaOnlyInYearX0"] = ", only in {0}",
        ["ComaX0ThroughX1"] = ", {0} through {1}",
        ["CommaStartingX0"] = ", starting {0}",
        ["First"] = "first",
        ["Second"] = "second",
        ["Third"] = "third",
        ["Fourth"] = "fourth",
        ["Fifth"] = "fifth",
        ["FirstWeekday"] = "first weekday",
        ["WeekdayNearestDayX0"] = "weekday nearest day {0}",
        ["SpaceX0OfTheMonth"] = " {0} of the month",
        ["SpaceAnd"] = " and",
        ["SpaceAndSpace"] = " and ",
        ["AMPeriod"] = "AM",
        ["PMPeriod"] = "PM",
    };

    private readonly char[] m_specialCharacters = ['/', '-', ',', '*'];
    private readonly string m_expression;
    private readonly Options m_options;
    private string[] m_expressionParts;
    private bool m_parsed;
    private readonly bool m_use24HourTimeFormat;
    private readonly CultureInfo m_culture;

    #endregion

    #region Constructors

    public ExpressionDescriptor(string expression) : this(expression, new Options()) { }

    public ExpressionDescriptor(string expression, Options options)
    {
        m_expression = expression;
        m_options = options;
        m_expressionParts = new string[7];
        m_parsed = false;
        m_culture = options.Culture ?? CultureInfo.CurrentUICulture;

        if (m_options.Use24HourTimeFormat != null)
        {
            m_use24HourTimeFormat = m_options.Use24HourTimeFormat.Value;
        }
        else
        {
            m_use24HourTimeFormat = m_culture.TwoLetterISOLanguageName != "en";
        }
    }

    #endregion

    #region Public Methods

    public string GetDescription(DescriptionTypeEnum type)
    {
        string description = string.Empty;

        try
        {
            if (!m_parsed)
            {
                ExpressionParser parser = new(m_expression, m_options);
                m_expressionParts = parser.Parse();
                m_parsed = true;
            }

            description = type switch
            {
                DescriptionTypeEnum.FULL => GetFullDescription(),
                DescriptionTypeEnum.TIMEOFDAY => GetTimeOfDayDescription(),
                DescriptionTypeEnum.HOURS => GetHoursDescription(),
                DescriptionTypeEnum.MINUTES => GetMinutesDescription(),
                DescriptionTypeEnum.SECONDS => GetSecondsDescription(),
                DescriptionTypeEnum.DAYOFMONTH => GetDayOfMonthDescription(),
                DescriptionTypeEnum.MONTH => GetMonthDescription(),
                DescriptionTypeEnum.DAYOFWEEK => GetDayOfWeekDescription(),
                DescriptionTypeEnum.YEAR => GetYearDescription(),
                _ => GetSecondsDescription(),
            };
        }
        catch (Exception ex)
        {
            if (!m_options.ThrowExceptionOnParseError)
                description = ex.Message;
            else
                throw;
        }

        if (description.Length > 0)
        {
            description = string.Concat(m_culture.TextInfo.ToUpper(description[0]), description[1..]);
        }

        return description;
    }

    #endregion

    #region Static API

    /// <summary>Returns a human-readable description of the specified cron expression using default options.</summary>
    public static string GetDescription(string expression)
        => GetDescription(expression, new Options());

    /// <summary>Returns a human-readable description of the specified cron expression.</summary>
    public static string GetDescription(string expression, Options options)
    {
        ExpressionDescriptor descriptor = new(expression, options);
        return descriptor.GetDescription(DescriptionTypeEnum.FULL);
    }

    #endregion

    #region Description Generators

    protected string GetFullDescription()
    {
        string description;

        try
        {
            string timeSegment = GetTimeOfDayDescription();
            string dayOfMonthDesc = GetDayOfMonthDescription();
            string monthDesc = GetMonthDescription();
            string dayOfWeekDesc = GetDayOfWeekDescription();
            string yearDesc = GetYearDescription();

            if (m_options.TimeAfterDescription && timeSegment.Length > 0)
        {
            // Korean-style: "매일 06:00" instead of "06:00, 매일"
            var rest = $"{dayOfMonthDesc}{dayOfWeekDesc}{monthDesc}{yearDesc}".TrimStart(',', ' ');
            description = rest.Length > 0 ? $"{rest} {timeSegment}" : timeSegment;
        }
        else
        {
            description = $"{timeSegment}{dayOfMonthDesc}{dayOfWeekDesc}{monthDesc}{yearDesc}";
        }
            description = TransformVerbosity(description, m_options.Verbose);
        }
        catch (Exception ex)
        {
            description = GetString("AnErrorOccurredWhenGeneratingTheExpressionD");
            if (m_options.ThrowExceptionOnParseError)
                throw new FormatException(description, ex);
        }

        return description;
    }

    protected string GetTimeOfDayDescription()
    {
        string secondsExpression = m_expressionParts[0];
        string minuteExpression = m_expressionParts[1];
        string hourExpression = m_expressionParts[2];

        StringBuilder description = new();

        if (minuteExpression.IndexOfAny(m_specialCharacters) == -1
            && hourExpression.IndexOfAny(m_specialCharacters) == -1
            && secondsExpression.IndexOfAny(m_specialCharacters) == -1)
        {
            description.Append(GetString("AtSpace")).Append(FormatTime(hourExpression, minuteExpression, secondsExpression));
        }
        else if (secondsExpression == "" && minuteExpression.Contains('-')
            && !minuteExpression.Contains(',')
            && hourExpression.IndexOfAny(m_specialCharacters) == -1)
        {
            string[] minuteParts = minuteExpression.Split('-');
            description.Append(string.Format(GetString("EveryMinuteBetweenX0AndX1"),
                FormatTime(hourExpression, minuteParts[0]),
                FormatTime(hourExpression, minuteParts[1])));
        }
        else if (secondsExpression == "" && hourExpression.Contains(',')
            && !hourExpression.Contains('-')
            && minuteExpression.IndexOfAny(m_specialCharacters) == -1)
        {
            string[] hourParts = hourExpression.Split(',');
            description.Append(GetString("At"));
            for (int i = 0; i < hourParts.Length; i++)
            {
                description.Append(' ').Append(FormatTime(hourParts[i], minuteExpression));
                if (i < hourParts.Length - 2) description.Append(',');
                if (i == hourParts.Length - 2) description.Append(GetString("SpaceAnd"));
            }
        }
        else
        {
            string secondsDescription = GetSecondsDescription();
            string minutesDescription = GetMinutesDescription();
            string hoursDescription = GetHoursDescription();

            description.Append(secondsDescription);
            if (description.Length > 0 && minutesDescription.Length > 0) description.Append(", ");
            description.Append(minutesDescription);
            if (description.Length > 0 && hourExpression.Length > 0) description.Append(", ");
            description.Append(hoursDescription);
        }

        return description.ToString();
    }

    protected string GetSecondsDescription()
    {
        return GetSegmentDescription(
            m_expressionParts[0],
            GetString("EverySecond"),
            s => s,
            s => string.Format(GetString("EveryX0Seconds"), s),
            s => GetString("SecondsX0ThroughX1PastTheMinute"),
            s => s == "0" ? string.Empty : GetString("AtX0SecondsPastTheMinute"),
            s => GetString("ComaX0ThroughX1"));
    }

    protected string GetMinutesDescription()
    {
        string secondsExpression = m_expressionParts[0];
        return GetSegmentDescription(
            m_expressionParts[1],
            GetString("EveryMinute"),
            s => s,
            s => string.Format(GetString("EveryX0Minutes"), s),
            s => GetString("MinutesX0ThroughX1PastTheHour"),
            s => (s == "0" && secondsExpression == "") ? string.Empty : GetString("AtX0MinutesPastTheHour"),
            s => GetString("ComaX0ThroughX1"));
    }

    protected string GetHoursDescription()
    {
        return GetSegmentDescription(
            m_expressionParts[2],
            GetString("EveryHour"),
            s => FormatTime(s, "0"),
            s => string.Format(GetString("EveryX0Hours"), s),
            s => GetString("BetweenX0AndX1"),
            s => GetString("AtX0"),
            s => GetString("ComaX0ThroughX1"));
    }

    protected string GetDayOfWeekDescription()
    {
        if (m_expressionParts[5] == "*")
            return string.Empty;

        return GetSegmentDescription(
            m_expressionParts[5],
            GetString("ComaEveryDay"),
            s =>
            {
                string exp = s.Contains('#') ? s.Remove(s.IndexOf('#'))
                           : s.Contains('L') ? s.Replace("L", string.Empty) : s;
                return m_culture.DateTimeFormat.GetDayName((DayOfWeek)Convert.ToInt32(exp));
            },
            s => string.Format(GetString("ComaEveryX0DaysOfTheWeek"), s),
            s => GetString("ComaX0ThroughX1"),
            s =>
            {
                if (s.Contains('#'))
                {
                    string dayOfWeekOfMonthNumber = s[(s.IndexOf('#') + 1)..];
                    string? dayOfWeekOfMonthDescription = dayOfWeekOfMonthNumber switch
                    {
                        "1" => GetString("First"),
                        "2" => GetString("Second"),
                        "3" => GetString("Third"),
                        "4" => GetString("Fourth"),
                        "5" => GetString("Fifth"),
                        _ => null,
                    };
                    return string.Concat(GetString("ComaOnThe"), dayOfWeekOfMonthDescription, GetString("SpaceX0OfTheMonth"));
                }
                else if (s.Contains('L'))
                {
                    return GetString("ComaOnTheLastX0OfTheMonth");
                }
                else
                {
                    return GetString("ComaOnlyOnX0");
                }
            },
            s => GetString("ComaX0ThroughX1"));
    }

    protected string GetMonthDescription()
    {
        return GetSegmentDescription(
            m_expressionParts[4],
            string.Empty,
            s => new DateTime(DateTime.Now.Year, Convert.ToInt32(s), 1).ToString("MMMM", m_culture),
            s => string.Format(GetString("ComaEveryX0Months"), s),
            s => GetString("ComaX0ThroughX1"),
            s => GetString("ComaOnlyInX0"),
            s => GetString("ComaX0ThroughX1"));
    }

    protected string GetDayOfMonthDescription()
    {
        string expression = m_expressionParts[3];

        switch (expression)
        {
            case "L":
                return GetString("ComaOnTheLastDayOfTheMonth");
            case "WL":
            case "LW":
                return GetString("ComaOnTheLastWeekdayOfTheMonth");
            default:
                Regex weekDayNumberMatches = new("(\\d{1,2}W)|(W\\d{1,2})");
                if (weekDayNumberMatches.IsMatch(expression))
                {
                    Match m = weekDayNumberMatches.Match(expression);
                    int dayNumber = int.Parse(m.Value.Replace("W", ""));
                    string dayString = dayNumber == 1
                        ? GetString("FirstWeekday")
                        : string.Format(GetString("WeekdayNearestDayX0"), dayNumber);
                    return string.Format(GetString("ComaOnTheX0OfTheMonth"), dayString);
                }

                Regex lastDayOffSetMatches = new("L-(\\d{1,2})");
                if (lastDayOffSetMatches.IsMatch(expression))
                {
                    Match m = lastDayOffSetMatches.Match(expression);
                    string offSetDays = m.Groups[1].Value;
                    return string.Format(GetString("CommaDaysBeforeTheLastDayOfTheMonth"), offSetDays);
                }

                if (expression == "*" && m_expressionParts[5] != "*")
                    return string.Empty;

                return GetSegmentDescription(expression,
                    GetString("ComaEveryDay"),
                    s => s,
                    s => s == "1" ? GetString("ComaEveryDay") : GetString("ComaEveryX0Days"),
                    s => GetString("ComaBetweenDayX0AndX1OfTheMonth"),
                    s => GetString("ComaOnDayX0OfTheMonth"),
                    s => GetString("ComaX0ThroughX1"));
        }
    }

    private string GetYearDescription()
    {
        return GetSegmentDescription(m_expressionParts[6],
            string.Empty,
            s => Regex.IsMatch(s, @"^\d+$")
                ? new DateTime(Convert.ToInt32(s), 1, 1).ToString("yyyy") : s,
            s => string.Format(GetString("ComaEveryX0Years"), s),
            s => GetString("ComaX0ThroughX1"),
            s => GetString("ComaOnlyInYearX0"),
            s => GetString("ComaX0ThroughX1"));
    }

    #endregion

    #region Segment Description

    protected string GetSegmentDescription(string expression,
        string allDescription,
        Func<string, string> getSingleItemDescription,
        Func<string, string> getIntervalDescriptionFormat,
        Func<string, string> getBetweenDescriptionFormat,
        Func<string, string> getDescriptionFormat,
        Func<string, string> getRangeFormat)
    {
        string? description;

        if (string.IsNullOrEmpty(expression))
        {
            description = string.Empty;
        }
        else if (expression == "*")
        {
            description = allDescription;
        }
        else if (expression.IndexOfAny(['/', '-', ',']) == -1)
        {
            description = string.Format(getDescriptionFormat(expression), getSingleItemDescription(expression));
        }
        else if (expression.Contains('/'))
        {
            string[] segments = expression.Split('/');
            description = string.Format(getIntervalDescriptionFormat(segments[1]), segments[1]);

            if (segments[0].Contains('-'))
            {
                string betweenSegmentDescription = GenerateBetweenSegmentDescription(segments[0], getBetweenDescriptionFormat, getSingleItemDescription);
                if (!betweenSegmentDescription.StartsWith(", "))
                    description += ", ";
                description += betweenSegmentDescription;
            }
            else if (segments[0].IndexOfAny(['*', ',']) == -1)
            {
                string rangeItemDescription = string.Format(getDescriptionFormat(segments[0]), getSingleItemDescription(segments[0]));
                rangeItemDescription = rangeItemDescription.Replace(", ", "");
                description += string.Format(GetString("CommaStartingX0"), rangeItemDescription);
            }
        }
        else if (expression.Contains(','))
        {
            string[] segments = expression.Split(',');
            string descriptionContent = string.Empty;

            for (int i = 0; i < segments.Length; i++)
            {
                if (i > 0 && segments.Length > 2)
                {
                    descriptionContent += ",";
                    if (i < segments.Length - 1) descriptionContent += " ";
                }

                if (i > 0 && segments.Length > 1 && (i == segments.Length - 1 || segments.Length == 2))
                    descriptionContent += GetString("SpaceAndSpace");

                if (segments[i].Contains('-'))
                {
                    string betweenSegmentDescription = GenerateBetweenSegmentDescription(segments[i], getRangeFormat, getSingleItemDescription);
                    betweenSegmentDescription = betweenSegmentDescription.Replace(", ", "");
                    descriptionContent += betweenSegmentDescription;
                }
                else
                {
                    descriptionContent += getSingleItemDescription(segments[i]);
                }
            }

            description = string.Format(getDescriptionFormat(expression), descriptionContent);
        }
        else if (expression.Contains('-'))
        {
            description = GenerateBetweenSegmentDescription(expression, getBetweenDescriptionFormat, getSingleItemDescription);
        }
        else
        {
            description = string.Empty;
        }

        return description ?? string.Empty;
    }

    protected string GenerateBetweenSegmentDescription(string betweenExpression,
        Func<string, string> getBetweenDescriptionFormat,
        Func<string, string> getSingleItemDescription)
    {
        string[] betweenSegments = betweenExpression.Split('-');
        string betweenSegment1Description = getSingleItemDescription(betweenSegments[0]);
        string betweenSegment2Description = getSingleItemDescription(betweenSegments[1]).Replace(":00", ":59");
        var betweenDescriptionFormat = getBetweenDescriptionFormat(betweenExpression);
        return string.Format(betweenDescriptionFormat, betweenSegment1Description, betweenSegment2Description);
    }

    #endregion

    #region Formatting Helpers

    protected string FormatTime(string hourExpression, string minuteExpression)
        => FormatTime(hourExpression, minuteExpression, string.Empty);

    protected string FormatTime(string hourExpression, string minuteExpression, string secondExpression)
    {
        int hour = Convert.ToInt32(hourExpression);
        string period = string.Empty;

        if (!m_use24HourTimeFormat)
        {
            period = GetString(hour >= 12 ? "PMPeriod" : "AMPeriod");
            if (period.Length > 0) period = $" {period}";
            if (hour > 12) hour -= 12;
            if (hour == 0) hour = 12;
        }

        string minute = Convert.ToInt32(minuteExpression).ToString();
        string second = string.IsNullOrEmpty(secondExpression)
            ? string.Empty
            : $":{Convert.ToInt32(secondExpression).ToString().PadLeft(2, '0')}";

        return $"{hour.ToString().PadLeft(2, '0')}:{minute.PadLeft(2, '0')}{second}{period}";
    }

    protected string TransformVerbosity(string description, bool useVerboseFormat)
    {
        if (!useVerboseFormat)
        {
            description = description.Replace(GetString("ComaEveryMinute"), string.Empty);
            description = description.Replace(GetString("ComaEveryHour"), string.Empty);
            description = description.Replace(GetString("ComaEveryDay"), string.Empty);
            description = Regex.Replace(description, @"\, ?$", "");
        }
        return description;
    }

    #endregion

    #region Resource Access

    /// <summary>
    /// Gets a localized string via <see cref="Options.StringResolver"/>.
    /// Falls back to English defaults when the resolver is not set or returns <c>null</c>.
    /// An empty string return from the resolver is treated as an intentional value (not a miss).
    /// </summary>
    protected string GetString(string resourceName)
    {
        var resolved = m_options.StringResolver?.Invoke(resourceName);
        if (resolved is not null)
            return resolved;

        return EnglishDefaults.TryGetValue(resourceName, out var fallback) ? fallback : resourceName;
    }

    #endregion
}
