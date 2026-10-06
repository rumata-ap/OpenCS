using System.Globalization;
using System.IO;
using System.Text;
using CScore.Import;
using CSfea.CScoreBridge.Structural;
using OpenCS.OpenSees.CScore;
using OpenCS.Services.Scad;
using Xunit;
using Xunit.Abstractions;

namespace OpenCS.Tests;

/// <summary>
/// Ручной прогон «плита Дорфмана» в CSfea: схема SCAD → RcStructuralModel → StructuralMesh (спека «Нелинейный расчёт
/// ЖБ оболочечно-стержневых схем в CSfea», срез 1). OPENCS_SCAD_NL_SPR — модель SCAD; OPENCS_SCAD_DIR — каталог
/// SCADAPIX (по умолчанию — найденная установка SCAD); OPENCS_CSFEA_OUT — каталог выгрузки CSV (необязательно).
/// Без OPENCS_SCAD_NL_SPR тест сразу выходит.
/// </summary>
public class ScadShellCsfeaManualTests(ITestOutputHelper output)
{
    /// <summary>Точки прогибомеров опыта (рис. 45 книги): 2, 4, 9.</summary>
    static readonly (string Name, double X, double Y)[] Points = [("2", 1.5, 4.5), ("4", 4.5, 4.5), ("9", 4.5, 7.5)];

    /// <summary>Узел центра плиты в модели SCAD.</summary>
    const int CenterNode = 510;

    /// <summary>Линейный расчёт L1 и L2 по отдельности против протокола SCAD: ΣZ(L1) = 43,32 т, центр −3,868 / −6,873 мм.</summary>
    [Fact]
    public void Linear()
    {
        if (Read() is not { } data) return;
        var (e, nu) = ScadShellScenario.ElasticPlate(data);
        var report = new List<string>();
        var scenario = ScadShellScenario.Build(data, new ScadShellScenarioOptions(true, ScadShellMaterialMode.Experiment,
            [new ScadShellStage("L1", [(1, 1.0)], 1.0), new ScadShellStage("L2", [(2, 1.0)], 1.0)]), report);
        var adapted = ScadRcModelAdapter.Adapt(new ScadRcModelInput
        {
            Data = data, PlateSection = scenario.PlateSection, ShellSection = ScadRcModelAdapter.ElasticShells(e, nu),
            Stages = scenario.Stages,
        });
        var build = RcStructuralMeshBuilder.Build(adapted.Model, new LinearRcSectionFactory());
        foreach (var s in report.Concat(adapted.Report).Concat(build.Report).Distinct()) output.WriteLine(s);
        output.WriteLine($"E = {e / 1e6:0} МПа, ν = {nu}; узлов {build.Mesh.NNodes}, оболочек {build.Mesh.Shells.Count}, " +
            $"стержней {build.Mesh.Beams.Count}, жёстких связей {build.Mesh.Links?.Links.Count ?? 0}");

        // Протокол SCAD «Суммарные внешние нагрузки» — без нагрузок в защемлённых узлах.
        var fixedNodes = adapted.Model.Supports.Where(s => s.Mask == 0x3F).Select(s => s.NodeId).ToHashSet();
        var lc1 = adapted.Model.LoadCases.Single(l => l.Id == 1);
        double free = -lc1.Nodal.Where(p => !fixedNodes.Contains(p.NodeId)).Sum(p => p.Force[2]) / 9810;
        output.WriteLine($"ΣZ L1 = {adapted.StageTotalDownN[0] / 9810:0.###} т, без защемлённых узлов {free:0.###} т (SCAD 43,32 т)");

        var u = adapted.Model.Stages.Select(st => build.Mesh.SolveLinear(build.Combination(st.Loads), build.Bc)).ToArray();
        for (int k = 0; k < u.Length; k++)
        {
            var f = build.Combination(adapted.Model.Stages[k].Loads);
            var r = build.Mesh.ComputeReactions(u[k], build.Bc, fExternal: f);
            double sumR = build.Bc.FixedDofs.Where(d => d % 6 == 2).Sum(d => r[d]);
            output.WriteLine($"{adapted.Model.Stages[k].Name}: ΣRz = {sumR / 9810:0.###} т, нагрузка {adapted.StageTotalDownN[k] / 9810:0.###} т");
        }
        var points = PointNodes(data);
        foreach (var (name, node) in points)
            output.WriteLine($"Точка {name} (узел {node}): L1 = {Uz(build, u[0], node) * 1000:0.###} мм, " +
                $"L2 = {Uz(build, u[1], node) * 1000:0.###} мм");
        double w1 = Uz(build, u[0], CenterNode) * 1000, w2 = Uz(build, u[1], CenterNode) * 1000;
        output.WriteLine($"Узел {CenterNode}: L1 = {w1:0.###} мм ({(w1 / -3.868 - 1) * 100:+0.00;-0.00} %), " +
            $"L2 = {w2:0.###} мм ({(w2 / -6.873 - 1) * 100:+0.00;-0.00} %) (SCAD линейный: −3,868 / −6,873 мм)");
        Export("linear", build, adapted.Model, u);

        Assert.Equal(43.32, free, 2);
        Assert.InRange(w1 / -3.868, 0.99, 1.01);
        Assert.InRange(w2 / -6.873, 0.99, 1.01);
    }

    /// <summary>
    /// Секущий расчёт, прогон A: бетон без растяжения, без ψs (проверка схемы). Ожидание по OpenSees 03.10 (ft = 0,05 МПа):
    /// т. 4 — 17,0 мм от L1, 67,4 мм от L2; т. 2/9 — 34,9 мм от L2.
    /// </summary>
    [Fact]
    public void SecantA_NoTension() => RunSecant("secant-A", new RcSecantOptions { TensionConcrete = false, Psi = false });

    /// <summary>
    /// Прогон B: растяжение бетона до трещины, ψs, ν = 0,2; трещина выключает растянутый бетон сечения (как стержневой
    /// НДМ по п. 8.2.32). Опыт (от нормативной нагрузки L2): т. 2 — 9,2, т. 4 — 15,1, т. 9 — 8,6 мм.
    /// </summary>
    [Fact]
    public void SecantB_Tension() => RunSecant("secant-B",
        new RcSecantOptions { Psi = true, PoissonUncracked = 0.2, PlateCrackRule = PlateCrackRule.Section });

    /// <summary>Прогон B с послойным правилом трещины (нетреснувшие растянутые слои работают, плюс ψs).</summary>
    [Fact]
    public void SecantB_TensionLayerRule() => RunSecant("secant-B-layer",
        new RcSecantOptions { Psi = true, PoissonUncracked = 0.2, PlateCrackRule = PlateCrackRule.Layer });

    /// <summary>
    /// Прогон B с геометрической нелинейностью (фон Карман/CR оболочек): проверка гипотезы о распоре — мембранные
    /// усилия от прогиба при защемлённых колоннах.
    /// </summary>
    [Fact]
    public void SecantB_Geometric() => RunSecant("secant-B-geom",
        new RcSecantOptions { Psi = true, PoissonUncracked = 0.2, Solver = new CSfea.Core.SecantPicardOptions { Geometric = true } });

    /// <summary>
    /// Прогон B с обнулённым блоком B секущей ABD: без физического распора (связи N с изгибом через смещение центра
    /// тяжести сечения с трещиной). Разница с прогоном B — вклад этого распора.
    /// </summary>
    [Fact]
    public void SecantB_NoCoupling() => RunSecant("secant-B-nocoupling",
        new RcSecantOptions { Psi = true, PoissonUncracked = 0.2, DropMembraneBendingCoupling = true });

    /// <summary>
    /// Упругая схема, L1 + L2 геометрически нелинейно (фон Карман, как SolveFrozen секущего расчёта с Geometric):
    /// история невязки Ньютона и прогиб центра против линейного.
    /// </summary>
    [Fact]
    public void GeometricElasticProbe()
    {
        if (Read() is not { } data) return;
        var (e, nu) = ScadShellScenario.ElasticPlate(data);
        var scenario = ScadShellScenario.Build(data, new ScadShellScenarioOptions(true, ScadShellMaterialMode.Experiment,
            [new ScadShellStage("L1", [(1, 1.0)], 1.0), new ScadShellStage("L2", [(2, 1.0)], 1.0)]), []);
        var adapted = ScadRcModelAdapter.Adapt(new ScadRcModelInput
        {
            Data = data, PlateSection = scenario.PlateSection, ShellSection = ScadRcModelAdapter.ElasticShells(e, nu),
            Stages = scenario.Stages,
        });
        var build = RcStructuralMeshBuilder.Build(adapted.Model, new LinearRcSectionFactory());
        var f = build.Combination(adapted.Model.Stages[0].Loads.Concat(adapted.Model.Stages[1].Loads).ToList());
        var uLin = build.Mesh.SolveLinear(f, build.Bc);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (u, history) = build.Mesh.SolveNonlinear(f, build.Bc, nSteps: 1, tol: 1e-8, maxIter: 30, corotational: false,
            u0: new double[build.Mesh.NDof], f0: f);
        foreach (var h in history) output.WriteLine($"итерация {h.Iteration}: невязка {h.Residual:e3}");
        output.WriteLine($"время {sw.Elapsed:mm\\:ss}; центр: линейно {Uz(build, uLin, CenterNode) * 1000:0.###} мм, " +
            $"нелинейно {Uz(build, u, CenterNode) * 1000:0.###} мм");
    }

    /// <summary>
    /// Армирование пластин по КЭ (как его видит сценарий с материалами опыта): центр, ключ сечения, слои
    /// (Asx/Asy, см²/м, и z, мм) — в OPENCS_CSFEA_OUT/csfea-plate-rebar.csv.
    /// </summary>
    [Fact]
    public void PlateRebarMap()
    {
        if (Read() is not { } data) return;
        var report = new List<string>();
        var scenario = ScadShellScenario.Build(data, new ScadShellScenarioOptions(false, ScadShellMaterialMode.Experiment,
            [new ScadShellStage("L1", [(1, 1.0)], 1.0)]), report);
        var nodes = data.Nodes.ToDictionary(n => n.Id);
        var sb = new StringBuilder("shell,x,y,key,layers\n");
        foreach (var el in data.Elements.Where(e => e.NodeIds.Length is 3 or 4))
        {
            if (scenario.PlateSection(el.Id) is not { } s) continue;
            double x = el.NodeIds.Average(n => nodes[n].X), y = el.NodeIds.Average(n => nodes[n].Y);
            string layers = string.Join(" ", s.Section.RebarLayers.Select(l => FormattableString.Invariant(
                $"x{l.Asx * 1e4:0.##}@{l.Zsx * 1e3:0}/y{l.Asy * 1e4:0.##}@{l.Zsy * 1e3:0}")));
            sb.Append(string.Join(",", el.Id, F(x), F(y), s.Key, layers)).Append('\n');
        }
        string? dir = Environment.GetEnvironmentVariable("OPENCS_CSFEA_OUT");
        if (!string.IsNullOrWhiteSpace(dir)) File.WriteAllText(Path.Combine(dir, "csfea-plate-rebar.csv"), sb.ToString());
        foreach (var r in report.Distinct()) output.WriteLine(r);
    }

    /// <summary>Опыт: прогибы от нормативной нагрузки (L2, без собственного веса), мм — т. 2, 4, 9.</summary>
    static readonly Dictionary<string, double> Experiment = new() { ["2"] = 9.2, ["4"] = 15.1, ["9"] = 8.6 };

    /// <summary>
    /// Схема SCAD с материалами опыта (<see cref="ScadShellMaterialMode.Experiment"/>): пластины — слоистые сечения
    /// по ЖБ-группам и заданному армированию (20 слоёв бетона), диаграммы — трёхлинейная бетона и двухлинейная
    /// арматуры (II группа), колонны — сечения CScore по заданному армированию; стадии L1 × 1, L2 по 0,2 × 5.
    /// </summary>
    void RunSecant(string name, RcSecantOptions options)
    {
        if (Read() is not { } data) return;
        var report = new List<string>();
        var scenario = ScadShellScenario.Build(data, new ScadShellScenarioOptions(false, ScadShellMaterialMode.Experiment,
            [new ScadShellStage("L1", [(1, 1.0)], 1.0), new ScadShellStage("L2", [(2, 1.0)], 0.2)]), report);
        var materials = scenario.BeamMaterials;
        RcShellSection Shell(ScadShellElementSection s)
        {
            var plate = s.Section.CloneForCalc();
            plate.NLayers = 20;
            var concrete = materials[plate.ConcreteMaterialId];
            var rebar = materials[plate.RebarMaterialId];
            var layerDiagrams = plate.RebarLayers.Select(l => l.MaterialId > 0 && materials.TryGetValue(l.MaterialId, out var lm)
                ? lm.GetDiagramms(CScore.DiagrammType.L2)?[CScore.CalcType.N] : null).ToArray();
            return new RcShellSection(s.Key)
            {
                Plate = plate,
                PlateMaterials = new CSfea.CScoreBridge.PlateSectionMaterials
                {
                    ConcreteDiagram = concrete.GetDiagramms(CScore.DiagrammType.L3)![CScore.CalcType.N],
                    RebarDiagram = rebar.GetDiagramms(CScore.DiagrammType.L2)![CScore.CalcType.N],
                    LayerDiagrams = layerDiagrams,
                    ConcreteE_MPa = concrete.E / 1000, Nu = 0.2,
                },
            };
        }
        var adapted = ScadRcModelAdapter.Adapt(new ScadRcModelInput
        {
            Data = data, PlateSection = scenario.PlateSection, ShellSection = Shell,
            BeamSection = scenario.BeamSection, BeamCalc = CScore.CalcType.N, Stages = scenario.Stages,
        });

        string? dir = Environment.GetEnvironmentVariable("OPENCS_CSFEA_OUT");
        StreamWriter? log = null;
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
            log = new StreamWriter(Path.Combine(dir, $"csfea-{name}-log.txt"), false, Encoding.UTF8) { AutoFlush = true };
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        RcSecantRun run;
        try
        {
            run = RcSecantAnalysis.Run(adapted.Model, new RcSecantOptions
            {
                TensionConcrete = options.TensionConcrete, Psi = options.Psi, PoissonUncracked = options.PoissonUncracked,
                PlateCrackRule = options.PlateCrackRule, ZeroStrainBand = options.ZeroStrainBand,
                DropMembraneBendingCoupling = options.DropMembraneBendingCoupling,
                Solver = new CSfea.Core.SecantPicardOptions
                {
                    Geometric = options.Solver.Geometric, Log = s => log?.WriteLine($"{sw.Elapsed:hh\\:mm\\:ss} {s}"),
                },
            });
        }
        finally { log?.Dispose(); }
        foreach (var s in report.Concat(adapted.Report).Concat(run.Build.Report).Distinct()) output.WriteLine(s);
        var r = run.Result;
        output.WriteLine($"{name}: {(r.Completed ? "все стадии пройдены" : r.Message)}; шагов {r.Steps.Count}, итераций " +
            $"{r.Iterations.Count}, время {sw.Elapsed:mm\\:ss}; оболочек с нелинейным законом " +
            $"{run.ShellStates.Count(s => s != null)}, стержней {run.BeamStates.Count(s => s != null)}");

        var points = PointNodes(data);
        double W(double[] u, int node) => -u[run.Build.Dof(node, 2)] * 1000;
        var sb = new StringBuilder("stage,factor,refinement,converged,iterations,residual,cracked_shells,yielded_shells,failed_shells," +
            string.Join(",", points.Select(p => $"w{p.Name}_mm")) + "\n");
        foreach (var st in r.Steps)
            sb.Append(string.Join(",", adapted.Model.Stages[st.Stage].Name, F(st.LoadFactor), st.IsRefinement ? 1 : 0,
                st.Converged ? 1 : 0, st.Iterations, F(st.TrueResidual), st.Shells.Count(x => x.Cracked),
                st.Shells.Count(x => x.Yielded), st.Shells.Count(x => x.Failed),
                string.Join(",", points.Select(p => F(W(st.U, p.Node)))))).Append('\n');
        if (!string.IsNullOrWhiteSpace(dir)) File.WriteAllText(Path.Combine(dir, $"csfea-{name}-steps.csv"), sb.ToString());

        var e1 = r.StageEnd(0);
        var e2 = r.StageEnd(1);
        foreach (var st in r.Steps.Where(s => s.Converged))
            output.WriteLine($"  {adapted.Model.Stages[st.Stage].Name} λ = {st.LoadFactor:0.###}: " +
                string.Join(", ", points.Select(p => $"т. {p.Name} {W(st.U, p.Node):0.00}")) +
                $" мм; итераций {st.Iterations}, невязка {st.TrueResidual:e2}, трещины {st.Shells.Count(x => x.Cracked)}, " +
                $"текучесть {st.Shells.Count(x => x.Yielded)}, отказ {st.Shells.Count(x => x.Failed)}");
        if (e1 != null && e2 != null)
            foreach (var (pn, node) in points)
                output.WriteLine($"Точка {pn}: L1 = {W(e1.U, node):0.00} мм, от L2 = {W(e2.U, node) - W(e1.U, node):0.00} мм " +
                    $"(опыт {Experiment[pn]:0.0} мм)");
        if (e1 != null && e2 != null) Export(name, run.Build, adapted.Model, [e1.U, e2.U]);
        if (r.Steps.LastOrDefault(s => s.Converged) is { } last && !string.IsNullOrWhiteSpace(dir))
            ExportElements(Path.Combine(dir, $"csfea-{name}-elements.csv"), run, last);
        Assert.True(r.Completed, r.Message);
    }

    /// <summary>
    /// CSV по оболочкам последнего сошедшегося шага: номер КЭ, центр, трещина/текучесть/отказ, усилия в центре по закону
    /// сечения (оси сечения; кН/м, кН·м/м).
    /// </summary>
    static void ExportElements(string path, RcSecantRun run, CSfea.Core.SecantStepResult step)
    {
        var mesh = run.Build.Mesh;
        var sb = new StringBuilder("shell,x,y,cracked,yielded,failed,Nx,Ny,Nxy,Mx,My,Mxy\n");
        for (int e = 0; e < mesh.Shells.Count; e++)
        {
            if (run.ShellStates[e] is not { } st) continue;
            var c = mesh.ShellCoords(e);
            var dofs = CSfea.Core.StructuralMesh.NodeDofs(mesh.Shells[e].Nodes);
            var (eps, kappa, gamma) = CSfea.Core.ShellElementForces.CenterStrainsGlobal(c, dofs.Select(d => step.U[d]).ToArray());
            if (mesh.Shells[e].Section is CSfea.Core.RotatedShellResponse rot) (eps, kappa, gamma) = rot.ToSection(eps, kappa, gamma);
            var f = st.TrueForces(eps, kappa, gamma);
            var s = step.Shells[e];
            sb.Append(string.Join(",", run.Build.ShellIds[e], F(c.Average(p => p[0])), F(c.Average(p => p[1])),
                s.Cracked ? 1 : 0, s.Yielded ? 1 : 0, s.Failed ? 1 : 0,
                F(f.N[0] / 1e3), F(f.N[1] / 1e3), F(f.N[2] / 1e3), F(f.M[0] / 1e3), F(f.M[1] / 1e3), F(f.M[2] / 1e3))).Append('\n');
        }
        File.WriteAllText(path, sb.ToString());
    }

    ScadSchemaData? Read()
    {
        string? spr = Environment.GetEnvironmentVariable("OPENCS_SCAD_NL_SPR");
        if (string.IsNullOrWhiteSpace(spr)) return null;
        string dir = Environment.GetEnvironmentVariable("OPENCS_SCAD_DIR") ?? ScadInstallLocator.FindDllDirectory()!;
        var native = ScadApiNative.Load(dir);
        using var s = new ScadApiSession(native);
        s.Open(spr);
        return ScadApiReader.Read(s, new ScadReadOptions(), null, CancellationToken.None).Data;
    }

    static List<(string Name, int Node)> PointNodes(ScadSchemaData data) =>
        Points.Select(p => (p.Name, data.Nodes.Where(n => Math.Abs(n.Z) < 1e-6)
            .OrderBy(n => Math.Pow(n.X - p.X, 2) + Math.Pow(n.Y - p.Y, 2)).First().Id)).ToList();

    static double Uz(RcStructuralMeshBuild b, double[] u, int node) => u[b.Dof(node, 2)];

    /// <summary>CSV: стадия, узел, x, y, z, uz (м) — все узлы плиты (z = 0) по стадиям.</summary>
    void Export(string name, RcStructuralMeshBuild b, RcStructuralModel model, IReadOnlyList<double[]> u)
    {
        string? dir = Environment.GetEnvironmentVariable("OPENCS_CSFEA_OUT");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("stage,node,x,y,z,uz\n");
        for (int k = 0; k < u.Count; k++)
            foreach (var n in model.Nodes.Where(n => Math.Abs(n.Z) < 1e-6))
                sb.Append(string.Join(",", model.Stages[k].Name, n.Id, F(n.X), F(n.Y), F(n.Z), F(Uz(b, u[k], n.Id)))).Append('\n');
        string path = Path.Combine(dir, $"csfea-{name}-displacements.csv");
        File.WriteAllText(path, sb.ToString());
        output.WriteLine($"Выгрузка: {path}");
    }

    static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
}
