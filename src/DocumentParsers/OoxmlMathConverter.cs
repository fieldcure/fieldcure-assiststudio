using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Math;

namespace FieldCure.DocumentParsers;

/// <summary>
/// Converts OOXML Math (<c>m:oMath</c>) elements to LaTeX notation.
/// Greek letters and other Unicode characters are preserved as-is;
/// only structural elements (fractions, subscripts, etc.) are converted to LaTeX commands.
/// </summary>
public static class OoxmlMathConverter
{
    /// <summary>
    /// Converts an OOXML math element to a LaTeX string.
    /// </summary>
    public static string ToLaTeX(OpenXmlElement element)
    {
        var sb = new StringBuilder();
        ConvertElement(element, sb);
        return sb.ToString().Trim();
    }

    private static void ConvertElement(OpenXmlElement element, StringBuilder sb)
    {
        switch (element)
        {
            // Fraction: m:f → \frac{num}{den}
            case Fraction f:
                sb.Append(@"\frac{");
                ConvertChildren(f.Numerator, sb);
                sb.Append("}{");
                ConvertChildren(f.Denominator, sb);
                sb.Append('}');
                break;

            // Subscript: m:sSub → {base}_{sub}
            case Subscript sub:
                ConvertChildren(sub.Base, sb);
                sb.Append("_{");
                ConvertChildren(sub.SubArgument, sb);
                sb.Append('}');
                break;

            // Superscript: m:sSup → {base}^{sup}
            case Superscript sup:
                ConvertChildren(sup.Base, sb);
                sb.Append("^{");
                ConvertChildren(sup.SuperArgument, sb);
                sb.Append('}');
                break;

            // SubSuperscript: m:sSubSup → {base}_{sub}^{sup}
            case SubSuperscript subSup:
                ConvertChildren(subSup.Base, sb);
                sb.Append("_{");
                ConvertChildren(subSup.SubArgument, sb);
                sb.Append("}^{");
                ConvertChildren(subSup.SuperArgument, sb);
                sb.Append('}');
                break;

            // Delimiter (parentheses): m:d → \left( ... \right)
            case Delimiter d:
                var beginChar = d.DelimiterProperties?
                    .GetFirstChild<BeginChar>()?.Val?.Value ?? "(";
                var endChar = d.DelimiterProperties?
                    .GetFirstChild<EndChar>()?.Val?.Value ?? ")";
                sb.Append($@"\left{beginChar}");
                foreach (var baseEl in d.Elements<Base>())
                    ConvertChildren(baseEl, sb);
                sb.Append($@"\right{endChar}");
                break;

            // N-ary operator (sum, integral, etc.): m:nary
            case Nary nary:
                var chr = nary.NaryProperties?
                    .GetFirstChild<AccentChar>()?.Val?.Value;
                var op = chr switch
                {
                    "∑" => @"\sum",
                    "∏" => @"\prod",
                    "∫" => @"\int",
                    "∮" => @"\oint",
                    _ => chr ?? @"\sum"
                };
                sb.Append(op);
                var subArg = nary.SubArgument;
                var supArg = nary.SuperArgument;
                if (subArg is not null && subArg.HasChildren)
                {
                    sb.Append("_{");
                    ConvertChildren(subArg, sb);
                    sb.Append('}');
                }
                if (supArg is not null && supArg.HasChildren)
                {
                    sb.Append("^{");
                    ConvertChildren(supArg, sb);
                    sb.Append('}');
                }
                sb.Append(' ');
                var naryBase = nary.Base;
                if (naryBase is not null)
                    ConvertChildren(naryBase, sb);
                break;

            // Math run: m:r → extract text from m:t
            case Run run:
                foreach (var t in run.Elements<Text>())
                    sb.Append(t.Text);
                break;

            // OfficeMath container: recurse into children
            case OfficeMath:
            case Paragraph:
            case Base:
                ConvertChildren(element, sb);
                break;

            // Skip property elements
            case FractionProperties:
            case SubscriptProperties:
            case SuperscriptProperties:
            case SubSuperscriptProperties:
            case DelimiterProperties:
            case NaryProperties:
            case RunProperties:
            case MathProperties:
            case ControlProperties:
                break;

            // Default: recurse
            default:
                ConvertChildren(element, sb);
                break;
        }
    }

    private static void ConvertChildren(OpenXmlElement? parent, StringBuilder sb)
    {
        if (parent is null) return;
        foreach (var child in parent.ChildElements)
            ConvertElement(child, sb);
    }
}
