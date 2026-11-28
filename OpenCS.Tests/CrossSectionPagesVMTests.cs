using CScore;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Страницы произвольного сечения, области, группы стержней и хомутов: правки — в базу только по «Сохранить» через
/// службу, «*» и «Не сохранять» по снимку, носитель группы не меняется сам.
/// </summary>
[Collection("AppViewModel pages")]
public class CrossSectionPagesVMTests
{
    static void WithApp(Action<AppViewModel> test) => RunSta(() =>
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-sectionvm-{Guid.NewGuid():N}.db");
        _dbPath = path;
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

    static string _dbPath = "";

    /// <summary>B25, A500C, контур 400×600, бетонная область и группа «Низ» с носителем, сечение «Б-1».</summary>
    static (Material B25, Material A500, MaterialArea Concrete, MaterialArea Bars, CrossSection Section) Seed(AppViewModel app)
    {
        var b25 = MaterialCatalog.CreateHeavyConcrete("B25")!;
        var a500 = MaterialCatalog.CreateRebar("A500C")!;
        Assert.Empty(MaterialAuthoring.Save(app.db, b25));
        Assert.Empty(MaterialAuthoring.Save(app.db, a500));
        var rect = ContourAuthoring.FromPoints([(-0.2, -0.3), (0.2, -0.3), (0.2, 0.3), (-0.2, 0.3)], "K", ContourType.Hull);
        Assert.Empty(ContourAuthoring.Save(app.db, rect));
        var concrete = new MaterialArea { Tag = "Бетон", Category = AreaCategory.Region, Material = b25, MaterialId = b25.Id };
        concrete.Hull = rect;
        concrete.PoolContour = rect;
        concrete.PoolContourId = rect.Id;
        var bars = new MaterialArea
        {
            Tag = "Низ", Category = AreaCategory.RebarGroup, Material = a500, MaterialId = a500.Id, HostArea = concrete,
            Fibers = [Fiber.CreatePoint(0.025, -0.15, -0.25), Fiber.CreatePoint(0.025, 0.15, -0.25)]
        };
        var s = new CrossSection { Tag = "Б-1", Areas = [concrete, bars] };
        Assert.Empty(CrossSectionAuthoring.Save(app.db, s, app.CalcSettings, [concrete, bars]));
        app.RefreshMaterialAreaLiveCollections();
        return (b25, a500, concrete, bars, s);
    }

    static string? TagInDb(AppViewModel app, int areaId)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT tag FROM material_areas WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", areaId);
        return cmd.ExecuteScalar() as string;
    }

    [Fact]
    public void SectionPage_AreaEdit_DirtyUntilSectionSave_WritesArea() => WithApp(app =>
    {
        var (_, _, concrete, _, s) = Seed(app);
        var vm = new CrossSectionVM(s, app);
        Assert.False(vm.IsDirty);
        vm.Areas[0].Tag = "Бетон B25";
        Assert.True(vm.IsDirty);                       // правка области — «*» сечения
        Assert.Equal("Бетон", TagInDb(app, concrete.Id)); // раньше правка так и не попадала в базу

        Assert.True(vm.Save());
        Assert.False(vm.IsDirty);
        Assert.Equal("Бетон B25", TagInDb(app, concrete.Id));
    });

    [Fact]
    public void SectionPage_Discard_RestoresAreaAndComposition() => WithApp(app =>
    {
        var (_, _, concrete, bars, s) = Seed(app);
        var vm = new CrossSectionVM(s, app);
        vm.Areas[0].Tag = "Другое";
        vm.RemoveAreaFromSectionCommand.Execute(vm.Areas[1]);
        Assert.Single(s.Areas);
        ((IUnsavedChanges)vm).DiscardChanges();
        Assert.False(vm.IsDirty);
        Assert.Equal("Бетон", concrete.Tag);
        Assert.Equal([concrete, bars], s.Areas);
    });

    [Fact]
    public void SectionPage_InvalidArea_NotSaved_StaysDirty() => WithApp(app =>
    {
        var (b25, _, _, _, s) = Seed(app);
        var vm = new CrossSectionVM(s, app);
        vm.Areas[1].Material = b25;                    // группа стержней с бетоном — отказ
        Assert.False(vm.Save());
        Assert.True(vm.IsDirty);
    });

    [Fact]
    public void AreaPage_MeshChanged_UpdatesFibersCount() => WithApp(app =>
    {
        var (_, _, concrete, _, _) = Seed(app);
        var vm = new MaterialAreaVM(concrete, app);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        concrete.SliceXY(4, 4);                        // как «Разбить» в окне сетки, открытом со страницы сечения

        vm.MeshChanged();

        Assert.Contains(nameof(MaterialAreaVM.FibersCount), changed);
        Assert.True(vm.FibersCount > 0);
    });

    [Fact]
    public void AreaPage_TranslateKeepsPoolContour_SavesOnlyOnSave() => WithApp(app =>
    {
        var (_, _, concrete, _, _) = Seed(app);
        var pool = concrete.PoolContour!;
        double x0 = pool.X[0];
        var vm = new MaterialAreaVM(concrete, app);
        vm.Tag = "Сдвинутая";
        Assert.True(vm.IsDirty);
        ((IUnsavedChanges)vm).DiscardChanges();
        Assert.Equal("Бетон", concrete.Tag);
        Assert.False(vm.IsDirty);
        Assert.Equal(x0, pool.X[0]);
    });

    [Fact]
    public void AreaPage_NewArea_HullFromPool_Saves() => WithApp(app =>
    {
        var (b25, _, concrete, _, _) = Seed(app);
        var pool = concrete.PoolContour!;
        var area = new MaterialArea { Tag = "Лишняя", Category = AreaCategory.Region };
        var vm = new MaterialAreaVM(area, app) { Material = b25 };
        vm.SelectedPoolContour = pool;
        Assert.Null(vm.Hull);                          // выбор в списке ещё не назначает контур
        vm.SetHullFromPoolCommand.Execute(pool);
        Assert.NotNull(vm.Hull);
        Assert.Equal(pool.Id, area.PoolContourId);
        Assert.True(vm.Save());
        Assert.True(area.Id > 0);
        Assert.Equal("Лишняя", TagInDb(app, area.Id));
    });

    [Fact]
    public void ParametricRebuild_ReleasesOpenPage_NewPageClean_DirtyPageAsks() => WithApp(app =>
    {
        var (b25, a500, _, _, _) = Seed(app);
        var def = CScore.ParametricRc.ParametricRcSectionDefinition.Rectangle(0.3, 0.5) with
        {
            Tag = "Ригель", ConcreteMaterialId = b25.Id, LongitudinalMaterialId = a500.Id,
            LowerRebar = CScore.ParametricRc.ParametricLongitudinalLayer.Physical(3, 0.02, -0.21),
        };
        var service = new ParametricRcSectionProjectService(app.db);
        var s = new CrossSection();
        Assert.Empty(service.GenerateAndSave(s, def).Diagnostics);

        // Страница без правок закрывается без вопроса; после перестройки новая страница — без «*».
        app.AskSaveOnLeave = _ => throw new Xunit.Sdk.XunitException("вопрос не ожидался");
        app.CurrentCrossSection = s;
        Assert.True(app.ReleaseSectionPage(s));
        Assert.Null(app.CurrentPage);
        Assert.Empty(service.GenerateAndSave(s, def with { HeightM = 0.6 }).Diagnostics);
        app.CurrentCrossSection = s;
        Assert.False(((IUnsavedChanges)app.CurrentPage!.DataContext).IsDirty);

        // Правка на странице — вопрос; «Отмена» — страница остаётся, перестройки нет.
        ((CrossSectionVM)app.CurrentPage.DataContext).Tag = "Ригель 2";
        string? asked = null;
        app.AskSaveOnLeave = name => { asked = name; return System.Windows.MessageBoxResult.Cancel; };
        Assert.False(app.ReleaseSectionPage(s));
        Assert.Equal("Ригель 2", asked);
        Assert.NotNull(app.CurrentPage);
    });

    [Fact]
    public void RebarPage_ExistingGroup_KeepsHost_SigSpOnlyOnSave() => WithApp(app =>
    {
        var (_, _, concrete, bars, _) = Seed(app);
        var vm = new RebarGroupEditorVM(bars, app);
        Assert.False(vm.IsDirty);
        Assert.Same(concrete, vm.SelectedRegion);       // раньше — первая область пула

        vm.SigSp = 500;
        Assert.True(vm.IsDirty);
        Assert.Equal(0, bars.SigSp);                    // в область — только по «Сохранить»
        Assert.True(((IUnsavedChanges)vm).SaveChanges());
        Assert.Equal(500, bars.SigSp);
        Assert.Equal(concrete.Id, bars.HostAreaId);
        Assert.False(vm.IsDirty);
        Assert.Equal(2, app.db.Circles.Count(c => c.GeometrySet == CrossSectionAuthoring.BarCircleSet(bars)));
    });

    [Fact]
    public void RebarPage_ZeroDiameter_Refused_AreaUntouched() => WithApp(app =>
    {
        var (_, _, _, bars, _) = Seed(app);
        var vm = new RebarGroupEditorVM(bars, app);
        vm.Bars[0].Diameter = 0;
        Assert.True(vm.IsDirty);
        Assert.False(((IUnsavedChanges)vm).SaveChanges());
        Assert.Equal(0.025, bars.Fibers[0].Diameter, 12);
        ((IUnsavedChanges)vm).DiscardChanges();
        Assert.False(vm.IsDirty);
        Assert.Equal(0.025, vm.Bars[0].Diameter, 12);
    });

    [Fact]
    public void StirrupPage_DraftNotInArea_SaveThroughService() => WithApp(app =>
    {
        var (_, a500, concrete, _, _) = Seed(app);
        var area = new MaterialArea { Tag = "Хомуты", Category = AreaCategory.Stirrups };
        var vm = new StirrupGroupVM(area, app) { SelectedMaterial = a500, SelectedAnchorArea = concrete };
        Assert.Empty(area.Stirrups);                    // раньше конструктор добавлял пустую группу в область
        Assert.False(((IUnsavedChanges)vm).SaveChanges()); // нет ни одного хомута
        Assert.Equal(0, area.Id);

        vm.AddLoopCommand.Execute(null);
        Assert.True(vm.IsDirty);
        Assert.Empty(area.Stirrups);
        Assert.True(((IUnsavedChanges)vm).SaveChanges());
        Assert.True(area.Id > 0);
        Assert.Single(area.Stirrups);
        Assert.False(vm.IsDirty);
    });

    [Fact]
    public void DeleteArea_UsedBySection_Refused() => WithApp(app =>
    {
        var (_, _, concrete, _, _) = Seed(app);
        Assert.NotEmpty(CrossSectionAuthoring.AreaUsedBy(app.db, concrete));
        Assert.Contains(concrete, app.db.MaterialAreas);
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
