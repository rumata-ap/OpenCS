using CScore;
using CScore.PlateStrip;
using OpenCS.Services;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using System.IO;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Служба сечений пластин: проверка с путями документа, запись, копия, удаление используемого запрещено.</summary>
[Collection("Loc language")]
public sealed class PlateSectionAuthoringTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_plate_{Guid.NewGuid():N}");
    public PlateSectionAuthoringTests()
    {
        Directory.CreateDirectory(_dir);
        Loc.SetLanguage("ru-RU");
    }
    public void Dispose()
    {
        Loc.SetLanguage(null);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    (DatabaseService Db, Material Concrete, Material Rebar) Project(string name)
    {
        var db = new DatabaseService(Path.Combine(_dir, name));
        var b25 = MaterialCatalog.CreateHeavyConcrete("B25")!;
        var a500 = MaterialCatalog.CreateRebar("A500C")!;
        Assert.Empty(MaterialAuthoring.Save(db, b25));
        Assert.Empty(MaterialAuthoring.Save(db, a500));
        return (db, b25, a500);
    }

    internal static PlateSection Plate(Material concrete, Material rebar, string tag = "Плита 200") => new()
    {
        Tag = tag, H = 0.2, NLayers = 10, ConcreteMaterialId = concrete.Id, RebarMaterialId = rebar.Id,
        RebarLayers =
        [
            new PlateRebarLayer { Name = "Низ", InputMode = "diameter_spacing", DiameterX = 0.012, SpacingX = 0.2,
                                  DiameterY = 0.012, SpacingY = 0.2, Zsx = -0.07, Zsy = -0.058 },
        ]
    };

    [Fact]
    public void Validate_PathsByField()
    {
        var (db, b25, a500) = Project("v.db");
        using var owner = db;
        Assert.Empty(PlateSectionAuthoring.Validate(Plate(b25, a500), db.Materials));

        var bad = Plate(a500, b25);   // бетон и арматура перепутаны
        bad.Tag = " ";
        bad.H = 0;
        bad.NLayers = 0;
        var errs = PlateSectionAuthoring.Validate(bad, db.Materials);
        foreach (var path in new[] { "tag", "h", "nLayers", "concrete", "rebar" })
            Assert.Contains(errs, e => e.Path == path);

        var noMat = Plate(b25, a500);
        noMat.ConcreteMaterialId = 0;
        noMat.RebarMaterialId = 0;
        errs = PlateSectionAuthoring.Validate(noMat, db.Materials);
        Assert.Contains(errs, e => e.Path == "concrete");
        Assert.Contains(errs, e => e.Path == "rebar");

        var diag = Plate(b25, a500);
        diag.ConcreteDiagramType = DiagrammType.SP16;
        Assert.Contains(PlateSectionAuthoring.Validate(diag, db.Materials), e => e.Path == "diagram");
    }

    [Fact]
    public void Validate_Layers_ByModeAndThickness()
    {
        var (db, b25, a500) = Project("l.db");
        using var owner = db;
        var ps = Plate(b25, a500);
        var l = ps.RebarLayers[0];

        l.SpacingX = 0;                    // Ø без шага
        l.DiameterY = -0.01;
        var errs = PlateSectionAuthoring.Validate(ps, db.Materials);
        Assert.Contains(errs, e => e.Path == "rebarLayers[0].x.step");
        Assert.Contains(errs, e => e.Path == "rebarLayers[0].y.d");

        l.DiameterX = 0;                   // по X нет арматуры совсем — допустимо, если есть по Y
        l.DiameterY = 0.012;
        l.Zsx = 0.5;                       // у пустого направления Zs не проверяется
        Assert.Empty(PlateSectionAuthoring.Validate(ps, db.Materials));

        l.Zsy = -0.1;                      // |z| = h/2 — на грани
        Assert.Contains(PlateSectionAuthoring.Validate(ps, db.Materials), e => e.Path == "rebarLayers[0].y.z");
        l.Zsy = -0.058;

        l.DiameterY = 0;
        l.SpacingY = 0;
        Assert.Contains(PlateSectionAuthoring.Validate(ps, db.Materials), e => e.Path == "rebarLayers[0]");  // пустой слой

        l.InputMode = "diameter_count";
        l.DiameterX = 0.016;
        l.CountPerMeterX = 0;
        Assert.Contains(PlateSectionAuthoring.Validate(ps, db.Materials), e => e.Path == "rebarLayers[0].x.count");

        l.InputMode = "direct";
        l.Asx = -1e-4;
        Assert.Contains(PlateSectionAuthoring.Validate(ps, db.Materials), e => e.Path == "rebarLayers[0].x.area");
        l.Asx = 5e-4;
        l.MaterialId = b25.Id;             // материал слоя — не арматура
        Assert.Contains(PlateSectionAuthoring.Validate(ps, db.Materials), e => e.Path == "rebarLayers[0].material");

        l.InputMode = "bad";
        Assert.Contains(PlateSectionAuthoring.Validate(ps, db.Materials), e => e.Path == "rebarLayers[0].mode");
    }

    [Fact]
    public void Save_InvalidWritesNothing_ValidRecalcsArea_Reloads()
    {
        var (db, b25, a500) = Project("s.db");
        var bad = Plate(b25, a500);
        bad.H = -1;
        Assert.NotEmpty(PlateSectionAuthoring.Save(db, bad));
        Assert.Empty(db.PlateSections);

        var ps = Plate(b25, a500);
        ps.PlateModel = "char1d_axial";
        ps.ConcreteDiagramType = DiagrammType.SP63;
        ps.RebarLayers[0].Angle = 30;
        Assert.Empty(PlateSectionAuthoring.Save(db, ps));
        Assert.True(ps.Id > 0);
        Assert.Equal(1, ps.Num);
        Assert.Equal(Math.PI * 0.012 * 0.012 / 4 / 0.2, ps.RebarLayers[0].Asx, 12);
        string path = Path.Combine(_dir, "s.db");
        db.Dispose();

        using var db2 = new DatabaseService(path);
        db2.LoadAll();
        var back = Assert.Single(db2.PlateSections);
        Assert.Equal(PlateSectionAuthoring.Fingerprint(ps), PlateSectionAuthoring.Fingerprint(back));
    }

    [Fact]
    public void CopyInto_KeepsModelDiagramFaceAngle_NotIdentity()
    {
        var (db, b25, a500) = Project("c.db");
        using var owner = db;
        var src = Plate(b25, a500);
        src.Id = 5; src.Num = 3; src.GeneratedForRegionId = 7;
        src.PlateModel = "char1d_principal";
        src.ConcreteDiagramType = DiagrammType.SP63;
        src.RebarLayers[0].Face = CScore.PlateRebar.RebarFace.MinusN;
        src.RebarLayers[0].Angle = 45;
        var dst = new PlateSection();
        PlateSectionAuthoring.CopyInto(src, dst);
        Assert.Equal(0, dst.Id);
        Assert.Null(dst.GeneratedForRegionId);
        Assert.Equal(PlateSectionAuthoring.Fingerprint(src), PlateSectionAuthoring.Fingerprint(dst));
        Assert.NotSame(src.RebarLayers[0], dst.RebarLayers[0]);
    }

    [Fact]
    public void TryDelete_UsedByShellTaskAndEquivalent_Refused_BarTaskIgnored()
    {
        var (db, b25, a500) = Project("d.db");
        using var owner = db;
        var ps = Plate(b25, a500);
        Assert.Empty(PlateSectionAuthoring.Save(db, ps));

        // Задача стержневого сечения с тем же числовым Id — не ссылка на сечение пластины.
        db.SaveCalcTask(new CalcTask { Tag = "НДС балки", Kind = "strain_state", SectionId = ps.Id });
        Assert.Empty(PlateSectionAuthoring.UsedBy(db, ps));

        db.SaveCalcTask(new CalcTask { Tag = "НДС плиты", Kind = "shell_strain_state", SectionId = ps.Id });
        db.SaveEquivalentSection(new EquivalentSection { Tag = "Полоса 1", SourcePlateSectionId = ps.Id });
        Assert.False(PlateSectionAuthoring.TryDelete(db, ps, out var usedBy));
        Assert.Contains(usedBy, u => u.Contains("НДС плиты"));
        Assert.Contains(usedBy, u => u.Contains("Полоса 1"));
        Assert.DoesNotContain(usedBy, u => u.Contains("НДС балки"));
        Assert.Single(db.PlateSections);

        var free = Plate(b25, a500, "Свободная");
        Assert.Empty(PlateSectionAuthoring.Save(db, free));
        Assert.True(PlateSectionAuthoring.TryDelete(db, free, out _));
        Assert.DoesNotContain(free, db.PlateSections);
    }

    [Fact]
    public void CrossSectionReferences_IgnoreShellTasks()
    {
        var (db, b25, a500) = Project("x.db");
        using var owner = db;
        db.SaveCalcTask(new CalcTask { Tag = "НДС плиты", Kind = "shell_strain_state", SectionId = 1 });
        db.SaveCalcTask(new CalcTask { Tag = "НДС балки", Kind = "strain_state", SectionId = 1 });
        var (tasks, _, _, _) = db.CrossSectionReferences(1);
        Assert.Equal(["НДС балки"], tasks);
    }

    [Fact]
    public void UsesMaterial_ConcreteRebarAndLayer()
    {
        var (db, b25, a500) = Project("m.db");
        using var owner = db;
        var ps = Plate(b25, a500);
        Assert.True(PlateSectionAuthoring.UsesMaterial(ps, b25.Id));
        Assert.True(PlateSectionAuthoring.UsesMaterial(ps, a500.Id));
        ps.RebarLayers[0].MaterialId = 99;
        Assert.True(PlateSectionAuthoring.UsesMaterial(ps, 99));
        Assert.False(PlateSectionAuthoring.UsesMaterial(ps, 0));
    }
}

/// <summary>Страница сечения пластины: правка черновика, запись только по «Сохранить», «*», «Не сохранять», удаление.</summary>
[Collection("AppViewModel pages")]
public class PlateSectionPageVMTests
{
    static void WithApp(Action<AppViewModel> test) => RunSta(() =>
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-platevm-{Guid.NewGuid():N}.db");
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

    static (Material B25, Material A500, PlateSection Plate) Seed(AppViewModel app)
    {
        var b25 = MaterialCatalog.CreateHeavyConcrete("B25")!;
        var a500 = MaterialCatalog.CreateRebar("A500C")!;
        Assert.Empty(MaterialAuthoring.Save(app.db, b25));
        Assert.Empty(MaterialAuthoring.Save(app.db, a500));
        var ps = PlateSectionAuthoringTests.Plate(b25, a500);
        Assert.Empty(PlateSectionAuthoring.Save(app.db, ps));
        return (b25, a500, ps);
    }

    [Fact]
    public void Edit_DirtyModelUntouched_SaveCopies_DiscardRestores() => WithApp(app =>
    {
        var (_, _, ps) = Seed(app);
        var vm = new PlateSectionVM(ps, app);
        Assert.False(vm.IsDirty);

        vm.HMm = 250;
        vm.RebarLayers[0].DiameterXMm = 16;
        Assert.True(vm.IsDirty);
        Assert.Equal(0.2, ps.H);                                  // расчёт не видит несохранённое
        Assert.Equal(0.012, ps.RebarLayers[0].DiameterX);

        Assert.True(vm.Save());
        Assert.False(vm.IsDirty);
        Assert.Equal(0.25, ps.H);
        Assert.Equal(Math.PI * 0.016 * 0.016 / 4 / 0.2, ps.RebarLayers[0].Asx, 12);

        vm.Tag = "Другое";
        vm.AddRebarLayer();
        Assert.Equal(string.Format(Loc.S("PlateLayerDefaultName"), 2), vm.RebarLayers[1].Name);
        Assert.True(vm.IsDirty);
        vm.DiscardChanges();
        Assert.False(vm.IsDirty);
        Assert.Equal("Плита 200", vm.Tag);
        Assert.Single(vm.RebarLayers);
        Assert.Single(ps.RebarLayers);
    });

    [Fact]
    public void Save_Invalid_KeepsDirty_ModelUnchanged() => WithApp(app =>
    {
        var (_, _, ps) = Seed(app);
        var vm = new PlateSectionVM(ps, app);
        vm.RebarLayers[0].ZsxMm = -150;                           // вне толщины 200
        Assert.False(vm.Save());
        Assert.True(vm.IsDirty);
        Assert.Equal(-0.07, ps.RebarLayers[0].Zsx);
    });

    [Fact]
    public void NewPage_SaveAddsToPool_LeaveAsks() => WithApp(app =>
    {
        var (b25, a500, _) = Seed(app);
        // Страница без Application не строится (стили App.xaml) — тот же VM в пустом элементе.
        var vm = new PlateSectionVM(new PlateSection(), app);
        app.CurrentPage = new System.Windows.Controls.UserControl { DataContext = vm };
        Assert.False(vm.IsDirty);
        vm.Tag = "Стена 160";
        vm.HMm = 160;
        vm.ConcreteMaterial = app.Concretes.First(m => m.Id == b25.Id);
        vm.RebarMaterial = app.Armatures.First(m => m.Id == a500.Id);
        Assert.EndsWith(" *", app.CurrentPageTitle);

        string? asked = null;
        app.AskSaveOnLeave = name => { asked = name; return System.Windows.MessageBoxResult.Yes; };
        app.CurrentPage = null!;
        Assert.Equal("Стена 160", asked);
        Assert.Null(app.CurrentPage);
        Assert.Contains(app.PlateSections, p => p.Tag == "Стена 160" && p.Id > 0);
        Assert.Contains(app.PlateSectionsLive, p => p.Tag == "Стена 160");
    });

    [Fact]
    public void Delete_Used_Refused_FreeShownDirty_DeletesWithoutAsk() => WithApp(app =>
    {
        var (_, _, ps) = Seed(app);
        app.db.SaveCalcTask(new CalcTask { Tag = "НДС плиты", Kind = "shell_strain_state", SectionId = ps.Id });
        app.AskConfirmDelete = _ => throw new Xunit.Sdk.XunitException("используемое — без подтверждения");
        // Отказ показывает MessageBox — проверяем службой, что удаление запрещено.
        Assert.NotEmpty(PlateSectionAuthoring.UsedBy(app.db, ps));

        var free = PlateSectionAuthoring.Draft(ps);
        free.Id = 0; free.Num = 0; free.Tag = "Свободная";
        Assert.Empty(PlateSectionAuthoring.Save(app.db, free));
        var vm = new PlateSectionVM(free, app);
        app.CurrentPage = new System.Windows.Controls.UserControl { DataContext = vm };
        vm.HMm = 300;              // правка без сохранения
        app.AskConfirmDelete = _ => System.Windows.MessageBoxResult.Yes;
        app.AskSaveOnLeave = _ => throw new Xunit.Sdk.XunitException("вопрос о сохранении удаляемого не ожидался");
        app.DeletePlateSection(free);
        Assert.Null(app.CurrentPage);
        Assert.DoesNotContain(free, app.PlateSections);
        Assert.Contains(ps, app.PlateSections);
    });

    [Fact]
    public void Duplicate_CopiesAllFields() => WithApp(app =>
    {
        var (_, _, ps) = Seed(app);
        ps.PlateModel = "char1d_axial";
        ps.ConcreteDiagramType = DiagrammType.SP63;
        Assert.Empty(PlateSectionAuthoring.Save(app.db, ps));
        app.DuplicatePlateSectionCommand.Execute(ps);
        var copy = Assert.Single(app.PlateSections, p => p != ps);
        Assert.Equal(string.Format(Loc.S("PlateCopyTagFormat"), "Плита 200"), copy.Tag);
        Assert.Equal("char1d_axial", copy.PlateModel);
        Assert.Equal(DiagrammType.SP63, copy.ConcreteDiagramType);
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
