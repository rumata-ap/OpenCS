using System.Text.Json;
using CScore;
using CScore.En1993;
using CScore.Fem;
using CScore.Fem.Imperfections;
using CScore.Sp16;
using CSfea.CScoreBridge.Structural;
using CSfea.Core;

namespace CSfea.Tests;

/// <summary>
/// Амплитуда несовершенства по форме потери устойчивости EN 1993-1-1 п. 5.3.2(11), ф. (5.9)–(5.11) (подсрез 5б):
/// моменты формы <see cref="RcBucklingResult.ModeMoments"/> против EI·π²/L² шарнирной колонны, продольные силы задачи
/// <see cref="RcBucklingResult.BarAxial"/>, сквозная цепочка постановка → резолвер (η_init = e₀ для полусинуса) и второй
/// порядок при N = χ·N_Rk — сечение исчерпано (N/N_Rk + M/M_el,Rk = 1, смысл (5.10)); колонна со ступенью N —
/// критическое сечение в нижней, сильнее сжатой части.
/// </summary>
[HarnessChecks]
public class ImperfectionModesAmplitudeTests
{
    private const double E = 2.06e11, L = 6.0, Ryn = 355_000;
    private static readonly double[] Dx = [1.0, 0.0, 0.0];

    // Квадратная труба 300×300×10 мм: I_x = I_y, W_x = W_y — ось изгиба формы на M_Rk не влияет.
    private const double B = 0.3, T = 0.01;
    private static readonly PolygonSection Poly = new(
        [(-B / 2, -B / 2), (B / 2, -B / 2), (B / 2, B / 2), (-B / 2, B / 2)],
        [[(-B / 2 + T, -B / 2 + T), (-B / 2 + T, B / 2 - T), (B / 2 - T, B / 2 - T), (B / 2 - T, -B / 2 + T)]]);

    private static readonly RcBeamSection Section = new("труба")
    {
        Elastic = new BeamSection(E, Poly.A, Poly.Ix, Poly.Ix, 2 * Poly.Ix),
    };

    private static double NRk => Poly.A * Ryn * 1000;
    private static double MRk => Poly.Ix / (B / 2) * Ryn * 1000;
    private static double Ncr => Math.PI * Math.PI * E * Poly.Ix / (L * L);

    [Fact]
    public static void RunAll()
    {
        TestHarness.Section("Моменты формы шарнирной колонны = EI·π²/L²·sin");
        RunModeMoments();

        TestHarness.Section("Сквозная цепочка: η_init по (5.9) = e₀ (5.10); второй порядок при N = χ·N_Rk");
        RunEndToEnd();

        TestHarness.Section("Ступень N: критическое сечение — в сильнее сжатой части");
        RunStepAxial();
    }

    private const int PinnedBase = 0b100111, PinnedTop = 0b100011, HoldY = 0b000010;

    private static RcStructuralModel Column(int n)
    {
        var m = new RcStructuralModel();
        for (int i = 0; i <= n; i++) m.Nodes.Add(new RcNode(i, 0, 0, L * i / n));
        for (int i = 0; i < n; i++) m.Beams.Add(new RcBeam(100 + i, i, i + 1, Section, Dx));
        m.Supports.Add(new RcSupport(0, PinnedBase));
        m.Supports.Add(new RcSupport(n, PinnedTop));
        for (int i = 1; i < n; i++) m.Supports.Add(new RcSupport(i, HoldY));
        return m;
    }

    private static void AddVertical(RcStructuralModel m, IReadOnlyDictionary<int, double> loads, int steps)
    {
        var lc = new RcLoadCase(1, "P");
        foreach (var (node, p) in loads) lc.Nodal.Add(new RcNodalLoad(node, [0.0, 0.0, -p, 0.0, 0.0, 0.0]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("P", [(1, 1.0)], steps));
    }

    private static RcBucklingResult Buckle(RcStructuralModel m) =>
        RcBucklingAnalysis.Run(m, [(1, 1.0)], [m.Beams.Select(b => b.Id).ToList()], new RcBucklingOptions(Modes: 3, MinSubdivisions: 8));

    private static void RunModeMoments()
    {
        const int n = 4;
        var m = Column(n);
        AddVertical(m, new Dictionary<int, double> { [n] = 1.0 }, 1);
        var r = Buckle(m);
        var moments = r.ModeMoments(r.Modes[0], r.Lambda[0], m.Nodes.Select(x => x.Id));
        var shape = r.ModeShape(r.Modes[0], m.Nodes.Select(x => x.Id));
        TestHarness.Check("моменты — на все стержни, n + 2 значения", m.Beams.All(b => moments[b.Id].Length == shape.BarPoints[b.Id].Length + 2));
        // Середина — узел J стержня 101; изгиб в плоскости XZ, наибольшая компонента.
        double mid = moments[101][^1].Select(Math.Abs).Max();
        TestHarness.CheckRel("|M| середины = EI·π²/L² (форма = 1 в середине)", mid, E * Poly.Ix * Math.PI * Math.PI / (L * L), 0.01);
        double quarter = moments[100][^1].Select(Math.Abs).Max();
        TestHarness.CheckRel("|M| четверти = EI·π²/L²·sin(π/4)", quarter, E * Poly.Ix * Math.PI * Math.PI / (L * L) * Math.Sin(Math.PI / 4), 0.01);
        TestHarness.CheckLess("|M| у шарнира ≈ 0", moments[100][0].Select(Math.Abs).Max() / mid, 0.01);
        var axial = r.BarAxial();
        TestHarness.Check("N задачи — у всех стержней −1 Н", m.Beams.All(b => Math.Abs(axial[b.Id] + 1) < 1e-6));
    }

    // Сечение CScore для резолвера: та же труба, сталь R_yn = 355 МПа.
    private static CrossSection Cross()
    {
        MaterialChars Chars(CalcType calc, double ry) => new(calc) { Type = MatType.Steel, Ry = ry, Ru = 1.4 * ry, E = 206_000_000, Fc = -ry, Ft = ry };
        var steel = new Material
        {
            Tag = "S355", Type = MatType.Steel,
            MaterialChars = [Chars(CalcType.C, 0.95 * Ryn), Chars(CalcType.CL, 0.95 * Ryn), Chars(CalcType.N, Ryn), Chars(CalcType.NL, Ryn)],
        };
        var area = new MaterialArea
        {
            Tag = "труба", Category = AreaCategory.Region, Material = steel,
            Hull = new Contour([-B / 2, B / 2, B / 2, -B / 2, -B / 2], [-B / 2, -B / 2, B / 2, B / 2, -B / 2], "outer"),
        };
        area.Contours.Add(new Contour([-B / 2 + T, -B / 2 + T, B / 2 - T, B / 2 - T, -B / 2 + T], [-B / 2 + T, B / 2 - T, B / 2 - T, -B / 2 + T, -B / 2 + T], "hole")
        {
            Type = ContourType.Hole,
        });
        return new CrossSection { Tag = "□300×10", Areas = [area] };
    }

    /// <summary>Амплитуда формы 1 через резолвер CScore: элементы сетки — стержни модели (теги = номера).</summary>
    private static (FemImperfectionResolution R, RcBucklingModeShape Shape) Resolve(RcStructuralModel m, RcBucklingResult r, string curve = "c")
    {
        var ids = m.Nodes.Select(x => x.Id).ToList();
        var shape = r.ModeShape(r.Modes[0], ids);
        var moments = r.ModeMoments(r.Modes[0], r.Lambda[0], ids);
        var lookup = new FemModesLookup
        {
            AnalysisTag = "Длины", CaseTag = "P", Lambda = r.Lambda, Axial = r.BarAxial(),
            Shapes = new Dictionary<int, FemModeShape>
            {
                [1] = new(shape.Nodes, shape.BarPoints) { Moments = moments },
            },
        };
        var elements = m.Beams.Select(b => new FemElement
        {
            ElemTag = b.Id.ToString(), ElemType = "beam", NodeIdsJson = JsonSerializer.Serialize(new[] { b.NodeI, b.NodeJ }),
        }).ToList();
        var nodes = m.Nodes.Select(x => new FemMeshNode { NodeTag = x.Id.ToString(), X = x.X, Y = x.Y, Z = x.Z }).ToList();
        var p = new FemModesParams { AnalysisId = 1, Curve = curve, Modes = [new() { Mode = 1, Amplitude = FemModeAmplitude.AmplitudeEn1993 }] };
        var item = new FemImperfection { Id = 1, Kind = FemImperfectionKinds.Modes, Tag = "Ф", ParamsJson = p.ToJson() };
        var cross = Cross();
        var res = FemImperfectionResolver.Resolve([(item, 1.0)], elements, nodes, [],
            new FemImperfectionContext { BeamCross = _ => cross, Modes = _ => lookup });
        return (res, shape);
    }

    private static void RunEndToEnd()
    {
        const int n = 4;
        var mb = Column(n);
        AddVertical(mb, new Dictionary<int, double> { [n] = 1000e3 }, 1);
        var r = Buckle(mb);
        var (res, shape) = Resolve(mb, r);
        TestHarness.Check("резолвер без ошибок", !res.HasErrors, string.Join("; ", res.Diagnostics.Select(d => d.Message)));
        if (res.HasErrors) return;
        var mode = ((FemResolvedModes)res.Items.Single()).Modes[0];
        double lambdaBar = Math.Sqrt(NRk / Ncr);
        var e0 = En1993Imperfections.ModeE0(En1993Curve.c, lambdaBar, MRk, NRk);
        TestHarness.Section($"справочно: λ̄ = {lambdaBar:0.###}, χ = {e0.Chi:0.###}, e₀ = {e0.E0 * 1000:0.##} мм, η_init = {mode.AmplitudeM * 1000:0.##} мм");
        TestHarness.CheckRel("η_init (5.9) = e₀ (5.10) для полусинуса", mode.AmplitudeM, e0.E0, 0.01);

        // Второй порядок при N = χ·N_Rk (упругий): N/N_Rk + M_середины/M_el,Rk = 1 (смысл (5.10)).
        double p = e0.Chi * NRk;
        var m = Column(n);
        AddVertical(m, new Dictionary<int, double> { [n] = p }, 5);
        double phiMid = shape.Nodes[n / 2][0];
        m.Imperfections.Add(RcBucklingModeShape.Sum([(shape, mode.AmplitudeM * Math.Sign(phiMid))]));
        var run = RcSecantAnalysis.Run(m, new RcSecantOptions
        {
            PhysicallyLinear = true,
            Solver = new SecantPicardOptions { Geometric = true, GeometricSecondOrder = true, TolDisplacement = 1e-10 },
        });
        TestHarness.Check("второй порядок сошёлся", run.Result.Completed, run.Result.Message ?? "");
        if (!run.Result.Completed) return;
        var f = run.LastConvergedFields();
        double mMax = Enumerable.Range(0, n).SelectMany(e => new[] { 4, 5, 10, 11 }
            .Select(c => Math.Abs(f.BeamForces[e * RcSecantStepFields.BeamForceComponents + c]))).Max();
        // Справочно для сайта: полусинус — собственная форма, точный ответ y = e₀/(1 − N/N_cr), M = N·y.
        double yMid = mode.AmplitudeM + Math.Abs(f.Displacements[RcSecantStepFields.NodeComponents * Array.IndexOf(f.NodeIds, n / 2)]);
        double yExact = e0.E0 / (1 - p / Ncr);
        TestHarness.Section(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"tube 300x10: A={Poly.A * 1e4:0.00} cm2, I={Poly.Ix * 1e8:0.0} cm4, NRk={NRk / 1e3:0.0} kN, MRk={MRk / 1e3:0.000} kN*m, " +
            $"Ncr={Ncr / 1e3:0.0} kN, N=chi*NRk={p / 1e3:0.0} kN, y: OpenCS={yMid * 1e3:0.000} exact={yExact * 1e3:0.000} mm, " +
            $"M: OpenCS={mMax / 1e3:0.000} exact={p * yExact / 1e3:0.000} kN*m, ratio={p / NRk + mMax / MRk:0.0000}"));
        TestHarness.CheckRel("N/N_Rk + M/M_el,Rk = 1 при N = χ·N_Rk", p / NRk + mMax / MRk, 1.0, 0.02);
    }

    private static void RunStepAxial()
    {
        const int n = 4;
        var mb = Column(n);
        AddVertical(mb, new Dictionary<int, double> { [n] = 500e3, [n / 2] = 500e3 }, 1);
        var r = Buckle(mb);
        var axial = r.BarAxial();
        TestHarness.Check("N: низ −1000 кН, верх −500 кН", Math.Abs(axial[100] + 1000e3) < 1 && Math.Abs(axial[103] + 500e3) < 1);
        var (res, _) = Resolve(mb, r);
        TestHarness.Check("резолвер без ошибок", !res.HasErrors, string.Join("; ", res.Diagnostics.Select(d => d.Message)));
        if (res.HasErrors) return;
        var source = ((FemResolvedModes)res.Items.Single()).Modes[0].Source;
        TestHarness.Check("критическое сечение — нижний КЭ (100 или 101)",
            source.Contains("критическое сечение — КЭ 100") || source.Contains("критическое сечение — КЭ 101"), source);
        TestHarness.Check("момент в критической точке не мал", res.Diagnostics.All(d => d.Code != "imperfection_modes_curvature"));
    }
}
