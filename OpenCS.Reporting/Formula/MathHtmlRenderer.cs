using System.Net;
using System.Text;

namespace OpenCS.Reporting.Formula;

/// <summary>Рендерит AST математического выражения в безопасный HTML-фрагмент.</summary>
public static class MathHtmlRenderer
{
    /// <summary>Строит HTML без скриптов и внешних ресурсов.</summary>
    public static string Render(MathNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var html = new StringBuilder();
        Append(html, node);
        return html.ToString();
    }

    static void Append(StringBuilder html, MathNode node)
    {
        switch (node)
        {
            case MathNode.Sequence sequence:
                foreach (var item in sequence.Items) Append(html, item);
                break;
            case MathNode.Text text:
                html.Append(WebUtility.HtmlEncode(text.Value));
                break;
            case MathNode.Operator op:
                html.Append(WebUtility.HtmlEncode(op.Value));
                break;
            case MathNode.Fraction fraction:
                html.Append("<span class=\"math-fraction\"><span class=\"math-numerator\">");
                Append(html, fraction.Numerator);
                html.Append("</span><span class=\"math-denominator\">");
                Append(html, fraction.Denominator);
                html.Append("</span></span>");
                break;
            case MathNode.Radical radical:
                html.Append("<span class=\"math-radical\">√<span>");
                Append(html, radical.Radicand);
                html.Append("</span></span>");
                break;
            case MathNode.Script script:
                html.Append("<span class=\"math-script\"><span class=\"math-base\">");
                Append(html, script.Base);
                html.Append("</span>");
                if (script.Subscript != null)
                {
                    html.Append("<sub>");
                    Append(html, script.Subscript);
                    html.Append("</sub>");
                }
                if (script.Superscript != null)
                {
                    html.Append("<sup>");
                    Append(html, script.Superscript);
                    html.Append("</sup>");
                }
                html.Append("</span>");
                break;
        }
    }
}
