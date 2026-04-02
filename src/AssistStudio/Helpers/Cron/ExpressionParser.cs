// Based on cron-expression-descriptor by Brady Holt
// https://github.com/bradymholt/cron-expression-descriptor
// Licensed under MIT License

using System.Globalization;
using System.Text.RegularExpressions;

namespace AssistStudio.Helpers.Cron;

/// <summary>
/// Cron Expression Parser
/// </summary>
public class ExpressionParser
{
    private readonly string m_expression;
    private readonly Options m_options;

    public ExpressionParser(string expression, Options options)
    {
        m_expression = expression;
        m_options = options;
    }

    /// <summary>
    /// Parses the cron expression string
    /// </summary>
    /// <returns>A 7 part string array, one part for each component of the cron expression (seconds, minutes, etc.)</returns>
    public string[] Parse()
    {
        string[] parsed = new string[7].Select(el => "").ToArray();

        if (string.IsNullOrEmpty(m_expression))
        {
            throw new MissingFieldException("Field 'expression' not found.");
        }

        string[] expressionPartsTemp = m_expression.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (expressionPartsTemp.Length < 5)
        {
            throw new FormatException($"Error: Expression only has {expressionPartsTemp.Length} parts. At least 5 parts are required.");
        }
        else if (expressionPartsTemp.Length == 5)
        {
            Array.Copy(expressionPartsTemp, 0, parsed, 1, 5);
        }
        else if (expressionPartsTemp.Length == 6)
        {
            bool isYearWithNoSecondsPart = Regex.IsMatch(expressionPartsTemp[5], "\\d{4}$")
                || expressionPartsTemp[4] == "?" || expressionPartsTemp[2] == "?";

            if (isYearWithNoSecondsPart)
            {
                Array.Copy(expressionPartsTemp, 0, parsed, 1, 6);
            }
            else
            {
                Array.Copy(expressionPartsTemp, 0, parsed, 0, 6);
            }
        }
        else if (expressionPartsTemp.Length == 7)
        {
            parsed = expressionPartsTemp;
        }
        else
        {
            throw new FormatException($"Error: Expression has too many parts ({expressionPartsTemp.Length}). Expression must not have more than 7 parts.");
        }

        NormalizeExpression(parsed);

        return parsed;
    }

    private void NormalizeExpression(string[] expressionParts)
    {
        expressionParts[3] = expressionParts[3].Replace("?", "*");
        expressionParts[5] = expressionParts[5].Replace("?", "*");

        if (expressionParts[0].StartsWith("0/"))
            expressionParts[0] = expressionParts[0].Replace("0/", "*/");
        if (expressionParts[1].StartsWith("0/"))
            expressionParts[1] = expressionParts[1].Replace("0/", "*/");
        if (expressionParts[2].StartsWith("0/"))
            expressionParts[2] = expressionParts[2].Replace("0/", "*/");
        if (expressionParts[3].StartsWith("1/"))
            expressionParts[3] = expressionParts[3].Replace("1/", "*/");
        if (expressionParts[4].StartsWith("1/"))
            expressionParts[4] = expressionParts[4].Replace("1/", "*/");
        if (expressionParts[5].StartsWith("1/"))
            expressionParts[5] = expressionParts[5].Replace("1/", "*/");
        if (expressionParts[6].StartsWith("1/"))
            expressionParts[6] = expressionParts[6].Replace("1/", "*/");

        // Adjust DOW based on dayOfWeekStartIndexZero option
        expressionParts[5] = Regex.Replace(expressionParts[5], @"(^\d)|([^#/\s]\d)", t =>
        {
            string dowDigits = Regex.Replace(t.Value, @"\D", "");
            string dowDigitsAdjusted = dowDigits;

            if (m_options.DayOfWeekStartIndexZero)
            {
                if (dowDigits == "7") dowDigitsAdjusted = "0";
            }
            else
            {
                dowDigitsAdjusted = (int.Parse(dowDigits) - 1).ToString();
            }

            return t.Value.Replace(dowDigits, dowDigitsAdjusted);
        });

        if (expressionParts[3] == "?")
            expressionParts[3] = "*";

        // Convert SUN-SAT to 0-6
        for (int i = 0; i <= 6; i++)
        {
            DayOfWeek currentDay = (DayOfWeek)i;
            string currentDayOfWeekDescription = currentDay.ToString()[..3].ToUpperInvariant();
            expressionParts[5] = Regex.Replace(expressionParts[5], currentDayOfWeekDescription,
                i.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        // Convert JAN-DEC to 1-12
        for (int i = 1; i <= 12; i++)
        {
            DateTime currentMonth = new(DateTime.Now.Year, i, 1);
            string currentMonthDescription = currentMonth.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant();
            expressionParts[4] = Regex.Replace(expressionParts[4], currentMonthDescription,
                i.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        if (expressionParts[0] == "0")
            expressionParts[0] = string.Empty;

        // Make single hour a self-range when seconds/minutes have intervals
        if (expressionParts[2].IndexOfAny(['*', '-', ',', '/']) == -1
            && (Regex.IsMatch(expressionParts[1], @"\*|\/") || Regex.IsMatch(expressionParts[0], @"\*|\/")))
        {
            expressionParts[2] += $"-{expressionParts[2]}";
        }

        for (int i = 0; i < expressionParts.Length; i++)
        {
            if (expressionParts[i] == "*/1")
                expressionParts[i] = "*";

            if (expressionParts[i].Contains('/')
                && expressionParts[i].IndexOfAny(['*', '-', ',']) == -1)
            {
                string? stepRangeThrough = i switch
                {
                    4 => "12",
                    5 => "6",
                    6 => "9999",
                    _ => null
                };

                if (stepRangeThrough != null)
                {
                    string[] parts = expressionParts[i].Split('/');
                    expressionParts[i] = $"{parts[0]}-{stepRangeThrough}/{parts[1]}";
                }
            }
        }
    }
}
