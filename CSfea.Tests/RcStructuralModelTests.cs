using CSfea.CScoreBridge.Structural;
using CSfea.Core;

namespace CSfea.Tests;

/// <summary>Нейтральная модель <see cref="RcStructuralModel"/> и построитель сетки.</summary>
[HarnessChecks]
public class RcStructuralModelTests
{
    private const double E = 30e9;

    [Fact]
    public static void RunAll()
    {
        RunCantileverUniformLoad();
        RunFixedFixedEndMoment();
        RunPlatePressure();
        RunSectionAxisRotation();
        RunUnconnectedNodes();
        RunSymmetryQuarter();
        RunFoundationUniformSettlement();
        RunFoundationTiltedPlate();
        RunFoundationPointLoad();
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
    /// <summary>
    /// Четверть плиты на колоннах по двум плоскостям симметрии: перемещения узлов четверти = полной схеме. Нагрузки —
    /// давление, узловые силы во всех узлах (делятся на плоскостях) и сила в центре (делится на 4); колонны с жёсткими
    /// телами оголовков внутри четверти.
    /// </summary>
    private static void RunSymmetryQuarter()
    {
        TestHarness.Section("RcSymmetry: четверть плиты на колоннах = полная схема");
        const int n = 12;
        const double l = 6.0, h = l / n;
        var m = new RcStructuralModel();
        int Id(int i, int j) => 1000 + i * (n + 1) + j;
        for (int i = 0; i <= n; i++)
            for (int j = 0; j <= n; j++) m.Nodes.Add(new RcNode(Id(i, j), i * h, j * h, 0));
        var sec = new RcShellSection("h200") { Elastic = new Laminate(new[] { new Ply(new OrthotropicMaterial(E, E, 0.2, E / 2.4), 0, 0.2) }) };
        int sid = 1;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++) m.Shells.Add(new RcShell(sid++, new[] { Id(i, j), Id(i + 1, j), Id(i + 1, j + 1), Id(i, j + 1) }, sec));
        var col = new RcBeamSection("col 0.3") { Elastic = new BeamSection(E, 0.09, 0.3 * 0.027 / 12, 0.3 * 0.027 / 12, 1.1e-3) };
        int k = 0;
        foreach (var (ci, cj) in new[] { (2, 2), (10, 2), (2, 10), (10, 10) })
        {
            int top = 1, bot = 2;
            top += 10 * k; bot += 10 * k;
            m.Nodes.Add(new RcNode(top, ci * h, cj * h, 0));
            m.Nodes.Add(new RcNode(bot, ci * h, cj * h, -3));
            m.Beams.Add(new RcBeam(1 + k, bot, top, col, new[] { 1.0, 0, 0 }));
            m.RigidBodies.Add(new RcRigidBody(1 + k, top, new[] { Id(ci, cj), Id(ci + 1, cj), Id(ci - 1, cj), Id(ci, cj + 1), Id(ci, cj - 1) }));
            m.Supports.Add(new RcSupport(bot, 0x3F));
            k++;
        }
        var lc = new RcLoadCase(1, "q + P");
        foreach (var s in m.Shells) lc.Shells.Add(new RcShellLoad(s.Id, 8e3, new[] { 0.0, 0.0, -1.0 }));
        foreach (var nd in m.Nodes.Where(x => x.Z == 0 && x.Id >= 1000)) lc.Nodal.Add(new RcNodalLoad(nd.Id, new[] { 0.0, 0, -500, 0, 0, 0 }));
        lc.Nodal.Add(new RcNodalLoad(Id(n / 2, n / 2), new[] { 0.0, 0, -4e4, 0, 0, 0 }));
        m.LoadCases.Add(lc);

        var q = RcSymmetry.Cut(m, new[] { new RcSymmetryPlane(0, l / 2), new RcSymmetryPlane(1, l / 2) });
        TestHarness.Check("в четверти 36 оболочек и одна колонна", q.Shells.Count == 36 && q.Beams.Count == 1 && q.RigidBodies.Count == 1,
            $"{q.Shells.Count} / {q.Beams.Count} / {q.RigidBodies.Count}");
        var bf = RcStructuralMeshBuilder.Build(m, new LinearRcSectionFactory());
        var bq = RcStructuralMeshBuilder.Build(q, new LinearRcSectionFactory());
        var uf = bf.Mesh.SolveLinear(bf.LoadCases[1], bf.Bc);
        var uq = bq.Mesh.SolveLinear(bq.LoadCases[1], bq.Bc);
        double sc = uf.Max(Math.Abs), d = 0;
        foreach (var nd in q.Nodes)
            for (int c = 0; c < 6; c++) d = Math.Max(d, Math.Abs(uf[bf.Dof(nd.Id, c)] - uq[bq.Dof(nd.Id, c)]));
        TestHarness.Check("перемещения четверти = полной схеме", d / sc < 1e-9, $"max|Δu|/max|u|={d / sc:e2}");
        TestHarness.Check("маска симметрии x: ux, θy, θz", RcSymmetry.SymmetryMask(0) == 0b110001);
        TestHarness.Check("маска симметрии y: uy, θx, θz", RcSymmetry.SymmetryMask(1) == 0b101010);
    }

    private static RcShellSection Isotropic(double h, double nu = 0.2) =>
        new($"iso {h}") { Elastic = new Laminate(new[] { new Ply(new OrthotropicMaterial(E, E, nu, E / (2 * (1 + nu))), 0, h) }) };

    /// <summary>Пружины основания: K_spring·u (Н) — силы основания на узлы.</summary>
    private static double[] FoundationForces(RcStructuralMeshBuild build, double[] u)
        => build.Bc.AssembleKSpring().ToCsc().Multiply(u);

    /// <summary>
    /// Свободная плита из искажённых Q4 и T3 на основании C1 под равномерным давлением: осадка q/C1 во всех узлах,
    /// сумма сил основания = q·A (опор по нормали нет, закреплены только DOF в плоскости).
    /// </summary>
    private static void RunFoundationUniformSettlement()
    {
        TestHarness.Section("RcStructuralModel: плита на основании C1 — осадка q/C1");
        var m = new RcStructuralModel();
        (int, double, double)[] pts = { (1, 0, 0), (2, 1.3, 0), (3, 3, 0), (4, 0, 1.1), (5, 1.6, 0.9), (6, 3, 1.2), (7, 0, 2), (8, 1.4, 2), (9, 3, 2) };
        foreach (var (id, x, y) in pts) m.Nodes.Add(new RcNode(id, x, y, 0));
        var sec = Isotropic(0.2);
        double c1 = 2e7, q = 50e3;
        int[][] shells = { new[] { 1, 2, 5, 4 }, new[] { 2, 3, 6, 5 }, new[] { 4, 5, 8 }, new[] { 4, 8, 7 }, new[] { 5, 6, 9, 8 } };
        for (int e = 0; e < shells.Length; e++) m.Shells.Add(new RcShell(e + 1, shells[e], sec, FoundationC1: c1));
        foreach (var (id, _, _) in pts) m.Supports.Add(new RcSupport(id, 0b100011));
        var lc = new RcLoadCase(1, "q");
        foreach (var sh in m.Shells) lc.Shells.Add(new RcShellLoad(sh.Id, q, new[] { 0.0, 0.0, -1.0 }));
        m.LoadCases.Add(lc);
        var build = RcStructuralMeshBuilder.Build(m, new LinearRcSectionFactory());
        var u = build.Mesh.SolveLinear(build.LoadCases[1], build.Bc);
        double worst = pts.Max(p => Math.Abs(u[build.Dof(p.Item1, 2)] / (-q / c1) - 1));
        TestHarness.Check($"осадка = q/C1 во всех узлах (откл. {worst:E1})", worst < 1e-3);
        var fs = FoundationForces(build, u);
        TestHarness.CheckRel("Σ сил основания = q·A", fs.Where((_, i) => i % 6 == 2).Sum(), -q * 6.0, 1e-9);
        TestHarness.Check("в отчёте — упругое основание", build.Report.Any(r => r.Contains("C1")));
    }

    /// <summary>Плита, наклонённая на 35° вокруг x: перемещение вдоль нормали q/C1, силы основания — вдоль нормали.</summary>
    private static void RunFoundationTiltedPlate()
    {
        TestHarness.Section("RcStructuralModel: наклонная плита на основании — осадка вдоль нормали");
        double a = 35 * Math.PI / 180, c = Math.Cos(a), sn = Math.Sin(a);
        var m = new RcStructuralModel();
        int nn = 3;
        double l = 3.0;
        for (int j = 0; j <= nn; j++)
            for (int i = 0; i <= nn; i++)
            {
                double x = i * l / nn + 0.1 * j, t = j * l / nn;
                m.Nodes.Add(new RcNode(j * (nn + 1) + i, x, t * c, t * sn));
            }
        var sec = Isotropic(0.25);
        double c1 = 1e7, q = 30e3;
        for (int j = 0; j < nn; j++)
            for (int i = 0; i < nn; i++)
            {
                int n0 = j * (nn + 1) + i;
                m.Shells.Add(new RcShell(n0, new[] { n0, n0 + 1, n0 + nn + 2, n0 + nn + 1 }, sec, FoundationC1: c1));
            }
        // Равномерная осадка вдоль нормали без поворотов — точное решение; закрепления его не трогают
        // (ux и повороты всех узлов, uy одного узла — снимают движения плиты как целого в её плоскости).
        foreach (var n in m.Nodes) m.Supports.Add(new RcSupport(n.Id, 0b111001));
        m.Supports.Add(new RcSupport(0, 0b000010));
        var lc = new RcLoadCase(1, "q по нормали");
        foreach (var sh in m.Shells) lc.Shells.Add(new RcShellLoad(sh.Id, q));
        m.LoadCases.Add(lc);
        var build = RcStructuralMeshBuilder.Build(m, new LinearRcSectionFactory());
        var u = build.Mesh.SolveLinear(build.LoadCases[1], build.Bc);
        var normal = new[] { 0.0, -sn, c };
        double worst = m.Nodes.Max(n =>
        {
            double un = Enumerable.Range(0, 3).Sum(k => u[build.Dof(n.Id, k)] * normal[k]);
            return Math.Abs(un / (q / c1) - 1);
        });
        TestHarness.Check($"перемещение вдоль нормали = q/C1 (откл. {worst:E1})", worst < 1e-3);
        var fs = FoundationForces(build, u);
        double area = l * l;
        for (int k = 0; k < 3; k++)
            TestHarness.Check($"Σ сил основания по оси {k} = q·A·n", Math.Abs(fs.Where((_, i) => i % 6 == k).Sum() - q * area * normal[k]) < 1e-6 * q * area);
    }

    /// <summary>
    /// Сосредоточенная сила в центре большой плиты на основании (четверть с симметрией): w = P/(8·√(D·C1))
    /// (Тимошенко, Войновский-Кригер, §57), ±3 %.
    /// </summary>
    private static void RunFoundationPointLoad()
    {
        TestHarness.Section("RcStructuralModel: сила в центре плиты на основании — P/(8√(D·C1))");
        double h = 0.08, nu = 0.2, c1 = 1e6, p = 100e3;
        double d = E * h * h * h / (12 * (1 - nu * nu));
        double lChar = Math.Pow(d / c1, 0.25);
        int nn = 60;
        double size = 6.0;   // ≈ 5,6 характерной длины; тонкая плита — сдвиг Миндлина под силой < 1 %
        var m = new RcStructuralModel();
        for (int j = 0; j <= nn; j++)
            for (int i = 0; i <= nn; i++) m.Nodes.Add(new RcNode(j * (nn + 1) + i, i * size / nn, j * size / nn, 0));
        var sec = Isotropic(h, nu);
        for (int j = 0; j < nn; j++)
            for (int i = 0; i < nn; i++)
            {
                int n0 = j * (nn + 1) + i;
                m.Shells.Add(new RcShell(n0, new[] { n0, n0 + 1, n0 + nn + 2, n0 + nn + 1 }, sec, FoundationC1: c1));
            }
        foreach (var n in m.Nodes)
        {
            int mask = 0b100011;                   // мембрана и поворот вокруг нормали — не нужны
            if (n.X == 0) mask |= 0b010000;        // симметрия по x = 0: θy = 0
            if (n.Y == 0) mask |= 0b001000;        // симметрия по y = 0: θx = 0
            m.Supports.Add(new RcSupport(n.Id, mask));
        }
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(0, new[] { 0, 0, -p / 4, 0, 0, 0 }));
        m.LoadCases.Add(lc);
        var build = RcStructuralMeshBuilder.Build(m, new LinearRcSectionFactory());
        var u = build.Mesh.SolveLinear(build.LoadCases[1], build.Bc);
        double w = -u[build.Dof(0, 2)];
        TestHarness.CheckRel($"w центра = P/(8√(D·C1)), l = {lChar:F2} м", w, p / (8 * Math.Sqrt(d * c1)), 0.03);
        var fs = FoundationForces(build, u);
        TestHarness.CheckRel("Σ сил основания = P/4", fs.Where((_, i) => i % 6 == 2).Sum(), -p / 4, 1e-6);
    }
}
