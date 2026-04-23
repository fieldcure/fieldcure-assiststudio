using AssistStudio.Helpers.Cron;
using System.Globalization;

namespace AssistStudio.App.Tests;

/// <summary>
/// Tests localized cron-expression descriptions used by the AssistStudio scheduling UI.
/// </summary>
[TestClass]
public class CronDescriptorTests
{
    #region Helpers

    private static readonly Dictionary<string, string> KoreanStrings = new()
    {
        ["EveryMinute"] = "매분",
        ["EveryHour"] = "매시간",
        ["EverySecond"] = "매초",
        ["AnErrorOccurredWhenGeneratingTheExpressionD"] = "표현식 설명을 생성하는 중 오류가 발생했습니다.",
        ["At"] = "",
        ["AtSpace"] = "",
        ["AtX0"] = "{0}",
        ["AtX0MinutesPastTheHour"] = "{0}분",
        ["AtX0SecondsPastTheMinute"] = "{0}초",
        ["BetweenX0AndX1"] = "{0}~{1} 사이",
        ["EveryMinuteBetweenX0AndX1"] = "{0}~{1} 사이 매분",
        ["EveryX0Seconds"] = "{0}초마다",
        ["EveryX0Minutes"] = "{0}분마다",
        ["EveryX0Hours"] = "{0}시간마다",
        ["SecondsX0ThroughX1PastTheMinute"] = "{0}~{1}초",
        ["MinutesX0ThroughX1PastTheHour"] = "{0}~{1}분",
        ["ComaEveryDay"] = ", 매일",
        ["ComaEveryMinute"] = ", 매분",
        ["ComaEveryHour"] = ", 매시간",
        ["ComaEveryX0Days"] = ", {0}일마다",
        ["ComaEveryX0DaysOfTheWeek"] = ", 주 {0}일마다",
        ["ComaEveryX0Months"] = ", {0}개월마다",
        ["ComaEveryX0Years"] = ", {0}년마다",
        ["ComaOnDayX0OfTheMonth"] = ", 매월 {0}일",
        ["ComaOnThe"] = ", 매월 ",
        ["ComaOnTheX0OfTheMonth"] = ", 매월 {0}",
        ["ComaOnTheLastDayOfTheMonth"] = ", 매월 마지막 날",
        ["ComaOnTheLastWeekdayOfTheMonth"] = ", 매월 마지막 평일",
        ["ComaOnTheLastX0OfTheMonth"] = ", 매월 마지막 {0}",
        ["ComaBetweenDayX0AndX1OfTheMonth"] = ", 매월 {0}~{1}일",
        ["CommaDaysBeforeTheLastDayOfTheMonth"] = ", 매월 마지막 날 {0}일 전",
        ["ComaOnlyOnX0"] = ", {0}에만",
        ["ComaOnlyInX0"] = ", {0}에만",
        ["ComaOnlyInYearX0"] = ", {0}년에만",
        ["ComaX0ThroughX1"] = ", {0}~{1}",
        ["CommaStartingX0"] = ", {0}부터",
        ["First"] = "첫 번째",
        ["Second"] = "두 번째",
        ["Third"] = "세 번째",
        ["Fourth"] = "네 번째",
        ["Fifth"] = "다섯 번째",
        ["FirstWeekday"] = "첫 번째 평일",
        ["WeekdayNearestDayX0"] = "{0}일에 가장 가까운 평일",
        ["SpaceX0OfTheMonth"] = " {0}",
        ["SpaceAnd"] = " 및",
        ["SpaceAndSpace"] = " 및 ",
        ["AMPeriod"] = "오전",
        ["PMPeriod"] = "오후",
    };

    private static Options EnOptions() => new()
    {
        Use24HourTimeFormat = true,
        Culture = new CultureInfo("en-US"),
    };

    private static Options KoOptions() => new()
    {
        Use24HourTimeFormat = true,
        TimeAfterDescription = true,
        Culture = new CultureInfo("ko-KR"),
        StringResolver = name => KoreanStrings.TryGetValue(name, out var v) ? v : null,
    };

    /// <summary>
    /// Gets Korean description with post-processing (mirrors ScheduleHelper behavior).
    /// </summary>
    private static string GetKorean(string cron)
    {
        var raw = ExpressionDescriptor.GetDescription(cron, KoOptions());
        return KoreanPostProcessor.Process(raw);
    }

    #endregion

    #region EN: Basic Intervals

    [TestMethod]
    [DataRow("* * * * *", "Every minute")]
    [DataRow("*/5 * * * *", "Every 5 minutes")]
    [DataRow("*/30 * * * *", "Every 30 minutes")]
    [DataRow("0 * * * *", "Every hour")]
    [DataRow("0 */2 * * *", "Every 2 hours")]
    [DataRow("0 */6 * * *", "Every 6 hours")]
    public void EN_BasicIntervals(string cron, string expected)
    {
        Assert.AreEqual(expected, ExpressionDescriptor.GetDescription(cron, EnOptions()));
    }

    #endregion

    #region EN: Fixed Time + Every Day

    [TestMethod]
    [DataRow("0 6 * * *", "At 06:00")]
    [DataRow("0 18 * * *", "At 18:00")]
    [DataRow("30 9 * * *", "At 09:30")]
    [DataRow("0 0 * * *", "At 00:00")]
    public void EN_FixedTimeEveryDay(string cron, string expected)
    {
        Assert.AreEqual(expected, ExpressionDescriptor.GetDescription(cron, EnOptions()));
    }

    #endregion

    #region EN: Day of Week

    [TestMethod]
    [DataRow("0 9 * * 1", "At 09:00, only on Monday")]
    [DataRow("0 9 * * 5", "At 09:00, only on Friday")]
    [DataRow("0 9 * * 1-5", "At 09:00, Monday through Friday")]
    [DataRow("0 9 * * 0,6", "At 09:00, only on Sunday and Saturday")]
    [DataRow("0 9 * * 1,3,5", "At 09:00, only on Monday, Wednesday, and Friday")]
    [DataRow("0 8 * * 6", "At 08:00, only on Saturday")]
    public void EN_DayOfWeek(string cron, string expected)
    {
        Assert.AreEqual(expected, ExpressionDescriptor.GetDescription(cron, EnOptions()));
    }

    #endregion

    #region EN: Month/Day + Range

    [TestMethod]
    [DataRow("0 0 1 * *", "At 00:00, on day 1 of the month")]
    [DataRow("0 0 15 * *", "At 00:00, on day 15 of the month")]
    [DataRow("0 0 1,15 * *", "At 00:00, on day 1 and 15 of the month")]
    [DataRow("0 0 */3 * *", "At 00:00, every 3 days")]
    [DataRow("0 6 * * 1-5", "At 06:00, Monday through Friday")]
    public void EN_MonthDayRange(string cron, string expected)
    {
        Assert.AreEqual(expected, ExpressionDescriptor.GetDescription(cron, EnOptions()));
    }

    #endregion

    #region EN: Special Characters

    [TestMethod]
    [DataRow("0 0 L * *", "At 00:00, on the last day of the month")]
    [DataRow("0 0 LW * *", "At 00:00, on the last weekday of the month")]
    public void EN_SpecialChars(string cron, string expected)
    {
        Assert.AreEqual(expected, ExpressionDescriptor.GetDescription(cron, EnOptions()));
    }

    #endregion

    #region KO: Basic Intervals

    [TestMethod]
    [DataRow("* * * * *", "매분")]
    [DataRow("*/5 * * * *", "5분마다")]
    [DataRow("*/30 * * * *", "30분마다")]
    [DataRow("0 * * * *", "매시간")]
    [DataRow("0 */2 * * *", "2시간마다")]
    [DataRow("0 */6 * * *", "6시간마다")]
    public void KO_BasicIntervals(string cron, string expected)
    {
        Assert.AreEqual(expected, GetKorean(cron));
    }

    #endregion

    #region KO: Fixed Time (TimeAfterDescription + Korean time format)

    [TestMethod]
    [DataRow("0 6 * * *", "매일 6시")]
    [DataRow("0 18 * * *", "매일 18시")]
    [DataRow("30 9 * * *", "매일 9시 30분")]
    [DataRow("0 0 * * *", "매일 0시")]
    public void KO_FixedTimeEveryDay(string cron, string expected)
    {
        Assert.AreEqual(expected, GetKorean(cron));
    }

    #endregion

    #region KO: Day of Week

    [TestMethod]
    [DataRow("0 9 * * 1", "월요일 9시")]
    [DataRow("0 9 * * 5", "금요일 9시")]
    [DataRow("0 9 * * 1-5", "평일 9시")]
    [DataRow("0 9 * * 0,6", "주말 9시")]
    [DataRow("0 9 * * 1,3,5", "월, 수, 금요일 9시")]
    [DataRow("0 8 * * 6", "토요일 8시")]
    [DataRow("30 14 * * 1-5", "평일 14시 30분")]
    public void KO_DayOfWeek(string cron, string expected)
    {
        Assert.AreEqual(expected, GetKorean(cron));
    }

    #endregion

    #region KO: Month/Day

    [TestMethod]
    [DataRow("0 0 1 * *", "매월 1일 0시")]
    [DataRow("0 0 15 * *", "매월 15일 0시")]
    [DataRow("0 0 1,15 * *", "매월 1일 및 15일 0시")]
    public void KO_MonthDay(string cron, string expected)
    {
        Assert.AreEqual(expected, GetKorean(cron));
    }

    #endregion

    #region KO: Range/Interval

    [TestMethod]
    [DataRow("*/15 9-17 * * *", "9시~17시 사이 15분마다")]
    [DataRow("0 0 */3 * *", "3일마다 0시")]
    [DataRow("0 6 * * 1-5", "평일 6시")]
    public void KO_RangeInterval(string cron, string expected)
    {
        Assert.AreEqual(expected, GetKorean(cron));
    }

    #endregion

    #region KO: Special Characters

    [TestMethod]
    [DataRow("0 0 L * *", "매월 마지막 날 0시")]
    [DataRow("0 0 LW * *", "매월 마지막 평일 0시")]
    [DataRow("0 0 * * 1#1", "매월 첫 번째 월요일 0시")]
    [DataRow("0 0 * * 5#3", "매월 세 번째 금요일 0시")]
    public void KO_SpecialChars(string cron, string expected)
    {
        Assert.AreEqual(expected, GetKorean(cron));
    }

    #endregion

    #region KO: At Removal Verification

    [TestMethod]
    [DataRow("0 6 * * *")]
    [DataRow("0 9 * * 1")]
    [DataRow("30 14 * * *")]
    public void KO_NoAtPrefix(string cron)
    {
        var result = GetKorean(cron);
        Assert.IsFalse(result.Contains("At"), $"Korean output should not contain 'At': {result}");
    }

    #endregion

    #region Error / Edge Cases

    [TestMethod]
    public void InvalidCron_ReturnsErrorMessage()
    {
        var result = ExpressionDescriptor.GetDescription("invalid", new Options
        {
            ThrowExceptionOnParseError = false,
        });
        Assert.IsFalse(string.IsNullOrEmpty(result));
    }

    [TestMethod]
    [ExpectedException(typeof(FormatException))]
    public void InvalidCron_Throws()
    {
        ExpressionDescriptor.GetDescription("invalid");
    }

    [TestMethod]
    [ExpectedException(typeof(FormatException))]
    public void TooFewParts_Throws()
    {
        ExpressionDescriptor.GetDescription("0 6");
    }

    #endregion
}
