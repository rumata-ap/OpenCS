using System.Windows;
using CScore;
using OpenCS.Reporting;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using OpenCS.Views;

namespace OpenCS.Services;

/// <summary>Экспорт отчёта по любому зарегистрированному виду расчётной задачи.</summary>
static class CalcReportExporter
{
    /// <summary>Строит карты, документирует результат и сохраняет отчёт выбранного формата.</summary>
    public static async Task ExportAsync(AppViewModel app, CalcResult? result)
    {
        if (result == null) return;

        var task = app.CalcTasks.FirstOrDefault(candidate => candidate.Id == result.TaskId);
        if (task == null || !app.ReportProviders.TryResolve(task, out var provider)) return;

        var section = app.CrossSections.FirstOrDefault(candidate => candidate.Id == task.SectionId);
        if (section == null) return;

        section.ResolveAndBuildDiagramms(app.CalcSettings.Sp63DescEtaMin,
            pool: app.Diagrams,
            rebarDifferentialDiagram: app.CalcSettings.RebarDifferentialDiagram,
            ekbEtaMin: app.CalcSettings.EkbDescEtaMin);

        var outputPath = app.FileDialogService.SaveFile(
            Loc.S("ReportExportFileFilter"),
            Loc.S("ReportExportDefaultExtension"),
            Loc.S("ReportExportDialogTitle"));
        if (string.IsNullOrWhiteSpace(outputPath)) return;

        try
        {
            var settings = app.CalcSettings;
            var svgExporter = new SectionStateSvgExporter();
            var images = new Dictionary<string, string>();
            foreach (var request in provider.DescribeImages(task, result))
            {
                bool ten = settings.ResolveConcreteTension(request.Calc);
                section.SetEps(request.Plane, request.Calc, ten);
                var mode = request.Mode == ReportImageMode.Stress
                    ? SectionPlotMode.Stress
                    : SectionPlotMode.Strain;
                var plot = new SectionPlotVM(section, request.Plane, request.Calc,
                    mode, settings, ten);
                images[request.Key] = svgExporter.Render(plot, request.Title);
            }

            var document = provider.Build(new ReportContext(task, result, section, images));
            var service = new ReportExportService(
                pdfConverter: app.WebRenderer,
                svgRasterizer: app.WebRenderer);
            await service.ExportAsync(document, outputPath);

            MessageBox.Show(Loc.S("ReportExportSuccess"), Loc.S("ReportExportInfo"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (ReportRenderingUnavailableException ex)
        {
            app.LogService.Error($"Отчёт: {ex.Reason} — {ex.Message}");

            string message = ex.Reason switch
            {
                ReportRenderingFailureReason.RuntimeMissing =>
                    string.Format(Loc.S("ReportWebView2Missing"), ex.RuntimeDownloadUrl),
                ReportRenderingFailureReason.TimedOut => Loc.S("ReportExportTimedOut"),
                _ => Loc.S("ReportExportFailed")
            };
            MessageBox.Show(message, Loc.S("ReportExportWarning"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (OperationCanceledException)
        {
            // Внутренняя отмена при закрытии приложения — экспорт больше не актуален.
        }
        catch (ObjectDisposedException)
        {
            // Движок уже освобождён закрывающимся окном.
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(Loc.S("ReportExportError"), ex.Message),
                Loc.S("ReportExportErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
