using System.IO.Compression;
using OpenCS.Reporting.Pandoc;
using Xunit;

namespace OpenCS.Reporting.Tests;

/// <summary>Проверяет настоящий переносимый комплект Pandoc/Typst, если он собран.</summary>
public sealed class PandocRealExportTests
{
    [Fact]
    public async Task BundledTools_export_docx_odt_rtf_and_pdf()
    {
        string runtimeBase = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "OpenCS", "bin", "Debug", "net9.0-windows"));
        if (!File.Exists(Path.Combine(runtimeBase, "Tools", "Pandoc", "pandoc.exe")))
            return;

        string directory = Directory.CreateTempSubdirectory("opencs-pandoc-real-").FullName;
        try
        {
            var document = new ReportDocument("Проверка отчёта OpenCS")
                .Add(new ReportHeading(1, "Исходные данные")
                )
                .Add(new ReportParagraph("Бетонное сечение и проверка формулы."))
                .Add(new ReportCalculationStep
                {
                    StepId = "formula",
                    Reference = "СП 63.13330",
                    Title = "Несущая способность",
                    Formula = ReportMathExpression.Latex(@"R_s A_s"),
                    Substitution = ReportMathExpression.Latex(@"435 \cdot 1256"),
                    Result = ReportMathExpression.Latex(@"N_{s} = 546{,}4"),
                    Unit = "кН",
                    StatusText = "условие выполнено",
                    Status = ReportCalculationStatus.Passed
                })
                .Add(new ReportPageBreak())
                .Add(new ReportHeading(1, "Схема")
                )
                .Add(new ReportImage("Схема сечения",
                    "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"120\" height=\"80\" viewBox=\"0 0 120 80\"><rect x=\"5\" y=\"5\" width=\"110\" height=\"70\" fill=\"#e8eef5\" stroke=\"#1f2937\"/><circle cx=\"25\" cy=\"60\" r=\"5\" fill=\"#b91c1c\"/><circle cx=\"95\" cy=\"60\" r=\"5\" fill=\"#b91c1c\"/></svg>"));

            var exporter = new PandocReportExporter(runtimeBaseDirectory: runtimeBase);
            string docx = Path.Combine(directory, "report.docx");
            string odt = Path.Combine(directory, "report.odt");
            string rtf = Path.Combine(directory, "report.rtf");
            string pdf = Path.Combine(directory, "report.pdf");
            await exporter.ExportAsync(document, docx);
            await exporter.ExportAsync(document, odt);
            await exporter.ExportAsync(document, rtf);
            await exporter.ExportAsync(document, pdf);

            Assert.True(new FileInfo(docx).Length > 4096);
            Assert.Equal([0x50, 0x4B], (await File.ReadAllBytesAsync(docx)).Take(2).ToArray());
            using (ZipArchive archive = ZipFile.OpenRead(docx))
            {
                ZipArchiveEntry svgEntry = Assert.Single(archive.Entries,
                    entry => entry.FullName.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase)
                        && entry.FullName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase));
                Assert.True(svgEntry.Length > 100);
            }
            using (ZipArchive archive = ZipFile.OpenRead(docx))
            using (StreamReader reader = new(archive.GetEntry("word/document.xml")!.Open()))
                Assert.Contains("<m:oMath", await reader.ReadToEndAsync());

            Assert.True(new FileInfo(odt).Length > 1024);
            Assert.Equal([0x50, 0x4B], (await File.ReadAllBytesAsync(odt)).Take(2).ToArray());
            using (ZipArchive archive = ZipFile.OpenRead(odt))
            {
                Assert.NotNull(archive.GetEntry("mimetype"));
                ZipArchiveEntry content = Assert.Single(archive.Entries,
                    entry => entry.FullName.Equals("content.xml", StringComparison.OrdinalIgnoreCase));
                using StreamReader reader = new(content.Open());
                string contentXml = await reader.ReadToEndAsync();
                Assert.Contains("OpenCS", contentXml);
                Assert.True(archive.Entries.Count(entry =>
                    entry.FullName.StartsWith("Formula-", StringComparison.OrdinalIgnoreCase)
                    && entry.FullName.EndsWith("content.xml", StringComparison.OrdinalIgnoreCase)) >= 3);
                Assert.Contains(archive.Entries, entry =>
                    entry.FullName.StartsWith("Pictures/", StringComparison.OrdinalIgnoreCase));
            }

            string rtfText = await File.ReadAllTextAsync(rtf);
            Assert.True(rtfText.Length > 100);
            Assert.StartsWith(@"{\rtf", rtfText, StringComparison.Ordinal);
            Assert.Contains("546", rtfText);
            Assert.True(new FileInfo(pdf).Length > 100);
            byte[] pdfBytes = await File.ReadAllBytesAsync(pdf);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdfBytes.Take(4).ToArray()));
            Assert.Contains("/Count 2", System.Text.Encoding.ASCII.GetString(pdfBytes));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }
}
