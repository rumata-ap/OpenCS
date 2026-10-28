using System.Collections.ObjectModel;
using System.Globalization;
using CScore;
using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Fem.Fragment.Masks;
using CScore.Fem.Loads;
using CScore.Planar;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit.Abstractions;
using static OpenCS.Gmsh.Tests.StoreyFragmentGmshFixture;

namespace OpenCS.Gmsh.Tests;

/// <summary>
/// Круговая проверка фрагмента этажа (спека масок 2026-10-28, разд. 7): родитель — своя схема (плита 6×6 м на z = 3,
/// колонны (0, 0), (6, 0), (3, 3) с заделкой на z = 0, стена y = 6 с заделкой низа) с сеткой Gmsh 0,5 м → линейный
/// CSfea раннером → фрагмент 1,5…4,5 м с сеткой Gmsh 0,4 м (узлы плиты не совпадают с родительскими) → маски <c>csfea_linear</c> → ГУ разрезов → линейный CSfea
/// фрагмента. Жёсткость родителя и фрагмента — одни сечения проекта (П200, К400): у сетки без КонЭ сечений нет
/// (<see cref="ProjectElementStiffnessSource"/> берёт плиту только из КонЭ), а жёсткости ЛИРЫ фрагмент не наследует.
/// Сетка родителя 1 м мала для кромок стены: разрез 1,5 м посреди ряда КЭ, равнодействующая маски низа стены расходится
/// с прямой суммой по КЭ на 10 %, равновесие масок «Полезной» — 2,0 % (на пороге); при 0,5 м — без предупреждений.
/// </summary>
public sealed class StoreyFragmentRoundTripTests(ITestOutputHelper output)
{
    const double Z1 = 1.5, Z2 = 4.5, Tol = 1e-6;
    const double ParentStep = 0.5;
    static readonly (double X, double Y)[] Columns = [(0, 0), (6, 0), (3, 3)];

    sealed class ListLog(ITestOutputHelper output) : ILogService
    {
        public ObservableCollection<LogEntry> LogEntries { get; } = [];
        public void Info(string message) => output.WriteLine(message);
        public void Warning(string message) => output.WriteLine("! " + message);
        public void Error(string message) => output.WriteLine("ОШИБКА " + message);
    }

    /// <summary>Родитель, посчитанный линейно: схема, постановка с результатом, ожидаемые ΣFz по загружениям.</summary>
    sealed record Parent(FemSchema Schema, FemAnalysis Analysis, Sections Sections, Dictionary<string, double> ExpectedFz);

    /// <summary>
    /// Своя схема-родитель: плита S1 и стена W1 (области, П200), колонны — по два КонЭ К400 (0…3, 3…6) с узлом в плите,
    /// заделка низа колонн и линейная заделка низа стены. «Пост.»: q = −5 кПа по плите, Fz = −3 кН в свободном узле
    /// (1, 1, 3). «Полезная»: q = −2 кПа по плите и ещё −2 кПа по КЭ восточной половины (нагрузка на КЭ сетки), −1 кН/м по
    /// колонне (0, 0), −1 кПа по стене (вертикально, в её плоскости), Fx = 2 кН в узле (1, 1, 3).
    /// </summary>
    async Task<Parent> CreateParent(FemCsfeaRunContext ctx, string root, double step = ParentStep)
    {
        var db = ctx.Db;
        var schema = new FemSchema { Tag = "Каркас" };
        db.SaveFemSchema(schema);
        var sections = AddSections(db);

        var slab = PlanarRegion.CreateFromContour(new Contour { X = [0, 6, 6, 0], Y = [0, 0, 6, 6] },
            frame: new Frame3D(new PlanarVector3(0, 0, 3), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)), tag: "S1");
        var wall = PlanarRegion.CreateFromContour(new Contour { X = [0, 6, 6, 0], Y = [0, 0, 6, 6] },
            frame: new Frame3D(new PlanarVector3(0, 6, 0), new(1, 0, 0), new(0, 0, 1), new(0, -1, 0)), tag: "W1");
        db.AddPlanarRegion(slab, schema.Id);
        db.AddPlanarRegion(wall, schema.Id);

        var nodes = new List<FemNode>();
        var members = new List<FemMember>
        {
            new() { Id = -100, ElemTag = "S1", ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = slab.Id, Kind = "plate", ThicknessM = 0.2, PlateSectionId = sections.Plate.Id },
            new() { Id = -101, ElemTag = "W1", ElemType = "shell", NodeIdsJson = "[]", PlanarRegionId = wall.Id, Kind = "wall", ThicknessM = 0.2, PlateSectionId = sections.Plate.Id },
        };
        // NodeIdsJson КонЭ — теги узлов схемы.
        for (int c = 0; c < Columns.Length; c++)
        {
            var (x, y) = Columns[c];
            for (int k = 0; k < 3; k++)
                nodes.Add(new FemNode { Id = -(10 * c + k + 1), NodeTag = S(10 * c + k + 1), X = x, Y = y, Z = 3 * k, DofMask = k == 0 ? 63 : 0 });
            for (int k = 0; k < 2; k++)
                members.Add(new FemMember
                {
                    Id = -(10 * c + k + 1), ElemTag = $"C{c}{(k == 0 ? "a" : "b")}", ElemType = "beam",
                    NodeIdsJson = $"[{10 * c + k + 1},{10 * c + k + 2}]", CrossSectionId = sections.Column.Id,
                });
        }
        nodes.Add(new FemNode { Id = -99, NodeTag = "99", X = 1, Y = 1, Z = 3 });

        var loadCases = new List<FemLoadCase>
        {
            new() { Id = -1, Tag = "Пост.", Sp20Type = "permanent", SelfWeightFactor = 0 },
            new() { Id = -2, Tag = "Полезная", SelfWeightFactor = 0 },
        };
        var nodeLoads = new List<FemNodeLoad>
        {
            new() { LoadCaseId = -1, NodeId = -99, Fz = -3000 },
            new() { LoadCaseId = -2, NodeId = -99, Fx = 2000 },
        };
        var wallBase = new FemLineSupport
        {
            MemberTag = "W1", Loop = BoundaryLoop.Outer, StartVertex = 0, X1 = 0, Y1 = 6, Z1 = 0, X2 = 6, Y2 = 6, Z2 = 0, Mask = 63,
        };
        db.SaveFemSchemaEdit(schema.Id, nodes, members, [], loadCases, nodeLoads, [], [], null, null, null, [wallBase]);

        var meshed = await Mesh(db, schema, step, root);
        var meshNodes = meshed.Mesh!.Nodes.ToDictionary(n => n.NodeTag, StringComparer.Ordinal);
        var east = meshed.Mesh.Elements
            .Where(e => e.ElemType == "shell" && FemElementGeometry.Of(e, meshNodes) is { } g && Math.Abs(g.Centroid.Z - 3) < Tol && g.Centroid.X > 3)
            .ToList();
        double eastArea = east.Sum(e => FemElementGeometry.Of(e, meshNodes)!.Area);

        var cases = db.GetFemLoadCases(schema.Id);
        int lcDead = cases.Single(c => c.Tag == "Пост.").Id, lcLive = cases.Single(c => c.Tag == "Полезная").Id;
        FemElementLoad Uniform(int lc, double q, string targetKind, IEnumerable<string> targets)
        {
            var load = new FemElementLoad
            {
                LoadCaseId = lc, LoadKind = FemElementLoadKinds.Uniform, Axis = "z", CoordinateSystem = "global", TargetKind = targetKind,
            };
            load.SetTargetTags(targets.ToArray());
            load.SetValues([q]);
            return load;
        }
        db.SaveFemLoadCasesAndMeshLoads(schema.Id, cases,
        [
            Uniform(lcDead, -5000, FemLoadTargetKinds.Members, ["S1"]),
            Uniform(lcLive, -2000, FemLoadTargetKinds.Members, ["S1"]),
            Uniform(lcLive, -2000, FemLoadTargetKinds.Elements, east.Select(e => e.ElemTag)),
            Uniform(lcLive, -1000, FemLoadTargetKinds.Members, ["C0a", "C0b"]),
            Uniform(lcLive, -1000, FemLoadTargetKinds.Members, ["W1"]),
        ], []);
        var expected = new Dictionary<string, double>
        {
            ["Пост."] = -5000 * 36 - 3000,
            ["Полезная"] = -2000 * 36 - 2000 * eastArea - 1000 * 6 - 1000 * 36,
        };

        db.LoadAll();
        Restore(db, sections);
        schema = db.FemSchemas.Single(s => s.Id == schema.Id);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Родитель: узлов {meshed.Mesh.Nodes.Count}, КЭ {meshed.Mesh.Elements.Count}, площадь КЭ востока {eastArea:0.###} м²"));

        var analysis = LinearByLoadCases(db, schema, "Линейный");
        var result = await FemCsfeaRunner.RunAsync(ctx, schema, analysis, null, CancellationToken.None);
        var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
        foreach (var line in summary.Errors.Concat(summary.Report)) output.WriteLine(line);
        Assert.Equal("ok", result.Status);
        analysis.ResultId = result.Id;
        analysis.Status = "ok";
        db.SaveFemAnalysis(analysis);
        foreach (var stage in summary.Stages)
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Родитель «{stage.Tag}»: ΣFz {stage.Fz / 1e3:0.###} кН"));
        return new Parent(schema, analysis, sections, expected);
    }

    [Fact]
    public async Task Parent_LinearRun_LoadsAsExpected_JunctionsShared()
    {
        await InTempRoot("opencs-fragment-roundtrip", async root =>
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var ctx = new FemCsfeaRunContext(db, GmshSettings(root), new ListLog(output));
            var parent = await CreateParent(ctx, root);

            var summary = FemCsfeaResultSummary.Parse(db.GetCalcResultById(parent.Analysis.ResultId!.Value)!.DataJson)!;
            Assert.Equal(["Пост.", "Полезная"], summary.Stages.Select(s => s.Tag));
            foreach (var stage in summary.Stages)
                Assert.Equal(parent.ExpectedFz[stage.Tag], stage.Fz, 1e-6 * Math.Abs(parent.ExpectedFz[stage.Tag]));

            // Сетка сопряжена: узлы плиты на стыке со стеной и узлы колонн на z = 3 — общие с КЭ обеих сторон.
            var mesh = db.GetFemMeshElements(parent.Schema.Id);
            var nodes = db.GetFemMeshNodes(parent.Schema.Id).ToDictionary(n => n.NodeTag, StringComparer.Ordinal);
            var geometry = mesh.Select(e => (e, g: FemElementGeometry.Of(e, nodes)!)).ToList();
            HashSet<string> Tags(Func<FemElementGeometry, bool> where) =>
                geometry.Where(x => where(x.g)).SelectMany(x => x.g.NodeTags).ToHashSet(StringComparer.Ordinal);
            var slabTags = Tags(g => g.IsShell && Math.Abs(g.Centroid.Z - 3) < Tol);
            var wallTags = Tags(g => g.IsShell && Math.Abs(g.Centroid.Y - 6) < Tol);
            var barTags = Tags(g => g.IsBar);
            var junction = nodes.Values.Where(n => Math.Abs(n.Y - 6) < Tol && Math.Abs(n.Z - 3) < Tol).ToList();
            Assert.True(junction.Count >= 7, $"узлов на стыке плиты со стеной {junction.Count}");
            Assert.All(junction, n => Assert.True(slabTags.Contains(n.NodeTag) && wallTags.Contains(n.NodeTag), $"узел стыка {n.NodeTag}"));
            foreach (var (x, y) in Columns)
            {
                var n = Assert.Single(nodes.Values, n => Math.Abs(n.X - x) < Tol && Math.Abs(n.Y - y) < Tol && Math.Abs(n.Z - 3) < Tol);
                Assert.True(slabTags.Contains(n.NodeTag) && barTags.Contains(n.NodeTag), $"узел колонны ({x}, {y}) в плите");
            }
        });
    }

    /// <summary>
    /// Фрагмент 1,5…4,5 м родителя: сечения КонЭ — от родителя (построитель), сетка Gmsh с шагом <paramref name="step"/>, маски
    /// <c>csfea_linear</c> из постановки родителя (без ошибок и без расхождения равновесия).
    /// </summary>
    async Task<FemSchema> CreateFragment(DatabaseService db, Parent parent, string root, double step)
    {
        var built = StoreyFragmentService.Build(db, parent.Schema.Id, new StoreyFragmentRequest(Z1, Z2, "Этаж 2"));
        foreach (var d in built.Draft.Diagnostics) output.WriteLine($"{(d.IsError ? "ОШИБКА" : "предупр.")} {d.Code}: {d.Message}");
        var fragment = Assert.IsType<FemSchema>(built.Schema);
        var members = db.GetFemMembers(fragment.Id);
        Assert.All(members, m => Assert.True(m.ElemType == "beam" ? m.CrossSectionId == parent.Sections.Column.Id : m.PlateSectionId == parent.Sections.Plate.Id,
            $"КонЭ фрагмента {m.ElemTag} без сечения родителя"));
        Assert.Contains(members, m => m.Kind == "wall");

        var meshed = await Mesh(db, fragment, step, root);
        int cutNodes = db.GetFemMeshNodes(fragment.Id).Count(n => Math.Abs(n.Z - Z1) < Tol || Math.Abs(n.Z - Z2) < Tol);
        output.WriteLine($"Фрагмент: узлов {meshed.Mesh!.Nodes.Count}, КЭ {meshed.Mesh.Elements.Count}, узлов на разрезах {cutNodes}");

        var extracted = StoreyFragmentMaskService.Extract(db, fragment, FragmentMaskSet.SourceCsfeaLinear, S(parent.Analysis.Id));
        foreach (var d in extracted.Diagnostics) output.WriteLine($"маски {(d.IsError ? "ОШИБКА" : "предупр.")} {d.Code}: {d.Message}");
        Assert.False(extracted.HasErrors);
        Assert.DoesNotContain(extracted.Diagnostics, d => d.Code == "fragment_mask_balance");

        db.LoadAll();
        Restore(db, parent.Sections);
        return db.FemSchemas.Single(s => s.Id == fragment.Id);
    }

    /// <summary>Линейный CSfea фрагмента раннером с пресетом <paramref name="preset"/>; результат записан.</summary>
    async Task<(CalcResult Result, FemCsfeaResultSummary Summary)> RunFragment(FemCsfeaRunContext ctx, FemSchema fragment, FragmentBcPreset preset)
    {
        ctx.Db.SetFragmentBcPreset(fragment, preset.ToString());
        var analysis = LinearByLoadCases(ctx.Db, fragment, $"Фрагмент · {preset}");
        var result = await FemCsfeaRunner.RunAsync(ctx, fragment, analysis, null, CancellationToken.None);
        var summary = FemCsfeaResultSummary.Parse(result.DataJson)!;
        foreach (var line in summary.Errors.Concat(summary.Report)) output.WriteLine(line);
        Assert.Equal("ok", result.Status);
        return (result, summary);
    }

    static Dictionary<int, double[]> Displacements(RcSecantStepFields f)
    {
        var result = new Dictionary<int, double[]>();
        for (int i = 0; i < f.NodeIds.Length; i++)
            result[f.NodeIds[i]] = f.Displacements.Skip(RcSecantStepFields.NodeComponents * i).Take(RcSecantStepFields.NodeComponents).ToArray();
        return result;
    }

    /// <summary>
    /// Прогибы плиты фрагмента в узлах плиты родителя (кроме кромки у стены y = 6): uz фрагмента — интерполяция по его
    /// КЭ плиты; по загружениям — max |uz| родителя и наибольшее расхождение.
    /// </summary>
    List<(string Tag, double Scale, double MaxDiff)> CompareSlab(DatabaseService db, Parent parent, FemSchema fragment, CalcResult result, string label)
    {
        var parentNodes = db.GetFemMeshNodes(parent.Schema.Id);
        var points = parentNodes.Where(n => Math.Abs(n.Z - 3) < Tol && n.Y < 6 - Tol).ToList();
        var fragmentNodes = db.GetFemMeshNodes(fragment.Id).ToDictionary(n => n.NodeTag, StringComparer.Ordinal);
        var slab = db.GetFemMeshElements(fragment.Id)
            .Select(e => FemElementGeometry.Of(e, fragmentNodes))
            .Where(g => g is { IsShell: true } && Math.Abs(g.Centroid.Z - 3) < Tol).Select(g => g!).ToList();
        var stages = FemCsfeaResultSummary.Parse(db.GetCalcResultById(parent.Analysis.ResultId!.Value)!.DataJson)!.Stages;

        var rows = new List<(string, double, double)>();
        for (int i = 0; i < stages.Count; i++)
        {
            var up = Displacements(FemCsfeaStepFieldsCodec.Unpack(db.GetFemResultStep(parent.Analysis.ResultId!.Value, i + 1)!));
            var uf = Displacements(FemCsfeaStepFieldsCodec.Unpack(db.GetFemResultStep(result.Id, i + 1)!));
            double scale = points.Max(n => Math.Abs(up[int.Parse(n.NodeTag, CultureInfo.InvariantCulture)][2]));
            double maxDiff = 0;
            string worst = "";
            foreach (var n in points)
            {
                var p = new PlanarVector3(n.X, n.Y, n.Z);
                var (g, shape) = slab.Select(g => (g, shape: g.ShellShapeAt(p))).First(x => x.shape != null);
                double wf = shape!.Select((w, k) => w * uf[int.Parse(g.NodeTags[k], CultureInfo.InvariantCulture)][2]).Sum();
                double wp = up[int.Parse(n.NodeTag, CultureInfo.InvariantCulture)][2];
                if (Math.Abs(wf - wp) > maxDiff)
                {
                    maxDiff = Math.Abs(wf - wp);
                    worst = string.Create(CultureInfo.InvariantCulture, $"({n.X:0.##}, {n.Y:0.##}): родитель {wp * 1e3:0.####} мм, фрагмент {wf * 1e3:0.####} мм");
                }
            }
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{label} «{stages[i].Tag}»: точек {points.Count}, max |uz| родителя {scale * 1e3:0.####} мм, наибольшее расхождение {maxDiff * 1e3:0.####} мм ({maxDiff / scale:P2}) в {worst}"));
            rows.Add((stages[i].Tag, scale, maxDiff));
        }
        return rows;
    }

    /// <summary>ΣF реакций узлов сетки на отметке разреза стороны и ΣF масок разрезов этой стороны по загружению фрагмента.</summary>
    static (PlanarVector3 Reaction, PlanarVector3 Mask) SideForces(DatabaseService db, FemSchema fragment, RcSecantStepFields fields,
        int loadCaseId, FragmentCutSide side)
    {
        double z = side == FragmentCutSide.Bottom ? Z1 : Z2;
        var atCut = db.GetFemMeshNodes(fragment.Id).Where(n => Math.Abs(n.Z - z) < Tol)
            .Select(n => int.Parse(n.NodeTag, CultureInfo.InvariantCulture)).ToHashSet();
        var reaction = PlanarVector3.Zero;
        for (int i = 0; i < fields.ReactionNodeIds.Length; i++)
            if (atCut.Contains(fields.ReactionNodeIds[i]))
            {
                int k = RcSecantStepFields.NodeComponents * i;
                reaction += new PlanarVector3(fields.Reactions[k], fields.Reactions[k + 1], fields.Reactions[k + 2]);
            }

        var cuts = db.GetFragmentCutsWithIds(fragment.Id).ToDictionary(c => c.Id, c => c.Cut);
        var mask = PlanarVector3.Zero;
        foreach (var item in db.GetFragmentMasks(fragment.Id)!.Items.Where(x => x.Key == FragmentMaskKey.OfLoadCase(loadCaseId)))
        {
            var cut = cuts[item.CutId];
            if (cut.Side != side) continue;
            mask += cut.Kind == FragmentCutKind.End
                ? item.Samples[0].F!.Force
                : FragmentMaskBalance.Resultant(new FragmentMaskCutInput(cut, null, null), item.Samples, PlanarVector3.Zero)!.Value.F;
        }
        return (reaction, mask);
    }

    /// <summary>Реакции узлов каждой стороны разрезов против масок по загружениям: ≤ 2 % ΣFz стадии.</summary>
    void AssertReactionsMatchMasks(DatabaseService db, FemSchema fragment, CalcResult result, FemCsfeaResultSummary summary)
    {
        var cases = db.GetFemLoadCases(fragment.Id);
        for (int i = 0; i < summary.Stages.Count; i++)
        {
            var fields = FemCsfeaStepFieldsCodec.Unpack(db.GetFemResultStep(result.Id, i + 1)!);
            int lc = cases.Single(c => c.Tag == summary.Stages[i].Tag).Id;
            foreach (var side in new[] { FragmentCutSide.Bottom, FragmentCutSide.Top })
            {
                var (r, m) = SideForces(db, fragment, fields, lc, side);
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"«{summary.Stages[i].Tag}» {side}: реакции ({r.X / 1e3:0.###}, {r.Y / 1e3:0.###}, {r.Z / 1e3:0.###}) кН, " +
                    $"маски ({m.X / 1e3:0.###}, {m.Y / 1e3:0.###}, {m.Z / 1e3:0.###}) кН, ΣFz стадии {summary.Stages[i].Fz / 1e3:0.###} кН"));
                // Реакция опоры и маска — обе действие на фрагмент (маска — действие отброшенной части): знак один.
                Assert.True((r - m).Length <= 0.02 * Math.Abs(summary.Stages[i].Fz),
                    $"«{summary.Stages[i].Tag}» {side}: |R − маска| = {(r - m).Length / 1e3:0.###} кН > 2 % ΣFz");
            }
        }
    }

    /// <summary>
    /// «Как в родителе», шаг фрагмента = шагу родителя (0,5 м): сетка плиты Gmsh у них совпадает, стена и колонны полосы
    /// сетятся заново (другие узлы по высоте) — прогибы плиты повторяют родителя ≤ 2 % max |uz|.
    /// </summary>
    [Fact]
    public async Task Kinematic_SameStep_FragmentRepeatsParent()
    {
        await InTempRoot("opencs-fragment-roundtrip", async root =>
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var ctx = new FemCsfeaRunContext(db, GmshSettings(root), new ListLog(output));
            var parent = await CreateParent(ctx, root);
            var fragment = await CreateFragment(db, parent, root, ParentStep);

            var (result, summary) = await RunFragment(ctx, fragment, FragmentBcPreset.Kinematic);
            Assert.DoesNotContain(summary.Report, l => l.Contains("не уравновешена", StringComparison.Ordinal));
            foreach (var (tag, scale, diff) in CompareSlab(db, parent, fragment, result, "Kinematic 0,5/0,5"))
                Assert.True(diff <= 0.02 * scale, $"«{tag}»: расхождение прогиба {diff / scale:P2} > 2 %");
            AssertReactionsMatchMasks(db, fragment, result, summary);
        });
    }

    /// <summary>
    /// «Как в родителе», фрагмент мельче родителя (0,4 против 0,5 м). Сама схема на этих сетках расходится на единицы
    /// процентов (точечные опоры колонн и сила в узле плиты — медленная сходимость), поэтому критерий относительный:
    /// расхождение фрагмента с родителем не больше расхождения родителя 0,4 м с родителем 0,5 м плюс 2 % max |uz|.
    /// </summary>
    [Fact]
    public async Task Kinematic_FinerFragment_WithinParentMeshDiscrepancy()
    {
        const double fine = 0.4;
        await InTempRoot("opencs-fragment-roundtrip", async root =>
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var ctx = new FemCsfeaRunContext(db, GmshSettings(root), new ListLog(output));
            var parent = await CreateParent(ctx, root);
            var reference = await CreateParent(ctx, root, fine);
            var mesh = CompareSlab(db, parent, reference.Schema, db.GetCalcResultById(reference.Analysis.ResultId!.Value)!,
                "родитель 0,4 против 0,5");
            var fragment = await CreateFragment(db, parent, root, fine);

            var (result, summary) = await RunFragment(ctx, fragment, FragmentBcPreset.Kinematic);
            Assert.DoesNotContain(summary.Report, l => l.Contains("не уравновешена", StringComparison.Ordinal));
            var rows = CompareSlab(db, parent, fragment, result, "Kinematic 0,4/0,5");
            for (int i = 0; i < rows.Count; i++)
                Assert.True(rows[i].MaxDiff <= mesh[i].MaxDiff + 0.02 * rows[i].Scale,
                    $"«{rows[i].Tag}»: фрагмент {rows[i].MaxDiff / rows[i].Scale:P2}, сетка родителя {mesh[i].MaxDiff / mesh[i].Scale:P2}");
            AssertReactionsMatchMasks(db, fragment, result, summary);
        });
    }
}
