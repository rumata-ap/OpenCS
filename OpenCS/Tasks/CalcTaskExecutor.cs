using CScore;
using OpenCS.Services;
using OpenCS.Utilites;

namespace OpenCS.Tasks;

/// <summary>
/// Единая точка асинхронного запуска расчётных задач с Busy/Cancel/Progress.
/// </summary>
public static class CalcTaskExecutor
{
    /// <param name="showResult">Показ сохранённого результата (навигация UI); вызывается до снятия занятости.</param>
    public static async Task RunAsync(IProjectContext app, CalcTask task,
        Action? onResultsChanged = null, Action<CalcResult>? showResult = null)
    {
        var busy = app.Busy;
        if (busy.IsBusy) return;

        if (!TryResolve(app, task, out var section, out var fi))
            return;

        var cts = busy.BeginBusyWithCancellation(
            string.Format(Loc.S("CalcTaskRunning"), task.Tag), indeterminate: true);

        var progress = new Progress<CalcTaskProgress>(p =>
            busy.ReportBusyProgress(p.Fraction, p.Message));

        var ctx = new TaskRunContext
        {
            Database = app.Db,
            FireSections = app.FireSections,
            CancellationToken = cts.Token,
            Progress = progress
        };

        try
        {
            var result = await Task.Run(
                () => TaskRunner.Run(task, section, fi!, app.CalcSettings, ctx),
                cts.Token).ConfigureAwait(true);

            if (cts.IsCancellationRequested)
            {
                app.LogService.Warning(Loc.S("CalcTaskCancelled"));
                busy.EndBusy(Loc.S("CalcTaskCancelled"));
                return;
            }

            busy.ReportBusyProgress(1.0, Loc.S("CalcTaskSavingResult"));
            app.Db.SaveCalcResult(result);

            var statusKey = result.Status switch
            {
                "ok" => "CalcResultOk",
                "not_converged" => "CalcResultNotConverged",
                "partial" => "CalcResultPartial",
                "not_passed" => "CalcResultNotPassed",
                "not_applicable" => "CalcResultNotApplicable",
                _ => "CalcResultError"
            };
            string done = string.Format(Loc.S(statusKey), task.Tag);
            LogLevel level = CalcResultLogHelper.ResolveLevel(result);
            if (level == LogLevel.Error)
            {
                string detail = CalcResultLogHelper.ExtractDetail(result);
                if (!string.IsNullOrWhiteSpace(detail))
                    done = string.Format(Loc.S("CalcResultErrorWithDetail"), task.Tag, detail);
                app.LogService.Error(done);
            }
            else if (level == LogLevel.Warning)
            {
                app.LogService.Warning(done);
            }
            else
            {
                app.LogService.Info(done);
            }
            if (TryGetThermalWarning(result, out string thermalWarning))
                app.LogService.Warning($"{task.Tag}: {thermalWarning}");
            onResultsChanged?.Invoke();
            showResult?.Invoke(result);
            busy.EndBusy(done);
        }
        catch (OperationCanceledException)
        {
            app.LogService.Warning(Loc.S("CalcTaskCancelled"));
            busy.EndBusy(Loc.S("CalcTaskCancelled"));
        }
        catch (Exception ex)
        {
            busy.EndBusy();
            app.Notifier.Error(ex.Message);
        }
    }

    /// <summary>Предупреждение огневой задачи об устаревшем тепловом расчёте (поле thermal_warning).</summary>
    static bool TryGetThermalWarning(CalcResult result, out string warning)
    {
        warning = "";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(result.DataJson);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && doc.RootElement.TryGetProperty("thermal_warning", out var w)
                && w.ValueKind == System.Text.Json.JsonValueKind.String)
                warning = w.GetString() ?? "";
        }
        catch (System.Text.Json.JsonException) { }
        return warning.Length > 0;
    }

    static bool TryResolve(IProjectContext app, CalcTask ct,
        out CrossSection section, out LoadItem? fi)
    {
        section = null!;
        fi = null;
        // Удалённая задача должна дать пояснение даже без сохранившегося сечения/нагрузки.
        if (ct.Kind == "steel_torsion") return true;

        // Оболочечные задачи используют PlateSection и не должны проходить
        // через реестр стержневых CrossSection.
        if (ct.Kind is "shell_simpl_wa_sls" or "shell_simpl_wa_uls"
            or "shell_simpl_capri_sls" or "shell_simpl_capri_uls"
            or "shell_simpl_wa_sls_batch" or "shell_simpl_wa_uls_batch"
            or "shell_simpl_capri_sls_batch" or "shell_simpl_capri_uls_batch"
            or "shell_strain_state" or "shell_strain_state_batch"
            or "shell_layered_uls" or "shell_layered_uls_batch"
            or "shell_layered_sls" or "shell_layered_sls_batch")
        {
            var plate = app.PlateSections.FirstOrDefault(s => s.Id == ct.SectionId);
            if (plate == null)
            {
                app.Notifier.Error(Loc.S("CalcTaskSectionNotFound"));
                return false;
            }

            fi = CalcTaskForceHelper.ResolveOptionalForceItem(ct, app.BarForceSets);
            return true;
        }

        var sec = app.CrossSections.FirstOrDefault(s => s.Id == ct.SectionId);
        if (sec == null)
        {
            app.Notifier.Error(Loc.S("CalcTaskSectionNotFound"));
            return false;
        }
        section = sec;

        var fs = app.BarForceSets.FirstOrDefault(f => f.Id == ct.ForceSetId);
        fi = fs?.Items.FirstOrDefault(i => i.Id == ct.ForceItemId);
        if (fi != null) return true;

        if (CalcTaskForceHelper.UsesManualForces(ct))
        {
            fi = CalcTaskForceHelper.ResolveSingleForces(ct, app.BarForceSets);
            if (fi == null)
            {
                app.Notifier.Error(Loc.S("CalcTaskForceItemNotFound"));
                return false;
            }
            return true;
        }

        // Стальной задаче без ручных усилий строка набора обязательна: пустой LoadItem превратил бы
        // удалённую строку в нулевые усилия и «пройденную» проверку одной гибкости.
        if (CScore.Sp16.Sp16Checker.ParseKind(ct.Kind) is { } steelKind
            && steelKind != CScore.Sp16.Sp16TaskKind.Constructive
            && CScore.Sp16.SteelDesignParams.Parse(ct.ParamsJson).ManualForces == null)
        {
            app.Notifier.Error(Loc.S("CalcTaskForceItemNotFound"));
            return false;
        }

        if (CalcTaskForceHelper.UsesDummyForceItem(ct))
        {
            fi = CalcTaskForceHelper.ResolveOptionalForceItem(ct, app.BarForceSets);
            return true;
        }

        app.Notifier.Error(Loc.S("CalcTaskForceItemNotFound"));
        return false;
    }
}
