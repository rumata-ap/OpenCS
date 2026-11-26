using System.Globalization;
using CScore;
using CScore.Fem;
using CScore.Fem.Loads;
using CScore.Planar;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit.Abstractions;
using static OpenCS.Gmsh.Tests.StoreyFragmentGmshFixture;

namespace OpenCS.Gmsh.Tests;

/// <summary>
/// «Развернуть ось» и «Сменить нормаль» (<see cref="FemMemberOrientation"/>) на своей схеме с реальной сеткой: плита 4×4 м
/// на трёх угловых опорах и колонне под четвёртым углом. Линейный CSfea до и после разворота даёт те же перемещения и
/// реакции (нагрузки в общих и местных осях, на КонЭ и на КЭ); сетка сохраняется (КЭ развёрнуты на месте); второй
/// разворот возвращает схему к исходной.
/// </summary>
public sealed class FemMemberOrientationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task FlipBarAndPlate_SameDisplacementsAndReactions_SecondFlipRestores()
    {
        await InTempRoot("opencs-orient", async root =>
        {
            using var db = new DatabaseService(Path.Combine(root, "test.db"));
            var (schema, sections) = await Build(db, root);
            var before = Snapshot(db, schema.Id);
            string cellsBefore = Cells(db, schema.Id);
            var resultsBefore = SolveAll(db, schema, sections);

            var log = new CollectingLog();
            var outcome = await FemMemberOrientation.ApplyAsync(db, schema, ["К1", "П1"], GmshSettings(root), log);
            Assert.True(outcome.Done, outcome.Error);
            Assert.True(outcome.MeshKept, string.Join("\n", outcome.Warnings));
            Assert.Empty(outcome.Warnings);
            foreach (var line in outcome.Info) output.WriteLine(line);
            db.LoadAll();
            Restore(db, sections);

            // Сетка та же (узлы — те же точки), нормали КЭ плиты — вниз, колонна — сверху вниз.
            var nodes = db.GetFemMeshNodes(schema.Id).ToDictionary(n => n.NodeTag);
            Assert.Equal(before.NodePoints, Points(nodes.Values));
            Assert.Equal(cellsBefore, Cells(db, schema.Id));
            foreach (var e in db.GetFemMeshElements(schema.Id).Where(e => e.ElemType == "shell"))
                Assert.True(FemElementGeometry.Of(e, nodes)!.ShellFrame().Z.Z < -0.999, $"КЭ {e.ElemTag}");
            var column = db.GetFemMembers(schema.Id).Single(m => m.ElemTag == "К1");
            Assert.Equal("[2,6]", column.NodeIdsJson);
            var region = db.GetPlanarRegions(schema.Id).Single();
            Assert.Equal(-1, region.Frame.LocalZ.Z, 12);

            var resultsAfter = SolveAll(db, schema, sections);
            foreach (var (lc, a) in resultsBefore)
            {
                var b = resultsAfter[lc];
                Compare($"{lc}: перемещения", a.U, b.U);
                Compare($"{lc}: реакции", a.R, b.R);
            }

            var back = await FemMemberOrientation.ApplyAsync(db, schema, ["К1", "П1"], GmshSettings(root), log);
            Assert.True(back.Done && back.MeshKept, back.Error);
            db.LoadAll();
            Restore(db, sections);
            var restored = Snapshot(db, schema.Id);
            Assert.Equal(before.Members, restored.Members);
            Assert.Equal(before.MemberLoads, restored.MemberLoads);
            Assert.Equal(before.ElementLoads, restored.ElementLoads);
            Assert.Equal(before.Elements, restored.Elements);
            Assert.Equal(before.Region, restored.Region);
            Assert.Equal(before.LineSupports, restored.LineSupports);
        });
    }

    sealed record State(string Members, string MemberLoads, string ElementLoads, string Elements, string Region,
        string LineSupports, string NodePoints);

    static string R(double v) => Math.Round(v, 9).ToString("R", CultureInfo.InvariantCulture);

    /// <summary>КЭ сетки как множества точек узлов — совпадает у той же сетки при любом обходе.</summary>
    static string Cells(DatabaseService db, int schemaId)
    {
        var nodes = db.GetFemMeshNodes(schemaId).ToDictionary(n => n.NodeTag);
        return string.Join(";", db.GetFemMeshElements(schemaId)
            .Select(e => string.Join(" ", FemMeshTopology.ReadNodeTags(e)!.Select(t => nodes[t]).Select(n => $"{R(n.X)},{R(n.Y)},{R(n.Z)}")
                .Order(StringComparer.Ordinal)))
            .Order(StringComparer.Ordinal));
    }

    static string Points(IEnumerable<FemMeshNode> nodes) =>
        string.Join(";", nodes.Select(n => $"{R(n.X)},{R(n.Y)},{R(n.Z)}").Order(StringComparer.Ordinal));

    static State Snapshot(DatabaseService db, int schemaId)
    {
        var members = db.GetFemMembers(schemaId).OrderBy(m => m.ElemTag, StringComparer.Ordinal).ToList();
        var region = db.GetPlanarRegions(schemaId).Single();
        var f = region.Frame;
        return new State(
            string.Join("|", members.Select(m => $"{m.ElemTag}:{m.NodeIdsJson}:{R(m.RotationDeg)}")),
            string.Join("|", db.GetFemMemberLoads(schemaId).Select(l =>
                $"{l.CoordinateSystem},{l.DistributionType},{R(l.StartOffsetM)},{R(l.EndOffsetM)},{R(l.QxStart)},{R(l.QyStart)},{R(l.QzStart)},{R(l.QxEnd)},{R(l.QyEnd)},{R(l.QzEnd)}")),
            string.Join("|", db.GetFemElementLoads(schemaId).Select(l =>
                $"{l.TargetKind}:{string.Join(",", l.TargetTags)}:{l.LoadKind}:{l.CoordinateSystem}:{l.Axis}:{string.Join(",", l.Values.Select(R))}")),
            string.Join("|", db.GetFemMeshElements(schemaId).OrderBy(e => e.ElemTag, StringComparer.Ordinal)
                .Select(e => $"{e.ElemTag}:{e.NodeIdsJson}")),
            $"{f.LocalX}/{f.LocalY}/{f.LocalZ}/" + string.Join(";", region.Contours.Select(c => string.Join(",", c.X.Select(R)) + "/" + string.Join(",", c.Y.Select(R)))),
            string.Join("|", db.GetFemLineSupports(schemaId).Select(s => $"{s.Key}:{R(s.X1)},{R(s.Y1)},{R(s.X2)},{R(s.Y2)}")),
            Points(db.GetFemMeshNodes(schemaId)));
    }

    sealed record Result(Dictionary<string, double[]> U, Dictionary<string, double[]> R);

    /// <summary>Линейный CSfea по каждому загружению: перемещения и реакции узлов по положению узла.</summary>
    Dictionary<string, Result> SolveAll(DatabaseService db, FemSchema schema, Sections sections)
    {
        var results = new Dictionary<string, Result>();
        foreach (var lc in db.GetFemLoadCases(schema.Id))
        {
            Restore(db, sections);
            var input = FemCsfeaInputBuilder.Build(db, schema.Id, new FemCsfeaSetup { Stages = [Single(lc.Tag, lc.Id)] });
            Assert.DoesNotContain(input.Diagnostics, x => x.IsError);
            var adapted = FemRcModelAdapter.Adapt(input);
            Assert.DoesNotContain(adapted.Diagnostics, x => x.IsError);
            var build = RcStructuralMeshBuilder.Build(adapted.Model, new LinearRcSectionFactory());
            var f = build.Combination(adapted.Model.Stages[0].Loads);
            var u = build.Mesh.SolveLinear(f, build.Bc);
            var r = build.Mesh.ComputeReactions(u, build.Bc, fExternal: f);
            var uByPoint = new Dictionary<string, double[]>();
            var rByPoint = new Dictionary<string, double[]>();
            foreach (var n in adapted.Model.Nodes)
            {
                string key = $"{R(n.X)},{R(n.Y)},{R(n.Z)}";
                uByPoint[key] = Enumerable.Range(0, 6).Select(k => u[build.Dof(n.Id, k)]).ToArray();
                rByPoint[key] = Enumerable.Range(0, 6).Select(k => r[build.Dof(n.Id, k)]).ToArray();
            }
            double fz = f.Where((_, i) => i % 6 == 2).Sum();
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{lc.Tag}: ΣFz {fz / 1e3:0.###} кН, max|u| {uByPoint.Values.SelectMany(x => x.Take(3)).Max(Math.Abs) * 1e3:0.####} мм"));
            results[lc.Tag] = new Result(uByPoint, rByPoint);
        }
        return results;
    }

    void Compare(string what, Dictionary<string, double[]> a, Dictionary<string, double[]> b)
    {
        Assert.Equal(a.Keys.Order(), b.Keys.Order());
        double scale = a.Values.SelectMany(v => v).Max(Math.Abs);
        double diff = a.Max(p => p.Value.Zip(b[p.Key], (x, y) => Math.Abs(x - y)).Max());
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{what}: max |Δ| {diff:E2} при max {scale:E3}"));
        Assert.True(scale > 0, $"{what}: всё нулевое");
        Assert.True(diff <= 1e-7 * scale, $"{what}: max |Δ| {diff:E3} при max {scale:E3}");
    }

    /// <summary>
    /// Плита П1 4×4 м (отметка 0, нормаль вверх) с узлами 1, 3, 4 на опорах (X Y Z) и колонной К1 6 → 2 высотой 3 м под
    /// углом 2 (узел 6 защемлён), линейная связь по стороне 3–4. Загружения: «q» — на плиту −5 кПа в общих осях; «loc» — на
    /// плиту −2 кПа по местной z, на колонну трапеция по местной z и сосредоточенная по местной y; «el» — на КЭ: точка
    /// по местной z на пластине, узловая на другой пластине, трапеция по местной z на втором КЭ колонны. Сетка построена.
    /// </summary>
    static async Task<(FemSchema Schema, Sections Sections)> Build(DatabaseService db, string root)
    {
        var schema = new FemSchema { Tag = "orient" };
        db.SaveFemSchema(schema);
        schema.AutoRigidJunctions = false;
        db.UpdateFemSchemaRigidJunctions(schema);
        var sections = AddSections(db);
        var nodes = new List<FemNode>
        {
            new() { SchemaId = schema.Id, NodeTag = "1", X = 0, Y = 0, Z = 0, DofMask = 0b000111 },
            new() { SchemaId = schema.Id, NodeTag = "2", X = 4, Y = 0, Z = 0 },
            new() { SchemaId = schema.Id, NodeTag = "3", X = 4, Y = 4, Z = 0, DofMask = 0b000111 },
            new() { SchemaId = schema.Id, NodeTag = "4", X = 0, Y = 4, Z = 0, DofMask = 0b000111 },
            new() { SchemaId = schema.Id, NodeTag = "6", X = 4, Y = 0, Z = -3, DofMask = 0b111111 },
        };
        var cases = new List<FemLoadCase>
        {
            new() { SchemaId = schema.Id, Tag = "q", SelfWeightFactor = 0 },
            new() { SchemaId = schema.Id, Tag = "loc", SelfWeightFactor = 0 },
            new() { SchemaId = schema.Id, Tag = "el", SelfWeightFactor = 0 },
        };
        db.SaveFemSchemaEdit(schema.Id, nodes, [], [], cases, []);

        var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] }, frame: Frame3D.Identity, tag: "П1");
        db.AddPlanarRegion(region, schema.Id);
        db.SaveFemMember(new FemMember
        {
            SchemaId = schema.Id, ElemTag = "П1", ElemType = "shell", PlanarRegionId = region.Id, PlateSectionId = sections.Plate.Id,
        });
        db.SaveFemMember(new FemMember
        {
            SchemaId = schema.Id, ElemTag = "К1", ElemType = "beam", NodeIdsJson = "[6,2]", CrossSectionId = sections.Column.Id,
            RotationDeg = 15,
        });
        db.LoadAll();
        Restore(db, sections);
        schema = db.FemSchemas.Single(s => s.Id == schema.Id);
        await Mesh(db, schema, 0.5, root);

        var session = FemSchemaAuthoring.OpenSession(db, schema);
        int Lc(string tag) => session.LoadCases.Single(c => c.Tag == tag).Id;
        var col = session.Members.Single(m => m.ElemTag == "К1");
        FemElementLoad L(string lc, string target, string kind, string cs, string axis, string targetKind, params double[] v)
        {
            var l = new FemElementLoad { SchemaId = schema.Id, LoadCaseId = Lc(lc), TargetKind = targetKind, LoadKind = kind, CoordinateSystem = cs, Axis = axis };
            l.SetTargetTags([target]);
            l.SetValues(v);
            return l;
        }
        session.ElementLoads.Add(L("q", "П1", FemElementLoadKinds.Uniform, "global", "z", FemLoadTargetKinds.Members, -5e3));
        session.ElementLoads.Add(L("loc", "П1", FemElementLoadKinds.Uniform, "local", "z", FemLoadTargetKinds.Members, -2e3));
        session.MemberLoads.Add(new FemMemberLoad
        {
            SchemaId = schema.Id, LoadCaseId = Lc("loc"), MemberId = col.Id, CoordinateSystem = "local", DistributionType = "trapezoidal",
            StartOffsetM = 0.5, EndOffsetM = 1.0, QzStart = 1e3, QzEnd = 3e3, QxStart = 200, QxEnd = 400,
        });
        session.MemberLoads.Add(new FemMemberLoad
        {
            SchemaId = schema.Id, LoadCaseId = Lc("loc"), MemberId = col.Id, CoordinateSystem = "local", DistributionType = "point",
            StartOffsetM = 1.0, QyStart = 5e3, QzStart = -2e3,
        });
        var elements = db.GetFemMeshElements(schema.Id);
        var shells = elements.Where(e => e.ElemType == "shell").OrderBy(e => int.Parse(e.ElemTag)).ToList();
        var bars = elements.Where(e => e.SourceMemberTag == "К1").OrderBy(e => int.Parse(e.ElemTag)).ToList();
        session.ElementLoads.Add(L("el", shells[3].ElemTag, FemElementLoadKinds.Point, "local", "z", FemLoadTargetKinds.Elements, -10e3, 0.1, 0.05));
        session.ElementLoads.Add(L("el", shells[7].ElemTag, FemElementLoadKinds.Nodal, "local", "z", FemLoadTargetKinds.Elements,
            [.. Enumerable.Range(1, FemMeshTopology.ReadNodeTags(shells[7])!.Count).Select(k => -1e3 * k)]));
        session.ElementLoads.Add(L("el", bars[1].ElemTag, FemElementLoadKinds.Trapezoid, "local", "z", FemLoadTargetKinds.Elements, 2e3, 0.1, 4e3, 0.4));
        var support = new FemLineSupport { SchemaId = schema.Id, MemberTag = "П1", Mask = 0b000100 };
        support.AssignEdge(FemLineSupportGeometry.Edges(session.Members.Single(m => m.ElemTag == "П1"), db.GetPlanarRegions(schema.Id).Single())[2]);
        session.LineSupports.Add(support);
        Assert.True(FemSchemaAuthoring.Save(db, session, out var diagnostics), string.Join("\n", diagnostics));
        db.LoadAll();
        Restore(db, sections);
        return (db.FemSchemas.Single(s => s.Id == schema.Id), sections);
    }

    sealed class CollectingLog : ILogService
    {
        public List<string> Lines { get; } = [];
        public void Info(string message) => Lines.Add(message);
        public void Warning(string message) => Lines.Add(message);
        public void Error(string message) => Lines.Add(message);
        public System.Collections.ObjectModel.ObservableCollection<LogEntry> LogEntries { get; } = [];
    }
}
