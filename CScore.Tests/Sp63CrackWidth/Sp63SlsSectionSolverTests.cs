using CScore;
using CScore.Sp63.CrackWidth;
using CScore.Sp63.Normal;
using CScore.Tests.Sp63Fixtures;
using Xunit;

namespace CScore.Tests.Sp63CrackWidth;

/// <summary>
/// Тесты общего SLS solver'а приведённого полосового сечения: полные характеристики, нейтральная
/// ось треснувшего сечения, ветки Mcrc (упругопластический момент п. 8.2.11 против стрессовой
/// эпюры п. 8.2.10) и численный паритет прямоугольника с текущим
/// <see cref="ShellSimplSolver.ComputeStripSls"/>.
/// </summary>
public sealed class Sp63SlsSectionSolverTests
{
    static readonly Material Concrete = Sp63SlsTestSections.B25();
    static readonly Material Rebar = Sp63SlsTestSections.A400();
    static MaterialChars CChars => Concrete.GetChars(CalcType.N)!;
    static MaterialChars RChars => Rebar.GetChars(CalcType.N)!;

    static Sp63SlsSectionGeometry Geometry(CrossSection section,
        Sp63NormalShapeKind shapeKind, int tensionDirection) =>
        Sp63SlsTestSections.GeometryOrFail(section, shapeKind, Sp63NormalAxis.Mx, tensionDirection);

    [Fact]
    public void FullProperties_Rectangle_MatchClosedForm()
    {
        var geometry = Sp63SlsTestSections.GeometryRectangle(0.5, 0.3);
        var result = Sp63SlsSectionSolver.ComputeFullProperties(geometry, alpha: 1.0);
        double area = 0.15 + geometry.TensionLayer.Area + geometry.CompressionLayer.Area;
        double first = 0.15 * 0.15 +
            geometry.TensionLayer.Area * geometry.TensionLayer.Coordinate +
            geometry.CompressionLayer.Area * geometry.CompressionLayer.Coordinate;
        double centroid = first / area;
        double secondAtZero = 0.5 * Math.Pow(0.3, 3) / 3.0 +
            geometry.TensionLayer.Area * Math.Pow(geometry.TensionLayer.Coordinate, 2) +
            geometry.CompressionLayer.Area * Math.Pow(geometry.CompressionLayer.Coordinate, 2);

        Assert.Equal(area, result.Area, 12);
        Assert.Equal(centroid, result.Centroid, 12);
        Assert.Equal(secondAtZero - area * centroid * centroid, result.Inertia, 12);
        Assert.Equal(0.3 - centroid, result.DistanceToTensionEdge, 12);
        Assert.Equal(result.Inertia / result.DistanceToTensionEdge, result.Wred, 12);
        Assert.Equal(result.Wred / area, result.Ex, 12);
    }

    [Fact]
    public void FullProperties_Tee_EqualsSumOfWebAndFlangeMoments()
    {
        var geometry = Sp63SlsTestSections.GeometryTopTee(0.6, 0.6, 0.2, 0.15);
        var result = Sp63SlsSectionSolver.ComputeFullProperties(geometry, alpha: 1.0);

        Assert.Equal(0.2 * 0.45 + 0.6 * 0.15 +
            geometry.TensionLayer.Area + geometry.CompressionLayer.Area, result.Area, 12);
        Assert.InRange(result.Centroid, 0.20, 0.30);
        Assert.True(result.Inertia > 0);
    }

    [Fact]
    public void NeutralAxis_RectangleMatchesStripSolver()
    {
        const double b = 0.5, h = 0.3, asT = 20.36e-4, asC = 1e-8;
        var geometry = Geometry(Sp63SlsTestSections.Rectangle(b, h),
            Sp63NormalShapeKind.Rectangular, 1);
        double alpha = Rebar.E / (Math.Abs(CChars.Fc) / 0.0015);

        var result = Sp63SlsSectionSolver.ComputeCrackTerm(
            geometry, CChars, RChars, 80.0, 0.0, 1.0, 0.5, 0.4,
            SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63);

        double expected = ShellSimplSolver.NeutralAxis(
            geometry.TensionLayer.Coordinate, geometry.CompressionLayer.Coordinate,
            asT / b, asC / b, alpha);
        Assert.Equal(expected, result.Xm, 9);
    }

    [Fact]
    public void NeutralAxis_Tee_IsContinuousAcrossFlangeBoundary()
    {
        var geometry = Sp63SlsTestSections.GeometryTopTee(0.6, 0.6, 0.2, 0.15);
        const double boundary = 0.15; // граница полки в ориентированных координатах (полка сжата)

        double areaLow = geometry.Area(0, boundary - 1e-9);
        double areaHigh = geometry.Area(0, boundary + 1e-9);
        double firstLow = geometry.FirstMoment(0, boundary - 1e-9);
        double firstHigh = geometry.FirstMoment(0, boundary + 1e-9);

        Assert.True(Math.Abs(areaHigh - areaLow) < 1e-6, "Площадь разрывна на границе полки.");
        Assert.True(Math.Abs(firstHigh - firstLow) < 1e-6, "Первый момент разрывен на границе полки.");
    }

    [Fact]
    public void CrackTerm_ThroughTension_XmIsNotClamped()
    {
        var geometry = Geometry(Sp63SlsTestSections.Rectangle(0.5, 0.3),
            Sp63NormalShapeKind.Rectangular, 1);

        var result = Sp63SlsSectionSolver.ComputeCrackTerm(
            geometry, CChars, RChars, 5.0, 2000.0, 1.0, 0.5, 0.4,
            SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63);

        Assert.True(result.Xm <= 0.0, $"xm = {result.Xm}: сквозное растяжение замаскировано clamp'ом.");
    }

    [Fact]
    public void Mcrc_Rectangle_UsesWplGammaForAllMethods()
    {
        var geometry = Geometry(Sp63SlsTestSections.Rectangle(0.5, 0.3),
            Sp63NormalShapeKind.Rectangular, 1);
        var full = Sp63SlsSectionSolver.ComputeFullProperties(geometry, Rebar.E / Concrete.E);
        double mu = geometry.TensionLayer.Area / (0.5 * 0.3);

        foreach (var (method, gamma) in new[]
        {
            (WplGammaMethod.Sp63, 1.3),
            (WplGammaMethod.Snip2030184, 1.75),
            (WplGammaMethod.Radaykin2018, 1.6 + 1.0 / (100.0 * Math.Sqrt(mu)))
        })
        {
            var result = Sp63SlsSectionSolver.ComputeCrackTerm(
                geometry, CChars, RChars, 80.0, 10.0, 1.0, 0.5, 0.4,
                SigmaSCrcMethod.ReleasedConcrete8137, method);

            Assert.Equal(gamma, result.Gamma, 9);
            Assert.Equal(CChars.Ft * gamma * full.Wred - 10.0 * full.Ex, result.Mcrc, 9);
        }
    }

    [Fact]
    public void Mcrc_CompressionFlangeTee_UsesFixedGamma13()
    {
        var geometry = Geometry(Sp63SlsTestSections.TopTee(0.6, 0.6, 0.2, 0.15),
            Sp63NormalShapeKind.Tee, -1);
        var full = Sp63SlsSectionSolver.ComputeFullProperties(geometry, Rebar.E / Concrete.E);

        var result = Sp63SlsSectionSolver.ComputeCrackTerm(
            geometry, CChars, RChars, 80.0, 0.0, 1.0, 0.5, 0.4,
            SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Radaykin2018);

        Assert.Equal(1.3, result.Gamma, 9);
        Assert.Equal(CChars.Ft * 1.3 * full.Wred, result.Mcrc, 9);
    }

    [Fact]
    public void Mcrc_ISection_UsesStressBlockWithHandCheck()
    {
        var geometry = Geometry(Sp63SlsTestSections.ISection(0.6, 0.6, 0.2, 0.1, 0.1),
            Sp63NormalShapeKind.Tee, 1);

        var result = Sp63SlsSectionSolver.ComputeCrackTerm(
            geometry, CChars, RChars, 80.0, 0.0, 1.0, 0.5, 0.4,
            SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63);

        double manual = ManualStressBlockMcrc(geometry, CChars, RChars);
        Assert.True(double.IsNaN(result.Gamma), "Для двутавра gamma-ветка не должна использоваться.");
        Assert.Equal(manual, result.Mcrc, 6);
        Assert.True(result.Mcrc > 0);
    }

    [Fact]
    public void Mcrc_TensionFlangeTee_UsesStressBlock()
    {
        var geometry = Geometry(Sp63SlsTestSections.TopTee(0.6, 0.6, 0.2, 0.15),
            Sp63NormalShapeKind.Tee, 1);

        var result = Sp63SlsSectionSolver.ComputeCrackTerm(
            geometry, CChars, RChars, 80.0, 0.0, 1.0, 0.5, 0.4,
            SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63);

        double manual = ManualStressBlockMcrc(geometry, CChars, RChars);
        Assert.True(double.IsNaN(result.Gamma), "Тавр с растянутой полкой должен идти по общей ветке п. 8.2.10.");
        Assert.Equal(manual, result.Mcrc, 6);
    }

    [Theory]
    [InlineData(30.0)]
    [InlineData(80.0)]
    [InlineData(400.0)]
    public void PsiS_ClampedToTenth(double moment)
    {
        var geometry = Geometry(Sp63SlsTestSections.Rectangle(0.5, 0.3),
            Sp63NormalShapeKind.Rectangular, 1);

        var result = Sp63SlsSectionSolver.ComputeCrackTerm(
            geometry, CChars, RChars, moment, 0.0, 1.0, 0.5, 0.4,
            SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63);

        Assert.InRange(result.PsiS, 0.1, 1.0);
    }

    [Fact]
    public void Acrc_ZeroWithoutCracks()
    {
        var geometry = Geometry(Sp63SlsTestSections.Rectangle(0.5, 0.3),
            Sp63NormalShapeKind.Rectangular, 1);

        var result = Sp63SlsSectionSolver.ComputeCrackTerm(
            geometry, CChars, RChars, 1.0, 0.0, 1.0, 0.5, 0.4,
            SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63);

        Assert.False(result.Cracked);
        Assert.Equal(0.0, result.Acrc, 12);
    }

    [Fact]
    public void CrackTerm_Rectangle_ParityWithStripSolver()
    {
        const double b = 0.5, h = 0.3, asT = 20.36e-4, asC = 1e-8, ds = 0.036, m = 80.0;
        var geometry = Geometry(Sp63SlsTestSections.Rectangle(b, h),
            Sp63NormalShapeKind.Rectangular, 1);
        Assert.Equal(asT, geometry.TensionLayer.Area, 12);

        var result = Sp63SlsSectionSolver.ComputeCrackTerm(
            geometry, CChars, RChars, m, 0.0, 1.0, 0.5, 0.4,
            SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63);
        var expected = ShellSimplSolver.ComputeStripSls(
            m / b, 0.0, h, 0.26, 0.04, asT / b, asC / b, ds,
            CChars, RChars, 1.0, 0.5, 0.4);

        Assert.Equal(expected.Xm, result.Xm, 9);
        Assert.Equal(expected.Psi_s, result.PsiS, 9);
        Assert.Equal(expected.Ls_m, result.Ls, 9);
        Assert.Equal(expected.Acrc_mm, result.Acrc, 6);
        Assert.Equal(expected.Sigma_s_MPa, result.SigmaS / 1000.0, 9);
        Assert.Equal(expected.Sigma_s_crc_MPa, result.SigmaSCrc / 1000.0, 9);
        Assert.Equal(expected.Mcrc * b, result.Mcrc, 9);
        Assert.Equal(expected.Cracked, result.Cracked);
    }

    /// <summary>
    /// Независимый ручной расчёт момента образования трещин по стрессовой эпюре п. 8.2.10:
    /// плоские сечения с деформацией крайнего растянутого бетона εb1,0 = 0,00015, треугольная
    /// эпюра сжатия с модулем Rb,ser/0,0015, трапеция растяжения с потолком Rbt,ser, упругая
    /// арматура. Вклады полос считаются формулами треугольника/трапеции эпюры напряжений,
    /// а не интегратором solver'а.
    /// </summary>
    static double ManualStressBlockMcrc(Sp63SlsSectionGeometry geometry,
        MaterialChars concrete, MaterialChars rebar)
    {
        const double epsFace = 0.00015;
        double rbSer = Math.Abs(concrete.Fc), rbt = concrete.Ft;
        double ec = rbSer / 0.0015, es = rebar.E;
        double h = geometry.Height;
        double asT = geometry.TensionLayer.Area, ysT = geometry.TensionLayer.Coordinate;
        double asC = geometry.CompressionLayer.Area, ysC = geometry.CompressionLayer.Coordinate;

        (double F, double M) Segment(double y0, double y1, double width, double x, double k)
        {
            double ySat = x + rbt / (ec * k);
            double force = 0.0, moment = 0.0;
            var cuts = new List<double> { y0 };
            if (x > y0 && x < y1) cuts.Add(x);
            if (ySat > y0 && ySat < y1) cuts.Add(ySat);
            cuts.Add(y1);
            cuts.Sort();
            for (int i = 0; i + 1 < cuts.Count; i++)
            {
                double a = cuts[i], mid = (cuts[i] + cuts[i + 1]) / 2.0, c = cuts[i + 1];
                if (mid >= ySat)
                {
                    force += width * rbt * (c - a);
                    moment += width * rbt * (c * c - a * a) / 2.0;
                }
                else
                {
                    force += width * ec * k * ((c * c - a * a) / 2.0 - x * (c - a));
                    moment += width * ec * k *
                        ((c * c * c - a * a * a) / 3.0 - x * (c * c - a * a) / 2.0);
                }
            }
            return (force, moment);
        }

        (double F, double M) Equilibrium(double x)
        {
            double k = epsFace / (h - x);
            double force = es * k * ((ysT - x) * asT + (ysC - x) * asC);
            double moment = es * k * ((ysT - x) * asT * ysT + (ysC - x) * asC * ysC);
            foreach (var band in geometry.Bands)
            {
                var (f, m) = Segment(band.Start, band.End, band.Width, x, k);
                force += f;
                moment += m;
            }
            return (force, moment);
        }

        double lo = 0.0, hi = h;
        for (int i = 0; i < 200; i++)
        {
            double mid = (lo + hi) / 2.0;
            if (Equilibrium(mid).F > 0.0) lo = mid; else hi = mid;
        }
        return Equilibrium((lo + hi) / 2.0).M;
    }
}
