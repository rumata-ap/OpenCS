using CScore;
using CScore.Sp63.CrackWidth;
using CScore.Sp63.Normal;
using CSfea.CScoreBridge.Structural;
using OpenCS.Services;
using Xunit;

namespace OpenCS.Tests.FemResult;

/// <summary>
/// Ширина раскрытия трещин стержней по результату CSfea (спека НДС сечений, срез 4): по записанной плоскости
/// деформаций — a = φ1·φ2·φ3·εs,m·ls у каждого растянутого стержня; запасной путь — упрощённая проверка по усилиям.
/// </summary>
public sealed class FemCsfeaBarCrackWidthsTests
{
    static MaterialChars Concrete(CalcType ct) => new()
    {
        Type = MatType.Concrete, TypeCalc = ct, Fc = -18500, Ft = 1550, E = 30_000_000,
        Ec0 = -0.002, Ec1 = -0.00029, Ec1Red = -0.0015, Ec2 = -0.0035,
        Et0 = 0.0001, Et1 = 0.000021, Et1Red = 0.00008, Et2 = 0.00015,
    };

    static MaterialChars Rebar(CalcType ct) => new()
    {
        Type = MatType.ReSteelF, TypeCalc = ct, Fc = -500000, Ft = 500000, E = 200_000_000, Ec2 = -0.0035, Et2 = 0.025,
    };

    static readonly CalcType[] Calcs = [CalcType.C, CalcType.CL, CalcType.N, CalcType.NL];

    /// <summary>Прямоугольник b × h (X — ширина, Y — высота) из B25, по стержню A500 диаметром d в каждом углу.</summary>
    static CrossSection Rect(double b, double h, double a, double d = 0.02)
    {
        var concreteMat = new Material { Id = 1, Tag = "B25", Type = MatType.Concrete, E = 30_000_000, MaterialChars = Calcs.Select(Concrete).ToList() };
        var rebarMat = new Material { Id = 2, Tag = "A500", Type = MatType.ReSteelF, E = 200_000_000, MaterialChars = Calcs.Select(Rebar).ToList() };
        var concrete = new MaterialArea
        {
            Id = 1, Tag = "бетон", Category = AreaCategory.Region, Material = concreteMat, MaterialId = 1, DiagrammType = DiagrammType.L3,
            Hull = new Contour([-b / 2, b / 2, b / 2, -b / 2, -b / 2], [-h / 2, -h / 2, h / 2, h / 2, -h / 2], "hull"),
        };
        concrete.SetWKT();
        concrete.SliceXY(nx: 10, ny: 20);
        var bars = new MaterialArea
        {
            Id = 2, Tag = "арматура", Category = AreaCategory.RebarGroup, Material = rebarMat, MaterialId = 2,
            DiagrammType = DiagrammType.L2, HostArea = concrete, HostAreaId = 1,
        };
        foreach (double x in new[] { -b / 2 + a, b / 2 - a })
            foreach (double y in new[] { -h / 2 + a, h / 2 - a })
                bars.Fibers.Add(Fiber.CreatePoint(d, x, y));
        var section = new CrossSection { Id = 1, Tag = "ЖБ", Areas = [concrete, bars] };
        section.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);
        return section;
    }

    // ------------------------------------------------------------ ls по п. 8.2.17

    /// <summary>
    /// Прямоугольник 0,2 × 0,3, a = 0,04, ⌀25: растянуты нижние стержни. Высота растянутого бетона 0,1 (между 2a = 0,08 и
    /// 0,5h = 0,15) — Abt = 0,2·0,1, ls = 0,5·Abt/As·ds без пределов; нейтральная ось у грани — высота 2a.
    /// </summary>
    [Theory]
    [InlineData(-0.05, 0.10)]   // нуль деформаций при Y = −0,05: от нижней грани 0,1
    [InlineData(-0.10, 0.08)]   // 0,05 < 2a — берётся 2a
    [InlineData(0.10, 0.15)]    // 0,25 > 0,5h — берётся 0,5h
    public void CrackSpacing_TensionZoneBounds(double yZero, double xt)
    {
        var s = Rect(0.2, 0.3, 0.04, d: 0.025);
        const double ky = -0.01;
        var k = new Kurvature { e0 = -ky * yZero, ky = ky, kz = 0 };

        double area = 2 * Math.PI * 0.0125 * 0.0125;
        double expected = Math.Clamp(0.5 * 0.2 * xt / area * 0.025, 0.25, 0.40);
        Assert.Equal(expected, FemCsfeaBarCrackWidths.CrackSpacing(s, k), 9);
    }

    // ------------------------------------------------------------ по записанной плоскости

    /// <summary>
    /// Свободно опёртая плита-балка 1 × 0,2 м, ⌀20 по углам, с силой в среднем узле, 12 КЭ, 5 шагов (секущий расчёт,
    /// вид N; при 6 КЭ и 4 шагах не сходится — к находке среза 2 о несходимости балки 0,3 × 0,5).
    /// </summary>
    static (RcStructuralModel Model, RcSecantStepFields Fields) SimpleBeam(bool psi)
    {
        const double l = 6.0, p = 20e3;
        const int n = 12;
        var rs = new RcBeamSection("ЖБ") { Cross = Rect(1.0, 0.2, 0.03), Calc = CalcType.N, TorsionGJ = 1e7 };
        var m = new RcStructuralModel();
        for (int i = 0; i <= n; i++) m.Nodes.Add(new RcNode(i, l * i / n, 0, 0));
        for (int i = 0; i < n; i++) m.Beams.Add(new RcBeam(10 + i, i, i + 1, rs, [0.0, 1.0, 0.0]));
        m.Supports.Add(new RcSupport(0, 0b001111));
        m.Supports.Add(new RcSupport(n, 0b000110));
        var lc = new RcLoadCase(1, "P");
        lc.Nodal.Add(new RcNodalLoad(n / 2, [0, 0, -p, 0, 0, 0]));
        m.LoadCases.Add(lc);
        m.Stages.Add(new RcStage("P", [(1, 1.0)], 5));
        var run = RcSecantAnalysis.Run(m, new RcSecantOptions { BeamShear = false, Psi = psi });
        Assert.True(run.Result.Completed, run.Result.Message);
        return (m, run.LastConvergedFields()!);
    }

    /// <summary>
    /// В середине пролёта (начало КЭ 16) — по записанной плоскости вручную: εs,m нижних стержней, ls по п. 8.2.17 для
    /// прямоугольника (Abt = b·xt), a_crc,1 = 1,4·0,5·εs,m·ls при всей нагрузке длительной (a_crc = a_crc,1). Деформации
    /// стержней записаны без галки деформаций сечений.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Midspan_MatchesEquation8128(bool psi)
    {
        var (m, f) = SimpleBeam(psi);
        Assert.False(f.HasStrains);
        Assert.True(f.HasBeamStrains);
        var elements = FemCsfeaBarCrackWidths.Elements(new FemRcModelResult(m, [], []));
        var widths = FemCsfeaBarCrackWidths.Compute(elements, f, 1.0, CalcType.N, psi);

        int k = Array.IndexOf(f.BeamIds, 16);
        int o = k * RcSecantStepFields.BeamStrainComponents;
        var plane = FemCsfeaBarSectionState.Plane(f.BeamStrains[o], f.BeamStrains[o + 1], f.BeamStrains[o + 2], yAlongLocalY: false);
        double sign = -Math.Sign(plane.ky);   // растянутая грань — по −ky
        double eps = plane.e0 + plane.ky * sign * -0.07;
        Assert.True(eps > 1e-4, $"εs = {eps}");
        double yZero = -plane.e0 / plane.ky;
        double xt = Math.Clamp(0.1 + sign * yZero, 0.06, 0.1);
        double ls = Math.Clamp(0.5 * 1.0 * xt / (2 * Math.PI * 1e-4) * 0.02, 0.2, 0.4);
        double expected = 1.4 * 0.5 * eps * ls * 1e3;

        var w = widths[16];
        Assert.Equal(expected, w.Long[0]!.Value, 6);
        Assert.Equal(w.Long[0]!.Value, w.Short[0]!.Value, 9);
        // У опоры (начало КЭ 10) момента нет — трещины нет.
        Assert.Equal(0.0, widths[10].Long[0]!.Value, 9);
    }

    /// <summary>
    /// Учёт ψs в расчёте даёт меньшую среднюю деформацию арматуры, чем расчёт без ψs (там ψs = 1 в запас), — и меньшую
    /// ширину раскрытия.
    /// </summary>
    [Fact]
    public void Psi_ReducesWidth()
    {
        var (m, f0) = SimpleBeam(psi: false);
        var (_, f1) = SimpleBeam(psi: true);
        var elements = FemCsfeaBarCrackWidths.Elements(new FemRcModelResult(m, [], []));
        double a0 = FemCsfeaBarCrackWidths.Compute(elements, f0, 1.0, CalcType.N, false)[16].Long[0]!.Value;
        double a1 = FemCsfeaBarCrackWidths.Compute(elements, f1, 1.0, CalcType.N, true)[16].Long[0]!.Value;
        Assert.True(a1 < a0, $"с ψs {a1}, без ψs {a0}");
    }

    /// <summary>
    /// Длительная часть: пара, совпадающая с полной, — как доля 1; доля 0,6 — досчёт по НДМ: a_crc,1 меньше, чем при доле 1,
    /// но не ноль; a_crc = a_crc,1 + a2 − a3 больше a_crc,1; доля 0 — a_crc,1 = 0.
    /// </summary>
    [Fact]
    public void LongPart_PairAndShare()
    {
        var (m, f) = SimpleBeam(psi: true);
        var elements = FemCsfeaBarCrackWidths.Elements(new FemRcModelResult(m, [], []));
        var full = FemCsfeaBarCrackWidths.Compute(elements, f, 1.0, CalcType.N, true)[16];
        var pair = FemCsfeaBarCrackWidths.Compute(elements, f, f, CalcType.N)[16];
        var part = FemCsfeaBarCrackWidths.Compute(elements, f, 0.6, CalcType.N, true)[16];
        var none = FemCsfeaBarCrackWidths.Compute(elements, f, 0.0, CalcType.N, true)[16];

        Assert.Equal(full.Long[0]!.Value, pair.Long[0]!.Value, 9);
        Assert.Equal(full.Short[0]!.Value, pair.Short[0]!.Value, 9);
        Assert.InRange(part.Long[0]!.Value, 0.05 * full.Long[0]!.Value, 0.9 * full.Long[0]!.Value);
        Assert.True(part.Short[0] > part.Long[0]);
        Assert.Equal(0.0, none.Long[0]!.Value, 12);
        Assert.Equal(full.Long[0]!.Value / 1.4, none.Short[0]!.Value, 6);
    }

    /// <summary>Записи без деформаций стержней (до 18.10.2026) — упрощённая проверка по усилиям точек.</summary>
    [Fact]
    public void WithoutBeamStrains_FallsBackToSimplified()
    {
        var (m, f) = SimpleBeam(psi: true);
        var old = new RcSecantStepFields
        {
            NodeIds = f.NodeIds, Displacements = f.Displacements, ShellIds = f.ShellIds, ShellForces = f.ShellForces,
            ShellStates = f.ShellStates, ShellFlags = f.ShellFlags, BeamIds = f.BeamIds, BeamForces = f.BeamForces, BeamFlags = f.BeamFlags,
        };
        Assert.False(old.HasBeamStrains);
        var elements = FemCsfeaBarCrackWidths.Elements(new FemRcModelResult(m, [], []));
        var widths = FemCsfeaBarCrackWidths.Compute(elements, old, 0.9, CalcType.N, true);
        int k = Array.IndexOf(f.BeamIds, 16);
        var direct = FemCsfeaBarCrackWidths.Simplified(elements[k], FemCsfeaBarSectionState.ElementForces(f, k, 0), 0.9);
        Assert.Equal(direct.Long, widths[16].Long[0]);
        Assert.Equal(direct.Short, widths[16].Short[0]);
    }

    // ------------------------------------------------------------ упрощённая проверка (запасной путь)

    static (double Long, double Short) Direct(CrossSection s, LoadItem load, Sp63NormalAxis axis, double share)
    {
        var r = Sp63CrackWidthChecker.Check(s, load, CalcType.N, new Sp63CrackWidthOptions(Sp63NormalShapeKind.Rectangular, axis,
            1.0, 0.5, 0.3, Mode: Sp63CrackWidthMode.LongAndShort, LongTermShare: share));
        Assert.Equal(Sp63CrackWidthStatus.Calculated, r.Status);
        return (r.Variables["acrc1"], r.Details[1].Applied);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Simplified_Uniaxial_MatchesBarCheck(bool yAlongLocalY)
    {
        var s = Rect(0.3, 0.5, 0.04);
        var e = new FemCsfeaBarCrackElement(1, s, yAlongLocalY);
        // Момент по высоте сечения (Y CScore): при Y по местной y — Mz КЭ (ось сечения Mx = −Mz), иначе My.
        var forces = yAlongLocalY ? (N: 30e3, My: 0.0, Mz: -150e3) : (N: 30e3, My: 150e3, Mz: 0.0);

        var (aLong, aShort) = FemCsfeaBarCrackWidths.Simplified(e, forces, 0.7);

        var expected = Direct(s, new LoadItem { N = 30, Mx = 150 }, Sp63NormalAxis.Mx, 0.7);
        Assert.True(expected.Long > 0);
        Assert.Equal(expected.Long, aLong!.Value, 9);
        Assert.Equal(expected.Short, aShort!.Value, 9);
    }

    [Fact]
    public void Simplified_Biaxial_LargestOfBendingPlanes()
    {
        var s = Rect(0.3, 0.5, 0.04);
        var e = new FemCsfeaBarCrackElement(1, s, YAlongLocalY: false);

        var (aLong, aShort) = FemCsfeaBarCrackWidths.Simplified(e, (0.0, 150e3, 60e3), 0.5);

        var x = Direct(s, new LoadItem { Mx = 150 }, Sp63NormalAxis.Mx, 0.5);
        var y = Direct(s, new LoadItem { My = 60 }, Sp63NormalAxis.My, 0.5);
        Assert.Equal(Math.Max(x.Long, y.Long), aLong!.Value, 9);
        Assert.Equal(Math.Max(x.Short, y.Short), aShort!.Value, 9);
    }

    [Fact]
    public void Simplified_LongForcesProportional_EqualShare()
    {
        var e = new FemCsfeaBarCrackElement(1, Rect(0.3, 0.5, 0.04), YAlongLocalY: false);
        var total = (N: 20e3, My: 140e3, Mz: 40e3);

        var byShare = FemCsfeaBarCrackWidths.Simplified(e, total, 0.6);
        var byPair = FemCsfeaBarCrackWidths.Simplified(e, total, 1.0, (0.6 * total.N, 0.6 * total.My, 0.6 * total.Mz));

        Assert.Equal(byShare.Long!.Value, byPair.Long!.Value, 9);
        Assert.Equal(byShare.Short!.Value, byPair.Short!.Value, 9);
    }
}
