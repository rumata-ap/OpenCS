using System.IO.Compression;
using OpenCS.Reporting;
using Xunit;

namespace OpenCS.Reporting.Tests;

public sealed class CalcpadCliIntegrationTests
{
    [Fact]
    public async Task Runner_ExportsPdfAndDocx_WhenCliIsConfigured()
    {
        var runner = new CalcpadCliRunner();
        string? cli = runner.ResolveExecutable();
        if (string.IsNullOrWhiteSpace(cli))
            return;

        string workDirectory = Path.Combine(Path.GetTempPath(), "OpenCS-report-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);
        try
        {
            var document = new ReportDocument("Интеграционный отчёт")
                .Add(new ReportHeading(1, "Проверка CalcpadCE"))
                .Add(new ReportFormula("(8.42)", "D11 = Σ(E·A·y²)", "D11 = 123", "123 кН·м²"));

            document.Add(new ReportImage("Карта деформаций", "<svg width=\"900\" height=\"650\"><rect width=\"900\" height=\"650\" fill=\"white\"/></svg>"));

            var service = new ReportExportService(
                calcpad: new CalcpadCliRunner(cli, TimeSpan.FromSeconds(60)));
            string pdf = Path.Combine(workDirectory, "report.pdf");
            string docx = Path.Combine(workDirectory, "report.docx");

            await service.ExportAsync(document, pdf);
            await service.ExportAsync(document, docx);

            Assert.True(File.Exists(pdf) && new FileInfo(pdf).Length > 5);
            Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf), 0, 5));
            using var archive = ZipFile.OpenRead(docx);
            Assert.Contains(archive.Entries, entry => entry.FullName == "[Content_Types].xml");
            Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase));
            var documentXml = archive.GetEntry("word/document.xml");
            Assert.NotNull(documentXml);
            using var reader = new StreamReader(documentXml!.Open());
            var xml = await reader.ReadToEndAsync();
            Assert.Contains("cx=\"8572500\"", xml);
            Assert.Contains("cy=\"6191250\"", xml);
            Assert.DoesNotContain("cx=\"0\"", xml);
            Assert.DoesNotContain("cy=\"0\"", xml);
        }
        finally
        {
            try { Directory.Delete(workDirectory, recursive: true); } catch { }
        }
    }
}
