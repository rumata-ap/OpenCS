using CSfea.Core;
using CSfea.CScoreBridge.Structural;

namespace CSfea.Tests;

/// <summary>
/// Силы концов стержней в полях шага при коротационной геометрической нелинейности — в осях деформированного КЭ
/// (<see cref="BeamCorotational.Beam3dLocalEndForces"/>): консоль с большим прогибом под концевой силой и под пролётной
/// нагрузкой, линейный предел.
/// </summary>
[HarnessChecks]
public class RcBeamDeformedAxesTests
{
    private const double E = 30e9, L = 3.0;
    private const int N = 16;

    private static readonly RcBeamSection Rect = new("rect 0.3×0.5")
    {
        Elastic = new BeamSection(E, 0.15, 0.3 * 0.125 / 12, 0.5 * 0.027 / 12, 2.8e-3),
    };

    private static readonly double Ei = E * Rect.Elastic!.Iy;

    [Fact]
    public static void RunAll()
    {
        TestHarness.Section("Концы CR-стержней: консоль, концевая сила, прогиб ~10 % L");
        RunTipForce();
        TestHarness.Section("Концы CR-стержней: консоль, пролётная нагрузка");
        RunSpanLoad();
        TestHarness.Section("Концы CR-стержней: линейный предел");
        RunLinearLimit();
    }

    private static RcStructuralModel Cantilever(double pz, double qz)
    {
        var m = new RcStructuralModel();
        for (int i = 0; i <= N; i++) m.Nodes.Add(new RcNode(i, L * i / N, 0, 0));
        for (int i = 0; i < N; i++) m.Beams.Add(new RcBeam(10 + i, i, i + 1, Rect, [0.0, 1.0, 0.0]));
        m.Supports.Add(new RcSupport(0, 0b111111));
        var lc = new RcLoadCase(1, "P");
        if (pz != 0) lc.Nodal.Add(new RcNodalLoad(N, [0, 0, pz, 0, 0, 0]));
        if (qz != 0) foreach (var b in m.Beams) lc.Beams.Add(new RcBeamLoad(b.Id, [0, 0, qz]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("P", [(1, 1.0)], 4));
        return m;
    }

    private static (RcSecantStepFields Fields, double[] U, RcStructuralMeshBuild Build) RunCr(RcStructuralModel m)
    {
        var run = RcSecantAnalysis.Run(m, new RcSecantOptions
        {
            PhysicallyLinear = true,
            Solver = new SecantPicardOptions { Geometric = true, GeometricSecondOrder = false },
        });
        TestHarness.Check("CR: расчёт сошёлся", run.Result.Completed);
        return (run.LastConvergedFields()!, run.Result.Steps[^1].U, run.Build);
    }

    private static double[] Beam(RcSecantStepFields f, int e) =>
        f.BeamForces.AsSpan(e * RcSecantStepFields.BeamForceComponents, RcSecantStepFields.BeamForceComponents).ToArray();

    // Оси деформированного КЭ консоли в плоскости xz: e1 — хорда, e2 = y, e3 = e1 × e2.
    private static (double[] E1, double[] E3) Axes(double[] u, RcStructuralMeshBuild b, int e)
    {
        double dx = L / N + u[b.Dof(e + 1, 0)] - u[b.Dof(e, 0)], dz = u[b.Dof(e + 1, 2)] - u[b.Dof(e, 2)];
        double l = Math.Sqrt(dx * dx + dz * dz);
        return ([dx / l, 0, dz / l], [-dz / l, 0, dx / l]);
    }

    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

    /// <summary>
    /// Pz на конце, прогиб ~10 % L. На конце j последнего КЭ на него действует сама нагрузка: N = e1·P, Qz = e3·P (в
    /// недеформированных осях N был бы 0), My ≈ 0. В среднем узле силы соседних КЭ, переведённые в глобальные оси,
    /// уравновешены.
    /// </summary>
    private static void RunTipForce()
    {
        double p = -0.1 * 3 * Ei / (L * L);
        var (f, u, b) = RunCr(Cantilever(p, 0));
        var last = Beam(f, N - 1);
        var (e1, e3) = Axes(u, b, N - 1);
        double[] pv = [0, 0, p];
        TestHarness.Check("конец: хорда последнего КЭ заметно повёрнута (|e1·P| > 10 % P)", Math.Abs(Dot(e1, pv)) > 0.1 * Math.Abs(p),
            $"e1·P/P = {Dot(e1, pv) / p:0.###}");
        TestHarness.CheckRel("конец j: N = e1·P", last[6], Dot(e1, pv), 1e-3);
        TestHarness.CheckRel("конец j: Qz = e3·P", last[8], Dot(e3, pv), 1e-3);
        TestHarness.Check("конец j: |My| ≪ P·L", Math.Abs(last[10]) < 1e-3 * Math.Abs(p) * L, $"{last[10]:e2}");
        TestHarness.Check("конец j: Qy, Mx, Mz ≈ 0", Math.Abs(last[7]) + Math.Abs(last[9]) + Math.Abs(last[11]) < 1e-6 * Math.Abs(p));
        // Средний узел: конец j КЭ 7 и конец i КЭ 8 — в глобальных осях (сила = N·e1 + Qz·e3).
        int a = N / 2 - 1;
        var (a1, a3) = Axes(u, b, a);
        var (b1, b3) = Axes(u, b, a + 1);
        var fa = Beam(f, a);
        var fb = Beam(f, a + 1);
        double gx = fa[6] * a1[0] + fa[8] * a3[0] + fb[0] * b1[0] + fb[2] * b3[0];
        double gz = fa[6] * a1[2] + fa[8] * a3[2] + fb[0] * b1[2] + fb[2] * b3[2];
        TestHarness.Check("средний узел: силы концов уравновешены", Math.Sqrt(gx * gx + gz * gz) < 1e-3 * Math.Abs(p),
            $"{gx:e2}, {gz:e2}");
        TestHarness.CheckRel("средний узел: My концов", fa[10], -fb[4], 1e-3);
        // Заделка: момент концов i первого КЭ = момент нагрузки по деформированному плечу.
        double arm = L + u[b.Dof(N, 0)];
        TestHarness.CheckRel("заделка: |My,i| = |P|·(L + ux)", Math.Abs(Beam(f, 0)[4]), Math.Abs(p) * arm, 1e-3);
    }

    /// <summary>
    /// Равномерная qz по всем КЭ, большой прогиб: каждый КЭ уравновешен в своих деформированных осях — сумма сил концов и
    /// нагрузки КЭ q·l (переведённой в оси КЭ) ≈ 0; на свободном конце N, Qz, My ≈ 0.
    /// </summary>
    private static void RunSpanLoad()
    {
        double q = -0.1 * 8 * Ei / (L * L * L);
        var (f, u, b) = RunCr(Cantilever(0, q));
        double le = L / N;
        double worst = 0;
        for (int e = 0; e < N; e++)
        {
            var fe = Beam(f, e);
            var (e1, e3) = Axes(u, b, e);
            double[] w = [0, 0, q * le];
            double rn = fe[0] + fe[6] + Dot(e1, w), rq = fe[2] + fe[8] + Dot(e3, w);
            worst = Math.Max(worst, Math.Sqrt(rn * rn + rq * rq));
        }
        TestHarness.Check("КЭ уравновешены с нагрузкой в своих осях", worst < 1e-3 * Math.Abs(q * le), $"{worst:e2} при q·l = {q * le:e2}");
        var tip = Beam(f, N - 1);
        TestHarness.Check("свободный конец: N, Qz, My ≈ 0",
            Math.Abs(tip[6]) + Math.Abs(tip[8]) < 1e-3 * Math.Abs(q * L) && Math.Abs(tip[10]) < 1e-3 * Math.Abs(q * L * L),
            $"N {tip[6]:e2}, Qz {tip[8]:e2}, My {tip[10]:e2}");
        double tipX = L + u[b.Dof(N, 0)];
        TestHarness.Check("прогиб большой: конец сместился к заделке > 0,1 %", tipX < 0.999 * L, $"{tipX:0.####}");
    }

    /// <summary>
    /// Малая нагрузка (концевая и пролётная, поворот конца ~1,5e-4 рад): силы концов CR совпадают с линейным расчётом с
    /// точностью до геометрического эффекта (порядка поворота).
    /// </summary>
    private static void RunLinearLimit()
    {
        double p = -1e-4 * 3 * Ei / (L * L), q = -1e-4 * 8 * Ei / (L * L * L);
        var m = Cantilever(p, q);
        var (cr, _, _) = RunCr(m);
        var lin = RcLinearAnalysis.Run(Cantilever(p, q)).Cases[0].Fields;
        double scale = lin.BeamForces.Max(Math.Abs);
        double diff = cr.BeamForces.Zip(lin.BeamForces, (x, y) => Math.Abs(x - y)).Max();
        TestHarness.Check("CR ≈ линейный по всем концам", diff < 1e-3 * scale, $"{diff / scale:e2}");
    }
}
