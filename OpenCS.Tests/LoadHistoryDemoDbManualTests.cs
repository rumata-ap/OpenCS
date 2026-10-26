using CScore;
using CScore.History;
using Microsoft.Data.Sqlite;
using OpenCS.LoadHistory;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Демопроект для снимков окон истории нагружения: балка 300×500 (B25, 3⌀20 А500С понизу), стадийное сечение
/// «Балка с надбетонкой» (часть 1 — ссылка на балку, часть 2 — надбетонка 300×100 из B30 с 2⌀12), набор усилий
/// и три задачи load_history с посчитанными результатами. Запуск:
/// <c>OPENCS_LOAD_HISTORY_DEMO_DB=путь\к\файлу.db dotnet test OpenCS.Tests --filter FullyQualifiedName~LoadHistoryDemoDbManualTests</c>.
/// Без переменной тест выходит.
/// </summary>
public sealed class LoadHistoryDemoDbManualTests(ITestOutputHelper output)
{
    [Fact]
    public void CreateDemoDatabase()
    {
        var path = Environment.GetEnvironmentVariable("OPENCS_LOAD_HISTORY_DEMO_DB");
        if (string.IsNullOrWhiteSpace(path)) return;
        if (File.Exists(path)) File.Delete(path);
        try
        {
            using (var db = new DatabaseService(path))
                Build(db);
            using (var db = new DatabaseService(path))
            {
                db.LoadAll();
                RunTasks(db);
            }
            output.WriteLine($"База: {path}");
        }
        finally { SqliteConnection.ClearAllPools(); }
    }

    static MaterialArea Region(string tag, Material m, double x0, double x1, double y0, double y1)
    {
        var a = new MaterialArea
        {
            Tag = tag, Category = AreaCategory.Region, Material = m, MaterialId = m.Id, DiagrammType = DiagrammType.L3,
        };
        a.Contours.Add(new Contour([x0, x1, x1, x0, x0], [y0, y0, y1, y1, y0], "hull") { Type = ContourType.Hull });
        a.SetWKT();
        return a;
    }

    static MaterialArea Bars(string tag, Material m, double d, double y, params double[] xs)
    {
        var a = new MaterialArea
        {
            Tag = tag, Category = AreaCategory.RebarGroup, Material = m, MaterialId = m.Id, DiagrammType = DiagrammType.L2,
        };
        foreach (var x in xs) a.Fibers.Add(Fiber.CreatePoint(d, x, y));
        return a;
    }

    /// <summary>Материалы, балка и стадийное сечение демопроекта (в памяти, без сохранения).</summary>
    internal static (Material[] Materials, CrossSection Beam, StagedSection Staged) CreateSections()
    {
        var b25 = MaterialCatalog.CreateHeavyConcrete("B25")!;
        var b30 = MaterialCatalog.CreateHeavyConcrete("B30")!;
        var a500 = MaterialCatalog.CreateRebar("А500С")!;

        // Балка 300×500, ось x — по ширине, y — по высоте, начало в центре балки.
        var beamConcrete = Region("Балка 300×500 (B25)", b25, -0.15, 0.15, -0.25, 0.25);
        var beamBars = Bars("Низ 3⌀20", a500, 0.020, -0.21, -0.10, 0.0, 0.10);
        var beamTopBars = Bars("Верх 2⌀10", a500, 0.010, 0.21, -0.10, 0.10);
        var topping = Region("Надбетонка 300×100 (B30)", b30, -0.15, 0.15, 0.25, 0.35);
        var toppingBars = Bars("Надбетонка 2⌀12", a500, 0.012, 0.30, -0.10, 0.10);

        var beam = new CrossSection { Num = 1, Tag = "Балка 300×500", Areas = [beamConcrete, beamBars, beamTopBars] };
        var staged = new StagedSection { Num = 2, Tag = "Балка с надбетонкой" };
        StagedPartsEditor.AddLinkedPart(staged, beam, "Балка");
        var own = StagedPartsEditor.AddOwnPart(staged, "Надбетонка");
        own.Areas.AddRange([topping, toppingBars]);
        StagedPartsEditor.Normalize(staged);
        return ([b25, b30, a500], beam, staged);
    }

    static void Build(DatabaseService db)
    {
        var (materials, beam, staged) = CreateSections();
        foreach (var m in materials) db.SaveMaterial(m);
        foreach (var a in staged.Areas)
        {
            a.MaterialId = a.Material!.Id; // материалы сохранены после создания областей
            db.SaveMaterialArea(a);
        }
        db.SaveCrossSection(beam);
        staged.Parts[0].SourceSectionId = beam.Id;
        db.SaveCrossSection(staged);

        var set = new ForceSet
        {
            Num = 1, Tag = "Нагрузки балки", Kind = "bar",
            Items =
            [
                new LoadItem { Label = "Монтаж: собственный вес и опалубка", Mx = -60 },
                new LoadItem { Label = "Эксплуатация", Mx = -110 },
                new LoadItem { Label = "Ремонт: частичная разгрузка", Mx = -40 },
            ],
        };
        db.SaveForceSet(set);
        db.LoadAll();
        set = db.ForceSets.Single(f => f.Id == set.Id);
        int ItemId(int num) => set.Items.Single(i => i.Num == num).Id;

        LoadHistoryStageParams Stage(string tag, int? part, StageForce f, int steps) =>
            new() { Tag = tag, ActivatePart = part, Force = f, Steps = steps };
        StageForce Item(int num) => new() { Mode = "item", ForceSetId = set.Id, ForceItemId = ItemId(num) };
        StageForce Manual(double mx) => new() { Mode = "manual", Mx = mx };
        var failure = new LoadHistoryStageParams { Tag = "failure", Kind = StageKind.Failure, Steps = 1, Force = Manual(-1) };

        // 1. Основной пример: монтаж → надбетонка → эксплуатация → разрушение, со сравнением «без истории».
        var basic = new LoadHistoryParams
        {
            Stages = [Stage("Монтаж", null, Item(1), 4), Stage("Надбетонка и эксплуатация", 1, Item(2), 5), failure],
            Failure = new FailureOptions { Steps = 40 },
        };
        // 2. Разгрузка и повторное нагружение + роль «Разрушается» у бетона надбетонки.
        var roles = new LoadHistoryParams
        {
            Stages =
            [
                Stage("Монтаж", null, Item(1), 4), Stage("Надбетонка и эксплуатация", 1, Item(2), 5),
                Stage("Ремонт (разгрузка)", null, Item(3), 3), Stage("Повторное нагружение", null, Manual(-110), 3), failure,
            ],
            Failure = new FailureOptions { Steps = 40 },
            PartRoles = [new PartRolesParams { PartIndex = 1, Concrete = PartLimitRole.Fails, Rebar = PartLimitRole.Limits }],
        };
        // 3. Эксплуатационный вид расчёта N: растяжение бетона учитывается, шаги после трещин — по кривизне.
        var serviceN = new LoadHistoryParams
        {
            Stages = [Stage("Монтаж", null, Item(1), 6), Stage("Надбетонка и эксплуатация", 1, Item(2), 6), failure],
            Failure = new FailureOptions { Steps = 40 },
            Fallback = LoadStepFallback.CurvatureControl,
        };

        int num = 1;
        foreach (var (tag, calc, p) in new[]
        {
            ("История: монтаж, надбетонка, разрушение", CalcType.C, basic),
            ("История: разгрузка и роль «Разрушается»", CalcType.C, roles),
            ("История N: с растяжением бетона", CalcType.N, serviceN),
        })
        {
            var errors = p.Validate(staged.Parts.Count);
            Assert.Empty(errors);
            db.SaveCalcTask(new CalcTask
            {
                Num = num++, Tag = tag, Kind = "load_history", SectionId = staged.Id, CalcType = calc, ParamsJson = p.ToJson(),
            });
        }
    }

    void RunTasks(DatabaseService db)
    {
        var settings = CalcSettings.Default;
        foreach (var task in db.CalcTasks.Where(t => t.Kind == "load_history").ToList())
        {
            var section = db.CrossSections.Single(s => s.Id == task.SectionId);
            var r = TaskRunner.Run(task, section, new LoadItem(), settings, new TaskRunContext { Database = db });
            db.SaveCalcResult(r);
            var d = LoadHistoryResultData.Parse(r.DataJson);
            output.WriteLine($"{task.Tag}: {r.Status}; шагов {d.Steps.Count}/{d.StepsNoHistory?.Count}; " +
                $"M_lim {d.With.MLim:F1}/{d.Without?.MLim:F1}; M_peak {d.With.MPeak:F1}/{d.Without?.MPeak:F1}; " +
                $"стоп {d.With.StopReason}; предупреждений {d.Steps.Count(s => s.Warnings.Count > 0)}; {d.Error}");
            Assert.NotEqual("error", r.Status);
        }
    }
}
