using CSfea.Core;
using CSfea.Sparse;

namespace CSfea.Tests;

/// <summary>Заданные перемещения по правым частям в <see cref="StructuralMesh"/> (многоправый линейный расчёт).</summary>
[HarnessChecks]
public class PrescribedDisplacementTests
{
    private const double E = 30e9;
    private const double I = 2.13e-3;
    private static readonly BeamSection Sec = new(E, 0.16, I, I, 3.6e-3);
    private static readonly double[] RefY = { 0.0, 1.0, 0.0 };

    [Fact]
    public static void RunAll()
    {
        RunClampedBeamSettlement();
        RunTwoRightHandSides();
        RunRigidBodyMasterSettlement();
        RunValidation();
    }

    /// <summary>Балка вдоль X из <paramref name="n"/> КЭ длиной <paramref name="l"/>; узлы 0…n.</summary>
    private static StructuralMesh Beam(double l, int n)
    {
        var nodes = Enumerable.Range(0, n + 1).Select(i => new[] { l * i / n, 0.0, 0.0 }).ToArray();
        var resp = new LinearBeamResponse(Sec);
        return new StructuralMesh(nodes, null,
            Enumerable.Range(0, n).Select(i => new StructuralBeam(i, i + 1, resp, RefY)).ToArray());
    }

    /// <summary>Значения на FixedDofs по карте «полный DOF → значение».</summary>
    private static double[] OnFixed(BoundaryConditions bc, Dictionary<int, double> values)
        => bc.FixedDofs.Select(d => values.TryGetValue(d, out var v) ? v : 0.0).ToArray();

    /// <summary>
    /// Заделка/заделка, осадка δ правого конца: реакции ∓12EIδ/L³, моменты в обеих заделках 6EIδ/L²
    /// (одного знака: M₀ + M₁ = L·R_z1 из равновесия моментов вокруг y, M₀ = M₁ по антисимметрии).
    /// </summary>
    private static void RunClampedBeamSettlement()
    {
        TestHarness.Section("Заданные перемещения: осадка заделки балки");
        double l = 6.0, delta = -0.01;
        var mesh = Beam(l, 2);
        var bc = new BoundaryConditions(mesh).Fix(new[] { 0, 2 });
        var pre = OnFixed(bc, new() { [6 * 2 + 2] = delta });
        var u = mesh.SolveLinear(new[] { new double[mesh.NDof] }, bc, null, new[] { pre })[0];

        TestHarness.CheckRel("uz правого конца = δ", u[6 * 2 + 2], delta, 1e-12);
        TestHarness.CheckRel("uz середины = δ/2 (кубическая линия перегиба)", u[6 * 1 + 2], delta / 2, 1e-9);
        var r = mesh.ComputeReactions(u, bc);
        double rz = 12 * E * I * delta / (l * l * l);
        double my = 6 * E * I * delta / (l * l);
        TestHarness.CheckRel("R_z правой опоры = 12EIδ/L³", r[6 * 2 + 2], rz, 1e-9);
        TestHarness.CheckRel("R_z левой опоры = −12EIδ/L³", r[2], -rz, 1e-9);
        TestHarness.CheckRel("M_y левой заделки = 6EIδ/L²", r[4], my, 1e-9);
        TestHarness.CheckRel("M_y правой заделки = 6EIδ/L²", r[6 * 2 + 4], my, 1e-9);
    }

    /// <summary>Нагрузка и осадка одной факторизацией = два отдельных одноправых расчёта с UFixed.</summary>
    private static void RunTwoRightHandSides()
    {
        TestHarness.Section("Заданные перемещения: две правые части одной факторизацией");
        var mesh = Beam(6.0, 4);
        var bc = new BoundaryConditions(mesh).Fix(new[] { 0 }).Fix(new[] { 4 }, new[] { 0, 1, 2 });
        var fLoad = new double[mesh.NDof];
        fLoad[6 * 2 + 2] = -5e4; fLoad[6 * 1 + 1] = 2e4; fLoad[6 * 3 + 3] = 1e3;
        var settle = new Dictionary<int, double> { [6 * 4 + 2] = -0.008, [6 * 0 + 4] = 0.002 };
        var fSettle = new double[mesh.NDof];
        fSettle[6 * 2 + 2] = 1e4;
        var res = mesh.SolveLinear(new[] { fLoad, fSettle }, bc, null, new[] { null, OnFixed(bc, settle) });

        var u0 = mesh.SolveLinear(fLoad, bc);
        var bc1 = new BoundaryConditions(mesh).Fix(new[] { 0 }).Fix(new[] { 4 }, new[] { 0, 1, 2 });
        foreach (var (d, v) in settle) bc1.Prescribe(d / 6, d % 6, v);
        var u1 = mesh.SolveLinear(fSettle, bc1);

        TestHarness.Check("правая часть без значений = SolveLinear(f, bc)", MaxRelDiff(u0, res[0]) < 1e-12,
            $"max|Δu|/max|u|={MaxRelDiff(u0, res[0]):e2}");
        TestHarness.Check("правая часть с осадкой = SolveLinear(f, bc с UFixed)", MaxRelDiff(u1, res[1]) < 1e-10,
            $"max|Δu|/max|u|={MaxRelDiff(u1, res[1]):e2}");
        TestHarness.CheckRel("на закреплённом DOF — заданное значение", res[1][6 * 0 + 4], 0.002, 1e-15);
    }

    /// <summary>
    /// Ведущий узел жёсткого тела закреплён с осадкой δ и поворотом θ_y; ведомый на выносе a по X получает
    /// u_z = δ − θ·a (θ × r, r = (a, 0, 0)) и тот же поворот. Сверка с той же схемой без связи, где эти значения заданы
    /// прямо в узле стержня.
    /// </summary>
    private static void RunRigidBodyMasterSettlement()
    {
        TestHarness.Section("Заданные перемещения: осадка ведущего узла жёсткого тела");
        double l = 4.0, a = 0.5, delta = -0.006, theta = 0.001;
        // 0 — заделка, 1 — ведущий (закреплён с заданными значениями), 2 — ведомый, 3 — середина, 4 — заделка.
        var nodes = new[]
        {
            new[] { 0.0, 0.0, 0.0 }, new[] { l, 0.0, 0.0 }, new[] { l + a, 0.0, 0.0 },
            new[] { l + a + l / 2, 0.0, 0.0 }, new[] { l + a + l, 0.0, 0.0 },
        };
        var resp = new LinearBeamResponse(Sec);
        var beams = new[]
        {
            new StructuralBeam(0, 1, resp, RefY), new StructuralBeam(2, 3, resp, RefY), new StructuralBeam(3, 4, resp, RefY),
        };
        var mesh = new StructuralMesh(nodes, null, beams, new[] { new RigidLink(1, 2) });
        var bc = new BoundaryConditions(mesh).Fix(new[] { 0, 1, 4 });
        var pre = OnFixed(bc, new() { [6 * 1 + 2] = delta, [6 * 1 + 4] = theta });
        var f = new double[mesh.NDof];
        f[6 * 3 + 2] = -2e4;
        var u = mesh.SolveLinear(new[] { f }, bc, null, new[] { pre })[0];

        TestHarness.CheckRel("uz ведомого = δ − θ·a", u[6 * 2 + 2], delta - theta * a, 1e-12);
        TestHarness.CheckRel("θ_y ведомого = θ", u[6 * 2 + 4], theta, 1e-12);

        var meshRef = new StructuralMesh(nodes, null, beams);
        var bcRef = new BoundaryConditions(meshRef).Fix(new[] { 0, 1, 2, 4 })
            .Prescribe(1, 2, delta).Prescribe(1, 4, theta).Prescribe(2, 2, delta - theta * a).Prescribe(2, 4, theta);
        var uRef = meshRef.SolveLinear(f, bcRef);
        TestHarness.CheckRel("uz середины = схеме без связи", u[6 * 3 + 2], uRef[6 * 3 + 2], 1e-9);
        TestHarness.CheckRel("θ_y середины = схеме без связи", u[6 * 3 + 4], uRef[6 * 3 + 4], 1e-9);
        var r = mesh.ComputeReactions(u, bc);
        var rRef = meshRef.ComputeReactions(uRef, bcRef);
        TestHarness.CheckRel("R_z правой заделки = схеме без связи", r[6 * 4 + 2], rRef[6 * 4 + 2], 1e-9);
        TestHarness.CheckRel("M_y правой заделки = схеме без связи", r[6 * 4 + 4], rRef[6 * 4 + 4], 1e-9);
    }

    /// <summary>Ошибки: длины, число правых частей, UFixed без prescribed.</summary>
    private static void RunValidation()
    {
        TestHarness.Section("Заданные перемещения: проверки аргументов");
        var mesh = Beam(6.0, 2);
        var bc = new BoundaryConditions(mesh).Fix(new[] { 0, 2 });
        var f = new double[mesh.NDof];
        TestHarness.Check("prescribed.Count ≠ loads.Count → ArgumentException",
            Throws<ArgumentException>(() => mesh.SolveLinear(new[] { f, f }, bc, null, new double[]?[] { null })));
        TestHarness.Check("длина prescribed[i] ≠ FixedDofs.Length → ArgumentException",
            Throws<ArgumentException>(() => mesh.SolveLinear(new[] { f }, bc, null, new[] { new double[3] })));
        var bcU = new BoundaryConditions(mesh).Fix(new[] { 0 }).Prescribe(2, 2, 0.01);
        TestHarness.Check("UFixed ≠ 0 без prescribed → NotSupportedException",
            Throws<NotSupportedException>(() => mesh.SolveLinear(new[] { f }, bcU)));
    }

    private static bool Throws<T>(Action a) where T : Exception
    {
        try { a(); return false; }
        catch (T) { return true; }
    }

    private static double MaxRelDiff(double[] a, double[] b)
    {
        double scale = Math.Max(a.Max(Math.Abs), 1e-300);
        double d = 0.0;
        for (int i = 0; i < a.Length; i++) d = Math.Max(d, Math.Abs(a[i] - b[i]));
        return d / scale;
    }
}
