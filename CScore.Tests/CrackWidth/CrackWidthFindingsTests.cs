using System;
using System.Linq;
using CScore;
using Xunit;

namespace CScore.Tests.CrackWidth;

// Находки разбора CrackWidthSolver (спека 2026-10-24-acrc-surface-design.md, раздел
// «Найдено в CrackWidthSolver»): косой изгиб и изгиб только My (Abt считалась по оси Y),
// трещина от одной N, длительная нагрузка ниже Mcrc при полной выше, σs ≤ Rs,ser (п. 8.2.16).
// Знаки: Mx = ∫σ·y·dA, My = ∫σ·x·dA — отрицательный момент растягивает грань с y < 0 (x < 0).
public class CrackWidthFindingsTests
{
    static CrossSection Rect(double b, double h, int nx, int ny, Material? steel = null,
        DiagrammType steelDiagram = DiagrammType.L2, params (double X, double Y, double D)[] bars) =>
        TestSections.Rect(b, h, nx, ny, steel, steelDiagram, bars);

    static CrossSection SymmetricRect() => TestSections.SymmetricRect();

    static void AssertRel(double expected, double actual, double tol, string what) =>
        Assert.True(Math.Abs(actual - expected) <= tol * Math.Max(Math.Abs(expected), 1e-12),
            $"{what}: ожидалось {expected:G10}, получено {actual:G10}");

    [Fact]
    public void MyOnly_EqualsMxOfRotatedSection()
    {
        var a = Rect(0.3, 0.5, 12, 20, bars: [(-0.10, -0.21, 0.016), (0.10, -0.21, 0.016)]);
        // То же сечение, повёрнутое на 90° (x ↔ y): арматура у грани x < 0.
        var b = Rect(0.5, 0.3, 20, 12, bars: [(-0.21, -0.10, 0.016), (-0.21, 0.10, 0.016)]);

        double mcrc = new CrackingSolver(a, CalcType.N).CrackingMoment(0, -1, 0).Mx;
        double m = 2.5 * mcrc;

        var resA = new CrackWidthSolver(a).Compute(N: 0.0, mxLong: m);
        var resB = new CrackWidthSolver(b).Compute(N: 0.0, mxLong: 0.0, myLong: m);

        Assert.True(resA.Cracked);
        Assert.True(resB.Cracked, $"Abt = {resB.Abt:G6}, Mcrc = {resB.Mcrc:G6}");
        AssertRel(resA.Abt, resB.Abt, 1e-6, "Abt");
        AssertRel(resA.Ls, resB.Ls, 1e-6, "ls");
        AssertRel(resA.AcrcLong, resB.AcrcLong, 1e-6, "acrc,long");
    }

    [Fact]
    public void ObliqueBending_AbtByDirection()
    {
        // Квадрат 0,4×0,4, три стержня у угла (−,−); сечение симметрично относительно
        // диагонали y = x, поэтому при моменте вдоль неё нейтральная линия ей перпендикулярна.
        var s = Rect(0.4, 0.4, 16, 16, bars: [(-0.15, -0.15, 0.016), (-0.05, -0.15, 0.016), (-0.15, -0.05, 0.016)]);
        var crc = new CrackingSolver(s, CalcType.N).CrackingMoment(0, -1, -1);
        Assert.True(crc.Converged);

        var res = new CrackWidthSolver(s).Compute(N: 0.0,
            mxLong: 2.5 * crc.Mx, myLong: 2.5 * crc.My);
        Assert.True(res.Cracked, $"Abt = {res.Abt:G6}");

        // s = n·(x, y), n = (−1, −1)/√2: крайнее растянутое волокно — угол (−0,2; −0,2).
        double sqrt2 = Math.Sqrt(2.0);
        double sT = 0.4 / sqrt2;
        double h = 0.8 / sqrt2;
        double sBar = new[] { 0.30, 0.20, 0.20 }.Average() / sqrt2;   // −(x + y)/√2 стержней
        double aCover = sT - sBar;
        AssertRel(h, res.H, 1e-3, "h по направлению");
        double xtEff = Math.Clamp(res.Xt, 2.0 * aCover, 0.5 * h);
        // Полоса s ≥ s_t − xt,eff у угла квадрата — прямоугольный треугольник с катетами xt,eff·√2.
        AssertRel(xtEff * xtEff, res.Abt, 1e-3, "Abt");
    }

    [Fact]
    public void CentralTension_Cracked_AbtIsWholeArea()
    {
        var s = SymmetricRect();
        var ncrc = new CrackingSolver(s, CalcType.N).CrackingLoadFactor(1.0, 0.0, 0.0);
        Assert.True(ncrc.Converged);
        double n = 2.0 * ncrc.N;

        var res = new CrackWidthSolver(s).Compute(N: n, mxLong: 0.0);
        var res10 = new CrackWidthSolver(s).Compute(N: n, mxLong: 0.0, phi3: 1.0);

        Assert.True(res.Cracked, $"Ncrc = {ncrc.N:G6}");
        AssertRel(0.3 * 0.5, res.Abt, 1e-9, "Abt");
        Assert.True(res.AcrcLong > 0.0);
        AssertRel(1.2 * res10.AcrcLong, res.AcrcLong, 1e-9, "φ3 = 1,2");
        Assert.True(res.OnsetByAxialForce);
    }

    [Fact]
    public void LongBelowMcrc_TotalAbove_ShortIsAcrc2()
    {
        var s = TestSections.RectWithBottomRebar();
        double mcrc = new CrackingSolver(s, CalcType.N).CrackingMoment(0, -1, 0).Mx;

        var res = new CrackWidthSolver(s).Compute(N: 0.0, mxLong: 0.5 * mcrc, mxTotal: 2.0 * mcrc);

        Assert.True(res.Cracked);
        Assert.Equal(0.0, res.Acrc1);
        Assert.Equal(0.0, res.Acrc3);
        Assert.Equal(0.0, res.AcrcLong);
        Assert.True(res.Acrc2 > 0.0);
        Assert.Equal(res.Acrc2, res.AcrcShort, 12);
    }

    [Fact]
    public void TensionBeyondPole_SigmaSCrcNearNcrcOverAs()
    {
        var s = SymmetricRect();
        var probe = new CrackingSolver(s, CalcType.N);
        double ncrc = probe.CrackingLoadFactor(1.0, 0.0, 0.0).N;
        double mcrc0 = probe.CrackingMoment(0, -1, 0).Mx;
        double n = 2.0 * ncrc, mx = 0.05 * mcrc0;

        // Эталон: точка трещинообразования на пропорциональном луче (λN, λMx), σs — на
        // пост-трещинной (ten = false) плоскости в этой точке.
        var lf = probe.CrackingLoadFactor(n, mx, 0.0);
        Assert.True(lf.Converged);
        Assert.True(lf.Lambda < 1.0);
        var crcPlane = new StrainSolver(s, CalcType.N, ten: false, ca: true).Solve(lf.N, lf.Mx, lf.My);
        double sigmaRef = MaxRebarStress(s, crcPlane, CalcType.N);

        var res = new CrackWidthSolver(s).Compute(N: n, mxLong: mx);

        Assert.True(res.Cracked);
        Assert.True(res.OnsetByAxialForce);
        AssertRel(sigmaRef, res.SigmaSCrc, 1e-2, "σs,crc");
        Assert.True(res.PsiS > 0.2 + 1e-6, $"ψs = {res.PsiS:G6}");
    }

    [Fact]
    public void SteelAboveRsSer_FailsSteelCheck()
    {
        // Арматура с условным пределом текучести (L3): после 0,9·Rs напряжение растёт до 1,1·Rs,
        // поэтому σs > Rs,ser достижимо до исчерпания несущей способности.
        var steel = TestMaterials.PrestressedRebar("A1000");
        var s = Rect(0.3, 0.5, 12, 20, steel, DiagrammType.L3, [(-0.10, -0.21, 0.016), (0.10, -0.21, 0.016)]);
        double mcrc = new CrackingSolver(s, CalcType.N).CrackingMoment(0, -1, 0).Mx;
        const double rsSer = 1_000_000.0;

        var solver = new CrackWidthSolver(s, acrcUltLong: 100.0, acrcUltShort: 100.0);
        CrackWidthResult? hit = null;
        for (double k = 2.0; k <= 20.0 && hit == null; k += 0.25)
        {
            var r = solver.Compute(N: 0.0, mxLong: k * mcrc);
            if (r.Cracked && r.SigmaS > rsSer * 1.001) hit = r;
        }

        Assert.NotNull(hit);
        AssertRel(rsSer, hit!.RsSer, 1e-9, "Rs,ser");
        Assert.True(hit.UtilSteel > 1.0, $"Кисп стали = {hit.UtilSteel:G6}");
        Assert.False(hit.PassedSteel);
    }

    [Fact]
    public void ConcreteCompressionUtil_GrowsWithMoment_AndDoesNotAffectPassed()
    {
        var s = TestSections.RectWithBottomRebar();
        double mcrc = new CrackingSolver(s, CalcType.N).CrackingMoment(0, -1, 0).Mx;
        var solver = new CrackWidthSolver(s);

        var low = solver.Compute(N: 0.0, mxLong: 2.0 * mcrc);
        var high = solver.Compute(N: 0.0, mxLong: 3.0 * mcrc);

        // При 3·Mcrc арматура A500 этого сечения уже на площадке (σs = Rs,ser — условие ещё выполнено).
        string info = $"бетон {low.UtilConcrete:G4} → {high.UtilConcrete:G4}, сталь {low.UtilSteel:G4} → {high.UtilSteel:G4}";
        Assert.True(low.UtilConcrete > 0.0, info);
        Assert.True(high.UtilConcrete > low.UtilConcrete, info);
        Assert.True(high.UtilConcrete < 1.0, info);
        Assert.True(low.UtilSteel > 0.0 && low.UtilSteel < 1.0, info);
        Assert.True(high.PassedSteel, info);
    }

    static double MaxRebarStress(CrossSection s, Kurvature plane, CalcType calc)
    {
        double max = 0.0;
        foreach (var area in s.Areas)
        {
            if (area.Material?.Type is not (MatType.ReSteelF or MatType.ReSteelU)) continue;
            var dgr = area.Material.GetDiagramms(area.DiagrammType, 0.85)![calc];
            foreach (var f in area.Fibers)
            {
                if (f.TypeFiber != FiberType.point) continue;
                double eps = plane.e0 + plane.ky * f.Y + plane.kz * f.X;
                if (eps <= 0.0) continue;
                max = Math.Max(max, dgr.Sig(eps, out _));
            }
        }
        return max;
    }
}
