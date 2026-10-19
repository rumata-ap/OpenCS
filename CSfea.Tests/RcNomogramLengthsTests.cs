using CSfea.CScoreBridge.Structural;
using CSfea.Core;

namespace CSfea.Tests;

/// <summary>
/// Номограммный поэлементный расчёт μ (<see cref="RcAlignmentChart"/>, <see cref="Sp16FrameTable"/>,
/// <see cref="RcNomogramLengths"/>): уравнения (2.9), (2.13) ASCE 1997 при G = 1 и в предельных случаях, формулы
/// табл. 31 СП 16, демо-рама по мотивам Бритвина (2 пролёта × 6 м, 2 этажа × 3 м; колонны — двутавр 360×320, ригели —
/// короб 300×300×15; эталон — ручной расчёт по G из погонных жёсткостей).
/// </summary>
[HarnessChecks]
public class RcNomogramLengthsTests
{
    private const double E = 2.06e11;

    // EI демо-рамы (кН·м²): колонна в плоскости 103817,373, из плоскости 29272,558; ригель 43343,178.
    private const double IcStrong = 103817.373e3 / E, IcWeak = 29272.558e3 / E, Ib = 43343.178e3 / E;

    private const int Fixed = 0b111111, Y = 0b000010;

    [Fact]
    public static void RunAll()
    {
        RunCharts();
        RunSp16Table();
        RunFrame(RcLengthMethod.Asce);
        RunFrame(RcLengthMethod.Sp16);
        RunStiffnessAndManual();
    }

    private static void RunCharts()
    {
        TestHarness.Section("Номограммы ASCE: ур. (2.9) и (2.13)");
        TestHarness.CheckRel("со смещением, G = 1/1", RcAlignmentChart.Sway(1, 1), 1.3173, 2e-4);
        TestHarness.CheckRel("без смещения, G = 1/1", RcAlignmentChart.Braced(1, 1), 0.7743, 2e-4);
        TestHarness.CheckRel("со смещением, заделка/заделка → 1", RcAlignmentChart.Sway(0, 0), 1.0, 1e-4);
        TestHarness.CheckRel("со смещением, заделка/шарнир → 2", RcAlignmentChart.Sway(0, double.PositiveInfinity), 2.0, 1e-4);
        TestHarness.Check("со смещением, шарнир/шарнир — механизм",
            double.IsPositiveInfinity(RcAlignmentChart.Sway(double.PositiveInfinity, double.PositiveInfinity)));
        TestHarness.CheckRel("без смещения, шарнир/шарнир → 1", RcAlignmentChart.Braced(double.PositiveInfinity, double.PositiveInfinity), 1.0, 1e-6);
        TestHarness.CheckRel("без смещения, заделка/шарнир → 0,699", RcAlignmentChart.Braced(0, double.PositiveInfinity), 0.6992, 5e-4);
        TestHarness.CheckRel("без смещения, заделка/заделка → 0,5", RcAlignmentChart.Braced(0, 0), 0.5, 1e-3);
        TestHarness.CheckRel("без смещения, G = 1/∞", RcAlignmentChart.Braced(1, double.PositiveInfinity), 0.8749, 5e-4);
    }

    private static void RunSp16Table()
    {
        TestHarness.Section("Табл. 31 СП 16");
        double n1 = Ib * 3 / (IcStrong * 6);
        TestHarness.CheckRel("крайняя нижнего этажа (p = ∞, n = n₁/2)", Sp16FrameTable.Mu(true, Sp16Storey.Bottom, double.PositiveInfinity, n1, 1).Mu, 1.6049, 2e-4);
        TestHarness.CheckRel("крайняя верхнего этажа (143)", Sp16FrameTable.Mu(true, Sp16Storey.Top, n1, n1, 1).Mu, 2.4829, 2e-4);
        TestHarness.CheckRel("средняя нижнего этажа (k = 2)", Sp16FrameTable.Mu(true, Sp16Storey.Bottom, double.PositiveInfinity, 2 * n1, 2).Mu, 1.4163, 2e-4);
        TestHarness.CheckRel("средняя верхнего этажа (144)", Sp16FrameTable.Mu(true, Sp16Storey.Top, 2 * n1, 2 * n1, 2).Mu, 1.7022, 2e-4);
        TestHarness.CheckRel("одноэтажная, заделка (142), n → ∞ → 1", Sp16FrameTable.Mu(true, Sp16Storey.Single, double.PositiveInfinity, 1e9, 1).Mu, 1.0, 1e-6);
        TestHarness.CheckRel("одноэтажная, шарнир (141), n → ∞ → 2", Sp16FrameTable.Mu(true, Sp16Storey.Single, 0, 1e9, 1).Mu, 2.0, 1e-6);
        TestHarness.CheckRel("несвободная, p = ∞, n = 0 → 0,707", Sp16FrameTable.Mu(false, Sp16Storey.Bottom, double.PositiveInfinity, 0, 1).Mu, Math.Sqrt(0.5), 1e-9);
        TestHarness.CheckRel("несвободная, p = n = 0 → 1", Sp16FrameTable.Mu(false, Sp16Storey.Top, 0, 0, 1).Mu, 1.0, 1e-9);
    }

    // Демо-рама: стержни 101–106 — колонны К1…К6 (по рядам x = 0, 6, 12 снизу вверх), 107–110 — ригели Р1…Р4.
    private static (RcStructuralModel Model, List<IReadOnlyList<int>> Units) Frame()
    {
        var col = new RcBeamSection("колонна") { Elastic = new BeamSection(E, 2.0e-2, IcStrong, IcWeak) };
        var beam = new RcBeamSection("ригель") { Elastic = new BeamSection(E, 1.6e-2, Ib, Ib) };
        var m = new RcStructuralModel();
        var ids = new Dictionary<(double, double), int>();
        int Node(double x, double z)
        {
            if (ids.TryGetValue((x, z), out int id)) return id;
            id = m.Nodes.Count + 1;
            m.Nodes.Add(new RcNode(id, x, 0, z));
            return ids[(x, z)] = id;
        }
        // Местная y — по Y: изгиб в плоскости рамы (вокруг Y) — EIy.
        void Member(double x0, double z0, double x1, double z1, RcBeamSection s) =>
            m.Beams.Add(new RcBeam(101 + m.Beams.Count, Node(x0, z0), Node(x1, z1), s, new[] { 0.0, 1.0, 0.0 }));
        foreach (double x in new[] { 0.0, 6, 12 })
        {
            Member(x, 0, x, 3, col);
            Member(x, 3, x, 6, col);
        }
        foreach (double z in new[] { 3.0, 6 })
        {
            Member(0, z, 6, z, beam);
            Member(6, z, 12, z, beam);
        }
        foreach (var node in m.Nodes) m.Supports.Add(new RcSupport(node.Id, node.Z == 0 ? Fixed : Y));
        var lc = new RcLoadCase(1, "Вертикальная + ветер");
        foreach (double z in new[] { 3.0, 6 })
        {
            lc.Nodal.Add(new RcNodalLoad(Node(0, z), new[] { 40e3, 0, -600e3, 0, 0, 0 }));
            lc.Nodal.Add(new RcNodalLoad(Node(6, z), new[] { 0.0, 0, -1200e3, 0, 0, 0 }));
            lc.Nodal.Add(new RcNodalLoad(Node(12, z), new[] { 0.0, 0, -600e3, 0, 0, 0 }));
        }
        m.LoadCases.Add(lc);
        return (m, m.Beams.Select(b => (IReadOnlyList<int>)[b.Id]).ToList());
    }

    private static (RcBucklingResult R, RcBucklingUnitLength?[] Form, StructuralLocalBuckling?[] Local) Solve(
        RcStructuralModel m, List<IReadOnlyList<int>> units)
    {
        var model = RcBucklingAnalysis.Prepare(m, units);
        var r = RcBucklingAnalysis.Solve(model, [(1, 1.0)], new RcBucklingOptions(Modes: 8));
        var local = RcBucklingAnalysis.SolveLocal(r, units);
        return (r, RcBucklingLengths.Assign(r, units, local: local), local);
    }

    /// <summary>
    /// Колонны в плоскости рамы (вокруг местной y) — со смещением по общей форме; G узлов этажей — из погонных
    /// жёсткостей, заделка — G = 1. Из плоскости — без смещения: ригели вокруг X не работают (кручение не учитывается),
    /// G = ∞, нижние — 0,875 (ASCE) / 0,707 (СП 16), верхние — 1. Ригели — без смещения: нижние μ ≈ 0,55, верхние ≈ 0,63 (колонна только снизу).
    /// </summary>
    private static void RunFrame(RcLengthMethod method)
    {
        TestHarness.Section($"Демо-рама Бритвина: {method}");
        var (m, units) = Frame();
        var (r, form, local) = Solve(m, units);
        var x = RcNomogramLengths.Assign(r, units, Enumerable.Range(0, units.Count), form,
            new RcNomogramOptions(method), local);
        bool sp16 = method == RcLengthMethod.Sp16;
        var expected = sp16
            ? new[] { 1.6049, 2.4829, 1.4163, 1.7022, 1.6049, 2.4829 }
            : new[] { 1.8912, 2.5123, 1.6872, 1.9131, 1.8912, 2.5123 };
        for (int i = 0; i < 6; i++)
        {
            var inPlane = x[i]!.AboutY;
            TestHarness.Check($"К{i + 1}: источник — номограмма, со смещением",
                inPlane.Source == RcBucklingLengthSource.Nomogram && inPlane.Nomogram!.Sway, $"{inPlane.Nomogram}");
            TestHarness.CheckRel($"К{i + 1}: μ в плоскости", inPlane.Mu, expected[i], 3e-3);
            TestHarness.Check($"К{i + 1}: по формам — справочно", inPlane.Form is { Source: RcBucklingLengthSource.Mode });
            var outPlane = x[i]!.AboutZ;
            bool lower = i % 2 == 0;
            double eo = lower ? (sp16 ? Math.Sqrt(0.5) : 0.8749) : 1.0;
            TestHarness.Check($"К{i + 1}: из плоскости без смещения", !outPlane.Nomogram!.Sway, $"{outPlane.Nomogram}");
            TestHarness.CheckRel($"К{i + 1}: μ из плоскости", outPlane.Mu, eo, 2e-3);
        }
        var k3 = x[2]!.AboutY.Nomogram!;
        TestHarness.CheckRel("К3: G низа — заделка по AISC", k3.A.G, 1.0, 1e-12);
        if (!sp16) TestHarness.CheckRel("К3: G верха", k3.B.G, 4.7905, 2e-3);
        else TestHarness.Check("К3: нижний этаж, k = 2", k3.Storey == Sp16Storey.Bottom && k3.Spans == 2, $"{k3.Storey}, k = {k3.Spans}");
        for (int i = 6; i < 10; i++)
        {
            var g = x[i]!.AboutY;
            TestHarness.Check($"Р{i - 5}: без смещения", !g.Nomogram!.Sway, $"{g.Nomogram}");
            TestHarness.Check($"Р{i - 5}: μ ≈ 0,55…0,63 (ASCE, у СП 16 — не колонна)", g.Mu is > 0.5 and < 0.65 && (!sp16 || g.Nomogram.Sp16NotApplicable), $"{g.Mu}");
        }
    }

    /// <summary>Правило жёсткости окружения и ручное задание дают то же смещение, что и общая форма на демо-раме.</summary>
    private static void RunStiffnessAndManual()
    {
        TestHarness.Section("Демо-рама: смещение по жёсткости и вручную");
        var (m, units) = Frame();
        var (r, form, local) = Solve(m, units);
        var columns = Enumerable.Range(0, 6).ToArray();
        var byStiffness = RcNomogramLengths.Assign(r, units, columns, form, new RcNomogramOptions(Sway: RcSwayRule.Stiffness), local);
        var byHand = RcNomogramLengths.Assign(r, units, columns, form, new RcNomogramOptions(Sway: RcSwayRule.Manual), local,
            (_, xy) => !xy);
        foreach (int i in columns)
        {
            TestHarness.Check($"К{i + 1}: по жёсткости — в плоскости со смещением, из плоскости без",
                byStiffness[i]!.AboutY.Nomogram!.Sway && !byStiffness[i]!.AboutZ.Nomogram!.Sway,
                $"{byStiffness[i]!.AboutY.Nomogram!.SwayValue}; {byStiffness[i]!.AboutZ.Nomogram!.SwayValue}");
            TestHarness.CheckRel($"К{i + 1}: вручную = по форме", byHand[i]!.AboutY.Mu, RcNomogramLengths.Assign(r, units, [i], form,
                new RcNomogramOptions(), local)[i]!.AboutY.Mu, 1e-12);
        }
    }
}
