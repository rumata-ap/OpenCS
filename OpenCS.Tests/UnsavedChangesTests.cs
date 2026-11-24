using CScore;
using OpenCS.Services;
using OpenCS.ViewModels;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Тесты страниц с AppViewModel — не параллельно: конструктор AppViewModel пересоздаёт файл базы, и под нагрузкой
/// удаление ловит занятый файл.
/// </summary>
[CollectionDefinition("AppViewModel pages", DisableParallelization = true)]
public sealed class AppViewModelPagesCollection { }

/// <summary>
/// Решения пользователя 22.11.2026: одна «Сохранить» у материала, «*» у несохранённых материала и диаграммы,
/// вопрос «Сохранить / Не сохранять / Отмена» при уходе со страницы.
/// </summary>
[Collection("AppViewModel pages")]
public class UnsavedChangesTests
{
    static void WithApp(Action<AppViewModel> test) => RunSta(() =>
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-unsaved-{Guid.NewGuid():N}.db");
        var app = new AppViewModel(new LogService(), new NullFileDialogService(), path);
        try { test(app); }
        finally
        {
            app.db.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { File.Delete(path + suffix); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    });

    [Fact]
    public void Material_OneSave_CreatesThenUpdates_DirtyFollows() => WithApp(app =>
    {
        var vm = new MaterialVM { Material = new Material(0), mvm = app };
        Assert.False(vm.IsDirty);
        var b25 = MaterialCatalog.CreateHeavyConcrete("B25")!;
        vm.Material = b25;
        Assert.True(vm.IsDirty);              // новый и не пустой

        vm.SaveCommand.Execute(null);
        Assert.True(b25.Id > 0);
        Assert.Single(app.db.Materials);
        Assert.False(vm.IsDirty);

        vm.Material.C!.Fc = -15000;           // правка ячейки набора — мимо свойств VM
        Assert.True(vm.IsDirty);
        vm.SaveCommand.Execute(null);
        Assert.Single(app.db.Materials);
        Assert.False(vm.IsDirty);
    });

    [Fact]
    public void Material_DiscardRestoresSavedState() => WithApp(app =>
    {
        var m = MaterialCatalog.CreateHeavyConcrete("B25")!;
        Assert.Empty(MaterialAuthoring.Save(app.db, m));
        var vm = new MaterialVM { Material = m, mvm = app };
        double fc = m.C!.Fc;
        vm.Tag = "Другой";
        m.C!.Fc = -1;
        ((OpenCS.Utilites.IUnsavedChanges)vm).DiscardChanges();
        Assert.Equal("B25", m.Tag);
        Assert.Equal(fc, m.C!.Fc, 6);
        Assert.False(vm.IsDirty);
    });

    [Fact]
    public void LeavingDirtyPage_AsksAndObeysAnswer() => WithApp(app =>
    {
        var m = MaterialCatalog.CreateHeavyConcrete("B25")!;
        Assert.Empty(MaterialAuthoring.Save(app.db, m));
        var vm = new MaterialVM { Material = m, mvm = app };
        var page = new UserControl { DataContext = vm };
        app.CurrentPage = page;
        var other = new UserControl();

        int asked = 0;
        var answer = MessageBoxResult.Cancel;
        app.AskSaveOnLeave = _ => { asked++; return answer; };

        app.CurrentPage = other;              // без правок — без вопроса
        Assert.Same(other, app.CurrentPage);
        Assert.Equal(0, asked);

        app.CurrentPage = page;
        vm.Tag = "B25 изм";
        Assert.EndsWith("*", app.CurrentPageTitle);
        app.CurrentPage = other;              // «Отмена» — остаться
        Assert.Same(page, app.CurrentPage);
        Assert.Equal(1, asked);

        answer = MessageBoxResult.No;         // «Нет» — отбросить
        app.CurrentPage = other;
        Assert.Same(other, app.CurrentPage);
        Assert.Equal("B25", m.Tag);

        app.CurrentPage = page;
        vm.Tag = "B25 сохр";
        answer = MessageBoxResult.Yes;        // «Да» — сохранить
        app.CurrentPage = other;
        Assert.Same(other, app.CurrentPage);
        Assert.False(vm.IsDirty);

        app.CurrentPage = page;
        vm.Tag = "";                          // «Да», но проверка не прошла — остаться
        app.CurrentPage = other;
        Assert.Same(page, app.CurrentPage);
    });

    [Fact]
    public void BlankNewMaterial_NotDirty_ClosedByCloseIfBlank() => WithApp(app =>
    {
        var vm = new MaterialVM { Material = new Material(0), mvm = app };
        app.CurrentPage = new UserControl { DataContext = vm };
        Assert.True(vm.IsBlank);
        vm.CloseIfBlank();
        Assert.Null(app.CurrentPage);
    });

    /// <summary>
    /// «Добавить» в узле «Свои диаграммы»: новый материал сразу с типом Custom — пустая страница без «*»; узел дерева
    /// виден, только когда такие материалы есть.
    /// </summary>
    [Fact]
    public void NewCustomMaterial_PresetType_NotDirty_NodeVisibleOnlyWithCustom() => WithApp(app =>
    {
        Assert.False(app.HasCustomMaterials);
        var vm = new MaterialVM { Material = new Material(0) { Type = MatType.Custom }, mvm = app };
        Assert.True(vm.IsCustom);
        Assert.True(vm.IsBlank);
        Assert.False(vm.IsDirty);
        vm.Tag = "Своя";
        Assert.True(vm.IsDirty);

        var custom = new Material(0) { Type = MatType.Custom, Tag = "Своя" };
        app.Materials.Add(custom);
        app.MaterialsSort();
        Assert.True(app.HasCustomMaterials);
        Assert.Contains(custom, app.CustomMaterials);
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
}
