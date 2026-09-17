using CScore;
using CScore.Sp63;
using CScore.Sp63.Normal;
using Xunit;
using Xunit.Abstractions;

namespace CScore.Tests.Sp63Normal;

/// <summary>
/// Сравнение круглого сечения из примеров IV.Г.2.1/IV.Г.2.2 Пособия Краковского к
/// СП 63.13330.2012 (расчёт по деформационной модели) с НДМ OpenCS и с формулами
/// приложения Д СП 63.13330.2018.
///
/// Исходные данные пособия: D = 400 мм; B25, γb1 = 1,0 (Rb = 14,5 МПа, Eb = 30 000 МПа);
/// A400 (Rs = Rsc = 350 МПа по редакции 2012 г., Es = 200 000 МПа); 6 стержней на окружности
/// D = 330 мм; N = 600 кН, M = 140 кН·м (полные), Nl = 400 кН, Ml = 100 кН·м; l0 = 4 м;
/// статически неопределимая конструкция; усилия по недеформированной схеме; двухлинейная
/// диаграмма бетона. Вердикт пособия: 6⌀25 — прочность обеспечена (κ = 0,01249 1/м);
/// 6⌀22 — не обеспечена.
///
/// Принятые в сравнении допущения:
/// — гибкость проверяется по п. 8.1.2 как l0/i &gt; 14 с i = D/4 для круга (порог по
///   умолчанию); l0/i = 40, поэтому η обязателен — без него НДМ «пропускает» 6⌀22;
/// — ψ = M1l/M1 = (Ml + Nl·rs)/(M + N·rs) = 0,6946 — моменты относительно крайнего стержня;
/// — ориентация шести стержней в пособии не указана — вердикт проверяется для двух
///   крайних ориентаций (стержни на оси, перпендикулярной плоскости изгиба, и на оси изгиба);
/// — приложение Д формально требует не менее 7 стержней, поэтому формулы Д.6–Д.9
///   вызываются напрямую; checker корректно возвращает неприменимость.
///
/// Фактические результаты на 17.09.2026: НДМ OpenCS (коэффициент запаса по моменту с η):
/// 6⌀25 — 1,055 / 1,095; 6⌀22 — 0,904 / 0,936 (ориентации 0° / 30°). Приложение Д:
/// 6⌀25 — M = 156,2 ≤ Mult = 160,0 кН·м; 6⌀22 — M = 159,1 &gt; Mult = 139,3 кН·м.
/// Кривизна OpenCS при M = η·N·e0 для 6⌀25: 0,01177 / 0,01332 1/м против 0,01249 у пособия.
/// </summary>
public sealed class Sp63CircularKrakovskyComparisonTests(ITestOutputHelper output)
{
    const double N = 600.0;
    const double M = 140.0;
    const double L0 = 4.0;
    const double Diameter = 0.40;
    const double BarCircleRadius = 0.165;
    const double Rb = 14_500.0;
    const double Rs = 350_000.0;
    const double KrakovskyCurvature = 0.01249;
    static readonly double Psi = (100.0 + 400.0 * BarCircleRadius) / (M + N * BarCircleRadius);

    static Material ConcreteB25()
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            Type = MatType.Concrete, Fc = -Rb, Ft = 1_050.0, E = 30_000_000.0,
            Ec1Red = -0.0015, Ec2 = -0.0035, Et1Red = 0.00008, Et2 = 0.00015
        };
        var m = new Material { Id = 300, Tag = "B25", Type = MatType.Concrete, E = 30_000_000.0 };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        return m;
    }

    static Material RebarA400()
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            Type = MatType.ReSteelF, Fc = -Rs, Ft = Rs, E = 200_000_000.0,
            Ec2 = -0.025, Et2 = 0.025
        };
        var m = new Material { Id = 301, Tag = "A400", Type = MatType.ReSteelF, E = 200_000_000.0 };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        return m;
    }

    /// <summary>Круг D = 400 мм (64-угольник) и 6 стержней, повёрнутых на rotationDeg.</summary>
    static CrossSection Build(double barDiameter, double rotationDeg)
    {
        const int segments = 64, mesh = 41;
        var concrete = ConcreteB25();
        var v = Sp63NormalFixtures.CircleVertices(Diameter / 2.0, segments);
        var hull = new Contour(v.Select(p => p.X).Append(v[0].X).ToList(),
            v.Select(p => p.Y).Append(v[0].Y).ToList(), "outer") { Type = ContourType.Hull };
        var area = new MaterialArea
        {
            Id = 1, Tag = "circle", Category = AreaCategory.Region,
            Material = concrete, MaterialId = concrete.Id,
            DiagrammType = DiagrammType.L2, Contours = [hull], NX = mesh, NY = mesh
        };
        area.Hull = hull;
        area.ResolveAndBuildDiagramms();
        area.SliceXY(mesh, mesh);

        double rot = rotationDeg * Math.PI / 180.0;
        var fibers = Sp63NormalFixtures.CircleVertices(BarCircleRadius, 6)
            .Select(p => Fiber.CreatePoint(barDiameter,
                p.X * Math.Cos(rot) - p.Y * Math.Sin(rot),
                p.X * Math.Sin(rot) + p.Y * Math.Cos(rot)))
            .ToArray();
        var rebar = MaterialArea.CreateRebarArea(fibers, RebarA400(), DiagrammType.L2, area);
        return new CrossSection { Tag = "IV.Г.2", Areas = [area, rebar] };
    }

    static LimitForceParams EtaParams() => new()
    {
        EtaEnabled = true, EtaL = L0, EtaMuX = 1.0, EtaMuY = 1.0,
        EtaPsiX = Psi, EtaPsiY = Psi
    };

    /// <summary>η по п. 8.1.15 и расчётный момент M = η·N·e0 с учётом ea (п. 8.1.7).</summary>
    static (double Eta, double DesignMoment) DesignMoment(CrossSection section)
    {
        double ea = Sp63MemberContext.AccidentalEccentricity(L0, Diameter);
        double e0 = Sp63MemberContext.EffectiveEccentricity(M / N, ea,
            Sp63StructuralScheme.StaticallyIndeterminate);
        var split = section.SplitStiffnessByMaterial();
        var eta = EccentricityAmplifier.AmplifyFormula(n: -N, m0: N * e0, l0: L0,
            h: Diameter, i: Diameter / 4.0,
            eiConcrete: Math.Min(split.EIxConcrete, split.EIyConcrete),
            eiRebar: Math.Min(split.EIxRebar, split.EIyRebar), psi: Psi);
        Assert.True(eta.Slender);
        Assert.True(eta.Stable);
        return (eta.Eta, eta.Eta * N * e0);
    }

    [Theory]
    [InlineData(0.025, 0.0, true)]
    [InlineData(0.025, 30.0, true)]
    [InlineData(0.022, 0.0, false)]
    [InlineData(0.022, 30.0, false)]
    public void Ndm_VerdictMatchesKrakovsky(double barDiameter, double rotationDeg,
        bool krakovskyPassed)
    {
        var section = Build(barDiameter, rotationDeg);

        var ndm = LimitForceSolver.ForCrossSection(section, CalcType.C, ten: false,
                etaParams: EtaParams())
            .MomentFactor(n: -N, mx: M, my: 0.0);

        output.WriteLine($"⌀{barDiameter * 1000:F0}, {rotationDeg}°: запас НДМ = {ndm.Factor:F4}, " +
                         $"η = {ndm.Eta?.X.Eta:F4}");
        Assert.True(ndm.Converged, $"НДМ не сошёлся: factor={ndm.Factor}");
        Assert.Equal(krakovskyPassed, ndm.Factor >= 1.0);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(30.0)]
    public void Ndm_CurvatureOfPassingSection_IsCloseToKrakovsky(double rotationDeg)
    {
        var section = Build(0.025, rotationDeg);
        var (_, designMoment) = DesignMoment(section);

        var solver = new StrainSolver(section, CalcType.C, ten: false, ca: true, tol: 0.05, maxIter: 100);
        var strain = solver.Solve(-N, designMoment, 0.0);

        output.WriteLine($"{rotationDeg}°: M = {designMoment:F2} кН·м; κ = {strain.ky:G5} 1/м " +
                         $"(пособие {KrakovskyCurvature})");
        Assert.True(solver.Converged, $"Не сошлось: невязка={solver.Residual}");
        Assert.InRange(Math.Abs(strain.ky), KrakovskyCurvature * 0.90, KrakovskyCurvature * 1.10);
    }

    [Theory]
    [InlineData(0.025, true)]
    [InlineData(0.022, false)]
    public void AppendixDFormulas_GiveSameVerdictAsKrakovsky(double barDiameter,
        bool krakovskyPassed)
    {
        var section = Build(barDiameter, 0.0);
        var (eta, designMoment) = DesignMoment(section);
        double asTot = 6.0 * Math.PI * barDiameter * barDiameter / 4.0;

        var capacity = Sp63CircularFormulas.Circular(N, Rb, Rs,
            Math.PI * Diameter * Diameter / 4.0, asTot, Diameter / 2.0, BarCircleRadius);

        output.WriteLine($"⌀{barDiameter * 1000:F0}: η = {eta:F4}; M = {designMoment:F2}; " +
                         $"Mult = {capacity.Mult:F2} кН·м; ξ = {capacity.XiCir:F4}; φ = {capacity.Phi:F4}");
        Assert.True(capacity.ConditionD7);
        Assert.Equal(krakovskyPassed, designMoment <= capacity.Mult);
    }

    [Fact]
    public void AppendixDChecker_IsFormallyNotApplicableForSixBars()
    {
        var section = Build(0.025, 0.0);
        var options = new Sp63NormalOptions(Sp63NormalShapeKind.Circular, Sp63NormalAxis.Mx,
            new Sp63MemberContext(L0, Sp63StructuralScheme.StaticallyIndeterminate, L0,
                Sp63NormalStabilityMode.Member, Psi));

        var result = Sp63NormalChecker.Check(section, new LoadItem { N = -N, Mx = M },
            CalcType.C, options);

        Assert.Equal(Sp63NormalStatus.NotApplicable, result.Status);
        Assert.Equal("circular_insufficient_bars", Assert.Single(result.ApplicabilityMessages).Code);
    }
}
