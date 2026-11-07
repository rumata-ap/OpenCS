using System.Globalization;
using CSfea.CScoreBridge.Structural;
using CSfea.Core;

namespace CSfea.Tests;

/// <summary>
/// Характерные примеры для сайта документации (несовершенства EN 1993-1-1 п. 5.3): стальная квадратная труба
/// 200×200×8 мм (острые углы: A = 61,44 см², I = 3781,6 см⁴), E = 210 ГПа, 8 КЭ, упругий секущий расчёт второго порядка.
/// (1) Вертикальная консоль H = 4 м с креном φ = 1/200 ((5.5): h = 4 м → α_h = 1, m = 1 → α_m = 1) — смещение верха и
/// момент заделки против точного решения δ = φ·tan(kH)/k, M = P·δ. (2) Шарнирно опёртый стержень L = 6 м с выгибом-
/// параболой e₀ = L/300 = 20 мм (табл. 5.1, кривая a, упругий расчёт) — прогиб и момент середины против точного решения
/// y = 8e₀/(kL)²·(sec(kL/2) − 1), M = P·y. Нагрузки P = 0,25; 0,5; 0,75 от P_cr; в журнал — числа для статей.
/// </summary>
[HarnessChecks]
public class ImperfectionArticleExamplesTests
{
    private const double E = 210e9, B = 0.2, T = 0.008;
    private static readonly double A = B * B - Math.Pow(B - 2 * T, 2);
    private static readonly double I = (Math.Pow(B, 4) - Math.Pow(B - 2 * T, 4)) / 12, Ei = E * I;
    private static readonly double[] Dx = [1.0, 0.0, 0.0];
    private static readonly RcBeamSection Tube = new("□200×8") { Elastic = new BeamSection(E, A, I, I, 2 * I) };

    private const int Fixed = 0x3F, PinnedBase = 0b100111, PinnedTop = 0b100011;
    private const int N = 8;

    [Fact]
    public static void RunAll()
    {
        TestHarness.Section(string.Create(CultureInfo.InvariantCulture, $"tube 200x8: A = {A * 1e4:0.00} cm2, I = {I * 1e8:0.0} cm4, EI = {Ei / 1e3:0.0} kN*m2"));
        TestHarness.Section("Консоль H = 4 м, крен φ = 1/200");
        foreach (double a in new[] { 0.25, 0.5, 0.75 }) RunCantilever(a);
        TestHarness.Section("Шарнирный стержень L = 6 м, выгиб e₀ = L/300");
        foreach (double a in new[] { 0.25, 0.5, 0.75 }) RunPinned(a);
    }

    private static RcStructuralModel Column(double length, int baseMask, int topMask, double p)
    {
        var m = new RcStructuralModel();
        for (int i = 0; i <= N; i++) m.Nodes.Add(new RcNode(i, 0, 0, length * i / N));
        for (int i = 0; i < N; i++) m.Beams.Add(new RcBeam(100 + i, i, i + 1, Tube, Dx));
        m.Supports.Add(new RcSupport(0, baseMask));
        if (topMask != 0) m.Supports.Add(new RcSupport(N, topMask));
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(N, [0.0, 0.0, -p, 0.0, 0.0, 0.0]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("P", [(1, 1.0)], 5));
        return m;
    }

    private static RcSecantStepFields? Solve(string name, RcStructuralModel m)
    {
        var run = RcSecantAnalysis.Run(m, new RcSecantOptions
        {
            PhysicallyLinear = true,
            Solver = new SecantPicardOptions { Geometric = true, GeometricSecondOrder = true, TolDisplacement = 1e-10 },
        });
        TestHarness.Check($"{name}: расчёт сошёлся", run.Result.Completed, run.Result.Message ?? "");
        return run.Result.Completed ? run.LastConvergedFields() : null;
    }

    private static double Ux(RcSecantStepFields f, int node)
        => f.Displacements[RcSecantStepFields.NodeComponents * Array.IndexOf(f.NodeIds, node)];

    private static double Moment(RcSecantStepFields f, int beamIndex, int end)
    {
        int o = beamIndex * RcSecantStepFields.BeamForceComponents + 6 * end;
        return Math.Sqrt(f.BeamForces[o + 4] * f.BeamForces[o + 4] + f.BeamForces[o + 5] * f.BeamForces[o + 5]);
    }

    private static void RunCantilever(double alpha)
    {
        const double h = 4.0, phi = 1.0 / 200;
        double pcr = Math.PI * Math.PI * Ei / (4 * h * h), p = alpha * pcr, k = Math.Sqrt(p / Ei);
        var m = Column(h, Fixed, 0, p);
        m.Imperfections.Add(new RcSway(phi, Dx));
        var f = Solve($"консоль α = {alpha}", m);
        if (f == null) return;
        double deltaExact = phi * Math.Tan(k * h) / k;             // полное смещение верха (крен + изгиб)
        double delta = phi * h + Ux(f, N);
        int s0 = Array.IndexOf(f.ReactionNodeIds, 0);
        double mBase = Math.Abs(f.Reactions[6 * s0 + 4]);
        double mElem = Moment(f, 0, 0);
        double approx = phi * h / (1 - alpha);                     // множитель 1/(1 − P/P_cr)
        TestHarness.Section(string.Create(CultureInfo.InvariantCulture,
            $"cantilever a={alpha}: Pcr={pcr / 1e3:0.0} kN, P={p / 1e3:0.0} kN, phi*H={phi * h * 1e3:0.00} mm, " +
            $"delta: OpenCS={delta * 1e3:0.000} exact={deltaExact * 1e3:0.000} mm ({(delta / deltaExact - 1) * 100:+0.000;-0.000}%), " +
            $"approx phiH/(1-a)={approx * 1e3:0.000} mm ({(approx / deltaExact - 1) * 100:+0.00;-0.00}%), " +
            $"Mbase: reaction={mBase / 1e3:0.000} element={mElem / 1e3:0.000} exact={p * deltaExact / 1e3:0.000} kN*m, " +
            $"1st order phi*P*H={phi * p * h / 1e3:0.000} kN*m"));
        TestHarness.CheckRel($"консоль α = {alpha}: смещение верха = φ·tan(kH)/k", delta, deltaExact, 0.005);
        TestHarness.CheckRel($"консоль α = {alpha}: момент заделки (реакция) = P·δ", mBase, p * deltaExact, 0.005);
    }

    private static void RunPinned(double alpha)
    {
        const double l = 6.0, e0 = l / 300;
        double pcr = Math.PI * Math.PI * Ei / (l * l), p = alpha * pcr, kl = Math.Sqrt(p / Ei) * l;
        var m = Column(l, PinnedBase, PinnedTop, p);
        m.Imperfections.Add(new RcBow(m.Beams.Select(b => b.Id).ToList(), e0, Dx));
        var f = Solve($"шарнирный α = {alpha}", m);
        if (f == null) return;
        double yExact = 8 * e0 / (kl * kl) * (1 / Math.Cos(kl / 2) - 1);   // полный прогиб середины (выгиб + изгиб)
        double y = e0 + Ux(f, N / 2);
        double mMid = Moment(f, N / 2, 0);
        double approx = e0 / (1 - alpha);
        TestHarness.Section(string.Create(CultureInfo.InvariantCulture,
            $"pinned a={alpha}: Pcr={pcr / 1e3:0.0} kN, P={p / 1e3:0.0} kN, e0={e0 * 1e3:0.0} mm, " +
            $"y: OpenCS={y * 1e3:0.000} exact={yExact * 1e3:0.000} mm ({(y / yExact - 1) * 100:+0.000;-0.000}%), " +
            $"approx e0/(1-a)={approx * 1e3:0.000} mm ({(approx / yExact - 1) * 100:+0.00;-0.00}%), " +
            $"Mmid: OpenCS={mMid / 1e3:0.000} exact={p * yExact / 1e3:0.000} kN*m ({(mMid / (p * yExact) - 1) * 100:+0.00;-0.00}%), " +
            $"1st order P*e0={p * e0 / 1e3:0.000} kN*m"));
        TestHarness.CheckRel($"шарнирный α = {alpha}: прогиб середины = 8e₀/(kL)²·(sec(kL/2) − 1)", y, yExact, 0.005);
        TestHarness.CheckRel($"шарнирный α = {alpha}: момент середины = P·y", mMid, p * yExact, 0.01);
    }
}
