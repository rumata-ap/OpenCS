using CSfea.Core;
using CSfea.Sparse;

namespace CSfea.Tests;

/// <summary>Жёсткие тела (MPC «ведущий — ведомый») в <see cref="StructuralMesh"/>.</summary>
public static class RigidLinksTests
{
    private const double E = 30e9;

    public static void RunAll()
    {
        RunRigidArmVsStiffBeam();
        RunTipBodyKinematics();
        RunPlateOnLinkedColumns();
        RunValidation();
    }

    private static readonly BeamSection Col = new(E, 0.16, 2.13e-3, 2.13e-3, 3.6e-3);

    /// <summary>Г-образная стойка: жёсткая консоль из двух КЭ ↔ та же консоль жёсткими связями.</summary>
    private static void RunRigidArmVsStiffBeam()
    {
        TestHarness.Section("Жёсткие связи: консоль-рычаг = очень жёсткий стержень");
        // 0 — заделка, 1 — верх стойки, 2 — середина рычага, 3 — конец рычага (вынос по x и y).
        var nodes = new[]
        {
            new[] { 0.0, 0.0, 0.0 }, new[] { 0.0, 0.0, 3.0 }, new[] { 0.6, 0.4, 3.0 }, new[] { 1.2, 0.8, 3.0 },
        };
        var colResp = new LinearBeamResponse(Col);
        var stiff = new LinearBeamResponse(new BeamSection(E * 1e7, 0.16, 2.13e-3, 2.13e-3, 3.6e-3));
        var f = new double[24];
        f[6 * 3 + 2] = -5e4; f[6 * 3 + 0] = 1e4; f[6 * 3 + 4] = 2e3;
        var refY = new[] { 1.0, 0.0, 0.0 };
        var refZ = new[] { 0.0, 0.0, 1.0 };

        var meshStiff = new StructuralMesh(nodes, null, new[]
        {
            new StructuralBeam(0, 1, colResp, refY), new StructuralBeam(1, 2, stiff, refZ), new StructuralBeam(2, 3, stiff, refZ),
        });
        var meshLink = new StructuralMesh(nodes, null, new[] { new StructuralBeam(0, 1, colResp, refY) },
            new[] { new RigidLink(1, 2), new RigidLink(1, 3) });

        var uS = meshStiff.SolveLinear(f, new BoundaryConditions(meshStiff).Fix(new[] { 0 }));
        var bcL = new BoundaryConditions(meshLink).Fix(new[] { 0 });
        var uL = meshLink.SolveLinear(f, bcL);
        double scale = uS.Max(Math.Abs);
        double d = 0.0;
        for (int i = 0; i < 24; i++) d = Math.Max(d, Math.Abs(uS[i] - uL[i]));
        TestHarness.Check("перемещения всех узлов совпадают", d / scale < 1e-5, $"max|Δu|/max|u|={d / scale:e2}");

        var r = meshLink.ComputeReactions(uL, bcL);
        TestHarness.CheckRel("Rz заделки = −Fz", r[2], 5e4, 1e-9);
        // Момент заделки вокруг y: от Fz на плече x = 1,2 и Fx на плече z = 3, плюс приложенный My.
        double myExpected = -(1.2 * 5e4 + 3.0 * 1e4 + 2e3);
        TestHarness.CheckRel("My заделки (силы через связь)", r[4], myExpected, 1e-9);

        var (uN, h) = meshLink.SolveNonlinear(f, bcL, nSteps: 2, tol: 1e-9, maxIter: 20);
        double dn = 0.0;
        for (int i = 0; i < 24; i++) dn = Math.Max(dn, Math.Abs(uN[i] - uL[i]));
        TestHarness.Check("Ньютон со связями сошёлся и близок к линейному (малые повороты)",
            h.AllConverged() && dn / scale < 2e-2, $"max|Δu|/max|u|={dn / scale:e2}");
    }

    /// <summary>Консоль с жёстким телом на конце: перенос перемещений поворотом и эквивалентность нагрузки.</summary>
    private static void RunTipBodyKinematics()
    {
        TestHarness.Section("Жёсткие связи: тело на конце консоли");
        int n = 6;
        double l = 3.0;
        var nodes = Enumerable.Range(0, n + 1).Select(i => new[] { i * l / n, 0.0, 0.0 }).ToList();
        int tip = n;
        nodes.Add(new[] { l, 0.5, -0.3 });   // ведомый (Beam)
        nodes.Add(new[] { l, -0.4, 0.2 });   // ведомый (Bar)
        int sBeam = n + 1, sBar = n + 2;
        var resp = new LinearBeamResponse(Col);
        var beams = Enumerable.Range(0, n).Select(i => new StructuralBeam(i, i + 1, resp)).ToArray();
        var links = new[] { new RigidLink(tip, sBeam), new RigidLink(tip, sBar, RigidLink.Translations) };
        var mesh = new StructuralMesh(nodes.ToArray(), null, beams, links);
        // Повороты Bar-ведомого ничем не удерживаются — закрепляем их.
        var bc = new BoundaryConditions(mesh).Fix(new[] { 0 }).Fix(new[] { sBar }, new[] { 3, 4, 5 });

        var f = new double[mesh.NDof];
        f[6 * sBeam + 1] = 2e4; f[6 * sBeam + 2] = -3e4; f[6 * sBar + 0] = 1e4;
        var u = mesh.SolveLinear(f, bc);

        double maxErr = 0.0;
        foreach (int s in new[] { sBeam, sBar })
        {
            var r = Dense.SubV(nodes[s], nodes[tip]);
            var th = new[] { u[6 * tip + 3], u[6 * tip + 4], u[6 * tip + 5] };
            var cross = Dense.Cross(th, r);
            for (int c = 0; c < 3; c++)
                maxErr = Math.Max(maxErr, Math.Abs(u[6 * s + c] - (u[6 * tip + c] + cross[c])));
        }
        for (int c = 3; c < 6; c++) maxErr = Math.Max(maxErr, Math.Abs(u[6 * sBeam + c] - u[6 * tip + c]));
        TestHarness.Check("u_s = u_m + θ_m × r, θ_s = θ_m (маска All)", maxErr < 1e-14, $"max ошибка={maxErr:e2}");

        // Та же нагрузка, перенесённая на ведущий узел (силы + моменты r × F), без связей.
        var nodesPlain = nodes.Take(n + 1).ToArray();
        var plain = new StructuralMesh(nodesPlain, null, beams);
        var fp = new double[plain.NDof];
        foreach (int s in new[] { sBeam, sBar })
        {
            var fs = new[] { f[6 * s], f[6 * s + 1], f[6 * s + 2] };
            var m = Dense.Cross(Dense.SubV(nodes[s], nodes[tip]), fs);
            for (int c = 0; c < 3; c++) { fp[6 * tip + c] += fs[c]; fp[6 * tip + 3 + c] += m[c]; }
        }
        var up = plain.SolveLinear(fp, new BoundaryConditions(plain).Fix(new[] { 0 }));
        double d = 0.0, sc = up.Max(Math.Abs);
        for (int i = 0; i < up.Length; i++) d = Math.Max(d, Math.Abs(up[i] - u[i]));
        TestHarness.Check("нагрузка на ведомом = сила + момент на ведущем", d / sc < 1e-12, $"{d / sc:e2}");
    }

    /// <summary>Плита на стойках, примкнутых через жёсткие тела (как КЭ 100 SCAD у колонн): равновесие.</summary>
    private static void RunPlateOnLinkedColumns()
    {
        TestHarness.Section("Жёсткие связи: плита на стойках через жёсткие тела — сумма реакций");
        int nn = 6;
        double l = 6.0, h = 0.2, hc = 3.0, q = 10e3;
        var (plateNodes, elements, ni) = PlateBuilder.Build(nn, l);
        var nodes = plateNodes.Select(p => new[] { p[0], p[1], hc }).ToList();
        var corners = new[] { ni(1, 1), ni(nn - 1, 1), ni(nn - 1, nn - 1), ni(1, nn - 1) };
        var beams = new List<StructuralBeam>();
        var links = new List<RigidLink>();
        var bases = new List<int>();
        var colResp = new LinearBeamResponse(Col);
        foreach (int c in corners)
        {
            int top = nodes.Count;
            nodes.Add(new[] { nodes[c][0], nodes[c][1], hc - 0.1 });   // верх стойки ниже срединной плоскости
            int bottom = nodes.Count;
            nodes.Add(new[] { nodes[c][0], nodes[c][1], 0.0 });
            bases.Add(bottom);
            beams.Add(new StructuralBeam(bottom, top, colResp, new[] { 1.0, 0.0, 0.0 }));
            links.Add(new RigidLink(top, c));   // ведущий — верх стойки, ведомый — узел плиты
            // Соседние узлы плиты — в то же тело (опорная зона колонны).
            links.Add(new RigidLink(top, c + 1));
        }
        var plate = new LinearLaminateResponse(PlateBuilder.Plate(E, 0.2, h));
        var mesh = new StructuralMesh(nodes.ToArray(), elements.Select(el => new StructuralShell(el, plate)).ToArray(),
                                      beams, links);
        var bc = new BoundaryConditions(mesh).Fix(bases);
        var f = new double[mesh.NDof];
        double ae = (l / nn) * (l / nn);
        foreach (var el in elements)
            foreach (int node in el) f[6 * node + 2] -= q * ae / 4.0;
        var u = mesh.SolveLinear(f, bc);
        var r = mesh.ComputeReactions(u, bc);
        TestHarness.CheckRel("ΣRz = q·A", bases.Sum(b => r[6 * b + 2]), q * l * l, 1e-9);
        double mx = bases.Sum(b => r[6 * b + 3] + nodes[b][1] * r[6 * b + 2]);
        double mxLoad = q * l * l * (l / 2);
        TestHarness.CheckRel("ΣMx реакций относительно оси x = момент нагрузки", mx, mxLoad, 1e-9);
    }

    private static void RunValidation()
    {
        TestHarness.Section("Жёсткие связи: запреты");
        var nodes = new[] { new[] { 0.0, 0.0, 0.0 }, new[] { 1.0, 0.0, 0.0 }, new[] { 2.0, 0.0, 0.0 } };
        TestHarness.Check("цепочка ведомый → ведущий — исключение",
            Throws(() => new RigidLinks(nodes, new[] { new RigidLink(0, 1), new RigidLink(1, 2) })));
        TestHarness.Check("двойное подчинение узла — исключение",
            Throws(() => new RigidLinks(nodes, new[] { new RigidLink(0, 2), new RigidLink(1, 2) })));
        var links = new RigidLinks(nodes, new[] { new RigidLink(0, 2, RigidLink.Translations) });
        TestHarness.Check("Bar: повороты ведомого остаются свободными DOF",
            !links.IsSlave(6 * 2 + 3) && links.IsSlave(6 * 2 + 2) && links.NReduced == 18 - 3);
        TestHarness.Check("закрепление ведомого DOF — исключение",
            Throws(() => links.ReduceFixedDofs(new[] { 6 * 2 + 1 })));

        // Произвольная маска: uz, θx, θy (связь «из плоскости»); r = (2, 0, 0) от узла 0 к узлу 2.
        var partial = new RigidLinks(nodes, new[] { new RigidLink(0, 2, 0b011100) });
        var rowUz = partial.Row(6 * 2 + 2).ToDictionary(t => t.Col, t => t.Coef);
        TestHarness.Check("маска 0x1C: ux, uy, θz ведомого свободны, uz/θx/θy подчинены",
            !partial.IsSlave(12) && !partial.IsSlave(13) && !partial.IsSlave(17)
            && partial.IsSlave(14) && partial.IsSlave(15) && partial.IsSlave(16));
        TestHarness.Check("маска 0x1C: u_s,z = u_m,z − θ_m,y·rx",
            rowUz.Count == 3 && rowUz[partial.ToReducedIndex(2)] == 1.0 && rowUz[partial.ToReducedIndex(4)] == -2.0
            && rowUz[partial.ToReducedIndex(3)] == 0.0);
        TestHarness.Check("пустая маска — исключение",
            Throws(() => new RigidLinks(nodes, new[] { new RigidLink(0, 2, 0) })));
    }

    private static bool Throws(Action a)
    {
        try { a(); return false; }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
    }
}
