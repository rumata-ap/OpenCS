using System.Windows;
using CScore;
using CScore.Fire.Entities;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Страница огневого сечения и команды дерева: черновик и «*», числа с запятой, нераспознанное — ошибка у поля без
/// записи, шаг сетки в мм, ручные ГУ рёбер на черновике и их сброс, смена сечения (сброс рёбер, вопрос о задачах),
/// удаление используемого — отказ, копия, окно создания.
/// </summary>
[Collection("AppViewModel pages")]
public class FireSectionPageVMTests
{
    sealed record Ctx(AppViewModel App, RecordingNotifier Notifier, CrossSection Section, FireSectionDef Fire);

    static void WithApp(Action<Ctx> test) => RunSta(() =>
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-firevm-{Guid.NewGuid():N}.db");
        var notifier = new RecordingNotifier();
        var app = new AppViewModel(new LogService(), new NullFileDialogService(), path, notifier);
        Loc.SetLanguage("ru-RU");
        try
        {
            var section = FireSectionAuthoringTests.Rect(app.db, 1, "К1");
            var fire = new FireSectionDef
            {
                Tag = "Ф1", SectionId = 1, FireDurationMin = 1, MeshStepM = 0.02, TimeStepS = 30, SnapshotStepMin = 0.5,
                Algorithm = "ruppert", SmoothIterTri = 2, BcPreset = "manual",
            };
            Assert.Empty(FireSectionAuthoring.Save(app.db, fire));
            test(new Ctx(app, notifier, section, fire));
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

    static FireSectionVM Page(Ctx c)
    {
        var vm = new FireSectionVM(c.Fire, c.App);
        c.App.CurrentPage = new System.Windows.Controls.UserControl { DataContext = vm };
        return vm;
    }

    [Fact]
    public void Comma_SavesValue_MeshInMm_StarOnlyUntilSave() => WithApp(c =>
    {
        var vm = Page(c);
        Assert.False(vm.IsDirty);
        Assert.Equal("20", vm.MeshStepMmText);

        vm.FireDurationText = "12,5";
        vm.MeshStepMmText = "15";
        Assert.True(vm.IsDirty);
        Assert.EndsWith(" *", c.App.CurrentPageTitle);
        Assert.Equal(1, c.Fire.FireDurationMin);               // модель не видит несохранённое

        Assert.True(vm.Save());
        Assert.False(vm.IsDirty);
        Assert.Equal(12.5, c.Fire.FireDurationMin);
        Assert.Equal(0.015, c.Fire.MeshStepM, 12);
        Assert.DoesNotContain("*", c.App.CurrentPageTitle);
    });

    [Fact]
    public void NotNumber_FieldError_SaveRefused_ModelUntouched() => WithApp(c =>
    {
        var vm = Page(c);
        vm.TimeStepText = "abc";
        Assert.True(vm.HasErrors);
        Assert.NotEmpty(vm.GetErrors(nameof(FireSectionVM.TimeStepText)).Cast<string>());
        Assert.True(vm.IsDirty);
        Assert.False(vm.Save());
        Assert.Contains(Loc.S("FireSection_TimeStep"), vm.ErrorText);
        Assert.Equal(30, c.Fire.TimeStepS);

        vm.TimeStepText = "0";                               // число, но вне диапазона — ошибка службы у поля
        Assert.False(vm.Save());
        Assert.NotEmpty(vm.GetErrors(nameof(FireSectionVM.TimeStepText)).Cast<string>());
        Assert.Equal(30, c.Fire.TimeStepS);
    });

    [Fact]
    public void EdgeClick_DirtyOnDraft_LeaveNo_RestoresEdges_Reset() => WithApp(c =>
    {
        var vm = Page(c);
        vm.RefreshPreview();
        var edge = vm.Preview.OuterEdges[0];
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.Preview.CycleEdgeBc(edge);
        Assert.True(vm.IsDirty);
        Assert.Equal(1, vm.ManualEdgeCount);
        // Подпись счётчика на странице обновляется вместе с числом (ручная проверка 24.11: оставалась прежней).
        Assert.Contains(nameof(FireSectionVM.ManualEdgesText), raised);
        Assert.Equal(string.Format(Loc.S("FireSection_ManualEdgesCount"), 1), vm.ManualEdgesText);
        Assert.Empty(c.Fire.Edges);                           // правка — в черновике, не в модели

        c.App.AskSaveOnLeave = _ => MessageBoxResult.No;
        c.App.CurrentPage = null!;
        Assert.Empty(c.Fire.Edges);

        var vm2 = Page(c);
        vm2.RefreshPreview();
        vm2.Preview.CycleEdgeBc(vm2.Preview.OuterEdges[1]);
        Assert.True(vm2.Save());
        Assert.Single(c.Fire.Edges);
        vm2.ResetEdgesCommand.Execute(null);
        Assert.Equal(0, vm2.ManualEdgeCount);
        Assert.True(vm2.IsDirty);
        Assert.True(vm2.Save());
        Assert.Empty(c.Fire.Edges);
    });

    [Fact]
    public void SectionChange_ResetsEdges_AsksAboutTasks_NoKeepsAll() => WithApp(c =>
    {
        var other = FireSectionAuthoringTests.Rect(c.App.db, 2, "К2", w: 0.3);
        c.Fire.Edges.Add(new FireBoundaryEdgeDef { ContourType = "outer", EdgeIndex = 0, BcType = "fire", AlphaConv = 25, Emissivity = 0.7 });
        c.App.db.SaveFireSection(c.Fire);
        var task = new CalcTask
        {
            Tag = "Кривизна", Kind = "fire_thermal_curvature", SectionId = 1,
            ParamsJson = new FireThermalCurvatureParams { FireSectionId = c.Fire.Id }.ToJson(),
        };
        Assert.Empty(CalcTaskAuthoring.Save(c.App.db, task));

        var vm = Page(c);
        vm.SelectedSection = other;
        Assert.Equal(0, vm.ManualEdgeCount);
        Assert.Contains(Loc.S("FireSection_EdgesResetOnSection"), vm.StatusText);

        c.Notifier.ConfirmAnswer = false;
        Assert.False(vm.Save());
        Assert.Single(c.Notifier.Questions);
        Assert.Equal(1, c.Fire.SectionId);
        Assert.Single(c.Fire.Edges);
        Assert.Equal(1, task.SectionId);

        c.Notifier.ConfirmAnswer = true;
        Assert.True(vm.Save());
        Assert.Equal(2, c.Fire.SectionId);
        Assert.Empty(c.Fire.Edges);
        Assert.Equal(2, task.SectionId);
    });

    [Fact]
    public void Delete_Used_Refused_WithNames_Free_Deleted_Duplicate() => WithApp(c =>
    {
        var task = new CalcTask
        {
            Tag = "Кривизна R60", Kind = "fire_thermal_curvature", SectionId = 1,
            ParamsJson = new FireThermalCurvatureParams { FireSectionId = c.Fire.Id }.ToJson(),
        };
        Assert.Empty(CalcTaskAuthoring.Save(c.App.db, task));
        c.App.AskConfirmDelete = _ => throw new Xunit.Sdk.XunitException("используемое — без подтверждения");
        c.App.DeleteFireSection(c.Fire);
        Assert.Contains(c.Fire, c.App.FireSections);
        Assert.Contains(c.Notifier.Warnings, w => w.Contains("Кривизна R60"));

        c.App.DuplicateFireSection(c.Fire);
        var copy = c.App.FireSections.Single(f => f.Tag == "Ф1 (копия)");
        Assert.Equal(2, copy.Num);

        var vm = new FireSectionVM(copy, c.App);
        c.App.CurrentPage = new System.Windows.Controls.UserControl { DataContext = vm };
        vm.FireDurationText = "90";                          // правка без сохранения
        c.App.AskConfirmDelete = _ => MessageBoxResult.Yes;
        c.App.AskSaveOnLeave = _ => throw new Xunit.Sdk.XunitException("вопрос о сохранении удаляемого не ожидался");
        c.App.DeleteFireSection(copy);
        Assert.Null(c.App.CurrentPage);
        Assert.DoesNotContain(copy, c.App.FireSections);
    });

    [Fact]
    public void New_FromDialog_SavedWithNumber_DialogVmRejectsBadDuration() => WithApp(c =>
    {
        var dvm = new Views.Dialogs.FireSectionDialogVM(c.App);
        Assert.Equal("Огневое сечение 2", dvm.Tag);
        dvm.FireDurationText = "abc";
        Assert.Null(dvm.TryBuild());
        Assert.Contains(Loc.S("FireSection_Duration"), dvm.ErrorText);
        dvm.FireDurationText = "120,5";
        var built = dvm.TryBuild();
        Assert.NotNull(built);
        Assert.Equal(120.5, built!.FireDurationMin);

        c.App.ShowFireSectionDialog = () => built;
        // Страница без Application не строится — подменяем её пустым элементом после создания.
        try { c.App.NewFireSection(); } catch (Exception) { /* FireSectionView требует ресурсов App.xaml */ }
        Assert.Contains(built, c.App.FireSections);
        Assert.True(built.Id > 0);
        Assert.Equal(2, built.Num);
    });
}
