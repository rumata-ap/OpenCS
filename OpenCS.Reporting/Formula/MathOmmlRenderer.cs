using M = DocumentFormat.OpenXml.Math;
using DocumentFormat.OpenXml;

namespace OpenCS.Reporting.Formula;

/// <summary>Рендерит общий математический AST в редактируемый OMML.</summary>
public static class MathOmmlRenderer
{
    /// <summary>Создаёт элемент <c>m:oMath</c>.</summary>
    public static M.OfficeMath Render(MathNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var math = new M.OfficeMath();
        Append(math, node);
        return math;
    }

    static void Append(OpenXmlCompositeElement parent, MathNode node)
    {
        switch (node)
        {
            case MathNode.Sequence sequence:
                foreach (var item in sequence.Items)
                    Append(parent, item);
                break;
            case MathNode.Text text:
                parent.AppendChild(new M.Run(new M.Text(text.Value)
                {
                    Space = SpaceProcessingModeValues.Preserve
                }));
                break;
            case MathNode.Operator op:
                parent.AppendChild(new M.Run(new M.Text(op.Value)
                {
                    Space = SpaceProcessingModeValues.Preserve
                }));
                break;
            case MathNode.Fraction fraction:
            {
                var element = new M.Fraction();
                var numerator = new M.Numerator();
                var denominator = new M.Denominator();
                Append(numerator, fraction.Numerator);
                Append(denominator, fraction.Denominator);
                element.AppendChild(numerator);
                element.AppendChild(denominator);
                parent.AppendChild(element);
                break;
            }
            case MathNode.Radical radical:
            {
                var element = new M.Radical();
                var basis = new M.Base();
                Append(basis, radical.Radicand);
                element.AppendChild(new M.RadicalProperties());
                element.AppendChild(new M.Degree());
                element.AppendChild(basis);
                parent.AppendChild(element);
                break;
            }
            case MathNode.Script script:
                AppendScript(parent, script);
                break;
        }
    }

    static void AppendScript(OpenXmlCompositeElement parent, MathNode.Script script)
    {
        if (script.Subscript != null && script.Superscript != null)
        {
            var element = new M.SubSuperscript();
            var basis = new M.Base();
            var subscript = new M.SubArgument();
            var superscript = new M.SuperArgument();
            Append(basis, script.Base);
            Append(subscript, script.Subscript);
            Append(superscript, script.Superscript);
            element.AppendChild(basis);
            element.AppendChild(subscript);
            element.AppendChild(superscript);
            parent.AppendChild(element);
            return;
        }

        if (script.Subscript != null)
        {
            var element = new M.Subscript();
            var basis = new M.Base();
            var subscript = new M.SubArgument();
            Append(basis, script.Base);
            Append(subscript, script.Subscript);
            element.AppendChild(basis);
            element.AppendChild(subscript);
            parent.AppendChild(element);
            return;
        }

        var superscriptElement = new M.Superscript();
        var superscriptBasis = new M.Base();
        var superscriptArgument = new M.SuperArgument();
        Append(superscriptBasis, script.Base);
        Append(superscriptArgument, script.Superscript ?? new MathNode.Text(string.Empty));
        superscriptElement.AppendChild(superscriptBasis);
        superscriptElement.AppendChild(superscriptArgument);
        parent.AppendChild(superscriptElement);
    }
}
