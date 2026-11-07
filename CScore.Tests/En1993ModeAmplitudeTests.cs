using System.Text.Json;
using CScore.En1993;
using CScore.Fem;
using CScore.Fem.Imperfections;
using CScore.Sp16;
using Xunit;

namespace CScore.Tests;

/// <summary>
/// Подсрез 5б несовершенств: амплитуда формы потери устойчивости по EN 1993-1-1 п. 5.3.2(11), ф. (5.9)–(5.11) —
/// χ (6.49), e₀ (5.10), смысл (5.10) (N = χ·N_Rk и e₀ дают N/N_Rk + M/M_Rk = 1), резолвер: α_ult,k по N задачи,
/// критическое сечение, ошибки и предупреждения.
/// </summary>
public class En1993ModeAmplitudeTests
{
    const double S355 = 355_000;
    const double E = 2.06e11;
    const double L = 6.0;

    [Fact]
    public void Chi_649()
    {
        var b = En1993Imperfections.Chi(1.0, En1993Curve.b);
        Assert.Equal(1.136, b.Phi, 12);
        Assert.Equal(1 / (1.136 + Math.Sqrt(1.136 * 1.136 - 1)), b.Chi, 12);
        Assert.Equal(0.5970, b.Chi, 4);
        Assert.Equal(1.0, En1993Imperfections.Chi(0.2, En1993Curve.d).Chi, 12);
        Assert.Equal(1.0, En1993Imperfections.Chi(0.1, En1993Curve.a).Chi, 12);
        Assert.Contains("6.49", b.Describe());
        Assert.Throws<ArgumentOutOfRangeException>(() => En1993Imperfections.Chi(-0.1, En1993Curve.a));
    }

    [Fact]
    public void E0_510()
    {
        double mRk = 300e3, nRk = 2000e3;
        var e = En1993Imperfections.ModeE0(En1993Curve.c, 1.2, mRk, nRk);
        Assert.Equal(0.49 * (1.2 - 0.2) * mRk / nRk, e.E0, 12);
        Assert.False(e.Small);
        var g = En1993Imperfections.ModeE0(En1993Curve.c, 1.2, mRk, nRk, 1.1);
        double l2 = g.Chi * 1.44;
        Assert.Equal(0.49 * 1.0 * mRk / nRk * (1 - l2 / 1.1) / (1 - l2), g.E0, 12);
        Assert.Contains("γ_M1 = " + 1.1.ToString(System.Globalization.CultureInfo.CurrentCulture), g.Describe());
        var small = En1993Imperfections.ModeE0(En1993Curve.c, 0.2, mRk, nRk);
        Assert.True(small.Small);
        Assert.Equal(0, small.E0);
        Assert.Contains("≤ 0,2", small.Describe());
        Assert.Throws<ArgumentOutOfRangeException>(() => En1993Imperfections.ModeE0(En1993Curve.c, 1, mRk, nRk, 0));
    }

    /// <summary>Смысл (5.10): шарнирная колонна, N = χ·N_Rk, погибь e₀, второй порядок — сечение исчерпано ровно.</summary>
    [Theory]
    [InlineData(En1993Curve.a), InlineData(En1993Curve.b), InlineData(En1993Curve.c), InlineData(En1993Curve.d)]
    public void E0_510_ExhaustsSectionAtChiNRk(En1993Curve curve)
    {
        double nRk = 3000e3, mRk = 400e3;
        foreach (double lambda in new[] { 0.5, 1.0, 1.5, 2.0 })
        {
            double nCr = nRk / (lambda * lambda);
            var e = En1993Imperfections.ModeE0(curve, lambda, mRk, nRk);
            double n = e.Chi * nRk;
            double m = n * e.E0 / (1 - n / nCr);
            Assert.Equal(1.0, n / nRk + m / mRk, 9);
        }
    }

    [Fact]
    public void ModeAmplitude_59_PinnedSineEqualsE0()
    {
        double ei = E * 1e-4, nCr = Math.PI * Math.PI * ei / (L * L);
        Assert.Equal(0.012, En1993Imperfections.ModeAmplitude(0.012, nCr, -ei * Math.PI * Math.PI / (L * L)), 12);
        Assert.Throws<ArgumentOutOfRangeException>(() => En1993Imperfections.ModeAmplitude(0.01, nCr, 0));
    }

    // ---------------------------------------------------------------- резолвер

    // Сварной двутавр 500×250×20×10 мм (оси контура: x — сильная), R_yn = 355 МПа.
    static CrossSection WeldedI()
    {
        const double h = 0.5, b = 0.25, tf = 0.02, tw = 0.01;
        double[] xs = [-b / 2, b / 2, b / 2, tw / 2, tw / 2, b / 2, b / 2, -b / 2, -b / 2, -tw / 2, -tw / 2, -b / 2, -b / 2];
        double[] ys = [-h / 2, -h / 2, -h / 2 + tf, -h / 2 + tf, h / 2 - tf, h / 2 - tf, h / 2, h / 2, h / 2 - tf, h / 2 - tf, -h / 2 + tf, -h / 2 + tf, -h / 2];
        MaterialChars Chars(CalcType calc, double ry) => new(calc) { Type = MatType.Steel, Ry = ry, Ru = 1.4 * ry, E = 206_000_000, Fc = -ry, Ft = ry };
        var steel = new Material
        {
            Tag = "S355", Type = MatType.Steel,
            MaterialChars = [Chars(CalcType.C, 0.95 * S355), Chars(CalcType.CL, 0.95 * S355), Chars(CalcType.N, S355), Chars(CalcType.NL, S355)],
        };
        return new CrossSection { Tag = "I500", Areas = [new MaterialArea { Tag = "I", Category = AreaCategory.Region, Material = steel, Hull = new Contour(xs, ys, "outer") }] };
    }

    static CrossSection Concrete() => new()
    {
        Tag = "300×300",
        Areas = [new MaterialArea { Tag = "бетон", Category = AreaCategory.Region, Material = new Material { Tag = "B25", Type = MatType.Concrete },
            Hull = new Contour([-0.15, 0.15, 0.15, -0.15, -0.15], [-0.15, -0.15, 0.15, 0.15, -0.15], "hull") }],
    };

    static readonly PolygonSection Poly = Polygon(WeldedI());

    static PolygonSection Polygon(CrossSection cs) =>
        new(cs.Areas[0].Hull!.Points.Select(p => (p.X, p.Y)), []);

    static double EI => E * Poly.Ix;
    static double NRk => Poly.A * S355 * 1000;
    static double MRkX => Poly.Ix / Math.Max(Poly.YMax - Poly.Yc, Poly.Yc - Poly.YMin) * S355 * 1000;
    static double NcrPinned => Math.PI * Math.PI * EI / (L * L);

    static readonly List<FemMeshNode> Nodes =
    [
        new() { NodeTag = "1", Z = 0 }, new() { NodeTag = "2", Z = 3 }, new() { NodeTag = "3", Z = 6 },
    ];

    static FemElement Beam(int tag, int i, int j) => new()
    {
        ElemTag = tag.ToString(), ElemType = "beam", NodeIdsJson = JsonSerializer.Serialize(new[] { i, j }), SourceMemberTag = "C",
    };

    static readonly List<FemElement> Elements = [Beam(10, 1, 2), Beam(11, 2, 3)];

    // Полусинус по высоте: КЭ 10 — x = 0; 1,5; 3, КЭ 11 — x = 3; 4,5; 6; моменты EI·η″ = EI·π²/L²·sin (прогиб по местной y → Mz).
    static double[][] Moments(double x0, double scale) =>
        [.. new[] { x0, x0 + 1.5, x0 + 3 }.Select(x => new[] { 0.0, -scale * EI * Math.PI * Math.PI / (L * L) * Math.Sin(Math.PI * x / L) })];

    static FemModesLookup Found(FemModesParams p, double n10 = -1000e3, double n11 = -1000e3, double m11Scale = 1, bool axial = true,
        double? alphaCr = null) => new()
    {
        AnalysisTag = "Длины", CaseTag = "C1", Lambda = [alphaCr ?? NcrPinned / 1000e3, 2 * (alphaCr ?? NcrPinned / 1000e3)],
        Axial = axial ? new Dictionary<int, double> { [10] = n10, [11] = n11 } : null,
        Shapes = new[] { 1, 2 }.ToDictionary(k => k, _ => new FemModeShape(
            new Dictionary<int, double[]> { [1] = [0, 0, 0, 0, 0, 0], [2] = [1, 0, 0, 0, 0, 0], [3] = [0, 0, 0, 0, 0, 0] },
            new Dictionary<int, double[][]> { [10] = [[Math.Sin(Math.PI / 4), 0, 0]], [11] = [[Math.Sin(3 * Math.PI / 4), 0, 0]] })
        {
            Moments = new Dictionary<int, double[][]> { [10] = Moments(0, 1), [11] = Moments(3, m11Scale) },
        }),
    };

    static FemImperfection Item(FemModesParams p) => new() { Id = 5, Kind = FemImperfectionKinds.Modes, Tag = "Ф", ParamsJson = p.ToJson() };

    static FemModesParams Params(int mode = 1, bool minus = false, string? critical = null, string? curve = null, double? gamma = null) => new()
    {
        AnalysisId = 7, CriticalElement = critical, Curve = curve, GammaM1 = gamma,
        Modes = [new FemModeAmplitude { Mode = mode, Amplitude = FemModeAmplitude.AmplitudeEn1993, Minus = minus }],
    };

    static FemImperfectionResolution Resolve(FemModesParams p, Func<FemModesParams, FemModesLookup> lookup, double factor = 1,
        Func<string, CrossSection?>? cross = null)
    {
        var section = WeldedI();
        return FemImperfectionResolver.Resolve([(Item(p), factor)], Elements, Nodes, [],
            new FemImperfectionContext { BeamCross = cross ?? (_ => section), Modes = lookup });
    }

    static double ExpectedE0(En1993Curve curve = En1993Curve.b, double gamma = 1) =>
        En1993Imperfections.ModeE0(curve, Math.Sqrt(NRk / NcrPinned), MRkX, NRk, gamma).E0;

    [Fact]
    public void Params_RoundTrip()
    {
        var back = FemModesParams.Parse(Params(2, minus: true, critical: "11", curve: "c", gamma: 1.1).ToJson());
        var m = Assert.Single(back.Modes);
        Assert.True(m.IsEn1993);
        Assert.True(m.Minus);
        Assert.Equal(("11", "c", 1.1), (back.CriticalElement, back.Curve, back.GammaM1));
        // Старые записи: амплитуда — мм.
        var old = FemModesParams.Parse("""{"AnalysisId":1,"Modes":[{"Mode":1,"AmplitudeMm":5}]}""");
        Assert.Equal(FemModeAmplitude.AmplitudeManual, old.Modes[0].Amplitude);
        Assert.Null(old.GammaM1);
    }

    [Fact]
    public void Resolve_PinnedColumn_AmplitudeEqualsE0()
    {
        var r = Resolve(Params(), p => Found(p));
        Assert.False(r.HasErrors, string.Join("; ", r.Diagnostics.Select(d => d.Message)));
        var m = Assert.IsType<FemResolvedModes>(Assert.Single(r.Items)).Modes.Single();
        double e0 = ExpectedE0();
        Assert.True(e0 > 0.005 && e0 < 0.05, $"e₀ = {e0}");
        Assert.Equal(e0, m.AmplitudeM, 9);
        Assert.Contains("(5.9)", m.Source);
        Assert.Contains("КЭ 10", m.Source);
        Assert.Contains("табл. 6.2", m.Source);
        // λ̄ не зависит от N: √(N_Rk/N_cr) — вторая форма (5.9) совпадает с первой.
        var r2 = Resolve(Params(), p => Found(p, -500e3, -500e3, alphaCr: NcrPinned / 500e3));
        Assert.Equal(e0, ((FemResolvedModes)r2.Items.Single()).Modes[0].AmplitudeM, 9);
    }

    [Fact]
    public void Resolve_SignFactorCurveGamma()
    {
        var r = Resolve(Params(minus: true, curve: "d", gamma: 1.1), p => Found(p), factor: -0.5);
        Assert.False(r.HasErrors, string.Join("; ", r.Diagnostics.Select(d => d.Message)));
        var a = ((FemResolvedModes)r.Items.Single()).Modes[0];
        Assert.Equal(0.5 * ExpectedE0(En1993Curve.d, 1.1), a.AmplitudeM, 9);
        Assert.Contains("задана вручную", a.Source);
        var resolved = (FemResolvedModes)r.Items.Single();
        Assert.Equal(0.5 * ExpectedE0(En1993Curve.d, 1.1), resolved.Nodes[2][0], 9);
    }

    [Fact]
    public void Resolve_CriticalSection()
    {
        // КЭ 11 сжат сильнее → α_ult,k у него; момент формы там мал → предупреждение и большая амплитуда.
        var r = Resolve(Params(), p => Found(p, -1000e3, -1500e3, m11Scale: 0.05));
        Assert.False(r.HasErrors, string.Join("; ", r.Diagnostics.Select(d => d.Message)));
        Assert.Contains(r.Diagnostics, d => d.Code == "imperfection_modes_curvature" && !d.IsError);
        var a = ((FemResolvedModes)r.Items.Single()).Modes[0];
        Assert.Contains("КЭ 11", a.Source);
        // Вручную — КЭ 10: N_cr = α_cr·1000 кН, момент полный.
        var manual = Resolve(Params(critical: "10"), p => Found(p, -1000e3, -1500e3, m11Scale: 0.05));
        Assert.DoesNotContain(manual.Diagnostics, d => d.Code == "imperfection_modes_curvature");
        double alphaUlt = NRk / 1500e3, alphaCr = NcrPinned / 1000e3;
        double e0 = En1993Imperfections.ModeE0(En1993Curve.b, Math.Sqrt(alphaUlt / alphaCr), MRkX, NRk).E0;
        Assert.Equal(e0 * alphaCr * 1000e3 / (EI * Math.PI * Math.PI / (L * L)), ((FemResolvedModes)manual.Items.Single()).Modes[0].AmplitudeM, 9);
        Assert.Contains("(вручную)", ((FemResolvedModes)manual.Items.Single()).Modes[0].Source);
        Assert.Contains(Resolve(Params(critical: "99"), p => Found(p)).Diagnostics, d => d.Code == "imperfection_modes_critical" && d.IsError);
    }

    [Fact]
    public void Resolve_HigherModeUsesItsLambda()
    {
        var r = Resolve(Params(mode: 2), p => Found(p));
        var a = ((FemResolvedModes)r.Items.Single()).Modes[0];
        Assert.Contains("определяющая форма", a.Source);
        double alphaCr = 2 * NcrPinned / 1000e3;
        double e0 = En1993Imperfections.ModeE0(En1993Curve.b, Math.Sqrt(NRk / 1000e3 / alphaCr), MRkX, NRk).E0;
        Assert.Equal(e0 * alphaCr * 1000e3 / (EI * Math.PI * Math.PI / (L * L)), a.AmplitudeM, 9);
    }

    [Fact]
    public void Resolve_ErrorsAndWarnings()
    {
        Assert.Contains(Resolve(Params(), p => Found(p, axial: false)).Diagnostics, d => d.Code == "imperfection_modes_moments" && d.IsError);
        Assert.Contains(Resolve(Params(gamma: 0), p => Found(p)).Diagnostics, d => d.Code == "imperfection_modes_gamma");
        Assert.Contains(Resolve(Params(curve: "x"), p => Found(p)).Diagnostics, d => d.Code == "imperfection_modes_curve");
        var rc = Resolve(Params(), p => Found(p), cross: _ => Concrete());
        Assert.Contains(rc.Diagnostics, d => d.Code == "imperfection_modes_no_steel" && d.IsError);
        Assert.Contains(rc.Diagnostics, d => d.Code == "imperfection_modes_not_steel" && !d.IsError);
        var steel = WeldedI();
        var mixed = Resolve(Params(), p => Found(p), cross: t => t == "10" ? Concrete() : steel);
        Assert.False(mixed.HasErrors);
        Assert.Contains(mixed.Diagnostics, d => d.Code == "imperfection_modes_not_steel");
        // Растянутые не входят: оба растянуты — нет сжатых стальных.
        Assert.Contains(Resolve(Params(), p => Found(p, 100e3, 100e3)).Diagnostics, d => d.Code == "imperfection_modes_no_steel");
        // λ̄ ≤ 0,2: α_cr в 30 раз больше α_ult,k — амплитуда 0.
        var small = Resolve(Params(), p => Found(p, alphaCr: 30 * NRk / 1000e3));
        Assert.False(small.HasErrors);
        Assert.Contains(small.Diagnostics, d => d.Code == "imperfection_modes_lambda_small");
        Assert.Equal(0, ((FemResolvedModes)small.Items.Single()).Modes[0].AmplitudeM);
    }

    [Fact]
    public void Resolve_ManualAndFormulaTogether_Report()
    {
        var p = new FemModesParams
        {
            AnalysisId = 7,
            Modes = [new() { Mode = 1, Amplitude = FemModeAmplitude.AmplitudeEn1993 }, new() { Mode = 2, AmplitudeMm = -4 }],
        };
        var r = Resolve(p, q => Found(q));
        Assert.False(r.HasErrors, string.Join("; ", r.Diagnostics.Select(d => d.Message)));
        var modes = (FemResolvedModes)r.Items.Single();
        Assert.Equal(ExpectedE0(), modes.Modes[0].AmplitudeM, 9);
        Assert.Equal(-0.004, modes.Modes[1].AmplitudeM, 12);
        Assert.Equal("", modes.Modes[1].Source);
        var report = new FemImperfectionReport();
        report.Modes.Add(new FemModeReportRow { Label = "Ф", Mode = 1, Amplitude = modes.Modes[0].AmplitudeM, Source = modes.Modes[0].Source });
        report.Modes.Add(new FemModeReportRow { Label = "Ф", Mode = 2, Amplitude = -0.004 });
        var lines = report.Lines().ToList();
        Assert.Contains(lines, l => l.Contains("5.3.2(11)") && l.Contains("(5.9)"));
        Assert.Contains(lines, l => l.Contains("(вручную)"));
    }
}
