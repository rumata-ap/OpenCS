using System.Text.Json;
using CScore;
using CScore.Fem;
using CScore.Planar;
using CSfea.CScoreBridge.Structural;
using OpenCS.Gmsh;
using Xunit.Abstractions;

namespace CSfea.Tests;

/// <summary>
/// Верификация авто-АЖТ примыканий (срез 5): круглая плита радиусом 3 м на колонне Ø 600, линейный расчёт CSfea.
/// С АЖТ по следу колонны радиальный момент у грани сходится к решению Кирхгофа для кольцевой пластины (внутренний
/// край защемлён, наружный шарнирно опёрт); без АЖТ колонна держит плиту одной точкой, и пик момента растёт с
/// измельчением сетки. Сетка — реальный Gmsh тем же конвейером, что «Построить сетку схемы» (без БД).
/// </summary>
[Trait("Category", "Verification")]
[HarnessChecks]
public class RigidJunctionColumnSlabTests(ITestOutputHelper output)
{
    const string Gmsh = @"C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe";
    const double A = 3.0, B = 0.3, T = 0.2, E = 30e9, Nu = 0.2, Q = 10e3;
    const int OuterSides = 96, ColumnSides = 32, SchemaId = 1;

    /// <summary>Упругие свойства: пластина t = 0,2, стержень — круг Ø 0,6.</summary>
    sealed class Elastic : IFemElementStiffnessSource
    {
        const double Area = Math.PI * B * B, I = Math.PI * B * B * B * B / 4;
        public FemShellStiffness? Shell(FemElement e) => e.ElemType == "shell" ? new(E, Nu, T) : null;
        public FemBarStiffness? Bar(FemElement e) => e.ElemType == "beam" ? new(E, E / (2 * (1 + Nu)), Area, I, I, 2 * I) : null;
        public double? UnitWeight(FemElement e) => null;
        public bool IsWithoutStiffness(FemElement e) => false;
        public double? BarArea(FemElement e) => e.ElemType == "beam" ? Area : null;
    }

    static (double[] X, double[] Y) Polygon(double r, int sides, bool closed)
    {
        int count = closed ? sides + 1 : sides;
        var x = new double[count];
        var y = new double[count];
        for (int i = 0; i < count; i++)
        {
            double f = 2 * Math.PI * i / sides;
            x[i] = r * Math.Cos(f);
            y[i] = r * Math.Sin(f);
        }
        return (x, y);
    }

    /// <summary>Радиальный момент кольцевой пластины Кирхгофа в конвенции OpenCS (плюс — растяжение верха):
    /// w = q r⁴/(64D) + C₁ r² ln r + C₂ r² + C₃ ln r + C₄ (w — вниз), w(b) = w′(b) = 0, w(a) = 0, M_r(a) = 0,
    /// M_r = −D(w″ + ν w′/r); возвращается −M_r.</summary>
    static Func<double, double> Analytic()
    {
        double d = E * T * T * T / (12 * (1 - Nu * Nu));
        static double[] W(double r) => [r * r * Math.Log(r), r * r, Math.Log(r), 1];
        static double[] W1(double r) => [2 * r * Math.Log(r) + r, 2 * r, 1 / r, 0];
        static double[] W2(double r) => [2 * Math.Log(r) + 3, 2, -1 / (r * r), 0];
        double P(double r) => Q * Math.Pow(r, 4) / (64 * d);
        double P1(double r) => Q * r * r * r / (16 * d);
        double P2(double r) => 3 * Q * r * r / (16 * d);
        double[] Mrow(double r) => W2(r).Zip(W1(r), (u2, u1) => u2 + Nu * u1 / r).ToArray();
        var c = Solve([W(B), W1(B), W(A), Mrow(A)], [-P(B), -P1(B), -P(A), -(P2(A) + Nu * P1(A) / A)]);
        return r =>
        {
            double w1 = P1(r) + W1(r).Zip(c, (k, v) => k * v).Sum();
            double w2 = P2(r) + W2(r).Zip(c, (k, v) => k * v).Sum();
            return d * (w2 + Nu * w1 / r);
        };
    }

    /// <summary>Метод Гаусса с выбором ведущего элемента (система 4×4 констант).</summary>
    static double[] Solve(double[][] m, double[] rhs)
    {
        int n = rhs.Length;
        var a = m.Select((row, i) => row.Append(rhs[i]).ToArray()).ToArray();
        for (int k = 0; k < n; k++)
        {
            int p = Enumerable.Range(k, n - k).MaxBy(i => Math.Abs(a[i][k]));
            (a[k], a[p]) = (a[p], a[k]);
            for (int i = k + 1; i < n; i++)
            {
                double f = a[i][k] / a[k][k];
                for (int j = k; j <= n; j++) a[i][j] -= f * a[k][j];
            }
        }
        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
            x[i] = (a[i][n] - Enumerable.Range(i + 1, n - i - 1).Sum(j => a[i][j] * x[j])) / a[i][i];
        return x;
    }

    /// <summary>Итог одного расчёта: тела, пик M_rr по центрам КЭ, среднее отношение к аналитике у грани
    /// (центры КЭ при b &lt; r &lt; b + 2h), сумма вертикальных реакций.</summary>
    sealed record Run(double Step, bool Junctions, int Bodies, int Slaves, double Peak, double PeakR, double FaceRatio,
        double Reaction);

    async Task<Run> Solve(double step, bool junctions, string root)
    {
        var nodes = new List<FemNode>
        {
            new() { SchemaId = SchemaId, NodeTag = "1", X = 0, Y = 0, Z = -3 },
            new() { SchemaId = SchemaId, NodeTag = "2", X = 0, Y = 0, Z = 0 },
        };
        var (cx, cy) = Polygon(B, ColumnSides, closed: true);
        var column = new CrossSection
        {
            Id = 1,
            Areas = [new MaterialArea { Tag = "concrete", Category = AreaCategory.Region, Hull = new Contour(cx, cy, "outer") }],
        };
        var frame = new Frame3D(new PlanarVector3(0, 0, 0), new PlanarVector3(1, 0, 0), new PlanarVector3(0, 1, 0), new PlanarVector3(0, 0, 1));
        var (ox, oy) = Polygon(A, OuterSides, closed: false);
        var region = PlanarRegion.CreateFromContour(new Contour { X = [.. ox], Y = [.. oy] }, frame: frame, tag: "P1");
        region.Id = 1;
        var members = new List<FemMember>
        {
            new() { SchemaId = SchemaId, ElemTag = "C1", ElemType = "beam", NodeIdsJson = "[1,2]", CrossSectionId = 1 },
            new() { SchemaId = SchemaId, ElemTag = "P1", ElemType = "shell", PlanarRegionId = region.Id },
        };

        var (beamNodes, beamElements) = FemMeshDiscretizer.Discretize(SchemaId, nodes, members, null);
        var (derivationNodes, derivationElements) = FemPlanarMeshPlan.DerivationBeams(SchemaId, nodes, members);
        var prefix = FemPlanarMeshMaterializer.Materialize(SchemaId, derivationNodes, derivationElements, nodes, []);
        var traces = junctions
            ? FemRigidJunctions.Build(region, nodes, members, id => id == 1 ? RigidJunctionSectionOutline.From(column) : null,
                new RigidJunctionOptions())
            : null;
        var constraints = FemPlanarMeshPlan.Constraints(SchemaId, nodes, members, prefix, region, junctions: traces);
        // Алгоритм 6 (Frontal-Delaunay) — как в настройках Gmsh приложения по умолчанию.
        var settings = new PlanarMeshSettings(FemPlanarMeshPlan.MeshSize(members[1], region, step), 6, PlanarMeshElementMode.Quads);
        var mesher = new GmshPlanarMesher(new GmshPlanarMesherOptions { ExecutablePath = Gmsh, ArtifactRoot = root });
        var snapshot = await mesher.BuildAsync(new PlanarMeshingRequest(region, settings, constraints.Constraints,
            constraints.SourceFingerprint, constraints.Diagnostics), CancellationToken.None);
        string label = $"h = {step:0.00} {(junctions ? "АЖТ" : "точка")}";
        TestHarness.Check($"{label}: сетка построена", snapshot.IsCalculable,
            string.Join("; ", snapshot.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));
        var regionMeshes = new List<FemPlanarRegionMesh> { new(members[1], region, snapshot, T, traces) };
        var mesh = FemPlanarMeshMaterializer.Materialize(SchemaId, beamNodes, beamElements, nodes, regionMeshes);
        var bodies = junctions
            ? FemRigidJunctionMaterializer.Build(mesh, regionMeshes, nodes, members, new HashSet<string>()).Bodies
            : [];

        // Контур — шарнир с закреплёнными Ux Uy: без АЖТ плиту в её плоскости держит один узел колонны, и поворот
        // вокруг z ничем не закреплён (K не положительно определена); на изгиб линейной плиты это не влияет.
        string Tag(string source) => mesh.Nodes.Single(n => n.SourceNodeTag == source).NodeTag;
        double edge = A * Math.Cos(Math.PI / OuterSides) - 1e-6;
        var supports = new List<FemMeshNodeSupport>
        {
            new() { NodeTag = Tag("1"), Mask = 0b111111 },
            new() { NodeTag = Tag("2"), Mask = 0b111111 },
        };
        supports.AddRange(mesh.Nodes.Where(n => Math.Abs(n.Z) < 1e-9 && Math.Sqrt(n.X * n.X + n.Y * n.Y) >= edge)
            .Select(n => new FemMeshNodeSupport { NodeTag = n.NodeTag, Mask = 0b111 }));
        var shells = mesh.Elements.Where(e => e.ElemType == "shell").ToList();
        var load = new FemElementLoad { LoadCaseId = 1, LoadKind = FemElementLoadKinds.Uniform, Axis = "z" };
        load.SetTargetTags(shells.Select(e => e.ElemTag).ToList());
        load.SetValues([-Q]);
        var adapted = FemRcModelAdapter.Adapt(new FemRcModelInput
        {
            MeshNodes = mesh.Nodes, MeshElements = mesh.Elements, Nodes = nodes, Members = members, Properties = new Elastic(),
            Supports = supports, RigidBodies = bodies, LoadCases = [new FemLoadCase { Id = 1, Tag = "q" }],
            ElementLoads = [load], Stages = [new FemRcStage("S", [(1, 1.0)])],
            RegionAxisX = new Dictionary<string, PlanarVector3> { ["P1"] = new(1, 0, 0) },
        });
        TestHarness.Check($"{label}: адаптер без ошибок", !adapted.HasErrors, string.Join("; ", adapted.Report));
        var f = RcLinearAnalysis.Run(adapted.Model).Cases[0].Fields;

        var byTag = mesh.Nodes.ToDictionary(n => n.NodeTag);
        var shellByTag = shells.ToDictionary(e => e.ElemTag);
        var analytic = Analytic();
        double peak = double.MinValue, peakR = 0, ratioSum = 0;
        int ratioCount = 0;
        for (int k = 0; k < f.ShellIds.Length; k++)
        {
            var element = shellByTag[f.ShellIds[k].ToString()];
            var corners = JsonSerializer.Deserialize<int[]>(element.NodeIdsJson)!.Select(t => byTag[t.ToString()]).ToList();
            double x = corners.Average(n => n.X), y = corners.Average(n => n.Y), r = Math.Sqrt(x * x + y * y);
            double c = x / r, s = y / r;
            int o = k * RcSecantStepFields.ShellForceComponents;
            double mrr = f.ShellForces[o + 3] * c * c + f.ShellForces[o + 4] * s * s + 2 * f.ShellForces[o + 5] * s * c;
            if (mrr > peak) (peak, peakR) = (mrr, r);
            if (r > B && r < B + 2 * step) { ratioSum += mrr / analytic(r); ratioCount++; }
        }
        double reaction = Enumerable.Range(0, f.ReactionNodeIds.Length).Sum(i => f.Reactions[6 * i + 2]);
        return new Run(step, junctions, bodies.Count, bodies.Sum(b => b.SlaveNodeTags.Count), peak, peakR,
            ratioCount > 0 ? ratioSum / ratioCount : double.NaN, reaction);
    }

    [Fact]
    public async Task ColumnSlab_FaceMomentConvergesWithJunctions()
    {
        if (!File.Exists(Gmsh)) { output.WriteLine($"Gmsh не найден: {Gmsh} — проверка пропущена."); return; }
        var root = Path.Combine(Path.GetTempPath(), $"opencs-junction-verify-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            TestHarness.Section("АЖТ примыкания: круглая плита на колонне Ø 600");
            var analytic = Analytic();
            double area = 0.5 * OuterSides * A * A * Math.Sin(2 * Math.PI / OuterSides);
            output.WriteLine($"Кирхгоф: M у грани колонны (r = {B}) = {analytic(B) / 1e3:0.00} кН·м/м, при r = 0,4 — {analytic(0.4) / 1e3:0.00}");
            var runs = new List<Run>();
            foreach (double h in new[] { 0.4, 0.2, 0.1 })
                foreach (bool j in new[] { true, false })
                {
                    var run = await Solve(h, j, root);
                    runs.Add(run);
                    output.WriteLine($"h = {h:0.00} {(j ? "АЖТ  " : "точка")}: тел {run.Bodies}, ведомых {run.Slaves}, " +
                                     $"пик M = {run.Peak / 1e3:0.00} кН·м/м при r = {run.PeakR:0.000}, M/Кирхгоф у грани = {run.FaceRatio:0.000}, " +
                                     $"ΣRz = {run.Reaction / 1e3:0.0} кН");
                    TestHarness.CheckRel($"h = {h:0.00} {(j ? "АЖТ" : "точка")}: ΣRz = q·S", run.Reaction, Q * area, 0.01);
                }
            var withJ = runs.Where(r => r.Junctions).ToList();
            var point = runs.Where(r => !r.Junctions).ToList();
            TestHarness.Check("одно тело на колонну", withJ.All(r => r.Bodies == 1));
            // 23.10: отношение к Кирхгофу 1,003 / 0,975 / 0,982 (h = 0,4 / 0,2 / 0,1), пик с АЖТ 19,50 / 19,74 / 19,74 кН·м/м;
            // без АЖТ 13,87 / 21,34 / 26,40 кН·м/м. У грани сетку задают вшитые рёбра следа, а не шаг — пик не меняется.
            foreach (var run in withJ)
                TestHarness.CheckRel($"АЖТ, h = {run.Step:0.00}: M у грани ≈ Кирхгоф", run.FaceRatio, 1.0, 0.05);
            TestHarness.CheckLess("АЖТ: пик h = 0,1 к h = 0,2 меняется меньше 2 %", Math.Abs(withJ[2].Peak / withJ[1].Peak - 1), 0.02);
            TestHarness.Check("точка: пик растёт с измельчением больше 15 %", point[2].Peak > 1.15 * point[1].Peak,
                $"{point[1].Peak:G4} → {point[2].Peak:G4}");
            TestHarness.Check("точка: пик h = 0,1 выше, чем с АЖТ", point[2].Peak > withJ[2].Peak);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { }
        }
    }
}
