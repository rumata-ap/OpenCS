using System.Globalization;
using CScore;
using CSfea.CScoreBridge;
using CSfea.CScoreBridge.Structural;
using Xunit.Abstractions;

namespace CSfea.Tests;

/// <summary>
/// Плита McNeice (1967; Jofriet, McNeice 1971): 914,4 × 914,4 × 44,45 мм на четырёх угловых опорах, сила в центре,
/// одна сетка у низа 282 мм²/м в двух направлениях (d = 33,3 мм). Данные — по DIANA Verification Report «McNeice Slab»:
/// бетон E = 28 600 МПа, ν = 0,15, fcm = 38 МПа, ftm = 2,9 МПа; сталь Es = 200 000 МПа, fy = 350 МПа. Опыт — прогиб узла
/// на оси симметрии в 76,2 мм от центра (кривая DIANA, оцифровка 07.10.2026). Четверть плиты по двум плоскостям симметрии.
/// </summary>
[Trait("Category", "Verification")]
public class McNeiceSlabTests(ITestOutputHelper output)
{
    const double L = 0.9144, H = 0.04445, D = 0.0333, As = 2.82e-4;
    const int N = 24;                       // КЭ на сторону полной плиты (38,1 мм)
    const double PMax = 12e3;               // Н, полная сила

    /// <summary>Опыт (DIANA, рис. «Experimental (McNeice 1967)»): прогиб, мм → сила, кН.</summary>
    static readonly (double W, double P)[] Experiment =
    [
        (0.25, 1.60), (0.75, 4.70), (1.00, 5.73), (1.25, 6.35), (1.50, 6.76), (1.75, 7.08), (2.00, 7.54), (2.25, 7.78),
        (2.50, 8.34), (2.75, 8.68), (3.00, 9.03), (3.25, 9.34), (3.50, 9.52), (3.75, 9.70), (4.00, 9.94), (4.25, 10.18),
        (4.50, 10.37), (4.75, 10.57), (5.00, 10.83),
    ];

    static MaterialChars ConcreteChars(CalcType ct) => new()
    {
        Type = MatType.Concrete, TypeCalc = ct, Fc = -38000, Ft = 2900, E = 28_600_000,
        Ec0 = -0.002, Ec1 = -0.6 * 38000 / 28.6e6, Ec1Red = -0.0015, Ec2 = -0.0035,
        Et0 = 0.0001, Et1 = 0.6 * 2900 / 28.6e6, Et1Red = 0.00008, Et2 = 0.00015,
    };

    static MaterialChars RebarChars(CalcType ct) => new()
    {
        Type = MatType.ReSteelF, TypeCalc = ct, Fc = -350000, Ft = 350000, E = 200_000_000, Ec2 = -0.025, Et2 = 0.025,
    };

    static RcShellSection Section()
    {
        var concrete = new Material
        {
            Id = 1, Tag = "C30/37 (McNeice)", Type = MatType.Concrete, E = 28_600_000,
            MaterialChars = [ConcreteChars(CalcType.C), ConcreteChars(CalcType.CL), ConcreteChars(CalcType.N), ConcreteChars(CalcType.NL)],
        };
        var rebar = new Material
        {
            Id = 2, Tag = "fy 350", Type = MatType.ReSteelF, E = 200_000_000,
            MaterialChars = [RebarChars(CalcType.C), RebarChars(CalcType.CL), RebarChars(CalcType.N), RebarChars(CalcType.NL)],
        };
        double z = -(D - H / 2);
        return new RcShellSection("McNeice")
        {
            Plate = new PlateSection
            {
                H = H, NLayers = 20, TensionConcrete = true, PlateModel = "layered", PoissonUncracked = 0.15,
                RebarLayers = [new PlateRebarLayer { Asx = As, Asy = As, Zsx = z, Zsy = z }],
            },
            PlateMaterials = new PlateSectionMaterials
            {
                ConcreteDiagram = concrete.GetDiagramms(DiagrammType.L3)![CalcType.N],
                RebarDiagram = rebar.GetDiagramms(DiagrammType.L2)![CalcType.N],
                ConcreteE_MPa = 28600, Nu = 0.15,
            },
        };
    }

    /// <summary>Полная плита: опоры uz в углах, сила в центре; стадия до <see cref="PMax"/> за <paramref name="steps"/> шагов.</summary>
    static RcStructuralModel FullModel(int steps, double factor = 1.0)
    {
        var m = new RcStructuralModel();
        int Id(int i, int j) => i * (N + 1) + j;
        for (int i = 0; i <= N; i++)
            for (int j = 0; j <= N; j++) m.Nodes.Add(new RcNode(Id(i, j), L * i / N, L * j / N, 0));
        var sec = Section();
        int e = 0;
        for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++) m.Shells.Add(new RcShell(e++, [Id(i, j), Id(i + 1, j), Id(i + 1, j + 1), Id(i, j + 1)], sec, [1, 0, 0]));
        // Только uz: в плане четверть держат плоскости симметрии (связи в плане в углу дали бы ложный распор).
        // Полная плита сама по себе в плане изменяема — считается только четверть.
        foreach (var c in new[] { Id(0, 0), Id(N, 0), Id(0, N), Id(N, N) }) m.Supports.Add(new RcSupport(c, 0b000100));
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(Id(N / 2, N / 2), [0, 0, -PMax, 0, 0, 0]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("P", [(1, factor)], steps));
        return m;
    }

    static RcStructuralModel Quarter(int steps, double factor = 1.0) =>
        RcSymmetry.Cut(FullModel(steps, factor), [new RcSymmetryPlane(0, L / 2), new RcSymmetryPlane(1, L / 2)]);

    /// <summary>Узел замера: на оси симметрии в 76,2 мм от центра (2 КЭ), в четверти — к опоре.</summary>
    static int MeasureNode => (N / 2 - 2) * (N + 1) + N / 2;
    static int CenterNode => (N / 2) * (N + 1) + N / 2;

    /// <summary>Кривая «сила — прогиб» секущим расчётом: (P, кН; прогиб узла замера и центра, мм; трещины; текучесть).</summary>
    /// <remarks>
    /// OPENCS_CSFEA_OUT — каталог выгрузки (необязательно): mcneice-{tag}-curve.csv (кривая), -elements.csv (центры КЭ
    /// четверти и их состояние на каждом сошедшемся шаге), -nodes.csv (прогибы узлов четверти на каждом шаге).
    /// </remarks>
    List<(double P, double W, double WCenter, int Cracked, int Yielded)> Run(string name, string tag, RcSecantOptions options, int steps = 24)
    {
        var m = Quarter(steps);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var run = RcSecantAnalysis.Run(m, options);
        var r = run.Result;
        var curve = r.Steps.Where(s => s.Converged).Select(s => (P: s.LoadFactor * PMax / 1e3,
            W: -s.U[run.Build.Dof(MeasureNode, 2)] * 1e3, WCenter: -s.U[run.Build.Dof(CenterNode, 2)] * 1e3,
            Cracked: s.Shells.Count(x => x.Cracked), Yielded: s.Shells.Count(x => x.Yielded))).ToList();
        output.WriteLine($"=== {name}: {(r.Completed ? "до конца" : r.Message)}; КЭ {m.Shells.Count}, время {sw.Elapsed:mm\\:ss}");
        output.WriteLine("P, кН; w, мм; w центра, мм; опыт при w, кН; трещины; текучесть");
        foreach (var c in curve)
            output.WriteLine(string.Join("; ", F(c.P), F(c.W), F(c.WCenter), F(ExperimentAt(c.W)), c.Cracked, c.Yielded));
        if (Environment.GetEnvironmentVariable("OPENCS_CSFEA_OUT") is { Length: > 0 } dir) Export(dir, tag, m, run);
        return curve;
    }

    static void Export(string dir, string tag, RcStructuralModel m, RcSecantRun run)
    {
        Directory.CreateDirectory(dir);
        static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        var steps = run.Result.Steps.Where(s => s.Converged).ToList();
        var curve = new System.Text.StringBuilder("P_kN,w_mm,w_center_mm,cracked,yielded\n");
        var elems = new System.Text.StringBuilder("P_kN,shell,x_mm,y_mm,cracked,yielded,failed\n");
        var nodes = new System.Text.StringBuilder("P_kN,node,x_mm,y_mm,uz_mm\n");
        var mesh = run.Build.Mesh;
        foreach (var s in steps)
        {
            string p = R(s.LoadFactor * PMax / 1e3);
            curve.Append(string.Join(",", p, R(-s.U[run.Build.Dof(MeasureNode, 2)] * 1e3), R(-s.U[run.Build.Dof(CenterNode, 2)] * 1e3),
                s.Shells.Count(x => x.Cracked), s.Shells.Count(x => x.Yielded))).Append('\n');
            for (int e = 0; e < mesh.Shells.Count; e++)
            {
                var c = mesh.ShellCoords(e);
                elems.Append(string.Join(",", p, run.Build.ShellIds[e], R(c.Average(q => q[0]) * 1e3), R(c.Average(q => q[1]) * 1e3),
                    s.Shells[e].Cracked ? 1 : 0, s.Shells[e].Yielded ? 1 : 0, s.Shells[e].Failed ? 1 : 0)).Append('\n');
            }
            foreach (var n in m.Nodes)
                nodes.Append(string.Join(",", p, n.Id, R(n.X * 1e3), R(n.Y * 1e3), R(s.U[run.Build.Dof(n.Id, 2)] * 1e3))).Append('\n');
        }
        File.WriteAllText(Path.Combine(dir, $"mcneice-{tag}-curve.csv"), curve.ToString());
        File.WriteAllText(Path.Combine(dir, $"mcneice-{tag}-elements.csv"), elems.ToString());
        File.WriteAllText(Path.Combine(dir, $"mcneice-{tag}-nodes.csv"), nodes.ToString());
    }

    /// <summary>
    /// Эпюры по толщине в КЭ четверти у центра (центр КЭ — 19 мм от осей симметрии) при P = 6 кН для правил Layer и
    /// Section с ψs: σx бетона по слоям (кПа → МПа), трещина слоя, εx; арматура x — ε, σ (с ψs), ψs. Печать и, при
    /// OPENCS_CSFEA_OUT, mcneice-profile-{layer|section}.csv.
    /// </summary>
    [Fact]
    public void ThicknessProfiles()
    {
        foreach (var (rule, tag) in new[] { (PlateCrackRule.Layer, "layer"), (PlateCrackRule.Section, "section") })
        {
            var m = Quarter(12, 0.5);
            var run = RcSecantAnalysis.Run(m, new RcSecantOptions { Psi = true, PoissonUncracked = 0.15, PlateCrackRule = rule });
            var end = run.Result.StageEnd(0)!;
            var mesh = run.Build.Mesh;
            int e = Enumerable.Range(0, mesh.Shells.Count).MinBy(i =>
            {
                var c = mesh.ShellCoords(i);
                return Math.Pow(c.Average(q => q[0]) - (L / 2 - L / N / 2), 2) + Math.Pow(c.Average(q => q[1]) - (L / 2 - L / N / 2), 2);
            });
            var dofs = CSfea.Core.StructuralMesh.NodeDofs(mesh.Shells[e].Nodes);
            var (eps, kappa, _) = CSfea.Core.ShellElementForces.CenterStrainsGlobal(mesh.ShellCoords(e), dofs.Select(d => end.U[d]).ToArray());
            var st = (PlateSecantShellState)run.ShellStates[e]!;
            var sec = Section();
            var plate = sec.Plate!;
            var mat = sec.PlateMaterials!;
            var ss = new ShellStrainState(eps[0], eps[1], eps[2], kappa[0], kappa[1], kappa[2]);
            var sb = new System.Text.StringBuilder("kind,z_mm,eps_x,sigma_x_MPa,cracked,psi\n");
            output.WriteLine($"=== {tag}: P = 6 кН, КЭ {run.Build.ShellIds[e]}, κx = {kappa[0]:e3} 1/м, трещин слоёв {st.Layers.CrackedCount}");
            for (int i = 0; i < plate.NLayers; i++)
            {
                var lp = plate.EvaluateConcreteLayer(i, ss, mat.ConcreteDiagram, null, st.Layers);
                double c2 = Math.Cos(lp.Theta) * Math.Cos(lp.Theta), s2 = 1 - c2;
                double sx = (lp.Sig1 * c2 + lp.Sig2 * s2) / 1e3;
                sb.Append(string.Join(",", "concrete", F3(lp.Z * 1e3), F3(ss.EpsX(lp.Z)), F3(sx), lp.Cracked ? 1 : 0, "")).Append('\n');
                output.WriteLine($"  слой {i,2}: z = {lp.Z * 1e3,7:0.00} мм, εx = {ss.EpsX(lp.Z) * 1e3,7:0.000}‰, σx = {sx,7:0.00} МПа{(lp.Cracked ? ", трещина" : "")}");
            }
            var rp = plate.EvaluateRebar(0, true, ss, mat.RebarDiagram, null, st.Layers);
            sb.Append(string.Join(",", "rebar", F3(rp.Z * 1e3), F3(rp.Eps), F3(rp.Sig / 1e3), "", F3(rp.Psi))).Append('\n');
            output.WriteLine($"  арматура x: z = {rp.Z * 1e3:0.00} мм, εs = {rp.Eps * 1e3:0.000}‰, σs = {rp.Sig / 1e3:0.0} МПа, ψs = {rp.Psi:0.000}");
            if (Environment.GetEnvironmentVariable("OPENCS_CSFEA_OUT") is { Length: > 0 } dir)
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"mcneice-profile-{tag}.csv"), sb.ToString());
            }
        }
    }

    static string F3(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>Сила опыта при прогибе w (линейная интерполяция, NaN вне кривой).</summary>
    static double ExperimentAt(double w)
    {
        var pts = new List<(double W, double P)> { (0, 0) };
        pts.AddRange(Experiment);
        for (int i = 1; i < pts.Count; i++)
            if (w <= pts[i].W) return pts[i - 1].P + (pts[i].P - pts[i - 1].P) * (w - pts[i - 1].W) / (pts[i].W - pts[i - 1].W);
        return double.NaN;
    }

    static string F(double v) => double.IsNaN(v) ? "—" : v.ToString("0.00", CultureInfo.InvariantCulture);

    [Fact]
    public void SectionPsi() => Run("Section, ψs", "section-psi", new RcSecantOptions { Psi = true, PoissonUncracked = 0.15, PlateCrackRule = PlateCrackRule.Section });

    [Fact]
    public void LayerPsi() => Run("Layer, ψs", "layer-psi", new RcSecantOptions { Psi = true, PoissonUncracked = 0.15, PlateCrackRule = PlateCrackRule.Layer });

    [Fact]
    public void SectionNoPsi() => Run("Section, без ψs", "section-nopsi", new RcSecantOptions { Psi = false, PoissonUncracked = 0.15, PlateCrackRule = PlateCrackRule.Section });

    [Fact]
    public void NoTension() => Run("бетон без растяжения", "no-tension", new RcSecantOptions { TensionConcrete = false, Psi = false });
}
