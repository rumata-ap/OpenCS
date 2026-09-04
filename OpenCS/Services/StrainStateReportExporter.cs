using System.Windows;
using CScore;
using OpenCS.Reporting;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using OpenCS.Views;

namespace OpenCS.Services;

/// <summary>Экспорт отчёта strain_state по результату расчёта из контекстного меню дерева
/// результатов — не требует открытого <see cref="CalcResultView"/>.</summary>
static class StrainStateReportExporter
{
    public static async Task ExportAsync(AppViewModel app, CalcResult? result)
    {
        if (result?.TaskKind != "strain_state") return;

        var task = app.CalcTasks.FirstOrDefault(t => t.Id == result.TaskId);
        var section = task != null ? app.CrossSections.FirstOrDefault(s => s.Id == task.SectionId) : null;
        if (task == null || section == null) return;

        section.ResolveAndBuildDiagramms(app.CalcSettings.Sp63DescEtaMin,
            pool: app.Diagrams,
            rebarDifferentialDiagram: app.CalcSettings.RebarDifferentialDiagram, ekbEtaMin: app.CalcSettings.EkbDescEtaMin);

        var k = CalcResultView.ParseKurvature(result.DataJson);
        var settings = app.CalcSettings;
        bool ten = settings.ResolveConcreteTension(task.CalcType);
        section.SetEps(k, task.CalcType, ten);

        var stressPlot = new SectionPlotVM(section, k, task.CalcType, SectionPlotMode.Stress, settings, ten);
        var strainPlot = new SectionPlotVM(section, k, task.CalcType, SectionPlotMode.Strain, settings, ten);

        var outputPath = app.FileDialogService.SaveFile(
            Loc.S("ReportExportFileFilter"),
            Loc.S("ReportExportDefaultExtension"),
            Loc.S("ReportExportDialogTitle"));
        if (string.IsNullOrWhiteSpace(outputPath)) return;

        try
        {
            var svgExporter = new SectionStateSvgExporter();
            var document = app.ReportProviders.Resolve(task).Build(new ReportContext(
                task,
                result,
                section,
                new Dictionary<string, string>
                {
                    ["stress"] = svgExporter.Render(stressPlot, Loc.S("ReportStressMapTitle")),
                    ["strain"] = svgExporter.Render(strainPlot, Loc.S("ReportStrainMapTitle"))
                }));

            var service = new ReportExportService(
                pdfConverter: app.WebRenderer,
                svgRasterizer: app.WebRenderer);
            await service.ExportAsync(document, outputPath);

            MessageBox.Show(Loc.S("ReportExportSuccess"), Loc.S("ReportExportInfo"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (ReportRenderingUnavailableException ex)
        {
            // Локализованный текст собирается здесь, в WPF-слое: portable-библиотека
            // отдаёт только Reason и технический debugMessage для лога.
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
            // Внутренняя отмена при закрытии приложения — не ошибка экспорта, молча выходим.
        }
        catch (ObjectDisposedException)
        {
            // Движок уже освобождён закрывающимся окном — экспорт больше не актуален.
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(Loc.S("ReportExportError"), ex.Message), Loc.S("ReportExportErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
