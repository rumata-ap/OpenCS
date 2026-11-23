using CScore;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using System.Windows;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Страницы наборов усилий: правка черновика, запись только по «Сохранить», «*», вопрос при уходе, имя не в формате
/// СП 20 не переписывается, CSV, удаление используемого запрещено, сочетания СП 20 из диалога.
/// </summary>
[Collection("AppViewModel pages")]
public class ForceSetPageVMTests
{
    static void WithApp(Action<AppViewModel, RecordingNotifier> test) => RunSta(() =>
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-forcevm-{Guid.NewGuid():N}.db");
        var notifier = new RecordingNotifier();
        var app = new AppViewModel(new LogService(), new NullFileDialogService(), path, notifier);
        Loc.SetLanguage("ru-RU");
        try { test(app, notifier); }
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

    static ForceSet Saved(AppViewModel app, ForceSet fs)
    {
        Assert.Empty(ForceSetAuthoring.Save(app.db, fs));
        return fs;
    }

    static void Show(AppViewModel app, object vm) =>
        app.CurrentPage = new System.Windows.Controls.UserControl { DataContext = vm };

    [Fact]
    public void Open_NotSp20Name_NotRenamed_NotDirty() => WithApp((app, _) =>
    {
        var fs = Saved(app, ForceSetAuthoringTests.Bar("РСУ 1"));
        var vm = new BarForceSetVM(fs, app);
        Show(app, vm);
        Assert.False(vm.IsDirty);
        Assert.Equal("РСУ 1", fs.Tag);
        Assert.Equal("РСУ 1", vm.Sp20.Preview);
        Assert.True(vm.Sp20.IsNotSp20Name);
        Assert.False(fs.IsModified);
        Assert.False(app.IsDirty);
        Assert.DoesNotContain("*", app.CurrentPageTitle);

        vm.Sp20.GammaFText = "1,2";   // правка поля собирает имя СП 20
        Assert.Equal("Q(γf=1,2): РСУ 1", vm.Sp20.Preview);
        Assert.True(vm.IsDirty);
        Assert.Equal("РСУ 1", fs.Tag);
    });

    [Fact]
    public void EditRow_Dirty_ModelUntouched_SaveCopies_DiscardRestores() => WithApp((app, _) =>
    {
        var fs = Saved(app, ForceSetAuthoringTests.Bar());
        int rowId = fs.Items[0].Id;
        var vm = new BarForceSetVM(fs, app);
        Show(app, vm);
        vm.Items[0].N = -555;
        Assert.True(vm.IsDirty);
        Assert.EndsWith(" *", app.CurrentPageTitle);
        Assert.Equal(-100, fs.Items[0].N);   // расчёт не видит несохранённое

        Assert.True(vm.Save());
        Assert.False(vm.IsDirty);
        Assert.Equal(-555, fs.Items[0].N);
        Assert.Equal(rowId, fs.Items[0].Id);

        vm.AddItem();
        vm.Sp20.Title = "Другое";
        Assert.True(vm.IsDirty);
        vm.DiscardChanges();
        Assert.False(vm.IsDirty);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("G: Вес", vm.Sp20.Preview);
        Assert.Equal(2, fs.Items.Count);
    });

    [Fact]
    public void GammaInvalid_FieldError_SaveRefused() => WithApp((app, _) =>
    {
        var fs = Saved(app, ForceSetAuthoringTests.Bar("Q(γf=1,4): Снег"));
        var vm = new BarForceSetVM(fs, app);
        vm.Sp20.GammaFText = "abc";
        Assert.True(vm.Sp20.HasErrors);
        Assert.Equal("Q(γf=1,4): Снег", vm.Sp20.Preview);   // имя не меняется
        Assert.True(vm.IsDirty);                              // неверный ввод — «*», «Сохранить» доступна
        Assert.True(vm.SaveCommand.CanExecute(null));
        Assert.False(vm.Save());
        vm.Sp20.GammaFText = "1,4";                           // исправлен — правок нет
        Assert.False(vm.IsDirty);
        vm.Sp20.GammaFText = "abc";
        vm.Items[0].Mx = 1;
        Assert.False(vm.Save());
        Assert.Equal(10, fs.Items[0].Mx);

        vm.Sp20.GammaFText = "0";   // 0 — из настроек
        Assert.False(vm.Sp20.HasErrors);
        Assert.Equal("Q: Снег", vm.Sp20.Preview);
    });

    [Fact]
    public void NewSet_EmptyNotSaved_LeaveAsks_SaveAddsToPool() => WithApp((app, _) =>
    {
        var vm = new ShellForceSetVM(new ForceSet { Tag = "Q: Ветер", Kind = "shell" }, app);
        Show(app, vm);
        Assert.False(vm.IsDirty);
        Assert.False(vm.Save());   // без строк
        Assert.DoesNotContain(vm.Model, app.ForceSets);

        vm.AddItem();
        vm.Items[0].Mx = 3.5;
        vm.Items[0].SigmaX = 100;
        vm.ThicknessMm = 250;
        vm.ConvertStressToForce();
        Assert.Equal(25, vm.Items[0].Nx, 9);
        vm.Items[0].SigmaX = null;   // σ можно очистить
        Assert.False(vm.Items[0].HasSigma);

        string? asked = null;
        app.AskSaveOnLeave = name => { asked = name; return MessageBoxResult.Yes; };
        app.CurrentPage = null!;
        Assert.Equal("Q: Ветер", asked);
        Assert.Contains(vm.Model, app.ForceSets);
        Assert.Contains(vm.Model, app.ShellForceSets);
        Assert.True(vm.Model.Id > 0);
        Assert.Null(vm.Model.ShellItems[0].SigmaX);
    });

    [Fact]
    public void CsvImport_ReplacesDraftOnly_BadFileNotifies() => WithApp((app, notifier) =>
    {
        var fs = Saved(app, ForceSetAuthoringTests.Bar());
        var vm = new BarForceSetVM(fs, app);
        string good = Path.Combine(Path.GetTempPath(), $"opencs-forces-{Guid.NewGuid():N}.csv");
        string bad = good + ".bad.csv";
        try
        {
            File.WriteAllLines(good, ["Label;N;Mx", "a;-12,5;3", "b;1;2", "c;0;0"]);
            File.WriteAllLines(bad, ["Label;N", "a;abc"]);
            Assert.False(vm.ImportCsvFrom(bad));
            Assert.Single(notifier.Errors);
            Assert.False(vm.IsDirty);

            Assert.True(vm.ImportCsvFrom(good));
            Assert.Equal(3, vm.Items.Count);
            Assert.Equal(-12.5, vm.Items[0].N);
            Assert.True(vm.IsDirty);
            Assert.Equal(2, fs.Items.Count);   // в проект — по «Сохранить»
        }
        finally
        {
            File.Delete(good);
            File.Delete(bad);
        }
    });

    [Fact]
    public void Delete_UsedRefused_FreeShownDirty_DeletedWithoutSaveQuestion() => WithApp((app, notifier) =>
    {
        var used = Saved(app, ForceSetAuthoringTests.Bar("Q: используемый"));
        app.db.SaveCalcTask(new CalcTask { Num = 1, Tag = "НДС", Kind = "strain_state", ForceSetId = used.Id });
        app.AskConfirmDelete = _ => throw new Xunit.Sdk.XunitException("используемый — без подтверждения");
        app.DeleteForceSet(used);
        Assert.Contains(used, app.ForceSets);
        Assert.Contains("НДС", Assert.Single(notifier.Warnings));

        var free = Saved(app, ForceSetAuthoringTests.Bar("Q: свободный"));
        var vm = new BarForceSetVM(free, app);
        Show(app, vm);
        vm.Items[0].N = 1;   // правка без сохранения
        app.AskConfirmDelete = _ => MessageBoxResult.Yes;
        app.AskSaveOnLeave = _ => throw new Xunit.Sdk.XunitException("вопрос о сохранении удаляемого не ожидался");
        app.DeleteForceSet(free);
        Assert.Null(app.CurrentPage);
        Assert.DoesNotContain(free, app.ForceSets);
        Assert.DoesNotContain(free, app.BarForceSets);

        // Удаление нескольких: используемый пропущен, остальные удалены.
        var a = Saved(app, ForceSetAuthoringTests.Bar("Q: a"));
        notifier.Warnings.Clear();
        Assert.Equal(1, app.DeleteForceSetsChecked([used, a]));
        Assert.Contains(used, app.ForceSets);
        Assert.DoesNotContain(a, app.ForceSets);
        Assert.Single(notifier.Warnings);
    });

    [Fact]
    public void Duplicate_ViaApp_KeepsSecondOrder() => WithApp((app, _) =>
    {
        var src = ForceSetAuthoringTests.Bar("G: Вес");
        src.SecondOrder = true;
        Saved(app, src);
        var copy = app.DuplicateForceSet(src)!;
        Assert.True(copy.SecondOrder);
        Assert.Equal("G: Вес (копия)", copy.Tag);
        Assert.Contains(copy, app.BarForceSets);
    });

    [Fact]
    public void Sp20Dialog_NameFollowsSelection_CreatesSets() => WithApp((app, _) =>
    {
        var g = Saved(app, ForceSetAuthoringTests.Bar("G: Вес"));
        var q = Saved(app, ForceSetAuthoringTests.Bar("Q: Снег"));
        var vm = new SP20DialogVM(app.BarForceSets, app) { Show = (_, _) => { } };
        vm.Items[0].IsSelected = true;
        Assert.Equal("СП20 — G: Вес", vm.ResultName);
        vm.Items[1].IsSelected = true;
        Assert.Equal("СП20 — G: Вес, Q: Снег", vm.ResultName);
        vm.ResultName = "Мои";
        vm.Items[1].IsSelected = false;
        Assert.Equal("Мои", vm.ResultName);
        vm.Items[1].IsSelected = true;

        bool done = false;
        vm.Done += () => done = true;
        Assert.True(vm.Generate());
        Assert.True(done);
        Assert.Equal(6, app.ForceSets.Count);
        Assert.Contains(app.BarForceSets, s => s.Tag == "Мои (основное (C))");
    });
}
