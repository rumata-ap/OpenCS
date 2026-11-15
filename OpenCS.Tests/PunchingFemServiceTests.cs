using System.Globalization;
using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
using CScore.Sp63Punching;
using CScore.Sp63Punching.Fem;
using CSfea.CScoreBridge.Structural;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Продавливание по МКЭ-схеме CSfea (срез 3): плита 6×6 м толщиной 200 мм на z = 3 (сетка 0,25 м) на колонне
/// 0,4×0,4 м, низ колонны заделан; «Пост.» — −10 кПа на плиту. Сила по разрезу основания пирамиды (узловые силы КЭ)
/// обязана совпасть с силой колонны минус нагрузка основания: это одно и то же равновесие.
/// </summary>
public sealed class PunchingFemServiceTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-punching-fem-{Guid.NewGuid():N}.db");
    const double Q = -10e3, Step = 0.25, Half = 3.0, Zs = 3.0;
    const int N = 24;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }

    static string S(int v) => v.ToString(CultureInfo.InvariantCulture);
    static int SlabNode(int i, int j) => 10000 + j * (N + 1) + i;

    sealed record Setup(DatabaseService Db, FemSchema Schema, FemAnalysis Analysis);

    /// <summary>Схема с колонной в узле сетки (ci, cj); <paramref name="above"/> — колонна продолжается вверх до z = 6.</summary>
    Setup Create(int ci = N / 2, int cj = N / 2, bool above = false, double windX = 0)
    {
        var db = new DatabaseService(_path);
        var schema = new FemSchema { Tag = "Плита" };
        db.SaveFemSchema(schema);
        var frame = new Frame3D(new PlanarVector3(0, 0, Zs), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));
        var region = PlanarRegion.CreateFromContour(new Contour([-Half, Half, Half, -Half, -Half], [-Half, -Half, Half, Half, -Half], "S1"), [], frame, "S1");
        region.MeshMaxElementSizeM = Step;
        db.AddPlanarRegion(region, schema.Id);

        var concrete = MaterialCatalog.CreateHeavyConcrete("B25", CatalogDirectory())!;
        var rebar = MaterialCatalog.CreateRebar("А500С", CatalogDirectory())!;
        db.AddMaterial(concrete);
        db.AddMaterial(rebar);
        var plate = new PlateSection
        {
            Tag = "П200", H = 0.2, ConcreteMaterialId = concrete.Id, RebarMaterialId = rebar.Id,
            RebarLayers =
            [
                new PlateRebarLayer { Name = "низ", InputMode = "direct", Asx = 5e-4, Asy = 5e-4, Zsx = -0.07, Zsy = -0.07, Face = RebarFace.MinusN },
                new PlateRebarLayer { Name = "верх", InputMode = "direct", Asx = 5e-4, Asy = 5e-4, Zsx = 0.07, Zsy = 0.07, Face = RebarFace.PlusN },
            ],
        };
        db.SavePlateSection(plate);
        var column = new CrossSection
        {
            Id = 9001, Tag = "К400",
            Areas =
            [
                new MaterialArea
                {
                    Tag = "concrete", Category = AreaCategory.Region, MaterialId = concrete.Id,
                    Hull = new Contour(new[] { -0.2, 0.2, 0.2, -0.2, -0.2 }, new[] { -0.2, -0.2, 0.2, 0.2, -0.2 }, "outer"),
                },
            ],
        };

        double cx = -Half + Step * ci, cy = -Half + Step * cj;
        var nodes = new List<FemNode>
        {
            new() { Id = -1, NodeTag = "1", X = cx, Y = cy, Z = 0, DofMask = 63 },
            new() { Id = -2, NodeTag = "2", X = cx, Y = cy, Z = Zs },
        };
        var members = new List<FemMember>
        {
            new() { Id = -1, ElemTag = "C1", ElemType = "beam", NodeIdsJson = "[1,2]", CrossSectionId = column.Id },
            new()
            {
                Id = -10, ElemTag = "S1", ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = region.Id, Kind = "plate",
                ThicknessM = 0.2, PlateSectionId = plate.Id,
            },
        };
        if (above)
        {
            nodes.Add(new FemNode { Id = -3, NodeTag = "3", X = cx, Y = cy, Z = 6 });
            members.Add(new FemMember { Id = -2, ElemTag = "C2", ElemType = "beam", NodeIdsJson = "[2,3]", CrossSectionId = column.Id });
        }
        var loadCases = new List<FemLoadCase> { new() { Id = -1, Tag = "Пост." }, new() { Id = -2, Tag = "Ветер" } };
        var load = new FemElementLoad { LoadCaseId = -1, LoadKind = FemElementLoadKinds.Uniform, Axis = "z", CoordinateSystem = "global" };
        load.SetTargetTags(Enumerable.Range(1, N * N).Select(S).ToArray());
        load.SetValues([Q]);
        var meshNodeLoads = new List<FemMeshNodeLoad>();
        if (windX != 0) meshNodeLoads.Add(new() { LoadCaseId = -2, MeshNodeTag = above ? "203" : S(SlabNode(ci, cj)), Fx = windX });
        else meshNodeLoads.Add(new() { LoadCaseId = -2, MeshNodeTag = S(SlabNode(0, 0)), Fz = -1e3 });
        db.SaveFemSchemaEdit(schema.Id, nodes, members, [], loadCases, [], [], [], null, [load], meshNodeLoads);

        var meshNodes = new List<FemMeshNode>();
        var meshElements = new List<FemElement>();
        for (int j = 0; j <= N; j++)
            for (int i = 0; i <= N; i++)
                meshNodes.Add(new FemMeshNode
                {
                    NodeTag = S(SlabNode(i, j)), X = -Half + Step * i, Y = -Half + Step * j, Z = Zs,
                    SourceNodeTag = i == ci && j == cj ? "2" : null,
                });
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
                meshElements.Add(new FemElement
                {
                    ElemTag = S(1 + j * N + i), ElemType = "shell", SourceMemberTag = "S1", ThicknessM = 0.2,
                    NodeIdsJson = $"[{SlabNode(i, j)},{SlabNode(i + 1, j)},{SlabNode(i, j + 1)},{SlabNode(i + 1, j + 1)}]",
                });
        string top = S(SlabNode(ci, cj));
        string Col(int k) => k == 3 ? top : S(100 + k);
        for (int k = 0; k < 3; k++)
            meshNodes.Add(new FemMeshNode { NodeTag = S(100 + k), X = cx, Y = cy, Z = k, SourceNodeTag = k == 0 ? "1" : null });
        for (int k = 0; k < 3; k++)
            meshElements.Add(new FemElement { ElemTag = S(5000 + k), ElemType = "beam", SourceMemberTag = "C1", NodeIdsJson = $"[{Col(k)},{Col(k + 1)}]" });
        if (above)
        {
            string Up(int k) => k == 3 ? top : S(200 + k);
            for (int k = 4; k <= 6; k++)
                meshNodes.Add(new FemMeshNode { NodeTag = S(200 + k), X = cx, Y = cy, Z = k, SourceNodeTag = k == 6 ? "3" : null });
            for (int k = 3; k < 6; k++)
                meshElements.Add(new FemElement { ElemTag = S(6000 + k), ElemType = "beam", SourceMemberTag = "C2", NodeIdsJson = $"[{Up(k)},{Up(k + 1)}]" });
        }
        db.SaveFemMeshSnapshot(schema.Id, meshNodes, meshElements);
        db.LoadAll();
        var parent = db.FemSchemas.Single(s => s.Id == schema.Id);
        db.CrossSections.Add(column);
        var lc = db.GetFemLoadCases(parent.Id);
        var stages = lc.Select(c => new FemAnalysisStage
        {
            Tag = c.Tag, LoadFactorStep = 1, MaxLoadFactor = 1,
            LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [c.Id] }.ToJson(),
        }).ToList();
        return new Setup(db, parent, SaveLinear(db, parent, stages));
    }

    static FemAnalysis SaveLinear(DatabaseService db, FemSchema schema, IReadOnlyList<FemAnalysisStage> stages)
    {
        var analysis = new FemAnalysis { SchemaId = schema.Id, Tag = "Линейный", Kind = FemCsfeaRunner.LinearAnalysisKind };
        analysis.ParamsJson = new FemAnalysisParams { Stages = [.. stages] }.ToJson();
        var input = FemCsfeaInputBuilder.Build(db, schema.Id, new FemCsfeaSetup { Stages = stages, GrossBars = true });
        Assert.DoesNotContain(input.Diagnostics, d => d.IsError);
        var adapted = FemRcModelAdapter.Adapt(input);
        Assert.False(adapted.HasErrors, string.Join("; ", adapted.Report));
        var run = RcLinearAnalysis.Run(adapted.Model, new RcLinearOptions { ShellForceAngles = adapted.ShellForceAngles });
        var summary = new FemCsfeaResultSummary
        {
            Kind = analysis.Kind, Completed = true,
            Steps = run.Cases.Select((c, i) => new FemCsfeaStepSummary(i + 1, i, 1, 1.0, false, true, 1, 0, null, Fields: true)).ToList(),
        };
        var result = new CalcResult
        {
            TaskKind = FemCsfeaRunner.TaskKind, TaskTag = analysis.Tag, Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            Status = "ok", DataJson = summary.ToJson(),
        };
        db.SaveCalcResult(result);
        db.SaveFemResultSteps(result.Id, run.Cases.Select((c, i) => (i + 1, FemCsfeaStepFieldsCodec.Pack(c.Fields))));
        analysis.ResultId = result.Id;
        analysis.Status = "ok";
        db.SaveFemAnalysis(analysis);
        return analysis;
    }

    /// <summary>Каталог материалов: вверх от сборки, иначе — от исходника теста (сборка вне репозитория).</summary>
    static string CatalogDirectory([System.Runtime.CompilerServices.CallerFilePath] string source = "")
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenCS.sln"))) dir = dir.Parent;
        dir ??= new FileInfo(source).Directory!.Parent!;
        return Path.Combine(dir.FullName, "OpenCS.App", "DataSource");
    }

    (PunchingFemSchemaData Data, PunchingFemSource Source, PunchingFemJoint Joint, PunchingFemNodeForces Forces) Evaluate(Setup s)
    {
        var data = PunchingFemService.Load(s.Db, s.Schema, new PunchingFemOptions());
        var joint = Assert.Single(data.Joints);
        Assert.True(joint.NoCheck == null, joint.NoCheckText);
        var diag = new List<FemValidationDiagnostic>();
        var source = PunchingFemService.OpenCsfea(s.Db, s.Schema, s.Analysis.Id, diag);
        Assert.True(source != null, string.Join("; ", diag.Select(d => d.Message)));
        Assert.True(source!.Nodal != null, source.NodalUnavailable);
        var forces = PunchingFemService.Evaluate(data, source, joint);
        Assert.True(forces.CutFailure == null, forces.CutFailure);
        return (data, source, joint, forces);
    }

    [Fact]
    public void InteriorColumn_ContourEqualsColumnMinusBaseLoad()
    {
        var s = Create();
        using var db = s.Db;
        var (_, source, joint, forces) = Evaluate(s);

        var r = Assert.IsType<PunchingLoadArea.Rectangle>(joint.Area);
        Assert.Equal(0.4, r.Bx, 9);
        Assert.Equal(0.2 - 0.03, joint.H0, 9);   // верхний слой: z = 0,07 → a = 0,03

        var key = source.Keys.Single(k => k.Label == "Пост.").Key;
        var f = forces.ByKey[key];
        double total = -Q * 2 * Half * 2 * Half / 1e3;            // 360 кН
        double baseArea = Math.Pow(0.4 + 2 * joint.H0, 2);
        Assert.Equal(total, f.ColumnAction!.Value, 3);
        Assert.Equal(Q / 1e3 * baseArea, f.BaseLoad!.Value, 6);
        Assert.Equal(f.ColumnForce!.Value, f.ContourForce!.Value, 3);
        Assert.Equal(PunchingFemForceMethod.Contour, f.Method);
        Assert.Equal(PunchingLoadSide.FromBelow, f.Side);
        Assert.Equal(total + Q / 1e3 * baseArea, f.F, 3);
        Assert.True(Math.Abs(f.Mx) < 1e-3 && Math.Abs(f.My) < 1e-3, $"Mx = {f.Mx}, My = {f.My}");
    }

    [Fact]
    public void EccentricLoad_ColumnMomentAndContourAgree()
    {
        var s = Create();
        using var db = s.Db;
        var (_, source, joint, forces) = Evaluate(s);
        // «Ветер»: −1 кН в углу (−3, −3): сила 1 кН, момент на плиту от колонны = −(рычаг × сила груза).
        var f = forces.ByKey[source.Keys.Single(k => k.Label == "Ветер").Key];
        Assert.Equal(1.0, f.ColumnAction!.Value, 4);
        Assert.Equal(0.0, f.BaseLoad!.Value, 9);
        Assert.Equal(f.ColumnForce!.Value, f.ContourForce!.Value, 4);
        // Груз −1 кН в точке (−3, −3) от колонны: момент груза (r × F) = (−3·(−1), 3·(−1)·… ) → колонна уравновешивает.
        Assert.Equal(-3.0, f.Mx, 3);
        Assert.Equal(3.0, f.My, 3);
    }

    [Fact]
    public void EdgeColumn_OpenContour_ContourEqualsColumnMinusBaseLoad()
    {
        var s = Create(ci: 1, cj: N / 2);   // ось колонны в 0,25 м от края: грань — в 0,05 м
        using var db = s.Db;
        var (_, source, joint, forces) = Evaluate(s);
        Assert.Single(joint.Edges);
        var f = forces.ByKey[source.Keys.Single(k => k.Label == "Пост.").Key];
        double baseArea = (0.25 + 0.2 + joint.H0) * (0.4 + 2 * joint.H0);
        Assert.Equal(Q / 1e3 * baseArea, f.BaseLoad!.Value, 6);
        Assert.Equal(f.ColumnForce!.Value, f.ContourForce!.Value, 3);
    }

    [Fact]
    public void ColumnAbove_NetForceIsDifference()
    {
        var s = Create(above: true);
        using var db = s.Db;
        var (_, source, joint, forces) = Evaluate(s);
        Assert.Equal(2, joint.Bars.Count);
        var f = forces.ByKey[source.Keys.Single(k => k.Label == "Пост.").Key];
        // Верхняя колонна без нагрузки: вся сила плиты — в нижнюю.
        Assert.Equal(360.0, f.ColumnAction!.Value, 2);
        Assert.Equal(f.ColumnForce!.Value, f.ContourForce!.Value, 3);
    }

    [Fact]
    public void CheckRunner_EnvelopeAndRowTask()
    {
        var s = Create();
        using var db = s.Db;
        var p = new PunchingFemCheckParams { SourceRef = s.Analysis.Id.ToString(CultureInfo.InvariantCulture) };
        var data = PunchingFemCheckRunner.Compute(db, s.Schema.Id, p, CalcType.C);
        Assert.Null(data.Error);
        var j = Assert.Single(data.Joints);
        Assert.Null(j.NoCheck);
        Assert.Equal(2, j.Rows.Count);
        Assert.Equal("Пост.", j.GoverningKey);
        Assert.True(j.Ratio > 0.1, $"Кисп = {j.Ratio}");
        Assert.Equal(j.Rows.Max(r => r.Ratio), data.Ratio);
        Assert.Contains(j.Status, new[] { "concrete", "reinforcement", "not_ensured" });
        Assert.Equal(j.Passed, data.Passed);

        // Задача по строке — та же проверка ядра с полной трассировкой.
        var row = j.Rows.Single(r => r.Key == "Пост.");
        var tp = PunchingFemCheckRunner.RowParams(j, row)!;
        Assert.Empty(tp.Validate());
        var st = PunchingRunner.ResolveStrengths(tp, db.Materials, CalcType.C, out var errors);
        Assert.Empty(errors);
        Assert.Equal(row.Ratio, PunchingChecker.Check(tp.ToInput(st.Rbt, st.Rsw)).Utilization, 12);
        Assert.Equal("Пост.", tp.Source!.Key);

        // Круговой проход JSON.
        var back = PunchingFemCheckData.Parse(data.ToJson())!;
        Assert.Equal(data.Ratio, back.Ratio);
        Assert.Equal(row.F, back.Joints[0].Rows.Single(r => r.Key == "Пост.").F);
    }

    [Fact]
    public void CheckRunner_ExcludedJoint_NoAnalysis_Error()
    {
        var s = Create();
        using var db = s.Db;
        var none = PunchingFemCheckRunner.Compute(db, s.Schema.Id, new PunchingFemCheckParams(), CalcType.C);
        Assert.NotNull(none.Error);
        var data = PunchingFemCheckRunner.Compute(db, s.Schema.Id, new PunchingFemCheckParams
        {
            SourceRef = s.Analysis.Id.ToString(CultureInfo.InvariantCulture),
            Overrides = [new PunchingFemJointOverride { Node = S(SlabNode(N / 2, N / 2)), Excluded = true }],
        }, CalcType.C);
        Assert.Empty(data.Joints);
        Assert.Contains(data.Warnings, w => w.Code == "punching_fem_no_joints");
    }

    [Fact]
    public void TaskParams_RunCore()
    {
        var s = Create();
        using var db = s.Db;
        var (data, source, joint, forces) = Evaluate(s);
        var (key, label) = source.Keys.Single(k => k.Label == "Пост.");
        var check = new PunchingFemCheckParams { ConcreteMaterialId = 0 };
        var p = PunchingFemService.TaskParams(joint, forces.ByKey[key], check, s.Schema.Tag, label);
        Assert.Empty(p.Validate());
        Assert.True(p.Forces.MomentsAreMloc);
        Assert.Equal("contour", p.Source!.Method);
        var st = PunchingRunner.ResolveStrengths(p, db.Materials, CalcType.C, out var errors);
        Assert.Empty(errors);
        var result = PunchingChecker.Check(p.ToInput(st.Rbt, st.Rsw));
        Assert.True(result.GoverningIndex >= 0);
        Assert.Equal(forces.ByKey[key].F, result.Fred, 9);
    }
}
