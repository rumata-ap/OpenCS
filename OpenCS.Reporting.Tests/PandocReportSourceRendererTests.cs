using OpenCS.Reporting.Pandoc;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверяет Pandoc Markdown без запуска внешнего процесса.</summary>
public sealed class PandocReportSourceRendererTests
{
    [Fact]
    public async Task RenderAsync_uses_pandoc_math_and_local_assets()
    {
        string directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var document = new ReportDocument("Отчёт")
                .Add(new ReportCalculationStep
                {
                    StepId = "formula",
                    Title = "Проверка",
                    Formula = ReportMathExpression.Latex(@"R_s / R_b"),
                    Substitution = ReportMathExpression.Inline("x<sub>1</sub><sup>2</sup>"),
                    Result = ReportMathExpression.Latex(@"x = 1{,}20")
                })
                .Add(new ReportCalculationStep
                {
                    StepId = "capacity",
                    Title = "Предельный изгибающий момент",
                    Formula = ReportMathExpression.Latex(@"M_{ult} = R_b b x"),
                    Result = ReportMathExpression.Latex(@"M_{ult} = 112{,}78"),
                    Unit = "кН·м"
                })
                .Add(new ReportFormula("(8.1)", "a<sub>crc</sub> ≤ a<sub>ult</sub>",
                    "12,4 ≤ 15,0", "условие выполнено"))
                .Add(new ReportImage("Схема", "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"10\"><rect width=\"20\" height=\"10\"/></svg>"));

            PandocReportSource source = await new PandocReportSourceRenderer()
                .RenderAsync(document, directory);
            string markdown = await File.ReadAllTextAsync(source.MarkdownPath);

            Assert.Contains("$R_s / R_b$", markdown);
            Assert.Contains("x~1~^2^", markdown);
            Assert.Contains("a~crc~", markdown);
            Assert.DoesNotContain("$$", markdown);
            Assert.DoesNotContain("&lt;sub&gt;", markdown);
            Assert.DoesNotContain("\\(", markdown);
            Assert.DoesNotContain("data:image", markdown);
            Assert.DoesNotContain("*Схема*", markdown);
            Assert.Single(source.AssetPaths);
            Assert.EndsWith(".svg", source.AssetPaths[0]);
            Assert.Contains("<svg", await File.ReadAllTextAsync(source.AssetPaths[0]));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RenderAsync_rejects_non_svg_report_image()
    {
        string directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var document = new ReportDocument("Отчёт")
                .Add(new ReportImage("Сломано", "не SVG"));
            PandocExportException error = await Assert.ThrowsAsync<PandocExportException>(() =>
                new PandocReportSourceRenderer().RenderAsync(document, directory));
            Assert.Equal(PandocExportFailureReason.InvalidAsset, error.Reason);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
