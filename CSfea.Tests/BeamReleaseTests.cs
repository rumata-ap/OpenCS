using CSfea.CScoreBridge.Structural;
using CSfea.Core;
using CSfea.Sparse;

namespace CSfea.Tests;

/// <summary>Шарниры концов стержня (<see cref="BeamReleases"/>): конденсация, нагрузка, узлы без жёсткости, CR.</summary>
[HarnessChecks]
public class BeamReleaseTests
{
    private const double E = 30e9;

    // Местные DOF конца: N, Qy, Qz, T, My, Mz.
    private const int Qy = 1 << 1, My = 1 << 4, Mz = 1 << 5;

    [Fact]
    public static void RunAll()
    {
        RunRecoverZeroEndForces();
        RunGerberBeam();
        RunGerberBeamDoubleHinge();
        RunPortalPinnedGirder();
        RunShearRelease();
        RunProppedCantileverLoad();
        RunGerberCorotational();
    }

    private static readonly RcBeamSection Rect = new("rect 0.3×0.5")
    {
        Elastic = new BeamSection(E, 0.15, 0.3 * 0.125 / 12, 0.5 * 0.027 / 12, 2.8e-3),
    };

    // Узлы по оси x, refVec = z: местная y — глобальная z, вертикальная нагрузка гнёт по Mz (бит 5).
    private static RcStructuralModel Line(params double[] xs)
    {
        var m = new RcStructuralModel();
        for (int i = 0; i < xs.Length; i++) m.Nodes.Add(new RcNode(i + 1, xs[i], 0, 0));
        for (int i = 0; i + 1 < xs.Length; i++) m.Beams.Add(new RcBeam(101 + i, i + 1, i + 2, Rect, new[] { 0.0, 0.0, 1.0 }));
        return m;
    }

    private static void WithRelease(RcStructuralModel m, int beamId, int relI, int relJ)
    {
        int i = m.Beams.FindIndex(b => b.Id == beamId);
        m.Beams[i] = m.Beams[i] with { ReleaseI = relI, ReleaseJ = relJ };
    }

    private static (RcStructuralMeshBuild Build, double[] U, double[] R) Solve(RcStructuralModel m)
    {
        var build = RcStructuralMeshBuilder.Build(m, new LinearRcSectionFactory());
        var f = build.LoadCases[1];
        var u = build.Mesh.SolveLinear(f, build.Bc);
        return (build, u, build.Mesh.ComputeReactions(u, build.Bc, fExternal: f));
    }

    // Локальные усилия концов стержня (12) по перемещениям: сконденсированная K·T·u.
    private static double[] EndForces(RcStructuralMeshBuild build, double[] u, int beamId)
    {
        int e = Array.IndexOf(build.BeamIds, beamId);
        var b = build.Mesh.Beams[e];
        var (t, l) = BeamElements.Beam3dT(build.Mesh.BeamCoords(e), b.RefVec);
        var ue = StructuralMesh.NodeDofs(new[] { b.I, b.J }).Select(d => u[d]).ToArray();
        var k = BeamReleases.Condense(BeamElements.Beam3dKLocal(b.Section, l), b.Releases);
        return Dense.MatVec(k, Dense.MatVec(t, ue));
    }

    /// <summary>Восстановленные перемещения концов дают нулевые усилия по освобождённым DOF и те же — по остальным.</summary>
    private static void RunRecoverZeroEndForces()
    {
        TestHarness.Section("Шарниры: восстановление перемещений концов");
        var s = new[,] { { 4.5e9, 2e7, -1e7 }, { 2e7, 8e7, 3e6 }, { -1e7, 3e6, 5e7 } };
        var resp = new SecantBeamResponse(s, 2e7, new BeamShearStiffness(9e8, 7e8));
        var k = BeamElements.Beam3dKLocal(resp, 2.5);
        int mask = BeamReleases.Mask(My | Mz, Qy | (1 << 3));
        var d = new[] { 1e-4, -2e-3, 3e-3, 1e-3, -4e-3, 5e-3, 2e-4, 1e-3, -1e-3, -2e-3, 3e-3, 1e-3 };
        var dr = BeamReleases.Recover(k, mask, d);
        var full = Dense.MatVec(k, dr);
        var cond = Dense.MatVec(BeamReleases.Condense(k, mask), d);
        double scale = cond.Max(Math.Abs), maxRel = 0, maxKept = 0;
        for (int i = 0; i < 12; i++)
        {
            if ((mask & (1 << i)) != 0) maxRel = Math.Max(maxRel, Math.Abs(full[i]));
            else maxKept = Math.Max(maxKept, Math.Abs(full[i] - cond[i]));
        }
        TestHarness.Check("усилия по освобождённым DOF = 0", maxRel < 1e-9 * scale, $"max {maxRel:e2} при {scale:e2}");
        TestHarness.Check("остальные = сконденсированная K·d", maxKept < 1e-9 * scale, $"max {maxKept:e2}");
        bool keptSame = Enumerable.Range(0, 12).Where(i => (mask & (1 << i)) == 0).All(i => dr[i] == d[i]);
        TestHarness.Check("сохранённые перемещения не меняются", keptSame);
    }

    // Балка Гербера: A(0) шарнир, B(4) каток, шарнир C(5), сила P в E(6,5), каток D(8). Участок CD — простая балка
    // (R_D = P/2, P/2 в шарнир), консоль BC с P/2: R_A = −P/8, R_B = 5P/8, M_B = P/2·1.
    private static RcStructuralModel Gerber()
    {
        var m = Line(0, 4, 5, 6.5, 8);
        m.Supports.Add(new RcSupport(1, 0b001111));   // X Y Z UX
        m.Supports.Add(new RcSupport(2, 0b000110));   // Y Z
        m.Supports.Add(new RcSupport(5, 0b000110));
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(4, new[] { 0.0, 0.0, -P, 0, 0, 0 }));
        m.LoadCases.Add(lc);
        return m;
    }

    private const double P = 50e3;

    private static void CheckGerber(RcStructuralMeshBuild build, double[] u, double[] r)
    {
        TestHarness.CheckRel("R_A = −P/8", r[build.Dof(1, 2)], -P / 8, 1e-9);
        TestHarness.CheckRel("R_B = 5P/8", r[build.Dof(2, 2)], 5 * P / 8, 1e-9);
        TestHarness.CheckRel("R_D = P/2", r[build.Dof(5, 2)], P / 2, 1e-9);
        var bc = EndForces(build, u, 102);   // B–C
        var ce = EndForces(build, u, 103);   // C–E
        TestHarness.CheckRel("|M_B| = P/2·1", Math.Abs(bc[5]), P / 2, 1e-9);
        TestHarness.Check("M в шарнире: конец стержня BC = 0", Math.Abs(bc[11]) < 1e-6 * P, $"{bc[11]:e2}");
        TestHarness.Check("M в шарнире: начало стержня CE = 0", Math.Abs(ce[5]) < 1e-6 * P, $"{ce[5]:e2}");
        TestHarness.CheckRel("Q в шарнире = P/2", Math.Abs(ce[1]), P / 2, 1e-9);
    }

    private static void RunGerberBeam()
    {
        TestHarness.Section("Шарниры: балка Гербера (шарнир на конце одного стержня)");
        var m = Gerber();
        WithRelease(m, 102, 0, Mz);
        var (build, u, r) = Solve(m);
        CheckGerber(build, u, r);
        TestHarness.Check("узлы не закреплялись", build.Report.All(s => !s.Contains("шарнир")), string.Join(" | ", build.Report));
    }

    /// <summary>Оба стержня в узле C освобождены по Mz: поворот узла без жёсткости закрепляется, решение то же.</summary>
    private static void RunGerberBeamDoubleHinge()
    {
        TestHarness.Section("Шарниры: балка Гербера — оба конца в узле освобождены");
        var m = Gerber();
        WithRelease(m, 102, 0, Mz);
        WithRelease(m, 103, Mz, 0);
        var (build, u, r) = Solve(m);
        CheckGerber(build, u, r);
        // Местная z = x × z = −y: поворот по Mz — глобальный UY.
        TestHarness.Check("в отчёте закреплён UY узла 3", build.Report.Any(s => s.Contains("шарнир") && s.Contains("3 UY")),
            string.Join(" | ", build.Report));
    }

    /// <summary>
    /// Рама: две заделанные стойки h, ригель L с шарнирами My, Mz на обоих концах. Горизонтальная H делится между
    /// стойками-консолями (момент заделки = h·Rx); равномерная q на ригеле — простая балка: в стойки только qL/2.
    /// </summary>
    private static void RunPortalPinnedGirder()
    {
        TestHarness.Section("Шарниры: рама с шарнирным ригелем");
        double h = 3.0, l = 6.0, hForce = 20e3, q = 15e3;
        var m = new RcStructuralModel();
        m.Nodes.Add(new RcNode(1, 0, 0, 0));
        m.Nodes.Add(new RcNode(2, 0, 0, h));
        m.Nodes.Add(new RcNode(3, l, 0, h));
        m.Nodes.Add(new RcNode(4, l, 0, 0));
        m.Beams.Add(new RcBeam(101, 1, 2, Rect, new[] { 1.0, 0.0, 0.0 }));
        m.Beams.Add(new RcBeam(102, 2, 3, Rect, new[] { 0.0, 0.0, 1.0 }, My | Mz, My | Mz));
        m.Beams.Add(new RcBeam(103, 4, 3, Rect, new[] { 1.0, 0.0, 0.0 }));
        m.Supports.Add(new RcSupport(1, 0x3F));
        m.Supports.Add(new RcSupport(4, 0x3F));
        var lc = new RcLoadCase(1, "H + q");
        lc.Nodal.Add(new RcNodalLoad(2, new[] { hForce, 0, 0, 0, 0, 0 }));
        lc.Beams.Add(new RcBeamLoad(102, new[] { 0.0, 0.0, -q }));
        m.LoadCases.Add(lc);
        var (build, u, r) = Solve(m);
        // Поровну — с точностью до продольной податливости ригеля (≈ 0,5 %).
        TestHarness.CheckRel("ΣRx = −H", r[build.Dof(1, 0)] + r[build.Dof(4, 0)], -hForce, 1e-9);
        TestHarness.CheckRel("Rx левой заделки ≈ −H/2", r[build.Dof(1, 0)], -hForce / 2, 1e-2);
        TestHarness.CheckRel("Rz левой = qL/2", r[build.Dof(1, 2)], q * l / 2, 1e-9);
        TestHarness.CheckRel("Rz правой = qL/2", r[build.Dof(4, 2)], q * l / 2, 1e-9);
        // Верх стойки без момента: момент заделки вокруг Y = h·Rx (равновесие стойки под силой в верхнем узле).
        TestHarness.CheckRel("My левой заделки = h·Rx", r[build.Dof(1, 4)], h * r[build.Dof(1, 0)], 1e-9);
        TestHarness.CheckRel("My правой заделки = h·Rx", r[build.Dof(4, 4)], h * r[build.Dof(4, 0)], 1e-9);
        var g = EndForces(build, u, 102);
        double mEnds = new[] { g[4], g[5], g[10], g[11] }.Max(Math.Abs);
        TestHarness.Check("моменты на концах ригеля = 0", mEnds < 1e-6 * hForce, $"{mEnds:e2}");
    }

    /// <summary>
    /// Освобождение поперечной силы: консоль A–B, стержень B–C с освобождённой Qy в начале, каток в C, сила P в B.
    /// Стержень BC не передаёт поперечную силу, момент в нём постоянен и у катка равен 0 — вся P на заделку A.
    /// </summary>
    private static void RunShearRelease()
    {
        TestHarness.Section("Шарниры: освобождение поперечной силы");
        var m = Line(0, 2, 4);
        WithRelease(m, 102, Qy, 0);
        m.Supports.Add(new RcSupport(1, 0x3F));
        m.Supports.Add(new RcSupport(3, 0b000110));
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(2, new[] { 0.0, 0.0, -P, 0, 0, 0 }));
        m.LoadCases.Add(lc);
        var (build, u, r) = Solve(m);
        TestHarness.CheckRel("R_A = P", r[build.Dof(1, 2)], P, 1e-9);
        TestHarness.Check("R_C = 0", Math.Abs(r[build.Dof(3, 2)]) < 1e-6 * P, $"{r[build.Dof(3, 2)]:e2}");
        var bc = EndForces(build, u, 102);
        TestHarness.Check("Qy в стержне BC = 0", Math.Abs(bc[1]) + Math.Abs(bc[7]) < 1e-6 * P, $"{bc[1]:e2}, {bc[7]:e2}");
    }

    /// <summary>
    /// Одноэлементная балка: заделка в A, шарнир по Mz на конце j, каток в B, равномерная q. Приведённая нагрузка даёт
    /// точные реакции балки с заделкой и шарниром: R_B = 3qL/8, M_A = qL²/8.
    /// </summary>
    private static void RunProppedCantileverLoad()
    {
        TestHarness.Section("Шарниры: погонная нагрузка у шарнирного конца");
        double l = 5.0, q = 12e3;
        var m = Line(0, l);
        WithRelease(m, 101, 0, Mz);
        m.Supports.Add(new RcSupport(1, 0x3F));
        m.Supports.Add(new RcSupport(2, 0b001111));
        var lc = new RcLoadCase(1, "q");
        lc.Beams.Add(new RcBeamLoad(101, new[] { 0.0, 0.0, -q }));
        m.LoadCases.Add(lc);
        var (build, _, r) = Solve(m);
        TestHarness.CheckRel("R_B = 3qL/8", r[build.Dof(2, 2)], 3 * q * l / 8, 1e-9);
        TestHarness.CheckRel("R_A = 5qL/8", r[build.Dof(1, 2)], 5 * q * l / 8, 1e-9);
        TestHarness.CheckRel("|M_A| = qL²/8", Math.Abs(r[build.Dof(1, 4)]), q * l * l / 8, 1e-9);
        TestHarness.Check("в отчёте закреплён UY узла 2", build.Report.Any(s => s.Contains("2 UY")), string.Join(" | ", build.Report));
    }

    /// <summary>Балка Гербера в CR (геометрическая нелинейность) при малой нагрузке совпадает с линейной.</summary>
    private static void RunGerberCorotational()
    {
        TestHarness.Section("Шарниры: CR-стержень");
        var m = Gerber();
        WithRelease(m, 102, 0, Mz);
        WithRelease(m, 103, Mz, 0);
        var build = RcStructuralMeshBuilder.Build(m, new LinearRcSectionFactory());
        // Нагрузка в 100 раз меньше: геометрическая поправка (при P — ~1e-3) уходит, расхождение — только от шарниров в CR.
        var f = build.LoadCases[1].Select(x => x / 100).ToArray();
        var uLin = build.Mesh.SolveLinear(f, build.Bc);
        var (uCr, hist) = build.Mesh.SolveNonlinear(f, build.Bc, nSteps: 2, tol: 1e-7, maxIter: 30);
        TestHarness.Check("CR сошёлся", hist.AllConverged(), $"невязка {hist[^1].Residual:e2}");
        double scale = uLin.Max(Math.Abs), d = 0;
        for (int i = 0; i < uLin.Length; i++) d = Math.Max(d, Math.Abs(uLin[i] - uCr[i]));
        TestHarness.Check("CR ≈ линейный (малая нагрузка)", d < 1e-4 * scale, $"max|Δu|/max|u| = {d / scale:e2}");
        var rCr = build.Mesh.ComputeReactions(uCr, build.Bc, build.Mesh.AssembleFInternal(uCr), f);
        TestHarness.CheckRel("CR: R_D = P/2", rCr[build.Dof(5, 2)], P / 200, 1e-4);
    }
}
