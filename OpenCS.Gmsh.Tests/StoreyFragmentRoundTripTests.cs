using System.Collections.ObjectModel;
using System.Globalization;
using CScore;
using CScore.Fem;
using CScore.Fem.Loads;
using CScore.Planar;
using OpenCS.Services;
using OpenCS.Tasks;
using OpenCS.Utilites;
using Xunit.Abstractions;
using static OpenCS.Gmsh.Tests.StoreyFragmentGmshFixture;

namespace OpenCS.Gmsh.Tests;

/// <summary>
/// Круговая проверка фрагмента этажа (спека масок 2026-10-28, разд. 7): родитель — своя схема (плита 6×6 м на z = 3,
/// колонны (0, 0), (6, 0), (3, 3) с заделкой на z = 0, стена y = 6 с заделкой низа) с сеткой Gmsh 1 м → линейный CSfea
/// раннером → фрагмент 1,5…4,5 м с сеткой Gmsh 0,5 м → маски <c>csfea_linear</c> → ГУ разрезов → линейный CSfea
/// фрагмента. Жёсткость родителя и фрагмента — одни сечения проекта (П200, К400): у сетки без КонЭ сечений нет
/// (<see cref="ProjectElementStiffnessSource"/> берёт плиту только из КонЭ), а жёсткости ЛИРЫ фрагмент не наследует.
/// </summary>
public sealed class StoreyFragmentRoundTripTests(ITestOutputHelper output)
{
    const double Z1 = 1.5, Z2 = 4.5, Tol = 1e-6;
    const double ParentStep = 1.0, FragmentStep = 0.5;
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
    async Task<Parent> CreateParent(FemCsfeaRunContext ctx, string root)
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

        var meshed = await Mesh(db, schema, ParentStep, root);
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
}
