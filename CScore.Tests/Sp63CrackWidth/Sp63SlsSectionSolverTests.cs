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
        Assert.Equal(1.0, result.Mcrc / manual, 3);
        Assert.True(result.Mcrc > 0);
    }

    [Fact]
    public void Mcrc_Rectangle_StressDiagramOption_UsesEpureWithHandCheck()
    {
        var geometry = Geometry(Sp63SlsTestSections.Rectangle(0.5, 0.3),
            Sp63NormalShapeKind.Rectangular, 1);

        var result = Sp63SlsSectionSolver.ComputeCrackTerm(
            geometry, CChars, RChars, 80.0, 0.0, 1.0, 0.5, 0.4,
            SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63,
            wplMethod: Sp63WplMethod.StressDiagram);
        var byGamma = Sp63SlsSectionSolver.ComputeCrackTerm(
            geometry, CChars, RChars, 80.0, 0.0, 1.0, 0.5, 0.4,
            SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63);

        Assert.True(double.IsNaN(result.Gamma));
        Assert.Equal(1.0, result.Mcrc / ManualStressBlockMcrc(geometry, CChars, RChars), 3);
        Assert.Equal(1.3, byGamma.Gamma, 9);
        // Сильно армированное сечение (μ ≈ 1,4 %): арматура в эпюре работает при деформации
        // 0,00015, поэтому γ = Wpl/Wred заметно выше упрощённого 1,3 (8.122).
        Assert.True(result.Wpl / result.Full!.Wred > 1.3);
        Assert.True(result.Mcrc > byGamma.Mcrc);
    }

    [Fact]
    public void Wpl_PlainRectangle_StressDiagram_MatchesClosedForm()
    {
        // Бетонный прямоугольник (арматура 1 мм²): при r = εel/εbt2, εel = Rbt,ser/Eb,
        // равновесие даёт x = t·√(2r − r²), t = h − x, и
        // M = b·Rbt,ser·t²·[⅔·(1 − r/2)·√(2r − r²) + (1 − r²)/2 + r²/3].
        // Для B25 (r = 1,55/(30000·0,00015) = 0,344) γ = Wpl/(b·h²/6) ≈ 1,75 — значение Гвоздева.
        const double b = 0.5, h = 0.3;
        var geometry = Geometry(Sp63SlsTestSections.Rectangle(b, h, 1e-6, 1e-6),
            Sp63NormalShapeKind.Rectangular, 1);

        var result = Sp63SlsSectionSolver.ComputeCrackTerm(
            geometry, CChars, RChars, 80.0, 0.0, 1.0, 0.5, 0.4,
            SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63,
            wplMethod: Sp63WplMethod.StressDiagram);

        double r = CChars.Ft / (CChars.E * Sp63SlsSectionSolver.EpsB1T0);
        double root = Math.Sqrt(2 * r - r * r);
        double t = h / (1 + root);
        double wpl = t * t * (2.0 / 3.0 * (1 - r / 2) * root + (1 - r * r) / 2 + r * r / 3) * b;
        Assert.Equal(1.0, result.Wpl / wpl, 3);
        Assert.Equal(1.75, wpl / (b * h * h / 6), 2);
    }

    [Fact]
    public void Mcrc_TeeWithCompressionFlange_StressDiagramOption_OverridesGamma()
    {
        var geometry = Geometry(Sp63SlsTestSections.TopTee(0.6, 0.6, 0.2, 0.15),
            Sp63NormalShapeKind.Tee, -1);

        var result = Sp63SlsSectionSolver.ComputeCrackTerm(
            geometry, CChars, RChars, 80.0, 0.0, 1.0, 0.5, 0.4,
            SigmaSCrcMethod.ReleasedConcrete8137, WplGammaMethod.Sp63,
            wplMethod: Sp63WplMethod.StressDiagram);

        Assert.True(double.IsNaN(result.Gamma));
        Assert.Equal(1.0, result.Mcrc / ManualStressBlockMcrc(geometry, CChars, RChars), 3);
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
        Assert.Equal(1.0, result.Mcrc / manual, 3);
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
    /// Независимый расчёт момента образования трещин по эпюре п. 8.2.10 (рисунок 8.17)
    /// послойным интегрированием: 4000 слоёв по высоте, ширина слоя — из площади профиля,
    /// деформация крайнего растянутого волокна 0,00015, бетон с начальным модулем Eb — упругое
    /// сжатие и растяжение до Rbt,ser, далее площадка Rbt,ser; упругая арматура. Не использует
    /// формулы трапеций solver'а.
    /// </summary>
    static double ManualStressBlockMcrc(Sp63SlsSectionGeometry geometry,
        MaterialChars concrete, MaterialChars rebar)
    {
        const double epsFace = 0.00015;
        const int layers = 4000;
        double eb = concrete.E, rbt = concrete.Ft, es = rebar.E;
        double h = geometry.Height, dy = h / layers;
        var widths = new double[layers];
        for (int i = 0; i < layers; i++)
            widths[i] = geometry.Area(i * dy, (i + 1) * dy) / dy;

        (double F, double M) Equilibrium(double x)
        {
            double k = epsFace / (h - x);
            double force = 0.0, moment = 0.0;
            for (int i = 0; i < layers; i++)
            {
                double y = (i + 0.5) * dy;
                double sigma = Math.Min(eb * k * (y - x), rbt);
                force += sigma * widths[i] * dy;
                moment += sigma * widths[i] * dy * y;
            }
            foreach (var (area, y) in new[]
                     {
                         (geometry.TensionLayer.Area, geometry.TensionLayer.Coordinate),
                         (geometry.CompressionLayer.Area, geometry.CompressionLayer.Coordinate)
                     })
            {
                force += es * k * (y - x) * area;
                moment += es * k * (y - x) * area * y;
            }
            return (force, moment);
        }

        double lo = 0.0, hi = h * 0.999;
        for (int i = 0; i < 100; i++)
        {
            double mid = (lo + hi) / 2.0;
            if (Equilibrium(mid).F > 0.0) lo = mid; else hi = mid;
        }
        return Equilibrium((lo + hi) / 2.0).M;
    }
}
