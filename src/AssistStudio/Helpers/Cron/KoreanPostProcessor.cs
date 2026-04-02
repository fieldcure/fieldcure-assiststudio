// Korean-specific post-processing for cron expression descriptions.
// Transforms ExpressionDescriptor output into natural Korean phrasing.

using System.Text.RegularExpressions;

namespace AssistStudio.Helpers.Cron;

/// <summary>
/// Transforms cron description strings into natural Korean phrasing.
/// Applied after <see cref="ExpressionDescriptor"/> produces a raw Korean description.
/// </summary>
public static partial class KoreanPostProcessor
{
    /// <summary>
    /// Applies all Korean post-processing transformations.
    /// </summary>
    public static string Process(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return description;

        description = FixDayOfMonthList(description);
        description = RemoveRedundantDaily(description);
        description = ReplaceWeekdayShorthands(description);
        description = AbbreviateMultipleDays(description);
        description = RemoveOnlySuffix(description);
        description = FormatTimesKorean(description);
        description = CleanupSpacing(description);
        description = AddDailyIfTimeOnly(description);

        return description;
    }

    /// <summary>
    /// Fixes day-of-month lists: "매월 1 및 15일" → "매월 1일 및 15일".
    /// In comma/and lists, only the last number gets "일" suffix from the format string.
    /// </summary>
    private static string FixDayOfMonthList(string desc)
    {
        // Match bare numbers before "및" or "," inside "매월 ... 일" context
        if (!desc.Contains("매월")) return desc;
        return DayOfMonthListPattern().Replace(desc, m => $"{m.Groups[1].Value}일{m.Groups[2].Value}");
    }

    /// <summary>
    /// Removes redundant "매일" prefix from interval expressions.
    /// "매일 5분마다" → "5분마다", "매일 매시간" → "매시간", "매일 매분" → "매분"
    /// Keeps "매일" when followed by a time ("매일 6시").
    /// </summary>
    private static string RemoveRedundantDaily(string desc)
    {
        // Remove "매일" when followed by interval expressions (마다, 매시간, 매분, 매초)
        // Keep "매일" only when followed by a fixed time (HH:MM)
        if (desc.StartsWith("매일 ") && !DailyTimePattern().IsMatch(desc))
        {
            var rest = desc[3..];
            // Also handle "매일 15분마다, 9시~17시 사이" → rearrange
            if (rest.Contains(", ") && rest.Contains("사이"))
            {
                var parts = rest.Split(", ", 2);
                return $"{parts[1]} {parts[0]}";
            }
            return rest;
        }
        return desc;
    }

    /// <summary>
    /// Replaces common weekday ranges/combinations with Korean shorthands.
    /// 월요일~금요일 → 평일, 일요일 및 토요일 → 주말
    /// </summary>
    private static string ReplaceWeekdayShorthands(string desc)
    {
        desc = desc.Replace("월요일~금요일", "평일");
        desc = desc.Replace("일요일 및 토요일", "주말");
        return desc;
    }

    /// <summary>
    /// Abbreviates multiple weekday names: 월요일, 수요일 및 금요일 → 월, 수, 금요일.
    /// Keeps the last day name full (with 요일), abbreviates preceding ones to single character.
    /// </summary>
    private static string AbbreviateMultipleDays(string desc)
    {
        // Match "X요일," or "X요일 및" (not the last one)
        desc = AbbreviateDayPattern().Replace(desc, m => m.Groups[1].Value + m.Groups[2].Value);
        // Clean up ", 및" → ", " artifact: "월, 수, 및 금요일" → "월, 수, 금요일"
        desc = desc.Replace(", 및 ", ", ");
        return desc;
    }

    /// <summary>
    /// Removes "에만" suffix that comes from ComaOnlyOnX0 resource.
    /// "월요일에만" → "월요일", "토요일에만" → "토요일"
    /// But preserves "에만" in month contexts like "1월에만" (from ComaOnlyInX0).
    /// </summary>
    private static string RemoveOnlySuffix(string desc)
    {
        // Remove "에만" after weekday names (요일에만 → 요일)
        desc = desc.Replace("요일에만", "요일");
        // Remove "에만" after 주말/평일
        desc = desc.Replace("주말에만", "주말");
        desc = desc.Replace("평일에만", "평일");
        return desc;
    }

    /// <summary>
    /// Converts 24-hour time format to Korean: 06:00 → 6시, 09:30 → 9시 30분.
    /// Handles times inside range expressions (09:00~17:59 → 9시~17시).
    /// </summary>
    private static string FormatTimesKorean(string desc)
    {
        // First handle time ranges: "HH:MM~HH:MM 사이" → "H시~H시" (strip :59 minutes and 사이)
        desc = TimeRangePattern().Replace(desc, m =>
        {
            int h1 = int.Parse(m.Groups[1].Value);
            int m1 = int.Parse(m.Groups[2].Value);
            int h2 = int.Parse(m.Groups[3].Value);
            // int m2 = int.Parse(m.Groups[4].Value); // usually :59, ignore for ranges

            var t1 = m1 == 0 ? $"{h1}시" : $"{h1}시 {m1}분";
            // For range end, only show hours (drop :59 artifact)
            var t2 = $"{h2}시";
            var suffix = m.Groups[5].Value; // " 사이" if present
            return $"{t1}~{t2}{suffix}";
        });

        // Then handle standalone times: "HH:MM" → "H시" or "H시 M분"
        desc = StandaloneTimePattern().Replace(desc, m =>
        {
            int h = int.Parse(m.Groups[1].Value);
            int min = int.Parse(m.Groups[2].Value);
            return min == 0 ? $"{h}시" : $"{h}시 {min}분";
        });

        return desc;
    }

    /// <summary>
    /// If the description is just a time with no other context (e.g. "6시"),
    /// prepends "매일" to make it natural ("매일 6시").
    /// </summary>
    private static string AddDailyIfTimeOnly(string desc)
    {
        // After post-processing, if it's just a time like "6시" or "9시 30분", add "매일"
        if (TimeOnlyPattern().IsMatch(desc))
            return $"매일 {desc}";
        return desc;
    }

    /// <summary>
    /// Cleans up double spaces and trailing/leading whitespace.
    /// </summary>
    private static string CleanupSpacing(string desc)
    {
        desc = MultipleSpacesPattern().Replace(desc, " ");
        return desc.Trim();
    }

    // Regex patterns (source-generated for performance)

    /// <summary>Matches "X요일," or "X요일 및" for abbreviation (not last day).</summary>
    [GeneratedRegex(@"([\uAC00-\uD7A3])요일(,| 및)")]
    private static partial Regex AbbreviateDayPattern();

    /// <summary>Matches time range: "HH:MM~HH:MM" optionally followed by " 사이".</summary>
    [GeneratedRegex(@"(\d{1,2}):(\d{2})~(\d{1,2}):(\d{2})( 사이)?")]
    private static partial Regex TimeRangePattern();

    /// <summary>Matches standalone time "HH:MM" (not part of a range).</summary>
    [GeneratedRegex(@"(\d{1,2}):(\d{2})")]
    private static partial Regex StandaloneTimePattern();

    /// <summary>Matches multiple consecutive spaces.</summary>
    [GeneratedRegex(@" {2,}")]
    private static partial Regex MultipleSpacesPattern();

    /// <summary>Matches a time-only string like "6시" or "9시 30분" (nothing else).</summary>
    [GeneratedRegex(@"^\d{1,2}시( \d{1,2}분)?$")]
    private static partial Regex TimeOnlyPattern();

    /// <summary>Matches "매일" followed by a time pattern (HH:MM or H시).</summary>
    [GeneratedRegex(@"^매일 \d{1,2}:")]
    private static partial Regex DailyTimePattern();

    /// <summary>Matches bare number before "및" or "," in day-of-month context.</summary>
    [GeneratedRegex(@"(\d+)( 및|,)(?=.*일)")]
    private static partial Regex DayOfMonthListPattern();
}
