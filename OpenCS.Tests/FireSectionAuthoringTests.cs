using CScore;
using CScore.Fire.Entities;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Служба огневых сечений: проверка полей и рёбер по контуру, номер и имя, копия (без теплового расчёта), запрет
/// удаления используемого задачами, смена сечения (задачи переходят, их результаты удаляются), запуск теплового
/// расчёта и его устаревание при изменении входных данных.
/// </summary>
[Collection("Loc language")]
public sealed class FireSectionAuthoringTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_fire_{Guid.NewGuid():N}");
    public FireSectionAuthoringTests()
    {
        Directory.CreateDirectory(_dir);
        Loc.SetLanguage("ru-RU");
    }
    public void Dispose()
    {
        Loc.SetLanguage(null);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    /// <summary>Прямоугольное бетонное сечение (м) с одним отверстием по желанию, в пуле проекта под заданным Id.</summary>
    internal static CrossSection Rect(DatabaseService db, int id, string tag, double w = 0.2, double h = 0.4, bool hole = false)
    {
        var hull = new Contour([0.0, w, w, 0.0, 0.0], [0.0, 0.0, h, h, 0.0], "rect") { Type = ContourType.Hull };
        var area = new MaterialArea { Tag = "concrete", Category = AreaCategory.Region, Contours = [hull],
            Material = new Material { Type = MatType.Concrete, Tag = "B25" } };
        area.Hull = hull;
        if (hole)
            area.Contours.Add(new Contour([0.05, 0.1, 0.1, 0.05, 0.05], [0.1, 0.1, 0.2, 0.2, 0.1], "h") { Type = ContourType.Hole });
        var s = new CrossSection { Id = id, Tag = tag, Areas = [area] };
        db.CrossSections.Add(s);
        return s;
    }

    DatabaseService NewDb(string name) => new(Path.Combine(_dir, name));

    static FireSectionDef Def(int sectionId, string tag = "Ф1") => new()
    {
        Tag = tag, SectionId = sectionId, FireDurationMin = 1, FireCurve = "iso834", MeshStepM = 0.02, TimeStepS = 30,
        SnapshotStepMin = 0.5, Algorithm = "ruppert", SmoothIterTri = 2, BcPreset = "3-sided", HoleBcPreset = "ambient",
    };

    static void NoErrors(IReadOnlyList<FieldError> errors) =>
        Assert.True(errors.Count == 0, string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}")));

    static void HasError(IReadOnlyList<FieldError> errors, string path) =>
        Assert.True(errors.Any(e => e.Path == path), $"нет ошибки {path}: " + string.Join("; ", errors.Select(e => e.Path)));

    [Fact]
    public void Validate_EachError()
    {
        var db = NewDb("v.db");
        Rect(db, 1, "К1", hole: true);
        NoErrors(FireSectionAuthoring.Validate(db, Def(1)));

        void Bad(Action<FireSectionDef> change, string path)
        {
            var d = Def(1);
            change(d);
            HasError(FireSectionAuthoring.Validate(db, d), path);
        }
        Bad(d => d.Tag = " ", "tag");
        Bad(d => d.SectionId = 99, "section");
        Bad(d => d.FireDurationMin = 0, "fire.durationMin");
        Bad(d => d.FireDurationMin = 1000, "fire.durationMin");
        Bad(d => d.FireCurve = "iso", "fire.curve");
        Bad(d => d.AggregateType = "granite", "fire.aggregate");
        Bad(d => d.MeshStepM = double.NaN, "mesh.stepMm");
        Bad(d => d.Algorithm = "x", "mesh.algorithm");
        Bad(d => d.SmoothIterTri = -1, "mesh.smoothing");
        Bad(d => d.TimeStepS = 0, "time.stepS");
        Bad(d => d.SnapshotStepMin = 5, "time.snapshotStepMin");
        Bad(d => d.Theta = 1.5, "time.theta");
        Bad(d => d.PicardTolCelsius = 0, "time.picardTol");
        Bad(d => d.PicardMaxIter = 0, "time.picardMaxIter");
        Bad(d => d.BcPreset = "4-sided", "bc.preset");
        Bad(d => d.HoleBcPreset = "fire", "bc.holes");
        Bad(d => d.Edges.Add(new() { ContourType = "outer", EdgeIndex = 4, BcType = "fire" }), "bc.edges[0].edge");
        Bad(d => d.Edges.Add(new() { ContourType = "hole", HoleIndex = 1, EdgeIndex = 0, BcType = "fire" }), "bc.edges[0].hole");
        Bad(d => d.Edges.Add(new() { ContourType = "outer", EdgeIndex = 0, BcType = "hot" }), "bc.edges[0].type");
        Bad(d => d.Edges.Add(new() { ContourType = "outer", EdgeIndex = 0, BcType = "fire", Emissivity = 2 }), "bc.edges[0].emissivity");
        Bad(d =>
        {
            d.Edges.Add(new() { ContourType = "outer", EdgeIndex = 1, BcType = "fire" });
            d.Edges.Add(new() { ContourType = "outer", EdgeIndex = 1, BcType = "ambient" });
        }, "bc.edges[1].edge");

        var ok = Def(1);
        ok.Edges.Add(new() { ContourType = "outer", EdgeIndex = 3, BcType = "ambient", AlphaConv = 9, Emissivity = 0.8 });
        ok.Edges.Add(new() { ContourType = "hole", HoleIndex = 0, EdgeIndex = 3, BcType = "adiabatic" });
        NoErrors(FireSectionAuthoring.Validate(db, ok));
    }

    [Fact]
    public void Validate_SectionWithoutConcreteContour_Rejected()
    {
        var db = NewDb("nc.db");
        db.CrossSections.Add(new CrossSection { Id = 5, Tag = "Пусто" });
        HasError(FireSectionAuthoring.Validate(db, Def(5)), "section");
        var steel = Rect(db, 6, "Сталь");
        steel.Areas[0].Material = new Material { Type = MatType.Steel, Tag = "C245" };
        HasError(FireSectionAuthoring.Validate(db, Def(6)), "section");
    }

    [Fact]
    public void Save_NumberAndDefaultTag_Duplicate_WithoutThermal()
    {
        var db = NewDb("s.db");
        Rect(db, 1, "К1");
        var a = Def(1);
        a.Edges.Add(new() { ContourType = "outer", EdgeIndex = 0, BcType = "ambient", AlphaConv = 9, Emissivity = 0.8 });
        NoErrors(FireSectionAuthoring.Save(db, a));
        Assert.Equal(1, a.Num);
        Assert.Equal(2, FireSectionAuthoring.NextNum(db));
        Assert.Equal("Огневое сечение 2", FireSectionAuthoring.DefaultTag(FireSectionAuthoring.NextNum(db)));

        Assert.True(FireThermalRunner.Run(db, a, db.CrossSections[0]) > 0);
        var copy = FireSectionAuthoring.Duplicate(db, a);
        Assert.Equal("Ф1 (копия)", copy.Tag);
        Assert.Equal(2, copy.Num);
        Assert.NotEqual(a.Id, copy.Id);
        Assert.Single(copy.Edges);
        Assert.NotSame(a.Edges[0], copy.Edges[0]);
        Assert.Null(db.GetFireThermalResultInfo(copy.Id));
        Assert.NotNull(db.GetFireThermalResultInfo(a.Id));
        Assert.False(FireSectionAuthoring.InputsChanged(a, copy));
        Assert.False(FireSectionAuthoring.SameContent(a, copy));
    }

    static CalcTask FireTask(FireSectionDef f, string kind = "fire_thermal_curvature") => new()
    {
        Tag = "Огневая", Kind = kind, SectionId = f.SectionId,
        ParamsJson = new FireThermalCurvatureParams { FireSectionId = f.Id }.ToJson(),
    };

    [Fact]
    public void Delete_UsedByTask_Refused_Unused_Deleted()
    {
        var db = NewDb("d.db");
        Rect(db, 1, "К1");
        var used = Def(1, "Ф1");
        NoErrors(FireSectionAuthoring.Save(db, used));
        var free = Def(1, "Ф2");
        NoErrors(FireSectionAuthoring.Save(db, free));
        var task = FireTask(used);
        NoErrors(CalcTaskAuthoring.Save(db, task));

        Assert.False(FireSectionAuthoring.TryDelete(db, used, out var usedBy));
        Assert.Contains(usedBy, u => u.Contains("Огневая"));
        Assert.Contains(used, db.FireSections);

        Assert.True(FireSectionAuthoring.TryDelete(db, free, out _));
        Assert.DoesNotContain(free, db.FireSections);
    }

    [Fact]
    public void Update_SectionChange_MovesTasks_DeletesTheirResults_KeepsThermal()
    {
        var db = NewDb("u.db");
        Rect(db, 1, "К1");
        Rect(db, 2, "К2", w: 0.3);
        var f = Def(1);
        NoErrors(FireSectionAuthoring.Save(db, f));
        var task = FireTask(f);
        NoErrors(CalcTaskAuthoring.Save(db, task));
        db.SaveCalcResult(new CalcResult { TaskId = task.Id, TaskKind = task.Kind, TaskTag = task.Tag, Created = "2026-11-24 12:00:00" });
        FireThermalRunner.Run(db, f, db.CrossSections[0]);

        var (tasks, results) = FireSectionAuthoring.SectionChangeImpact(db, f);
        Assert.Single(tasks);
        Assert.Equal(1, results);

        var edited = FireSectionAuthoring.Clone(f);
        edited.SectionId = 2;
        NoErrors(FireSectionAuthoring.Update(db, f, edited));
        Assert.Equal(2, f.SectionId);
        Assert.Equal(2, task.SectionId);
        Assert.DoesNotContain(db.CalcResults, r => r.TaskId == task.Id);
        NoErrors(CalcTaskAuthoring.Validate(db, task));
        var (has, stale) = FireThermalRunner.State(db, f, db.CrossSections[1]);
        Assert.True(has);
        Assert.NotNull(stale);
    }

    [Fact]
    public void Runner_CurrentThenStale_AfterDurationChange_MeshBlocked()
    {
        var db = NewDb("r.db");
        var section = Rect(db, 1, "К1");
        var f = Def(1);
        NoErrors(FireSectionAuthoring.Save(db, f));
        Assert.Equal((false, (string?)null), FireThermalRunner.State(db, f, section));

        FireThermalRunner.Run(db, f, section);
        Assert.Equal((true, (string?)null), FireThermalRunner.State(db, f, section));

        var edited = FireSectionAuthoring.Clone(f);
        edited.FireDurationMin = 2;
        NoErrors(FireSectionAuthoring.Update(db, f, edited));
        var (has, stale) = FireThermalRunner.State(db, f, section);
        Assert.True(has);
        Assert.NotNull(stale);

        var coarse = FireSectionAuthoring.Clone(f);
        coarse.MeshStepM = 0.05;
        var check = FireThermalRunner.Check(coarse, section);
        Assert.NotNull(check.Error);
        Assert.Contains("50", check.Error);
        Assert.Throws<InvalidOperationException>(() => FireThermalRunner.Run(db, coarse, section));
    }

    [Fact]
    public void EffectiveAggregate_OwnThenConcreteMaterialThenSilicate()
    {
        var db = NewDb("a.db");
        var s = Rect(db, 1, "К1");
        var f = Def(1);
        Assert.Equal("silicate", FireSectionAuthoring.EffectiveAggregate(f, s));
        s.Areas[0].Material!.AggregateType = "carbonate";
        Assert.Equal("carbonate", FireSectionAuthoring.EffectiveAggregate(f, s));
        f.AggregateType = "lightweight";
        Assert.Equal("lightweight", FireSectionAuthoring.EffectiveAggregate(f, s));
    }
}
