using CScore;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;

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
                _ => "CalcResultError"
            };
            string done = string.Format(Loc.S(statusKey), task.Tag);
            app.LogService.Info(done);
            onResultsChanged?.Invoke();
            if (navigateToResult)
                app.CurrentPage = UiServices.Pages.CreateCalcResultPage(result);
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
            UiServices.Dialogs.ShowErrorText(ex.Message, Loc.S("Error"));
        }
    }

    static bool TryResolve(AppViewModel app, CalcTask ct,
        out CrossSection section, out LoadItem? fi)
    {
        section = null!;
        fi = null;

        var sec = app.CrossSections.FirstOrDefault(s => s.Id == ct.SectionId);
        if (sec == null)
        {
            UiServices.Dialogs.ShowErrorText(Loc.S("CalcTaskSectionNotFound"), Loc.S("Error"));
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
                UiServices.Dialogs.ShowErrorText(Loc.S("CalcTaskForceItemNotFound"), Loc.S("Error"));
                return false;
            }
            return true;
        }

        if (CalcTaskForceHelper.UsesDummyForceItem(ct))
        {
            fi = CalcTaskForceHelper.ResolveOptionalForceItem(ct, app.BarForceSets);
            return true;
        }

        UiServices.Dialogs.ShowErrorText(Loc.S("CalcTaskForceItemNotFound"), Loc.S("Error"));
        return false;
    }
}
