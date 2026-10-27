using CScore;
using CScore.Fem;
using CScore.LimitSurface;
using OpenCS.LimitSurface;
using OpenCS.Tasks;
using OpenCS.Utilites;
using System.Windows;

namespace OpenCS.Views;

/// <summary>
/// «Создать задачу» по строке окна пакетного расчёта или проверки по КЭ: одиночная задача строится фабрикой
/// (<see cref="BatchRowTaskFactory"/>, <see cref="FemCheckRowTaskFactory"/>), сохраняется, сразу считается,
/// и открывается её результат с ходом вычислений. Повторный выбор той же строки не плодит задачи: прежняя
/// обновляется, если пакет или проверка с тех пор поменяли параметры, иначе показывается её последний результат.
/// </summary>
static class BatchRowTask
{
    public static void CreateAndRun(AppViewModel app, CalcTask batch, int rowNum)
    {
        if (app.IsBusy) return;

        string? sectionTag = batch.Kind.StartsWith("shell_", StringComparison.Ordinal)
            ? app.PlateSections.FirstOrDefault(s => s.Id == batch.SectionId)?.Tag
            : app.CrossSections.FirstOrDefault(s => s.Id == batch.SectionId)?.Tag;
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

        RunOrOpen(app, BatchRowTaskFactory.FindExisting(app.CalcTasks, batch, rowNum), built);
    }

    /// <summary>По точке окна поверхности: одиночная задача предельного усилия в режиме луча Кисп.</summary>
    public static void CreateAndRun(AppViewModel app, CalcTask surface, LimitSurfacePointRecord point, UtilizationMode mode)
    {
        if (app.IsBusy) return;
        string? sectionTag = app.CrossSections.FirstOrDefault(s => s.Id == surface.SectionId)?.Tag;
        if (sectionTag == null)
        {
            MessageBox.Show(Loc.S("CalcTaskSectionNotFound"), Loc.S("Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        var built = LimitSurfaceRowTaskFactory.Create(surface, point, mode, sectionTag);
        RunOrOpen(app, LimitSurfaceRowTaskFactory.FindExisting(app.CalcTasks, surface, point.Num, mode), built);
    }

    /// <summary>
    /// По строке проверки по КЭ: сечение КЭ (копия собранного проверкой — в проект), параметры КЭ и усилия строки —
    /// как в самой проверке.
    /// </summary>
    public static async void CreateAndRun(AppViewModel app, FemCheck check, FemCheckRowKey key)
    {
        if (app.IsBusy) return;
        CalcTask? built;
        try
        {
            built = await FemCheckRowTaskFactory.BuildAsync(app, check, key,
                section =>
                {
                    app.CrossSections.Add(section);
                    app.RefreshSectionLiveCollections();
                    app.LogService.Info(string.Format(Loc.S("FemCheckRowTaskSectionCopied"), section.Tag));
                },
                plate =>
                {
                    app.PlateSections.Add(plate);
                    app.LogService.Info(string.Format(Loc.S("FemCheckRowTaskSectionCopied"), plate.Tag));
                });
        }
        catch (InvalidOperationException ex)
        {
            app.LogService.Error($"{check.DisplayTag}: {ex.Message}");
            MessageBox.Show(ex.Message, Loc.S("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (built == null) return;

        RunOrOpen(app, FemCheckRowTaskFactory.FindExisting(app.CalcTasks, check, key), built);
    }

    /// <summary>Новая задача — сохранить и посчитать; прежняя — обновить и пересчитать, если изменилась, иначе открыть.</summary>
    static async void RunOrOpen(AppViewModel app, CalcTask? task, CalcTask built)
    {
        if (task == null)
        {
            task = built;
            task.Num = app.CalcTasks.Count > 0 ? app.CalcTasks.Max(t => t.Num) + 1 : 1;
            app.db.SaveCalcTask(task);
            app.LogService.Info(string.Format(Loc.S("CalcTaskCreated"), task.Tag));
        }
        else if (task.Kind != built.Kind || task.ParamsJson != built.ParamsJson || task.ForceSetId != built.ForceSetId
                 || task.ForceItemId != built.ForceItemId || task.CalcType != built.CalcType
                 || task.SectionId != built.SectionId)
        {
            // Пакет или проверка пересчитаны с другими параметрами — прежний результат задачи уже не про эту строку.
            task.Kind = built.Kind;
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
