using System.Windows;
using CScore;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.Views;

namespace OpenCS.Tasks;

/// <summary>
/// Единая точка асинхронного запуска расчётных задач с Busy/Cancel/Progress.
/// </summary>
public static class CalcTaskExecutor
{
    public static async Task RunAsync(AppViewModel app, CalcTask task,
        Action? onResultsChanged = null, bool navigateToResult = true)
    {
        if (app.IsBusy) return;

        if (!TryResolve(app, task, out var section, out var fi))
            return;

        var cts = app.BeginBusyWithCancellation(
            string.Format(Loc.S("CalcTaskRunning"), task.Tag), indeterminate: true);

        var progress = new Progress<CalcTaskProgress>(p =>
            app.ReportBusyProgress(p.Fraction, p.Message));

        var ctx = new TaskRunContext
        {
            Database = app.db,
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
                app.EndBusy(Loc.S("CalcTaskCancelled"));
                return;
            }

            app.ReportBusyProgress(1.0, Loc.S("CalcTaskSavingResult"));
            app.db.SaveCalcResult(result);

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
            if (navigateToResult)
                app.CurrentPage = new CalcResultView(result, app);
            app.EndBusy(done);
        }
        catch (OperationCanceledException)
        {
            app.LogService.Warning(Loc.S("CalcTaskCancelled"));
            app.EndBusy(Loc.S("CalcTaskCancelled"));
        }
        catch (Exception ex)
        {
            app.EndBusy();
            MessageBox.Show(ex.Message, Loc.S("Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
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

    static bool TryResolve(AppViewModel app, CalcTask ct,
        out CrossSection section, out LoadItem? fi)
    {
        section = null!;
        fi = null;

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
                MessageBox.Show(Loc.S("CalcTaskSectionNotFound"), Loc.S("Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            fi = CalcTaskForceHelper.ResolveOptionalForceItem(ct, app.BarForceSets);
            return true;
        }

        var sec = app.CrossSections.FirstOrDefault(s => s.Id == ct.SectionId);
        if (sec == null)
        {
            MessageBox.Show(Loc.S("CalcTaskSectionNotFound"), Loc.S("Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(Loc.S("CalcTaskForceItemNotFound"), Loc.S("Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            return true;
        }

        if (CalcTaskForceHelper.UsesDummyForceItem(ct))
        {
            fi = CalcTaskForceHelper.ResolveOptionalForceItem(ct, app.BarForceSets);
            return true;
        }

        MessageBox.Show(Loc.S("CalcTaskForceItemNotFound"), Loc.S("Error"),
            MessageBoxButton.OK, MessageBoxImage.Error);
        return false;
    }
}
