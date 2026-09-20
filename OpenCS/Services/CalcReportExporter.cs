using System.Windows;
using CScore;
using OpenCS.Reporting;
using OpenCS.Reporting.Pandoc;
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
        var plateSection = task.Kind == "shell_layered_sls"
            ? app.PlateSections.FirstOrDefault(candidate => candidate.Id == task.SectionId)
            : null;
        if (section == null && plateSection == null) return;

        CScore.ParametricRc.ParametricRcSectionDefinition? parametricSection = null;
        string? parametricSectionWarning = null;
        if (section != null && (task.Kind is "sp63_normal" or "shear_inclined"))
        {
            var parametricService = new ParametricRcSectionProjectService(app.db);
            var state = parametricService.GetState(section);
            if (state.LoadStatus == ParametricRcDefinitionLoadStatus.Supported && !state.IsStale &&
                parametricService.TryGetDefinition(section, out var definition))
                parametricSection = definition;
            else
                parametricSectionWarning = state.LoadStatus switch
                {
                    ParametricRcDefinitionLoadStatus.Missing =>
                        "Параметрический источник сечения не найден — показана универсальная схема.",
                    ParametricRcDefinitionLoadStatus.InvalidJson =>
                        "Параметрический источник сечения повреждён — показана универсальная схема.",
                    ParametricRcDefinitionLoadStatus.UnsupportedFutureVersion =>
                        "Версия параметрического источника сечения не поддерживается — показана универсальная схема.",
                    _ when state.IsStale =>
                        "Параметрический источник устарел относительно геометрии — показана универсальная схема.",
                    _ => "Параметрический источник сечения недоступен — показана универсальная схема."
                };
        }

        section?.ResolveAndBuildDiagramms(app.CalcSettings.Sp63DescEtaMin,
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
                if (section == null) continue;
                bool ten = settings.ResolveConcreteTension(request.Calc);
                section.SetEps(request.Plane, request.Calc, ten);
                var mode = request.Mode == ReportImageMode.Stress
                    ? SectionPlotMode.Stress
                    : SectionPlotMode.Strain;
                var plot = new SectionPlotVM(section, request.Plane, request.Calc,
                    mode, settings, ten);
                images[request.Key] = svgExporter.Render(plot, request.Title);
            }

            var document = provider.Build(new ReportContext(task, result, section, plateSection,
                images, parametricSection, parametricSectionWarning));
            var service = new ReportExportService();
            await service.ExportAsync(document, outputPath);

            MessageBox.Show(Loc.S("ReportExportSuccess"), Loc.S("ReportExportInfo"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (PandocExportException ex)
        {
            app.LogService.Error($"Отчёт Pandoc: {ex.Reason} — {ex.Message}");

            string message = ex.Reason is PandocExportFailureReason.BundleMissing or
                PandocExportFailureReason.BundleIntegrity
                ? Loc.S("ReportPandocBundleMissing")
                : ex.Reason == PandocExportFailureReason.TimedOut
                    ? Loc.S("ReportExportTimedOut")
                    : Loc.S("ReportExportFailed");
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
