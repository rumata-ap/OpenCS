using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CScore;
using OpenCS.Reporting;
using OpenCS.Services;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Сквозная проверка: реальный документ strain_state с настоящей SVG-картой НДС
/// проходит все четыре ветки экспорта.</summary>
[Collection("WebView2")]
public sealed class ReportExportAcceptanceTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("opencs-acceptance-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    // Тот же JSON-контракт, что в OpenCS.Reporting.Tests/StrainStateReportProviderTests.cs.
    const string DataJson = """
        {
          "converged": true, "iterations": 4, "residual": 0.01,
          "e0": -0.0002, "ky": 0.001, "kz": -0.002,
          "N_target": 100, "Mx_target": 20, "My_target": -30,
          "N_result": 100, "Mx_result": 20, "My_result": -30,
          "formula_version": "SP63.13330.2021/8.1",
          "stiffness": { "source": "contour", "d11": 11, "d12": 12, "d13": 13, "d21": 12, "d22": 22, "d23": 23, "d31": 13, "d32": 23, "d33": 33 },
          "extrema": { "eps_b_min": -0.001, "eps_b_max": 0.002, "eps_s_min": -0.003, "eps_s_max": 0.004 },
          "rebar": [
            { "num": 1, "x_mm": -120, "y_mm": -180, "eps": 0.0012, "sigma_mpa": 240 },
            { "num": 2, "x_mm": 120, "y_mm": -180, "eps": 0.0011, "sigma_mpa": 220 }
          ]
        }
        """;

    const string LimitMomentDataJson = """
        {"solver_method":"fast","converged":true,"iterations":9,"newton_iterations":9,
         "factor":1.545731,"utilization":0.646943,"governing":"concrete",
         "N_target":0,"Mx_target":-50,"My_target":0,
         "N_limit":0,"Mx_limit":-77.2865,"My_limit":0,
         "e0":0.00985834,"ky":-0.08905561,"kz":0,
         "eps_contour_min":-0.0035,"eps_cu":-0.0035,
         "eps_rebar_max":0.01947635,"eps_su":0.025,
         "N_result":0,"Mx_result":-77.2865,"My_result":0,"eta":null}
        """;

    static ReportDocument BuildDocument()
    {
        // "|" в теге проверяет экранирование ячеек GFM в Markdown-ветке.
        var task = new CalcTask { Id = 7, Kind = "strain_state", Tag = "Колонна | ось А", CalcType = CalcType.C };
        var result = new CalcResult
        {
            TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
            Status = "ok", DataJson = DataJson
        };
        // Реальное сечение с посчитанным НДС, а не пустое: на пустом карта состоит из
        // одних осей, и тест не отличил бы «карты отрисованы» от «карты пустые».
        var section = ReportFixtures.BuildBeam();
        var k = new Kurvature { e0 = 0.001, ky = -0.005, kz = 0 };
        section.SetEps(k, CalcType.C);

        var plot = new SectionPlotVM(section, k, CalcType.C, SectionPlotMode.Strain);
        var exporter = new SectionStateSvgExporter();

        return new StrainStateReportProvider().Build(new ReportContext(task, result, section,
            new Dictionary<string, string>
            {
                ["stress"] = exporter.Render(plot, "Карта напряжений σ"),
                ["strain"] = exporter.Render(plot, "Карта деформаций ε")
            }));
    }

    static ReportDocument BuildLimitMomentDocument()
    {
        var task = new CalcTask { Id = 438, Kind = "limit_moment", Tag = "Балка 18", CalcType = CalcType.C };
        var result = new CalcResult
        {
            TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag,
            Status = "ok", DataJson = LimitMomentDataJson
        };
        var section = ReportFixtures.BuildBeam();
        var provider = new LimitForceReportProvider();
        var registry = new ReportProviderRegistry([provider]);
        Assert.True(registry.TryResolve(task, out var resolved));

        var images = new Dictionary<string, string>();
        var svgExporter = new SectionStateSvgExporter();
        foreach (var request in resolved.DescribeImages(task, result))
        {
            section.SetEps(request.Plane, request.Calc, ten: false);
            var mode = request.Mode == ReportImageMode.Stress
                ? SectionPlotMode.Stress
                : SectionPlotMode.Strain;
            var plot = new SectionPlotVM(section, request.Plane, request.Calc, mode);
            images[request.Key] = svgExporter.Render(plot, request.Title);
        }
        return resolved.Build(new ReportContext(task, result, section, images));
    }

    [Fact]
    public async Task Export_HtmlAndMarkdown_WorkWithoutWebView2()
    {
        var document = BuildDocument();
        var service = new ReportExportService();

        string html = Path.Combine(_dir, "report.html");
        string md = Path.Combine(_dir, "report.md");
        await service.ExportAsync(document, html);
        await service.ExportAsync(document, md);

        string htmlText = await File.ReadAllTextAsync(html);
        Assert.Contains("<sub>", htmlText);
        Assert.Contains("data:image/svg+xml;base64,", htmlText);
        Assert.Contains("<thead><tr><th>Параметр</th><th>Значение</th></tr></thead>", htmlText);

        string mdText = await File.ReadAllTextAsync(md);
        Assert.Contains("Колонна \\| ось А", mdText);
        Assert.Contains("![", mdText);
        Assert.DoesNotContain("\r\n\r\n\r\n", mdText);
    }

    [Fact]
    public async Task Export_LimitMoment_HtmlAndMarkdown_WorkWithoutWebView2()
    {
        var document = BuildLimitMomentDocument();
        var service = new ReportExportService();
        string html = Path.Combine(_dir, "limit-moment.html");
        string md = Path.Combine(_dir, "limit-moment.md");
        await service.ExportAsync(document, html);
        await service.ExportAsync(document, md);

        string htmlText = await File.ReadAllTextAsync(html);
        Assert.Contains("77.2865", htmlText);
        Assert.Contains("data:image/svg+xml;base64,", htmlText);
        Assert.Contains("исчерпание по бетону сжатой зоны", htmlText);
        string mdText = await File.ReadAllTextAsync(md);
        Assert.Contains("77.2865", mdText);
        Assert.Contains("![", mdText);
    }

    [SkippableFact]
    public void Export_DocxAndPdf_ProduceValidFiles()
    {
        string docx = Path.Combine(_dir, "report.docx");
        string pdf = Path.Combine(_dir, "report.pdf");
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                // Application не создаётся (он один на AppDomain), а диспетчер
                // передаётся рендереру явно: Application.Current мог остаться от другого
                // теста и указывать на уже завершившийся поток.
                var pump = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(pump));
                async Task RunAsync()
                {
                    await using var renderer = new WebView2ReportRenderer(
                        Path.Combine(_dir, "wv2-user-data"), pump);
                    var service = new ReportExportService(
                        pdfConverter: renderer, svgRasterizer: renderer);
                    var document = BuildDocument();
                    await service.ExportAsync(document, docx);
                    await service.ExportAsync(document, pdf);
                }
                _ = RunAsync().ContinueWith(t =>
                {
                    if (t.IsFaulted) error = t.Exception?.GetBaseException();
                    pump.InvokeShutdown();
                }, TaskScheduler.FromCurrentSynchronizationContext());
                Dispatcher.Run();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(2)))
            throw new TimeoutException("Экспорт DOCX/PDF не завершился за 2 минуты.");

        Skip.If((error as ReportRenderingUnavailableException)?.Reason
                == ReportRenderingFailureReason.RuntimeMissing,
            "WebView2 Runtime не установлен на этой машине.");
        Assert.Null(error);

        byte[] docxBytes = File.ReadAllBytes(docx);
        Assert.Equal([0x50, 0x4B], docxBytes.Take(2).ToArray());
        using (var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(docx, false))
        {
            Assert.NotEmpty(package.MainDocumentPart!.ImageParts);
            foreach (var image in package.MainDocumentPart.ImageParts)
            {
                using var stream = image.GetStream();
                Assert.True(stream.Length > 2_000,
                    "Растеризованная карта подозрительно мала — вероятно, отрисовалась пустой.");
            }
            Assert.Contains("Колонна | ось А", package.MainDocumentPart!.Document!.InnerText);
        }

        byte[] pdfBytes = File.ReadAllBytes(pdf);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdfBytes, 0, 5));
        Assert.True(pdfBytes.Length > 10_000, "PDF подозрительно мал — вероятно, карты не отрисовались.");
    }

    [SkippableFact]
    public void Export_LimitMoment_DocxAndPdf_ProduceValidFiles()
    {
        string docx = Path.Combine(_dir, "limit-moment.docx");
        string pdf = Path.Combine(_dir, "limit-moment.pdf");
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                var pump = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(pump));
                async Task RunAsync()
                {
                    await using var renderer = new WebView2ReportRenderer(
                        Path.Combine(_dir, "limit-moment-wv2-user-data"), pump);
                    var service = new ReportExportService(
                        pdfConverter: renderer, svgRasterizer: renderer);
                    var document = BuildLimitMomentDocument();
                    await service.ExportAsync(document, docx);
                    await service.ExportAsync(document, pdf);
                }
                _ = RunAsync().ContinueWith(t =>
                {
                    if (t.IsFaulted) error = t.Exception?.GetBaseException();
                    pump.InvokeShutdown();
                }, TaskScheduler.FromCurrentSynchronizationContext());
                Dispatcher.Run();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(2)))
            throw new TimeoutException("Экспорт limit_moment DOCX/PDF не завершился за 2 минуты.");

        Skip.If((error as ReportRenderingUnavailableException)?.Reason
                == ReportRenderingFailureReason.RuntimeMissing,
            "WebView2 Runtime не установлен на этой машине.");
        Assert.Null(error);

        byte[] docxBytes = File.ReadAllBytes(docx);
        Assert.Equal([0x50, 0x4B], docxBytes.Take(2).ToArray());
        using (var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(docx, false))
        {
            Assert.True(package.MainDocumentPart!.ImageParts.Count() >= 2);
            Assert.Contains("77.2865", package.MainDocumentPart!.Document!.InnerText);
        }
        byte[] pdfBytes = File.ReadAllBytes(pdf);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdfBytes, 0, 5));
        Assert.True(pdfBytes.Length > 10_000, "PDF limit_moment подозрительно мал.");
    }
}
