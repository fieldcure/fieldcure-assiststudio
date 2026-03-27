using System.Text.RegularExpressions;

namespace FieldCure.DocumentParsers;

/// <summary>
/// Normalizes Hancom equation script to LaTeX notation.
/// Hancom's equation format is already close to LaTeX (<c>_{2}</c>, <c>^{+}</c>),
/// so this primarily maps Hancom-specific commands to their LaTeX equivalents.
/// </summary>
public static partial class HancomMathNormalizer
{
    /// <summary>
    /// Hancom-specific command → LaTeX mapping.
    /// Extend this dictionary as new Hancom commands are discovered.
    /// </summary>
    private static readonly Dictionary<string, string> CommandMap = new(StringComparer.Ordinal)
    {
        // Arrows
        ["rarrow"] = @"\rightarrow",
        ["larrow"] = @"\leftarrow",
        ["lrarrow"] = @"\leftrightarrow",
        ["Rarrow"] = @"\Rightarrow",
        ["Larrow"] = @"\Leftarrow",
        ["LRarrow"] = @"\Leftrightarrow",
        ["uarrow"] = @"\uparrow",
        ["darrow"] = @"\downarrow",

        // Greek (uppercase — lowercase are typically Unicode already)
        ["ALPHA"] = @"\Alpha",
        ["BETA"] = @"\Beta",
        ["GAMMA"] = @"\Gamma",
        ["DELTA"] = @"\Delta",
        ["SIGMA"] = @"\Sigma",
        ["OMEGA"] = @"\Omega",
        ["PI"] = @"\Pi",
        ["THETA"] = @"\Theta",
        ["LAMBDA"] = @"\Lambda",
        ["PHI"] = @"\Phi",
        ["PSI"] = @"\Psi",

        // Operators
        ["times"] = @"\times",
        ["cdot"] = @"\cdot",
        ["div"] = @"\div",
        ["pm"] = @"\pm",
        ["mp"] = @"\mp",
        ["leq"] = @"\leq",
        ["geq"] = @"\geq",
        ["neq"] = @"\neq",
        ["approx"] = @"\approx",
        ["equiv"] = @"\equiv",
        ["inf"] = @"\infty",

        // Functions
        ["sqrt"] = @"\sqrt",
        ["sum"] = @"\sum",
        ["int"] = @"\int",
        ["prod"] = @"\prod",
        ["lim"] = @"\lim",
        ["log"] = @"\log",
        ["ln"] = @"\ln",
        ["sin"] = @"\sin",
        ["cos"] = @"\cos",
        ["tan"] = @"\tan",
    };

    /// <summary>
    /// Converts Hancom equation script text to LaTeX notation.
    /// </summary>
    public static string ToLaTeX(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return "";

        // Remove "수식입니다." prefix (accessibility text added by Hancom)
        var result = script.Replace("수식입니다.", "").Trim();

        // 1. Strip all backtick delimiters first (they can appear inside braces too)
        result = result.Replace("`", "");

        // 2. Handle REL arrow pattern (without backticks):
        //    REL rarrow {label} {} → \xrightarrow{label}
        result = RelArrowRegex().Replace(result, match =>
        {
            var arrow = match.Groups[1].Value.Trim();
            var label = match.Groups[2].Value.Trim();
            var cmd = arrow.Contains("larrow", StringComparison.OrdinalIgnoreCase)
                ? @"\xleftarrow" : @"\xrightarrow";
            return string.IsNullOrEmpty(label) ? $"{cmd}{{}}" : $"{cmd}{{{label}}}";
        });

        // 3. Handle simple arrow keywords: rarrow → \rightarrow
        result = SimpleArrowRegex().Replace(result, match =>
        {
            var arrow = match.Groups[1].Value.Trim();
            return CommandMap.TryGetValue(arrow, out var latex) ? $" {latex} " : $" {arrow} ";
        });

        // 4. Replace remaining Hancom commands with LaTeX equivalents
        foreach (var (hancom, latex) in CommandMap)
        {
            result = Regex.Replace(result, $@"\b{Regex.Escape(hancom)}\b", latex);
        }

        // Normalize whitespace
        result = MultiSpaceRegex().Replace(result, " ").Trim();

        return result;
    }

    [GeneratedRegex(@"REL\s+(\w+arrow)\s+\{([^}]*)\}\s+\{[^}]*\}", RegexOptions.IgnoreCase)]
    private static partial Regex RelArrowRegex();

    [GeneratedRegex(@"\b(\w+arrow)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SimpleArrowRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpaceRegex();
}
