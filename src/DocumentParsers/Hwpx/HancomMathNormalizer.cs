using System.Text.RegularExpressions;

namespace FieldCure.DocumentParsers;

/// <summary>
/// Normalizes Hancom equation script (HML/HWPX format) to LaTeX notation.
/// Architecture based on hml-equation-parser (Apache 2.0, OpenBapul/hml-equation-parser)
/// with HWPX-specific extensions:
/// <list type="bullet">
///   <item>Uppercase <c>OVER</c> support (HWPX uses <c>OVER</c>, not <c>over</c>)</item>
///   <item>Double-brace accent notation (e.g., <c>hat {{x}}</c>)</item>
///   <item>Tilde <c>~</c> spacing conversion</item>
///   <item><c>UNDEROVER ∫</c> integral pattern</item>
///   <item><c>REL rarrow {label}</c> labeled-arrow pattern</item>
/// </list>
/// </summary>
public static partial class HancomMathNormalizer
{
    #region Conversion Maps

    /// <summary>
    /// Direct token → LaTeX substitution map.
    /// Source: hml-equation-parser/convertMap.json (Apache 2.0) + HWPX extensions.
    /// Tokens mapped to empty string are silently removed.
    /// </summary>
    private static readonly Dictionary<string, string> ConvertMap =
        new(StringComparer.Ordinal)
        {
            // ── Set / Logic ────────────────────────────────────────────────
            ["TIMES"] = @"\times",      ["times"] = @"\times",
            ["SMALLSUM"] = @"\sum",     ["sum"] = @"\sum",
            ["SMALLPROD"] = @"\prod",   ["prod"] = @"\prod",
            ["SMALLINTER"] = @"\cap",
            ["CUP"] = @"\cup",          ["inter"] = @"\bigcap",
            ["union"] = @"\bigcup",
            ["OPLUS"] = @"\oplus",      ["OMINUS"] = @"\ominus",
            ["OTIMES"] = @"\otimes",    ["ODIV"] = @"\oslash",
            ["ODOT"] = @"\odot",
            ["LOR"] = @"\lor",          ["LAND"] = @"\land",
            ["SUBSET"] = @"\subset",    ["SUPERSET"] = @"\supset",
            ["SUBSETEQ"] = @"\subseteq", ["SUPSETEQ"] = @"\supseteq",
            ["IN"] = @"\in",            ["OWNS"] = @"\owns",
            ["NOTIN"] = @"\notin",
            ["SQCAP"] = @"\sqcap",      ["SQCUP"] = @"\sqcup",
            ["SQSUBSET"] = @"\sqsubset", ["SQSUBSETEQ"] = @"\sqsubseteq",
            ["BIGSQCUP"] = @"\bigsqcup",
            ["BIGOPLUS"] = @"\bigoplus", ["BIGOTIMES"] = @"\bigotimes",
            ["BIGODOT"] = @"\bigodot",  ["BIGUPLUS"] = @"\biguplus",
            ["SMCOPROD"] = @"\coprod",  ["coprod"] = @"\coprod",
            ["BIGOMINUS"] = @"{{\Large\ominus}}",
            ["BIGODIV"] = @"{{\Large\oslash}}",

            // ── Relations ──────────────────────────────────────────────────
            ["LEQ"] = @"\leq",       ["GEQ"] = @"\geq",
            ["leq"] = @"\leq",       ["geq"] = @"\geq",
            ["<<"] = @"\ll",         [">>"] = @"\gg",
            ["<<<"] = @"\lll",       [">>>"] = @"\ggg",
            ["PREC"] = @"\prec",     ["SUCC"] = @"\succ",
            ["SIM"] = @"\sim",
            ["APPROX"] = @"\approx", ["approx"] = @"\approx",
            ["SIMEQ"] = @"\simeq",   ["CONG"] = @"\cong",
            ["=="] = @"\equiv",      ["equiv"] = @"\equiv",
            ["!="] = @"\neq",        ["neq"] = @"\neq",
            ["DOTEQ"] = @"\doteq",   ["ASYMP"] = @"\asymp",
            ["ISO"] = @"\Bumpeq",    ["DSUM"] = @"\dotplus",
            ["XOR"] = @"\veebar",
            ["⊐"] = @"\sqsupset",   ["⊒"] = @"\sqsupseteq",

            // ── Arithmetic ────────────────────────────────────────────────
            ["±"] = @"\pm",          ["pm"] = @"\pm",
            ["-+"] = @"\mp",         ["mp"] = @"\mp",
            ["÷"] = @"\div",         ["div"] = @"\div",
            ["UPLUS"] = @"\uplus",
            ["CIRC"] = @"\circ",     ["BULLET"] = @"\bullet",
            ["DEG"] = @" ^\circ",
            ["AST"] = @"\ast",       ["STAR"] = @"\bigstar",
            ["BIGCIRC"] = @"\bigcirc",
            ["cdot"] = @"\cdot",

            // ── Delimiters ────────────────────────────────────────────────
            ["LEFT"] = @"\left",     ["RIGHT"] = @"\right",
            ["⌈"] = @"\lceil",      ["⌉"] = @"\rceil",
            ["⌊"] = @"\lfloor",     ["⌋"] = @"\rfloor",
            ["∥"] = @"\|",          ["PVER"] = @"\|",

            // ── Calculus / Analysis ───────────────────────────────────────
            ["sqrt"] = @"\sqrt",
            ["int"] = @"\int",       ["dint"] = @"\iint",
            ["tint"] = @"\iiint",    ["oint"] = @"\oint",
            ["lim"] = @"\lim",       ["Lim"] = @"\lim",
            ["Partial"] = @"\partial",
            ["INF"] = @"\infty",     ["inf"] = @"\infty",
            ["NABLA"] = @"\nabla",   ["PROPTO"] = @"\propto",
            // Unicode integral/sum/product symbols embedded directly in HWPX scripts
            ["∫"] = @"\int",         ["∬"] = @"\iint",
            ["∭"] = @"\iiint",      ["∮"] = @"\oint",
            ["∑"] = @"\sum",         ["∏"] = @"\prod",

            // ── Misc math ─────────────────────────────────────────────────
            ["EMPTYSET"] = @"\emptyset",
            ["THEREFORE"] = @"\therefore", ["BECAUSE"] = @"\because",
            ["EXIST"] = @"\exists",  ["FORALL"] = @"\forall",
            ["prime"] = @"'",        ["DIAMOND"] = @"\diamond",
            ["LAPLACE"] = @"\mathcal{L}",
            ["TRIANGLE"] = @"\triangle",
            ["ANGLE"] = @"\angle",   ["MSANGLE"] = @"\measuredangle",
            ["SANGLE"] = @"\sphericalangle",
            ["VDASH"] = @"\vdash",   ["DASHV"] = @"\dashv",
            ["BOT"] = @"\bot",       ["TOP"] = @"\top",
            ["MODELS"] = @"\models",
            ["CDOTS"] = @"\cdots",   ["LDOTS"] = @"\ldots",
            ["VDOTS"] = @"\vdots",   ["DDOTS"] = @"\ddots",
            ["DAGGER"] = @"\dagger", ["DDAGGER"] = @"\ddagger",
            ["MAPSTO"] = @"\mapsto", ["under"] = @"\underline",
            ["LSLANT"] = @"\diagup", ["RSLANT"] = @"\diagdown",
            ["CENTIGRADE"] = @"^{\circ}C",
            ["FAHRENHEIT"] = @"^{\circ}F",

            // ── Arrows ────────────────────────────────────────────────────
            ["larrow"] = @"\leftarrow",
            ["rarrow"] = @"\rightarrow",
            ["lrarrow"] = @"\leftrightarrow",
            ["Larrow"] = @"\Leftarrow",
            ["Rarrow"] = @"\Rightarrow",
            ["LRarrow"] = @"\Leftrightarrow",
            ["uarrow"] = @"\uparrow",
            ["darrow"] = @"\downarrow",
            ["LARROW"] = @"\Leftarrow",
            ["RARROW"] = @"\Rightarrow",
            ["UPARROW"] = @"\Uparrow",
            ["DOWNARROW"] = @"\Downarrow",
            ["udarrow"] = @"\updownarrow",
            ["<->"] = @"\leftrightarrow",
            ["->"] = @"\rightarrow",
            ["UDARROW"] = @"\Updownarrow",
            ["LRARROW"] = @"\Leftrightarrow",
            ["NWARROW"] = @"\nwarrow",   ["SEARROW"] = @"\searrow",
            ["NEARROW"] = @"\nearrow",   ["SWARROW"] = @"\swarrow",
            ["HOOKLEFT"] = @"\hookleftarrow",
            ["HOOKRIGHT"] = @"\hookrightarrow",

            // ── Greek (lowercase keyword form) ────────────────────────────
            // Note: HWPX typically embeds Unicode directly (τ, λ, γ, …).
            // These entries handle keyword-form input.
            ["alpha"] = @"\alpha",   ["beta"] = @"\beta",
            ["gamma"] = @"\gamma",   ["delta"] = @"\delta",
            ["epsilon"] = @"\epsilon", ["zeta"] = @"\zeta",
            ["eta"] = @"\eta",       ["theta"] = @"\theta",
            ["iota"] = @"\iota",     ["kappa"] = @"\kappa",
            ["lambda"] = @"\lambda", ["mu"] = @"\mu",
            ["nu"] = @"\nu",         ["xi"] = @"\xi",
            ["omicron"] = @"\omicron", ["pi"] = @"\pi",
            ["rho"] = @"\rho",       ["sigma"] = @"\sigma",
            ["tau"] = @"\tau",       ["upsilon"] = @"\upsilon",
            ["phi"] = @"\phi",       ["chi"] = @"\chi",
            ["psi"] = @"\psi",       ["omega"] = @"\omega",

            // ── Greek (uppercase keyword form) ────────────────────────────
            ["ALPHA"] = "A",         ["BETA"] = "B",
            ["GAMMA"] = @"\Gamma",   ["DELTA"] = @"\Delta",
            ["EPSILON"] = "E",       ["ZETA"] = "Z",
            ["ETA"] = "H",           ["THETA"] = @"\Theta",
            ["IOTA"] = "I",          ["KAPPA"] = "K",
            ["LAMBDA"] = @"\Lambda", ["MU"] = "M",
            ["NU"] = "N",            ["XI"] = @"\Xi",
            ["OMICRON"] = "O",       ["PI"] = @"\Pi",
            ["RHO"] = "P",           ["SIGMA"] = @"\Sigma",
            ["TAU"] = "T",           ["UPSILON"] = @"\Upsilon",
            ["PHI"] = @"\Phi",       ["CHI"] = "X",
            ["PSI"] = @"\Psi",       ["OMEGA"] = @"\Omega",

            // ── Trig / Functions ──────────────────────────────────────────
            ["log"] = @"\log",       ["ln"] = @"\ln",
            ["sin"] = @"\sin",       ["cos"] = @"\cos",
            ["tan"] = @"\tan",

            // ── HWPX-specific ─────────────────────────────────────────────
            // OVER: HWPX uses uppercase; normalize to lowercase for ReplaceFrac
            ["OVER"] = "over",
            // UNDEROVER: structural keyword before nary symbol (∫, ∑ …).
            // Removing it leaves the symbol to naturally take _ and ^ limits.
            ["UNDEROVER"] = "",
            // REL: handled by regex pre-pass; remove any remnants
            ["REL"] = "",
            ["BUILDREL"] = "",
        };

    /// <summary>
    /// Tokens replaced with internal HULK markers for deferred structural processing.
    /// (accent/matrix operators that need bracket-matching transforms)
    /// </summary>
    private static readonly Dictionary<string, string> MiddleConvertMap =
        new(StringComparer.Ordinal)
        {
            ["matrix"] = "HULKMATRIX",   ["pmatrix"] = "HULKPMATRIX",
            ["bmatrix"] = "HULKBMATRIX", ["dmatrix"] = "HULKDMATRIX",
            ["eqalign"] = "HULKEQALIGN", ["cases"] = "HULKCASE",
            ["vec"] = "HULKVEC",         ["dyad"] = "HULKDYAD",
            ["acute"] = "HULKACUTE",     ["grave"] = "HULKGRAVE",
            ["dot"] = "HULKDOT",         ["ddot"] = "HULKDDOT",
            ["bar"] = "HULKBAR",         ["hat"] = "HULKHAT",
            ["check"] = "HULKCHECK",     ["arch"] = "HULKARCH",
            ["tilde"] = "HULKTILDE",     ["BOX"] = "HULKBOX",
            ["OVERBRACE"] = "HULKOVERBRACE",
            ["UNDERBRACE"] = "HULKUNDERBRACE",
        };

    /// <summary>HULK accent marker → LaTeX accent command.</summary>
    private static readonly Dictionary<string, string> BarConvertMap =
        new(StringComparer.Ordinal)
        {
            ["HULKVEC"] = @"\overrightarrow",
            ["HULKDYAD"] = @"\overleftrightarrow",
            ["HULKACUTE"] = @"\acute",
            ["HULKGRAVE"] = @"\grave",
            ["HULKDOT"] = @"\dot",
            ["HULKDDOT"] = @"\ddot",
            ["HULKBAR"] = @"\overline",
            ["HULKHAT"] = @"\widehat",
            ["HULKCHECK"] = @"\check",
            ["HULKARCH"] = @"\overset{\frown}",
            ["HULKTILDE"] = @"\widetilde",
            ["HULKBOX"] = @"\boxed",
        };

    /// <summary>HULK matrix marker → LaTeX matrix environment.</summary>
    private static readonly Dictionary<string, (string Begin, string End, bool RemoveOuter)>
        MatrixConvertMap = new(StringComparer.Ordinal)
        {
            ["HULKMATRIX"]  = (@"\begin{matrix}",  @"\end{matrix}",  true),
            ["HULKPMATRIX"] = (@"\begin{pmatrix}", @"\end{pmatrix}", true),
            ["HULKBMATRIX"] = (@"\begin{bmatrix}", @"\end{bmatrix}", true),
            ["HULKDMATRIX"] = (@"\begin{vmatrix}", @"\end{vmatrix}", true),
            ["HULKCASE"]    = (@"\begin{cases}",   @"\end{cases}",   true),
            ["HULKEQALIGN"] = (@"\eqalign{",       "}",              false),
        };

    /// <summary>HULK brace marker → LaTeX brace command.</summary>
    private static readonly Dictionary<string, string> BraceConvertMap =
        new(StringComparer.Ordinal)
        {
            ["HULKOVERBRACE"]  = @"\overbrace",
            ["HULKUNDERBRACE"] = @"\underbrace",
        };

    #endregion

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Converts Hancom equation script text to LaTeX notation.
    /// </summary>
    public static string ToLaTeX(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return "";

        // Remove Hancom accessibility prefix
        var result = script.Replace("수식입니다.", "").Trim();

        // Pre-pass: REL arrow pattern before tokenization
        //   REL rarrow {label} {} → \xrightarrow{label}
        //   REL larrow {label} {} → \xleftarrow{label}
        result = RelArrowRegex().Replace(result, match =>
        {
            var arrowKw = match.Groups[1].Value;
            var label   = match.Groups[2].Value.Trim();
            var cmd = arrowKw.Contains("larrow", StringComparison.OrdinalIgnoreCase)
                ? @"\xleftarrow" : @"\xrightarrow";
            return string.IsNullOrEmpty(label) ? $"{cmd}{{}}" : $"{cmd}{{{label}}}";
        });

        // Tokenize: backticks → space; pad braces, pipes and & with spaces
        // | must be padded so that "RIGHT |LEFT" doesn't merge into "|LEFT" token
        result = result.Replace("`", " ")
                       .Replace("{", " { ")
                       .Replace("}", " } ")
                       .Replace("|", " | ")
                       .Replace("&", " & ");

        var tokens = result.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                           .ToList();

        // Token mapping (ConvertMap takes priority over MiddleConvertMap)
        for (var i = 0; i < tokens.Count; i++)
        {
            if (ConvertMap.TryGetValue(tokens[i], out var conv))
                tokens[i] = conv;
            else if (MiddleConvertMap.TryGetValue(tokens[i], out var mid))
                tokens[i] = mid;
        }

        // Remove tokens mapped to "" (UNDEROVER, REL, etc.)
        tokens = tokens.Where(t => t.Length > 0).ToList();

        // Fix: \left { → \left \{   and   \right } → \right \}
        for (var i = 1; i < tokens.Count; i++)
        {
            if (tokens[i] == "{" && tokens[i - 1] == @"\left")  tokens[i] = @"\{";
            if (tokens[i] == "}" && tokens[i - 1] == @"\right") tokens[i] = @"\}";
        }

        result = string.Join(" ", tokens);

        // Structural transforms (order matters)
        result = ReplaceFrac(result);
        result = ReplaceRootOf(result);
        result = ReplaceAllMatrix(result);
        result = ReplaceAllBar(result);
        result = ReplaceAllBrace(result);

        // ~ is Hancom math spacing notation → convert to space for clean LaTeX
        result = result.Replace("~", " ");

        // Normalize whitespace
        result = MultiSpaceRegex().Replace(result, " ").Trim();

        return result;
    }

    // ── Bracket utilities ─────────────────────────────────────────────────

    /// <summary>
    /// Finds the balanced <c>{ }</c> group starting at or after <paramref name="fromIndex"/>.
    /// Returns <c>(startInclusive, endExclusive)</c>.
    /// </summary>
    private static (int Start, int End) FindBracketsForward(string s, int fromIndex)
    {
        var start = s.IndexOf('{', fromIndex);
        if (start < 0) throw new InvalidOperationException("No opening bracket");
        var depth = 1;
        for (var i = start + 1; i < s.Length; i++)
        {
            if      (s[i] == '{') depth++;
            else if (s[i] == '}') { depth--; if (depth == 0) return (start, i + 1); }
        }
        throw new InvalidOperationException("Unmatched bracket");
    }

    /// <summary>
    /// Finds the balanced <c>{ }</c> group whose closing <c>}</c> appears immediately
    /// before <paramref name="beforeIndex"/> (skipping whitespace).
    /// Returns <c>(startInclusive, endExclusive)</c>.
    /// </summary>
    private static (int Start, int End) FindBracketsBackward(string s, int beforeIndex)
    {
        var pos = beforeIndex - 1;
        while (pos >= 0 && s[pos] == ' ') pos--;
        if (pos < 0 || s[pos] != '}')
            throw new InvalidOperationException("No closing bracket before index");

        var end = pos + 1;
        var depth = 1;
        pos--;
        while (pos >= 0)
        {
            if      (s[pos] == '}') depth++;
            else if (s[pos] == '{') { depth--; if (depth == 0) return (pos, end); }
            pos--;
        }
        throw new InvalidOperationException("Unmatched bracket");
    }

    // ── Structural transforms ─────────────────────────────────────────────

    /// <summary>
    /// Replaces <c>{num} over {den}</c> with <c>\frac{num}{den}</c>.
    /// Handles both lowercase <c>over</c> (HML) and HWPX's <c>OVER</c>
    /// (already normalized to lowercase by the token map).
    /// </summary>
    private static string ReplaceFrac(string s)
    {
        const string keyword = " over ";
        while (true)
        {
            var cursor = s.IndexOf(keyword, StringComparison.Ordinal);
            if (cursor < 0) break;
            try
            {
                var (numStart, numEnd) = FindBracketsBackward(s, cursor);
                var before    = s[..numStart];
                var numerator = s[numStart..numEnd];
                var after     = s[(cursor + keyword.Length)..];
                s = before + @"\frac" + numerator + " " + after;
            }
            catch (InvalidOperationException) { break; }
        }
        return s;
    }

    /// <summary>
    /// Replaces <c>root {n} of {expr}</c> with <c>\sqrt[n]{expr}</c>.
    /// </summary>
    private static string ReplaceRootOf(string s)
    {
        const string rootKw = " root ";
        const string ofKw   = " of ";
        while (true)
        {
            var rootPos = s.IndexOf(rootKw, StringComparison.Ordinal);
            if (rootPos < 0) break;
            var ofPos = s.IndexOf(ofKw, rootPos + rootKw.Length, StringComparison.Ordinal);
            if (ofPos < 0) break;
            try
            {
                var (s1, e1) = FindBracketsForward(s, rootPos + 1);
                var (s2, e2) = FindBracketsForward(s, ofPos + 1);
                var n    = s[(s1 + 1)..(e1 - 1)];
                var expr = s[(s2 + 1)..(e2 - 1)];
                s = s[..rootPos] + @" \sqrt[" + n + "]{" + expr + "}" + s[e2..];
            }
            catch (InvalidOperationException) { break; }
        }
        return s;
    }

    /// <summary>
    /// Replaces matrix HULK markers with LaTeX matrix environments.
    /// Tries to remove the wrapping <c>{ }</c> group (HML style) when available.
    /// </summary>
    private static string ReplaceAllMatrix(string s)
    {
        foreach (var (key, (begin, end, removeOuter)) in MatrixConvertMap)
        {
            while (true)
            {
                var cursor = s.IndexOf(key, StringComparison.Ordinal);
                if (cursor < 0) break;
                try
                {
                    var (eStart, eEnd) = FindBracketsForward(s, cursor);
                    var inner = s[(eStart + 1)..(eEnd - 1)]
                        .Replace("#",     @" \\ ")
                        .Replace("&amp;", "&");

                    if (removeOuter)
                    {
                        // Try to consume the surrounding { } group (HML convention)
                        try
                        {
                            var p = cursor - 1;
                            while (p >= 0 && s[p] != '{') p--;
                            if (p >= 0)
                            {
                                var (bStart, bEnd) = FindBracketsForward(s, p);
                                s = s[..bStart] + begin + inner + end + s[bEnd..];
                                continue;
                            }
                        }
                        catch (InvalidOperationException) { /* no outer group — fall through */ }
                    }

                    s = s[..cursor] + begin + inner + end + s[eEnd..];
                }
                catch (InvalidOperationException) { break; }
            }
        }
        return s;
    }

    /// <summary>
    /// Replaces accent HULK markers (<c>hat</c>, <c>vec</c>, <c>bar</c>, …) with
    /// LaTeX accent commands.
    /// <para>
    /// HWPX-specific: does NOT attempt outer-group removal. The original hml-equation-parser
    /// uses <c>_findOutterBrackets</c>, which causes an infinite loop when HWPX double-brace
    /// notation (<c>hat {{x}}</c>) is present. Replacing the marker in-place is correct
    /// for both HML and HWPX formats.
    /// </para>
    /// </summary>
    private static string ReplaceAllBar(string s)
    {
        foreach (var (barKey, barCmd) in BarConvertMap)
        {
            while (true)
            {
                int cursor = s.IndexOf(barKey, StringComparison.Ordinal);
                if (cursor < 0) break;
                try
                {
                    var (eStart, eEnd) = FindBracketsForward(s, cursor);
                    string elem = s[eStart..eEnd];
                    // Replace: HULKXXX {arg} → \cmd{arg}
                    s = s[..cursor] + barCmd + elem + s[eEnd..];
                }
                catch (InvalidOperationException) { break; }
            }
        }
        return s;
    }

    /// <summary>
    /// Replaces OVERBRACE/UNDERBRACE markers with LaTeX brace commands.
    /// </summary>
    private static string ReplaceAllBrace(string s)
    {
        foreach (var (braceKey, braceCmd) in BraceConvertMap)
        {
            while (true)
            {
                int cursor = s.IndexOf(braceKey, StringComparison.Ordinal);
                if (cursor < 0) break;
                try
                {
                    var (s1, e1) = FindBracketsForward(s, cursor);
                    var (s2, e2) = FindBracketsForward(s, e1);
                    string elem1 = s[s1..e1];
                    string elem2 = s[s2..e2];
                    s = s[..cursor] + braceCmd + elem1 + "^" + elem2 + s[e2..];
                }
                catch (InvalidOperationException) { break; }
            }
        }
        return s;
    }

    // ── Regex helpers ─────────────────────────────────────────────────────

    [GeneratedRegex(@"REL\s+(\w+arrow)\s+\{([^}]*)\}\s+\{[^}]*\}", RegexOptions.IgnoreCase)]
    private static partial Regex RelArrowRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpaceRegex();
}
