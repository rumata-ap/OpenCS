using System.Globalization;
using CScore;
using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using CScore.Planar;
using CScore.PlateRebar;
using CScore.Submodel;
using CSfea.CScoreBridge.Structural;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Снятие масок фрагмента из сохранённого результата CSfea родителя (срез 3 C/D, задача 4). Родитель: плита 6×6 м на
/// z = 3 (сетка 4×4, КЭ 1–16, узлы 1001…1025) на четырёх колоннах 0,4×0,4 в углах, z 0…6 (КЭ по 1 м, узел z = 3 —
/// угол плиты), низ колонн заделан. Загружения: «Пост.» — −5 кПа на плиту (−180 кН); «Ветер» — 20 кН по +X в центре
/// плиты и −30 кН по Z на верху колонны C1 (вне полосы). Полоса 1,5…4,5 рассекает колонны в серединах КЭ.
/// Маска — действие отброшенной части на фрагмент, поэтому ΣF масок = −(нагрузки полосы): «Пост.» — +180 кН по Z на
/// нижних разрезах (по 45 кН на колонну; рамная поперечная сила от поворота угла плиты в сумме — ноль), «Ветер» —
/// −20 кН по X в сумме; верх C1 — −30 кН по Z без момента (консоль над полосой). Низ C1 сверяется с реакцией заделки:
/// кусок колонны под разрезом без нагрузок.
/// </summary>
public sealed partial class StoreyFragmentMaskServiceTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"opencs-fragment-maskservice-{Guid.NewGuid():N}.db");
    static readonly StoreyFragmentRequest Band = new(1.5, 4.5, "Этаж 2");
    const double Q = -5000, Wind = 20e3, Top = -30e3;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }

    static string S(int v) => v.ToString(CultureInfo.InvariantCulture);
    static int Slab(int i, int j) => 1001 + j * 5 + i;
    static readonly (int I, int J)[] Corners = [(0, 0), (4, 0), (4, 4), (0, 4)];
    static int ColumnNode(int c, int k) => k == 3 ? Slab(Corners[c - 1].I, Corners[c - 1].J) : 100 * c + k;

    sealed record Parent(DatabaseService Db, FemSchema Schema, FemSchema Fragment, CrossSection Column);

    Parent Create()
    {
        var db = new DatabaseService(_path);
        var schema = new FemSchema { Tag = "Каркас" };
        db.SaveFemSchema(schema);
        var frame = new Frame3D(new PlanarVector3(0, 0, 3), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));
        var region = PlanarRegion.CreateFromContour(new Contour([-3.0, 3, 3, -3, -3], [-3.0, -3, 3, 3, -3], "S1"), [], frame, "S1");
        region.MeshMaxElementSizeM = 1.5;
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
        var column = Square(9001, 0.4, concrete);

        var nodes = new List<FemNode>();
        var members = new List<FemMember>();
        for (int c = 1; c <= 4; c++)
        {
            var (x, y) = (-3 + 1.5 * Corners[c - 1].I, -3 + 1.5 * Corners[c - 1].J);
            nodes.Add(new FemNode { Id = -2 * c + 1, NodeTag = S(2 * c - 1), X = x, Y = y, Z = 0, DofMask = 63 });
            nodes.Add(new FemNode { Id = -2 * c, NodeTag = S(2 * c), X = x, Y = y, Z = 6 });
            members.Add(new FemMember
            {
                Id = -c, ElemTag = $"C{c}", ElemType = "beam", NodeIdsJson = $"[{-2 * c + 1},{-2 * c}]", CrossSectionId = column.Id,
            });
        }
        members.Add(new FemMember
        {
            Id = -10, ElemTag = "S1", ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = region.Id, Kind = "plate",
            ThicknessM = 0.2, PlateSectionId = plate.Id,
        });
        var loadCases = new List<FemLoadCase> { new() { Id = -1, Tag = "Пост." }, new() { Id = -2, Tag = "Ветер" } };
        var load = new FemElementLoad { LoadCaseId = -1, LoadKind = FemElementLoadKinds.Uniform, Axis = "z", CoordinateSystem = "global" };
        load.SetTargetTags(Enumerable.Range(1, 16).Select(S).ToArray());
        load.SetValues([Q]);
        var meshNodeLoads = new List<FemMeshNodeLoad>
        {
            new() { LoadCaseId = -2, MeshNodeTag = S(Slab(2, 2)), Fx = Wind },
            new() { LoadCaseId = -2, MeshNodeTag = S(ColumnNode(1, 6)), Fz = Top },
        };
        db.SaveFemSchemaEdit(schema.Id, nodes, members, [], loadCases, [], [], [], null, [load], meshNodeLoads);

        var meshNodes = new List<FemMeshNode>();
        var meshElements = new List<FemElement>();
        for (int j = 0; j <= 4; j++)
            for (int i = 0; i <= 4; i++)
                meshNodes.Add(new FemMeshNode { NodeTag = S(Slab(i, j)), X = -3 + 1.5 * i, Y = -3 + 1.5 * j, Z = 3 });
        for (int j = 0; j < 4; j++)
            for (int i = 0; i < 4; i++)
                meshElements.Add(new FemElement
                {
                    ElemTag = S(1 + j * 4 + i), ElemType = "shell", SourceMemberTag = "S1", ThicknessM = 0.2,
                    NodeIdsJson = $"[{Slab(i, j)},{Slab(i + 1, j)},{Slab(i, j + 1)},{Slab(i + 1, j + 1)}]",
                });
        for (int c = 1; c <= 4; c++)
        {
            var (x, y) = (-3 + 1.5 * Corners[c - 1].I, -3 + 1.5 * Corners[c - 1].J);
            for (int k = 0; k <= 6; k++)
                if (k != 3)
                    meshNodes.Add(new FemMeshNode
                    {
                        NodeTag = S(ColumnNode(c, k)), X = x, Y = y, Z = k,
                        SourceNodeTag = k == 0 ? S(2 * c - 1) : k == 6 ? S(2 * c) : null,
                    });
            for (int k = 0; k < 6; k++)
                meshElements.Add(new FemElement
                {
                    ElemTag = S(500 + 10 * c + k), ElemType = "beam", SourceMemberTag = $"C{c}",
                    NodeIdsJson = $"[{ColumnNode(c, k)},{ColumnNode(c, k + 1)}]",
                });
        }
        db.SaveFemMeshSnapshot(schema.Id, meshNodes, meshElements);
        db.LoadAll();
        var parent = db.FemSchemas.Single(s => s.Id == schema.Id);
        var built = StoreyFragmentService.Build(db, parent.Id, Band);
        var fragment = Assert.IsType<FemSchema>(built.Schema);
        // Сечение колонн — только в памяти (как в соседних тестах): в cross_sections его нет, LoadAll его сбрасывает.
        db.CrossSections.Add(column);
        return new Parent(db, parent, fragment, column);
    }

    // λmax = 1 явно: у стадии без него разбор параметров берёт легаси-максимум 10 (FemAnalysisParams.Parse).
    static FemAnalysisStage Single(string tag, int lc) => new()
    {
        Tag = tag, LoadFactorStep = 1, MaxLoadFactor = 1, LoadExpressionJson = new FemLoadExpression { Mode = FemLoadExpressionMode.Single, LoadCaseIds = [lc] }.ToJson(),
    };

    static FemAnalysisStage Sum(string tag, params int[] lcs) => new()
    {
        Tag = tag, LoadFactorStep = 1, MaxLoadFactor = 1,
        LoadExpressionJson = new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum, Terms = lcs.Select(id => new FemLoadTerm { LoadCaseId = id, Coefficient = 1 }).ToList(),
        }.ToJson(),
    };

    /// <summary>
    /// Результат постановки вида <paramref name="kind"/> со стадиями <paramref name="stages"/>, посчитанный линейно по
    /// <paramref name="solved"/> (у секущего — накопленные сочетания стадий) и записанный, как его пишет исполнитель.
    /// </summary>
    static FemAnalysis SaveResult(DatabaseService db, FemSchema parent, string kind, IReadOnlyList<FemAnalysisStage> stages,
        IReadOnlyList<FemAnalysisStage> solved, FemAnalysis? analysis = null)
    {
        analysis ??= new FemAnalysis { SchemaId = parent.Id, Tag = kind == FemCsfeaRunner.LinearAnalysisKind ? "Линейный" : "Секущий", Kind = kind };
        analysis.ParamsJson = new FemAnalysisParams { Stages = [.. stages] }.ToJson();
        var input = FemCsfeaInputBuilder.Build(db, parent.Id, new FemCsfeaSetup { Stages = solved, GrossBars = true });
        Assert.DoesNotContain(input.Diagnostics, d => d.IsError);
        var adapted = FemRcModelAdapter.Adapt(input);
        Assert.False(adapted.HasErrors, string.Join("; ", adapted.Report));
        var run = RcLinearAnalysis.Run(adapted.Model, new RcLinearOptions { ShellForceAngles = adapted.ShellForceAngles });
        var summary = new FemCsfeaResultSummary
        {
            Kind = kind, Completed = true,
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

    /// <summary>ΣF масок ключа и сила маски по разрезу «x,y|сторона» (узел разреза; C1 — «-3,-3»).</summary>
    static (PlanarVector3 Sum, Dictionary<string, Dof6> ByCut) Forces(DatabaseService db, FemSchema fragment, FragmentMaskKey key)
    {
        var cuts = db.GetFragmentCutsWithIds(fragment.Id).ToDictionary(c => c.Id, c => c.Cut);
        var nodes = db.GetFemNodes(fragment.Id).ToDictionary(n => n.NodeTag);
        var byCut = new Dictionary<string, Dof6>();
        var sum = PlanarVector3.Zero;
        foreach (var item in db.GetFragmentMasks(fragment.Id)!.Items.Where(i => i.Key == key))
        {
            var f = Assert.Single(item.Samples).F!;
            var cut = cuts[item.CutId];
            var n = nodes[cut.NodeTag!];
            byCut[string.Create(CultureInfo.InvariantCulture, $"{n.X:0.#},{n.Y:0.#}|{cut.Side}")] = f;
            sum += f.Force;
        }
        return (sum, byCut);
    }

    static void Near(PlanarVector3 expected, PlanarVector3 actual, double scale, string what) =>
        Assert.True((expected - actual).Length <= 1e-6 * scale, $"{what}: ожидалось {expected}, получено {actual}");

    [Fact]
    public void Extract_Linear_MasksBalanceBandLoads_AndTrackSource()
    {
        var p = Create();
        using var db = p.Db;
        var lc = db.GetFemLoadCases(p.Schema.Id);
        var fragmentLc = db.GetFemLoadCases(p.Fragment.Id);
        Assert.Equal(FragmentMaskStatus.None, StoreyFragmentMaskService.CheckMasks(db, p.Fragment));
        var stages = new[] { Single("Пост.", lc[0].Id), Single("Ветер", lc[1].Id) };
        var analysis = SaveResult(db, p.Schema, FemCsfeaRunner.LinearAnalysisKind, stages, stages);

        var r = StoreyFragmentMaskService.Extract(db, p.Fragment, FragmentMaskSet.SourceCsfeaLinear, S(analysis.Id));

        Assert.False(r.HasErrors, string.Join("\n", r.Diagnostics.Select(d => d.Message)));
        Assert.DoesNotContain(r.Diagnostics, d => d.Code is "fragment_mask_balance" or "fragment_mask_balance_skipped"
            or "fragment_mask_cut_unmatched" or "fragment_mask_no_forces" or "fragment_mask_no_displacement");
        Assert.Contains(r.Diagnostics, d => d.Code == "fragment_mask_summary");
        var set = db.GetFragmentMasks(p.Fragment.Id)!;
        Assert.Equal((FragmentMaskSet.SourceCsfeaLinear, S(analysis.Id), "Линейный"), (set.SourceKind, set.SourceRef, set.SourceLabel));
        Assert.Equal(8 * 2, set.Items.Count);
        Assert.Equal(r.Diagnostics.Count, StoreyFragmentBuildReport.FromJson(set.ReportJson).Count);

        var permanent = FragmentMaskKey.OfLoadCase(fragmentLc[0].Id);
        var (sumP, byCutP) = Forces(db, p.Fragment, permanent);
        Near(new(0, 0, -Q * 36), sumP, -Q * 36, "ΣF масок «Пост.»");
        Near(PlanarVector3.Zero, byCutP["-3,-3|Top"].Force, -Q * 36, "верх C1, «Пост.»");
        Assert.Equal(-Q * 9, byCutP["-3,-3|Bottom"].Z, 1e-6 * -Q * 36);
        // Кусок колонны C1 ниже разреза (0…1,5 м) без нагрузок: его действие на фрагмент = реакция заделки, момент —
        // перенесённый в точку разреза: M = M_R + (r_опоры − r_разреза) × F_R, r_опоры − r_разреза = (0, 0, −1,5).
        var fields = FemCsfeaStepFieldsCodec.Unpack(db.GetFemResultStep(analysis.ResultId!.Value, 1)!);
        int ri = Array.IndexOf(fields.ReactionNodeIds, ColumnNode(1, 0));
        var rf = new PlanarVector3(fields.Reactions[6 * ri], fields.Reactions[6 * ri + 1], fields.Reactions[6 * ri + 2]);
        var rm = new PlanarVector3(fields.Reactions[6 * ri + 3], fields.Reactions[6 * ri + 4], fields.Reactions[6 * ri + 5]);
        Assert.True(Math.Abs(rf.X) > 1e3, $"у угловой колонны рамная поперечная сила: {rf}");
        Near(rf, byCutP["-3,-3|Bottom"].Force, -Q * 36, "низ C1 = реакция, сила");
        Near(rm + new PlanarVector3(0, 0, -1.5).Cross(rf), byCutP["-3,-3|Bottom"].Moment, -Q * 36, "низ C1 = реакция, момент");

        var (sumW, byCutW) = Forces(db, p.Fragment, FragmentMaskKey.OfLoadCase(fragmentLc[1].Id));
        Near(new(-Wind, 0, 0), sumW, Wind, "ΣF масок «Ветер»");
        Near(new(0, 0, Top), byCutW["-3,-3|Top"].Force, Wind, "верх C1, «Ветер»");
        Near(PlanarVector3.Zero, byCutW["-3,-3|Top"].Moment, Wind, "верх C1, «Ветер», момент");

        Assert.Equal(FragmentMaskStatus.Current, StoreyFragmentMaskService.CheckMasks(db, p.Fragment));
        SaveResult(db, p.Schema, FemCsfeaRunner.LinearAnalysisKind, stages, stages, analysis);
        Assert.Equal(FragmentMaskStatus.Stale, StoreyFragmentMaskService.CheckMasks(db, p.Fragment));

        Assert.False(StoreyFragmentMaskService.Extract(db, p.Fragment, FragmentMaskSet.SourceCsfeaLinear, S(analysis.Id)).HasErrors);
        Assert.Equal(FragmentMaskStatus.Current, StoreyFragmentMaskService.CheckMasks(db, p.Fragment));
        Exec($"UPDATE fem_element_loads SET values_json='[-6000]' WHERE schema_id={p.Schema.Id}");
        Assert.Equal(FragmentMaskStatus.Stale, StoreyFragmentMaskService.CheckMasks(db, p.Fragment));

        db.DeleteFemAnalysis(analysis);
        Assert.Equal(FragmentMaskStatus.SourceDeleted, StoreyFragmentMaskService.CheckMasks(db, p.Fragment));
        Assert.NotNull(db.GetFragmentMasks(p.Fragment.Id));
    }

    /// <summary>
    /// Секущий источник: стадии накапливаются — «Пост.», затем «Ветер» поверх; ключи — сочетания {Пост. × 1} и
    /// {Пост. × 1 + Ветер × 1} на Id фрагмента, у второго ΣF = (−20 кН, 0, +180 кН).
    /// </summary>
    [Fact]
    public void Extract_Secant_KeysAreAccumulatedStageCombinations()
    {
        var p = Create();
        using var db = p.Db;
        var lc = db.GetFemLoadCases(p.Schema.Id);
        var fragmentLc = db.GetFemLoadCases(p.Fragment.Id);
        var analysis = SaveResult(db, p.Schema, FemCsfeaRunner.AnalysisKind,
            [Single("Пост.", lc[0].Id), Single("Ветер", lc[1].Id)], [Single("Пост.", lc[0].Id), Sum("Пост.+Ветер", lc[0].Id, lc[1].Id)]);

        var r = StoreyFragmentMaskService.Extract(db, p.Fragment, FragmentMaskSet.SourceCsfeaSecant, S(analysis.Id));

        Assert.False(r.HasErrors, string.Join("\n", r.Diagnostics.Select(d => d.Message)));
        Assert.DoesNotContain(r.Diagnostics, d => d.Code == "fragment_mask_balance");
        var keys = db.GetFragmentMasks(p.Fragment.Id)!.Items.Select(i => i.Key).Distinct().ToList();
        var both = FragmentMaskKey.OfStageTerms([(fragmentLc[0].Id, 1.0), (fragmentLc[1].Id, 1.0)]);
        Assert.Equal([FragmentMaskKey.OfStageTerms([(fragmentLc[0].Id, 1.0)]), both], keys);
        Near(new(-Wind, 0, -Q * 36), Forces(db, p.Fragment, both).Sum, -Q * 36, "ΣF масок сочетания");
        Assert.Equal(FragmentMaskStatus.Current, StoreyFragmentMaskService.CheckMasks(db, p.Fragment));
    }

    [Fact]
    public void Extract_Errors_KeepPreviousMasks()
    {
        var p = Create();
        using var db = p.Db;
        var lc = db.GetFemLoadCases(p.Schema.Id);
        var stages = new[] { Single("Пост.", lc[0].Id) };
        var analysis = SaveResult(db, p.Schema, FemCsfeaRunner.LinearAnalysisKind, stages, stages);
        Assert.False(StoreyFragmentMaskService.Extract(db, p.Fragment, FragmentMaskSet.SourceCsfeaLinear, S(analysis.Id)).HasErrors);
        int items = db.GetFragmentMasks(p.Fragment.Id)!.Items.Count;

        Assert.True(StoreyFragmentMaskService.Extract(db, p.Fragment, FragmentMaskSet.SourceCsfeaLinear, "999999").HasErrors);
        Assert.True(StoreyFragmentMaskService.Extract(db, p.Fragment, FragmentMaskSet.SourceCsfeaSecant, S(analysis.Id)).HasErrors);
        Assert.True(StoreyFragmentMaskService.Extract(db, p.Fragment, FragmentMaskSet.SourceScad, "").HasErrors);
        Assert.True(StoreyFragmentMaskService.Extract(db, p.Schema, FragmentMaskSet.SourceCsfeaLinear, S(analysis.Id)).HasErrors);
        Assert.Equal(items, db.GetFragmentMasks(p.Fragment.Id)!.Items.Count);

        db.DeleteFemSchema(p.Schema);
        Assert.Equal(FragmentMaskStatus.SourceDeleted, StoreyFragmentMaskService.CheckMasks(db, p.Fragment));
    }

    static CrossSection Square(int id, double a, Material concrete) => new()
    {
        Id = id, Tag = "К400",
        Areas =
        [
            new MaterialArea
            {
                Tag = "concrete", Category = AreaCategory.Region, MaterialId = concrete.Id,
                Hull = new Contour(new[] { -a / 2, a / 2, a / 2, -a / 2, -a / 2 }, new[] { -a / 2, -a / 2, a / 2, a / 2, -a / 2 }, "outer"),
            },
        ],
    };

    static string CatalogDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenCS.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "OpenCS.App", "DataSource");
    }

    void Exec(string sql)
    {
        using var c = new SqliteConnection($"Data Source={_path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
