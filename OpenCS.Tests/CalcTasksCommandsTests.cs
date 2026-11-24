using System.Windows;
using CScore;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Команды задач дерева и страницы: правка с устаревшими результатами (Да/Нет/Отмена), копия, удаление с вопросом,
/// окно параметров потерь после создания из дерева, подписи сечения и набора у задачи пластины.
/// </summary>
[Collection("AppViewModel pages")]
public class CalcTasksCommandsTests
{
    sealed record Ctx(AppViewModel App, RecordingNotifier Notifier, CrossSection Section, PlateSection Plate, ForceSet Shell);

    static void WithApp(Action<Ctx> test) => RunSta(() =>
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-taskcmd-{Guid.NewGuid():N}.db");
        var notifier = new RecordingNotifier();
        var app = new AppViewModel(new LogService(), new NullFileDialogService(), path, notifier);
        Loc.SetLanguage("ru-RU");
        try
        {
            var section = new CrossSection { Tag = "К1" };
            app.db.SaveCrossSection(section);
            app.CrossSections.Add(section);
            var plate = new PlateSection { Tag = "Плита", H = 0.2 };
            app.db.SavePlateSection(plate);
            if (!app.PlateSections.Contains(plate)) app.PlateSections.Add(plate);
            var shell = ForceSetAuthoringTests.Shell("РСУ плиты");
            Assert.Empty(ForceSetAuthoring.Save(app.db, shell));
            test(new Ctx(app, notifier, section, plate, shell));
        }
        finally
        {
            Loc.SetLanguage(null);
            app.db.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { File.Delete(path + suffix); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    });

    static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw new Xunit.Sdk.XunitException(error.ToString());
    }

    static CalcTask Strain(Ctx c, double n) => new()
    {
        Tag = "НДС", Kind = "strain_state", SectionId = c.Section.Id, CalcType = CalcType.C,
        ParamsJson = $$"""{"N":{{n}},"Mx":10,"My":0}"""
    };

    static CalcTask Saved(Ctx c, CalcTask t)
    {
        Assert.Empty(CalcTaskAuthoring.Save(c.App.db, t));
        return t;
    }

    static void AddResult(Ctx c, CalcTask t) =>
        c.App.db.SaveCalcResult(new CalcResult { TaskId = t.Id, TaskKind = t.Kind, TaskTag = t.Tag, Created = "2026-11-24 12:00:00" });

    static CalcTask EditedCopy(CalcTask t, Action<CalcTask> change)
    {
        var e = new CalcTask();
        CalcTaskAuthoring.CopyInto(t, e);
        change(e);
        return e;
    }

    [Fact]
    public void Edit_WithResults_AsksAboutStaleResults() => WithApp(c =>
    {
        var t = Saved(c, Strain(c, -100));
        AddResult(c, t);
        var answers = new Queue<MessageBoxResult>([MessageBoxResult.Cancel, MessageBoxResult.No, MessageBoxResult.Yes]);
        var questions = new List<string>();
        c.App.AskYesNoCancel = q => { questions.Add(q); return answers.Dequeue(); };

        // Отмена — правка не сохраняется.
        c.App.ShowCalcTaskDialog = (_, _) => EditedCopy(t, e => e.ParamsJson = """{"N":-200,"Mx":10,"My":0}""");
        Assert.False(c.App.EditCalcTask(t));
        Assert.Contains("-100", t.ParamsJson);
        Assert.Equal(1, CalcTaskAuthoring.ResultCount(c.App.db, t));

        // Нет — правка сохранена, результаты оставлены.
        Assert.True(c.App.EditCalcTask(t));
        Assert.Contains("-200", t.ParamsJson);
        Assert.Equal(1, CalcTaskAuthoring.ResultCount(c.App.db, t));

        // Да — результаты удалены.
        c.App.ShowCalcTaskDialog = (_, _) => EditedCopy(t, e => e.ParamsJson = """{"N":-300,"Mx":10,"My":0}""");
        Assert.True(c.App.EditCalcTask(t));
        Assert.Equal(0, CalcTaskAuthoring.ResultCount(c.App.db, t));
        Assert.Equal(3, questions.Count);
        Assert.Contains("НДС", questions[0]);

        // Только имя — без вопроса.
        AddResult(c, t);
        c.App.ShowCalcTaskDialog = (_, _) => EditedCopy(t, e => e.Tag = "Новое имя");
        Assert.True(c.App.EditCalcTask(t));
        Assert.Equal(3, questions.Count);
        Assert.Equal("Новое имя", t.Tag);
        Assert.Equal(1, CalcTaskAuthoring.ResultCount(c.App.db, t));
    });

    [Fact]
    public void Duplicate_And_Delete() => WithApp(c =>
    {
        var t = Saved(c, Strain(c, -100));
        AddResult(c, t);
        var copy = c.App.DuplicateCalcTask(t)!;
        Assert.Equal("НДС (копия)", copy.Tag);
        Assert.Equal(t.Num + 1, copy.Num);
        Assert.Contains(copy, c.App.CalcTasks);
        Assert.Equal(0, CalcTaskAuthoring.ResultCount(c.App.db, copy));

        var asked = new List<string>();
        c.App.AskConfirmDelete = q => { asked.Add(q); return MessageBoxResult.No; };
        Assert.False(c.App.DeleteCalcTask(t));
        Assert.Contains(t, c.App.CalcTasks);
        Assert.Contains("(1)", asked[0]);

        c.App.AskConfirmDelete = _ => MessageBoxResult.Yes;
        Assert.True(c.App.DeleteCalcTask(t));
        Assert.DoesNotContain(t, c.App.CalcTasks);
        Assert.DoesNotContain(c.App.CalcResults, r => r.TaskId == t.Id);
    });

    [Fact]
    public void New_FromTree_Numbered_ParamsWindowForPrestress() => WithApp(c =>
    {
        var opened = new List<string>();
        c.App.ShowCalcTaskParamsWindow = ct => opened.Add(ct.Kind);
        c.App.ShowCalcTaskDialog = (_, _) => new CalcTask
        {
            Tag = "Потери", Kind = "prestress_loss", SectionId = c.Section.Id, CalcType = CalcType.C, ParamsJson = "{}"
        };
        var t = c.App.NewCalcTask("other")!;
        Assert.Equal(1, t.Num);
        Assert.Equal(["prestress_loss"], opened);

        // Неверная задача (пакет без набора) — не создаётся, сообщение.
        c.App.ShowCalcTaskDialog = (_, _) => new CalcTask
        {
            Tag = "Пакет", Kind = "strain_state_batch", SectionId = c.Section.Id, CalcType = CalcType.C, ParamsJson = "{}"
        };
        Assert.Null(c.App.NewCalcTask());
        Assert.NotEmpty(c.Notifier.Warnings);
        Assert.Single(c.App.CalcTasks);
    });

    [Fact]
    public void PlateTask_LabelsResolved() => WithApp(c =>
    {
        var t = Saved(c, new CalcTask
        {
            Tag = "Плита", Kind = "shell_simpl_wa_uls", SectionId = c.Plate.Id, ForceSetId = c.Shell.Id,
            ForceItemId = c.Shell.ShellItems[1].Id, CalcType = CalcType.C, ParamsJson = "{}"
        });
        var vm = CalcTaskVM.Build(t, c.App.db);
        Assert.Equal("Плита", vm.SectionTag);
        Assert.Equal("РСУ плиты", vm.ForceSetTag);
        Assert.Equal("2", vm.ForceItemLabel);

        var page = new CalcTasksPageVM(c.App, null);
        Assert.Equal("Плита", page.Tasks.Single().SectionTag);
        page.Dispose();
    });
}
