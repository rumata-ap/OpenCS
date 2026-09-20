using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверяет новый нейтральный блок пошаговой формулы.</summary>
public sealed class ReportCalculationStepRendererTests
{
    [Fact]
    public void Calculation_step_is_rendered_as_math_in_html_and_markdown()
    {
        var document = new ReportDocument("Тест")
            .Add(new ReportCalculationStep
            {
                StepId = "test.capacity",
                Reference = "8.1.8",
                Title = "Высота сжатой зоны",
                Formula = ReportMathExpression.Latex(@"x = \frac{R_s A_s}{R_b b}"),
                Substitution = ReportMathExpression.Latex(@"x = \frac{4434 \cdot 12}{117.2 \cdot 30}"),
                Result = ReportMathExpression.Latex("x = 11.65"),
                Unit = "см",
                Status = ReportCalculationStatus.Passed,
                StatusText = "выполнено"
            });

        string html = new HtmlReportRenderer().Render(document);
        string markdown = new MarkdownReportRenderer().Render(document);

        Assert.Contains("math-fraction", html);
        Assert.Contains("выполнено", html);
        Assert.Contains(@"\(", markdown);
        Assert.Contains(@"\frac", markdown);
    }
}
