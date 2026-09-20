using DocumentFormat.OpenXml.Math;
using DocumentFormat.OpenXml.Packaging;
using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверяет, что DOCX содержит OMML, а не только текст формулы.</summary>
public sealed class OpenXmlMathReportTests
{
    [Fact]
    public async Task Calculation_step_is_written_as_editable_omml()
    {
        var document = new ReportDocument("Тест")
            .Add(new ReportCalculationStep
            {
                StepId = "test.capacity",
                Title = "Формула",
                Formula = ReportMathExpression.Latex(@"x = \frac{R_s}{R_b}"),
                Result = ReportMathExpression.Latex("x = 1.2")
            });

        byte[] bytes = await new OpenXmlReportRenderer().RenderAsync(document, null);
        using var stream = new MemoryStream(bytes);
        using var package = WordprocessingDocument.Open(stream, false);
        var body = package.MainDocumentPart?.Document?.Body
            ?? throw new InvalidOperationException("DOCX body is missing.");
        var mathBlocks = body.Descendants<OfficeMath>().ToList();
        Assert.Equal(2, mathBlocks.Count);
        var math = mathBlocks[0];

        Assert.NotEmpty(math.Descendants<Fraction>());
        Assert.NotEmpty(math.Descendants<Subscript>());
        Assert.NotEmpty(math.Descendants<SubArgument>());
        Assert.DoesNotContain(math.Descendants<Subscript>(), element =>
            element.ChildElements.Any(child => child.LocalName == "r"));
    }
}
