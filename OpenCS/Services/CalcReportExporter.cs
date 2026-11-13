using CScore;
using OpenCS.Reporting;
using OpenCS.Reporting.Pandoc;
using OpenCS.Tasks;
using OpenCS.Utilites;
using OpenCS.ViewModels;

namespace OpenCS.Services;

/// <summary>Экспорт отчёта по любому зарегистрированному виду расчётной задачи.</summary>
static class CalcReportExporter
{
    /// <summary>Строит карты, документирует результат и сохраняет отчёт выбранного формата.</summary>
    public static async Task ExportAsync(IProjectContext app, IFileDialogService fileDialogs, CalcResult? result)
    {
        if (result == null) return;

        var task = app.CalcTasks.FirstOrDefault(candidate => candidate.Id == result.TaskId);
        if (task == null || !app.ReportProviders.TryResolve(task, out var provider)) return;

        // Пластинные задачи (одиночные и пакеты) ссылаются на плитное сечение; стержневое сечение
        // с тем же Id к ним не относится (иначе в отчёте пластины — «Балка 300×600»).
        bool plateTask = task.Kind.StartsWith("shell_", StringComparison.Ordinal);
        var section = plateTask ? null : app.CrossSections.FirstOrDefault(candidate => candidate.Id == task.SectionId);
        var plateSection = plateTask
            ? app.PlateSections.FirstOrDefault(candidate => candidate.Id == task.SectionId)
            : null;
        if (section == null && plateSection == null) return;

        CScore.ParametricRc.ParametricRcSectionDefinition? parametricSection = null;
        string? parametricSectionWarning = null;
        if (section != null && (task.Kind is "sp63_normal" or "shear_inclined"))
        {
            var parametricService = new ParametricRcSectionProjectService(app.Db);
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

        var outputPath = fileDialogs.SaveFile(
            Loc.S("ReportExportFileFilter"),
            Loc.S("ReportExportDefaultExtension"),
            Loc.S("ReportExportDialogTitle"));
        if (string.IsNullOrWhiteSpace(outputPath)) return;

        try
        {
            var settings = app.CalcSettings;
            var svgExporter = new SectionStateSvgExporter();
            Dictionary<string, string> Images(IReportProvider p, CalcTask t, CalcResult r)
            {
                var images = new Dictionary<string, string>();
                foreach (var request in p.DescribeImages(t, r))
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
                return images;
            }

            var context = new ReportContext(task, result, section, plateSection,
                Images(provider, task, result), parametricSection, parametricSectionWarning);
            ReportDocument document;
            if (provider is BatchReportProvider batchProvider)
            {
                var detail = await GoverningRowReportAsync(app, task, result, section, plateSection, Images);
                document = batchProvider.Build(context, detail);
            }
            else
                document = provider.Build(context);
            var service = new ReportExportService();
            await service.ExportAsync(document, outputPath);

            app.Notifier.Info(Loc.S("ReportExportSuccess"), Loc.S("ReportExportInfo"));
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
            app.Notifier.Warning(message, Loc.S("ReportExportWarning"));
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
            app.Notifier.Error(string.Format(Loc.S("ReportExportError"), ex.Message), Loc.S("ReportExportErrorTitle"));
        }
    }

    /// <summary>
    /// Отчёт одиночной задачи по определяющей строке пакета: задача строится фабрикой (в проект не сохраняется),
    /// считается в памяти, отчёт — её поставщиком. null — строки нет, одиночного отчёта для вида нет или расчёт упал
    /// (тогда в отчёте пакета — указание создать задачу по строке).
    /// </summary>
    static async Task<ReportDocument?> GoverningRowReportAsync(IProjectContext app, CalcTask batch, CalcResult result,
        CrossSection? section, PlateSection? plateSection,
        Func<IReportProvider, CalcTask, CalcResult, Dictionary<string, string>> images)
    {
        try
        {
            var data = BatchReportData.Parse(batch.Kind, result.DataJson);
            if (data.GoverningNum is not { } num || !BatchRowTaskFactory.Supports(batch.Kind)) return null;
            string? sectionTag = section?.Tag ?? plateSection?.Tag;
            if (sectionTag == null) return null;

            var single = BatchRowTaskFactory.Create(batch, num, sectionTag, app.ForceSets, app.PlateSections);
            // Задача в проект не сохраняется и номера не получает — в шапке вложенного отчёта
            // был «№ 0»; показываем номер пакета и строку, к которой относится ход вычислений.
            single.Num = batch.Num;
            single.Tag = $"строка № {num} пакета — {single.Tag}";
            if (!app.ReportProviders.TryResolve(single, out var singleProvider) || singleProvider is BatchReportProvider)
                return null;
            var item = app.BarForceSets.FirstOrDefault(fs => fs.Id == single.ForceSetId)?
                .Items.FirstOrDefault(i => i.Id == single.ForceItemId) ?? new LoadItem();
            var ctx = new TaskRunContext { Database = app.Db, FireSections = app.FireSections };
            var settings = app.CalcSettings;
            var singleResult = await Task.Run(() => TaskRunner.Run(single, section!, item, settings, ctx));
            if (singleResult.Status == "error") return null;
            return singleProvider.Build(new ReportContext(single, singleResult, section, plateSection,
                images(singleProvider, single, singleResult)));
        }
        catch (Exception ex)
        {
            app.LogService.Warning($"Отчёт пакета «{batch.Tag}»: ход определяющей строки не построен — {ex.Message}");
            return null;
        }
    }
}
