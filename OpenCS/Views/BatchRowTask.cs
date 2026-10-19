using CScore;
using OpenCS.Tasks;
using OpenCS.Utilites;
using System.Windows;

namespace OpenCS.Views;

/// <summary>
/// «Создать задачу» по строке окна пакетного расчёта: одиночная задача строится BatchRowTaskFactory,
/// сохраняется, сразу считается, и открывается её результат с ходом вычислений.
/// Повторный выбор той же строки не плодит задачи: прежняя обновляется, если пакет с тех пор
/// поменял параметры, иначе показывается её последний результат.
/// </summary>
static class BatchRowTask
{
    public static async void CreateAndRun(AppViewModel app, CalcTask batch, int rowNum)
    {
        if (app.IsBusy) return;

        string? sectionTag = batch.Kind.StartsWith("shell_", StringComparison.Ordinal)
            ? app.PlateSections.FirstOrDefault(s => s.Id == batch.SectionId)?.Tag
            : (app.CrossSections.FirstOrDefault(s => s.Id == batch.SectionId)
               ?? app.TwoStageSectionsLive.FirstOrDefault(s => s.Id == batch.SectionId))?.Tag;
        if (sectionTag == null)
        {
            MessageBox.Show(Loc.S("CalcTaskSectionNotFound"), Loc.S("Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        CalcTask built;
        try
        {
            built = BatchRowTaskFactory.Create(batch, rowNum, sectionTag, app.ForceSets, app.PlateSections);
        }
        catch (InvalidOperationException ex)
        {
            app.LogService.Error(ex.Message);
            MessageBox.Show(Loc.S("CalcTaskForceItemNotFound"), Loc.S("Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var task = BatchRowTaskFactory.FindExisting(app.CalcTasks, batch, rowNum);
        if (task == null)
        {
            task = built;
            task.Num = app.CalcTasks.Count > 0 ? app.CalcTasks.Max(t => t.Num) + 1 : 1;
            app.db.SaveCalcTask(task);
            app.LogService.Info(string.Format(Loc.S("CalcTaskCreated"), task.Tag));
        }
        else if (task.ParamsJson != built.ParamsJson || task.ForceSetId != built.ForceSetId
                 || task.ForceItemId != built.ForceItemId || task.CalcType != built.CalcType
                 || task.SectionId != built.SectionId)
        {
            // Пакет пересчитан с другими параметрами — прежний результат задачи уже не про эту строку.
            task.ParamsJson = built.ParamsJson;
            task.ForceSetId = built.ForceSetId;
            task.ForceItemId = built.ForceItemId;
            task.CalcType = built.CalcType;
            task.SectionId = built.SectionId;
            app.db.SaveCalcTask(task);
        }
        else if (app.CalcResults.LastOrDefault(r => r.TaskId == task.Id) is { } last)
        {
            app.CurrentPage = new CalcResultView(last, app);
            return;
        }

        await CalcTaskExecutor.RunAsync(app, task,
            showResult: r => app.CurrentPage = new CalcResultView(r, app));
    }
}
