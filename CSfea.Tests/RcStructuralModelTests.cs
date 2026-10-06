using CSfea.CScoreBridge.Structural;
using CSfea.Core;

namespace CSfea.Tests;

/// <summary>Нейтральная модель <see cref="RcStructuralModel"/> и построитель сетки.</summary>
public static class RcStructuralModelTests
{
    private const double E = 30e9;

    public static void RunAll()
    {
        RunCantileverUniformLoad();
        RunFixedFixedEndMoment();
        RunPlatePressure();
        RunSectionAxisRotation();
        RunUnconnectedNodes();
    }

    private static readonly RcBeamSection Rect = new("rect 0.3×0.5")
    {
        Elastic = new BeamSection(E, 0.15, 0.3 * 0.125 / 12, 0.5 * 0.027 / 12, 2.8e-3),
    };

    private static RcStructuralModel Line(int n, double l, double[] dir)
    {
        var m = new RcStructuralModel();
        for (int i = 0; i <= n; i++) m.Nodes.Add(new RcNode(10 + i, dir[0] * l * i / n, dir[1] * l * i / n, dir[2] * l * i / n));
        for (int i = 0; i < n; i++) m.Beams.Add(new RcBeam(100 + i, 10 + i, 11 + i, Rect, new[] { 0.0, 0.0, 1.0 }));
        return m;
    }

    /// <summary>Консоль под равномерной нагрузкой: прогиб qL⁴/8EI, реакции qL и qL²/2.</summary>
    private static void RunCantileverUniformLoad()
    {
        TestHarness.Section("RcStructuralModel: консоль под равномерной нагрузкой");
        double l = 4.0, q = 12e3;
        // Ось стержня — диагональ в плоскости xy: проверяется поворот в локальные оси.
        var dir = new[] { Math.Sqrt(0.5), Math.Sqrt(0.5), 0.0 };
        var m = Line(4, l, dir);
        m.Supports.Add(new RcSupport(10, 0x3F));
        var lc = new RcLoadCase(1, "q");
        foreach (var b in m.Beams) lc.Beams.Add(new RcBeamLoad(b.Id, new[] { 0.0, 0.0, -q }));
        m.LoadCases.Add(lc);
        var build = RcStructuralMeshBuilder.Build(m, new LinearRcSectionFactory());
        var f = build.LoadCases[1];
        var u = build.Mesh.SolveLinear(f, build.Bc);

        // Нагрузка −z в локальных осях (refVec = z) — направление −y, изгиб на EIz.
        double ei = E * Rect.Elastic!.Iz;
        TestHarness.CheckRel("прогиб конца qL⁴/8EI", -u[build.Dof(14, 2)], q * Math.Pow(l, 4) / (8 * ei), 1e-9);
        var k = build.Mesh.AssembleK().ToCsc().Multiply(u);
        var r = new double[k.Length];
        for (int i = 0; i < r.Length; i++) r[i] = k[i] - f[i];
        TestHarness.CheckRel("Rz заделки = qL", r[build.Dof(10, 2)], q * l, 1e-9);
        // Момент заделки = r × F относительно опоры: |M| = qL²/2, вектор ⟂ оси стержня в плоскости xy.
        double mx = r[build.Dof(10, 3)], my = r[build.Dof(10, 4)];
        TestHarness.CheckRel("|M| заделки = qL²/2", Math.Sqrt(mx * mx + my * my), q * l * l / 2, 1e-9);
        TestHarness.Check("ΣF нагрузки = qL", Math.Abs(f.Where((_, i) => i % 6 == 2).Sum() + q * l) < 1e-6);
    }

    /// <summary>Защемлённая балка: момент в заделке qL²/12 из согласованных моментов.</summary>
    private static void RunFixedFixedEndMoment()
    {
        TestHarness.Section("RcStructuralModel: защемлённая балка — момент qL²/12");
        double l = 6.0, q = 10e3;
        var m = Line(3, l, new[] { 1.0, 0.0, 0.0 });
        m.Supports.Add(new RcSupport(10, 0x3F));
        m.Supports.Add(new RcSupport(13, 0x3F));
        var lc = new RcLoadCase(1, "q");
        foreach (var b in m.Beams) lc.Beams.Add(new RcBeamLoad(b.Id, new[] { 0.0, -q, 0.0 }));
        m.LoadCases.Add(lc);
        var build = RcStructuralMeshBuilder.Build(m, new LinearRcSectionFactory());
        var f = build.LoadCases[1];
        var u = build.Mesh.SolveLinear(f, build.Bc);
        var k = build.Mesh.AssembleK().ToCsc().Multiply(u);
        double mzA = k[build.Dof(10, 5)] - f[build.Dof(10, 5)];
        double mzB = k[build.Dof(13, 5)] - f[build.Dof(13, 5)];
        TestHarness.CheckRel("Mz заделки A = qL²/12", mzA, q * l * l / 12, 1e-9);
        TestHarness.CheckRel("Mz заделки B = −qL²/12", mzB, -q * l * l / 12, 1e-9);
        // refVec = z — локальная ось y вертикальна, нагрузка по глобальной y изгибает вокруг локальной y (EIy).
        double ei = E * Rect.Elastic!.Iy;
        // Середина пролёта не в узле: проверяем узел на L/3 — w = q·x²(L−x)²/(24EI).
        double x = l / 3;
        TestHarness.CheckRel("прогиб в L/3 = qx²(L−x)²/24EI", -u[build.Dof(11, 1)], q * x * x * (l - x) * (l - x) / (24 * ei), 1e-9);
    }

    /// <summary>Давление на пластину из искажённых Q4 и T3: ΣF = q·A, направление по нормали и глобальное.</summary>
    private static void RunPlatePressure()
    {
        TestHarness.Section("RcStructuralModel: давление на пластину — ΣF = q·A");
        var m = new RcStructuralModel();
        // Квадрат 3×2 м: два искажённых Q4 и два T3.
        (int, double, double)[] pts = { (1, 0, 0), (2, 1.3, 0), (3, 3, 0), (4, 0, 1.1), (5, 1.6, 0.9), (6, 3, 1.2), (7, 0, 2), (8, 1.4, 2), (9, 3, 2) };
        foreach (var (id, x, y) in pts) m.Nodes.Add(new RcNode(id, x, y, 0));
        var sec = new RcShellSection("h200") { Elastic = new Laminate(new[] { new Ply(new OrthotropicMaterial(E, E, 0.2, E / 2.4), 0, 0.2) }) };
        m.Shells.Add(new RcShell(1, new[] { 1, 2, 5, 4 }, sec));
        m.Shells.Add(new RcShell(2, new[] { 2, 3, 6, 5 }, sec));
        m.Shells.Add(new RcShell(3, new[] { 4, 5, 8 }, sec));
        m.Shells.Add(new RcShell(4, new[] { 4, 8, 7 }, sec));
        m.Shells.Add(new RcShell(5, new[] { 5, 6, 9, 8 }, sec));
        foreach (int n in new[] { 1, 3, 7, 9 }) m.Supports.Add(new RcSupport(n, 0b000111));
        double q = 5e3;
        var down = new RcLoadCase(1, "вниз");
        var normal = new RcLoadCase(2, "по нормали");
        foreach (var s in m.Shells)
        {
            down.Shells.Add(new RcShellLoad(s.Id, q, new[] { 0.0, 0.0, -1.0 }));
            normal.Shells.Add(new RcShellLoad(s.Id, q));
        }
        m.LoadCases.Add(down);
        m.LoadCases.Add(normal);
        var build = RcStructuralMeshBuilder.Build(m, new LinearRcSectionFactory());
        double area = 3 * 2;
        TestHarness.CheckRel("ΣFz (глобальное −z) = −q·A", build.LoadCases[1].Where((_, i) => i % 6 == 2).Sum(), -q * area, 1e-12);
        TestHarness.CheckRel("ΣFz (по нормали +z) = q·A", build.LoadCases[2].Where((_, i) => i % 6 == 2).Sum(), q * area, 1e-12);
        var u = build.Mesh.SolveLinear(build.LoadCases[1], build.Bc);
        var r = build.Mesh.ComputeReactions(u, build.Bc, fExternal: build.LoadCases[1]);
        // Часть нагрузки приходится прямо на опорные узлы — реакция полная только с учётом внешней нагрузки.
        TestHarness.CheckRel("ΣRz опор = q·A", new[] { 1, 3, 7, 9 }.Sum(n => r[build.Dof(n, 2)]), q * area, 1e-9);
    }

    /// <summary>Ось сечения под углом: ортотропная пластина = пластина с повёрнутыми слоями.</summary>
    private static void RunSectionAxisRotation()
    {
        TestHarness.Section("RcStructuralModel: ось сечения оболочки → поворот в оси КЭ");
        var mat = new OrthotropicMaterial(4 * E, E, 0.2, 0.4 * E);
        double axisAngle = 0.6;   // ось x сечения в глобальных осях
        var axis = new[] { Math.Cos(axisAngle), Math.Sin(axisAngle), 0.0 };
        RcStructuralModel Model(RcShellSection sec, double[]? sectionAxis)
        {
            var m = new RcStructuralModel();
            int nn = 4;
            double l = 4.0;
            for (int j = 0; j <= nn; j++)
                for (int i = 0; i <= nn; i++)
                    m.Nodes.Add(new RcNode(j * (nn + 1) + i, i * l / nn + 0.07 * j, j * l / nn, 0));
            for (int j = 0; j < nn; j++)
                for (int i = 0; i < nn; i++)
                {
                    int a = j * (nn + 1) + i;
                    m.Shells.Add(new RcShell(a, new[] { a, a + 1, a + nn + 2, a + nn + 1 }, sec, sectionAxis));
                }
            for (int i = 0; i <= nn; i++) m.Supports.Add(new RcSupport(i, 0x3F));
            var lc = new RcLoadCase(1, "q");
            foreach (var s in m.Shells) lc.Shells.Add(new RcShellLoad(s.Id, 3e3, new[] { 0.0, 0.3, -1.0 }));
            m.LoadCases.Add(lc);
            return m;
        }
        var rotatedSec = new RcShellSection("ortho") { Elastic = new Laminate(new[] { new Ply(mat, 0.0, 0.2) }) };
        // У всех КЭ ось x — горизонталь (ребро 0 → 1), значит в осях КЭ слой повёрнут ровно на axisAngle.
        var directSec = new RcShellSection("ortho-rot") { Elastic = new Laminate(new[] { new Ply(mat, axisAngle, 0.2) }) };
        var b1 = RcStructuralMeshBuilder.Build(Model(rotatedSec, axis), new LinearRcSectionFactory());
        var b2 = RcStructuralMeshBuilder.Build(Model(directSec, null), new LinearRcSectionFactory());
        var u1 = b1.Mesh.SolveLinear(b1.LoadCases[1], b1.Bc);
        var u2 = b2.Mesh.SolveLinear(b2.LoadCases[1], b2.Bc);
        double sc = u2.Max(Math.Abs), d = 0;
        for (int i = 0; i < u1.Length; i++) d = Math.Max(d, Math.Abs(u1[i] - u2[i]));
        TestHarness.Check("перемещения совпадают", d / sc < 1e-10, $"max|Δu|/max|u|={d / sc:e2}");
        TestHarness.Check("КЭ обёрнуты в RotatedShellResponse", b1.Mesh.Shells.All(s => s.Section is RotatedShellResponse r
            && Math.Abs(r.Angle - axisAngle) < 1e-12));
    }

    /// <summary>Узлы без КЭ закрепляются с отчётом; ведомый без КЭ — только свободные DOF.</summary>
    private static void RunUnconnectedNodes()
    {
        TestHarness.Section("RcStructuralModel: узлы без КЭ");
        var m = Line(2, 2.0, new[] { 1.0, 0.0, 0.0 });
        m.Nodes.Add(new RcNode(50, 5, 5, 5));          // одиночный
        m.Nodes.Add(new RcNode(51, 2, 0.5, 0));        // ведомый (только перемещения) без КЭ
        m.RigidBodies.Add(new RcRigidBody(1, 12, new[] { 51 }, RigidLink.Translations));
        m.Supports.Add(new RcSupport(10, 0x3F));
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(51, new[] { 0.0, 0.0, -1e4, 0, 0, 0 }));
        m.LoadCases.Add(lc);
        var build = RcStructuralMeshBuilder.Build(m, new LinearRcSectionFactory());
        var fixedDofs = build.Bc.FixedDofs.ToHashSet();
        TestHarness.Check("одиночный узел закреплён полностью", Enumerable.Range(0, 6).All(c => fixedDofs.Contains(build.Dof(50, c))));
        TestHarness.Check("у ведомого закреплены только повороты",
            Enumerable.Range(3, 3).All(c => fixedDofs.Contains(build.Dof(51, c))) && !fixedDofs.Contains(build.Dof(51, 2)));
        TestHarness.Check("отчёт о закреплениях", build.Report.Count == 2, string.Join(" | ", build.Report));
        var u = build.Mesh.SolveLinear(build.LoadCases[1], build.Bc);
        // Сила на ведомом с плечом 0,5 м по y закручивает консоль: θx конца ≠ 0, прогиб конца = PL³/3EI
        // (локальная ось y = глобальная z — изгиб вокруг локальной z, EIz).
        double ei = E * Rect.Elastic!.Iz;
        TestHarness.CheckRel("прогиб конца консоли PL³/3EI", -u[build.Dof(12, 2)], 1e4 * 8 / (3 * ei), 1e-9);
        TestHarness.Check("кручение от плеча силы", Math.Abs(u[build.Dof(12, 3)]) > 0);
    }
}
