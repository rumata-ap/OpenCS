using CScore.En1993;
using CScore.Sp16;

using Xunit;

namespace CScore.Tests;

/// <summary>
/// Эквивалентные несовершенства EN 1993-1-1 (п. 5.3.2): φ по (5.5), табл. 5.1, 6.1, 6.2, критерий п. 5.3.2(6).
/// Значения — по тексту ТКП EN 1993-1-1-2009 (с. 31–32, 53–54 PDF).
/// </summary>
public sealed class En1993ImperfectionsTests
{
    // Ручной расчёт (5.5): α_h = 2/√h в пределах [2/3; 1], α_m = √(0,5·(1 + 1/m)), φ = α_h·α_m/200.
    //  h = 4 м:  2/√4 = 1        → α_h = 1;
    //  h = 9 м:  2/√9 = 0,6667   → α_h = 2/3 (граница);
    //  h = 16 м: 2/√16 = 0,5     → α_h = 2/3 (нижний предел);
    //  h = 6,25 м: 2/2,5 = 0,8   → α_h = 0,8;
    //  m = 1: α_m = 1; m = 2: √0,75 = 0,866025; m = 5: √0,6 = 0,774597.
    [Theory]
    [InlineData(4.0, 1, 1.0, 1.0)]
    [InlineData(9.0, 2, 2.0 / 3, 0.8660254037844386)]
    [InlineData(16.0, 5, 2.0 / 3, 0.7745966692414834)]
    [InlineData(6.25, 1, 0.8, 1.0)]
    [InlineData(1.0, 2, 1.0, 0.8660254037844386)]
    public void Phi_Formula55(double h, int m, double alphaH, double alphaM)
    {
        var r = En1993Imperfections.Phi(h, m);
        Assert.Equal(alphaH, r.AlphaH, 12);
        Assert.Equal(alphaM, r.AlphaM, 12);
        Assert.Equal(alphaH * alphaM / 200, r.Phi, 14);
        Assert.Contains("1/200", r.Describe());
    }

    [Fact]
    public void Phi_RejectsInvalid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => En1993Imperfections.Phi(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => En1993Imperfections.Phi(4, 0));
    }

    [Theory]
    [InlineData(En1993Curve.a0, 350, 300, 0.13)]
    [InlineData(En1993Curve.a, 300, 250, 0.21)]
    [InlineData(En1993Curve.b, 250, 200, 0.34)]
    [InlineData(En1993Curve.c, 200, 150, 0.49)]
    [InlineData(En1993Curve.d, 150, 100, 0.76)]
    public void Tables51And61(En1993Curve curve, double elastic, double plastic, double alpha)
    {
        Assert.Equal(elastic, En1993Imperfections.LOverE0(curve, plastic: false));
        Assert.Equal(plastic, En1993Imperfections.LOverE0(curve, plastic: true));
        Assert.Equal(1 / elastic, En1993Imperfections.E0OverL(curve, false), 15);
        Assert.Equal(alpha, En1993Imperfections.Alpha(curve));
        Assert.Equal(curve, En1993Imperfections.ParseCurve(curve.ToString().ToUpperInvariant()));
    }

    static SteelProfile I(SteelFabrication fab, double h, double b, double tf) =>
        new() { Kind = SteelProfileKind.IBeam, Fabrication = fab, H = h, Bf1 = b, Tf1 = tf, Tw = tf / 2 };

    const double S355 = 345_000, S420 = 440_000, S460 = 450_000;

    [Theory]
    // Прокатный двутавр h/b > 1,2: t_f ≤ 40 — y: a | a0, z: b | a0.
    [InlineData(0.5, 0.2, 0.016, S355, true, En1993Curve.a)]
    [InlineData(0.5, 0.2, 0.016, S355, false, En1993Curve.b)]
    [InlineData(0.5, 0.2, 0.040, S420, true, En1993Curve.a)]    // t_f = 40 — граница включительно; R_yn = 440 — ещё S235–S420
    [InlineData(0.5, 0.2, 0.040, S460, true, En1993Curve.a0)]
    [InlineData(0.5, 0.2, 0.040, S460, false, En1993Curve.a0)]
    // 40 < t_f ≤ 100 — y: b | a, z: c | a.
    [InlineData(0.5, 0.2, 0.041, S355, true, En1993Curve.b)]
    [InlineData(0.5, 0.2, 0.100, S355, false, En1993Curve.c)]
    [InlineData(0.5, 0.2, 0.060, S460, false, En1993Curve.a)]
    // h/b ≤ 1,2 (граница 1,2 включительно): t_f ≤ 100 — y: b | a, z: c | a; t_f > 100 — d | c.
    [InlineData(0.36, 0.3, 0.020, S355, true, En1993Curve.b)]
    [InlineData(0.36, 0.3, 0.020, S355, false, En1993Curve.c)]
    [InlineData(0.36, 0.3, 0.100, S460, true, En1993Curve.a)]
    [InlineData(0.36, 0.3, 0.101, S355, true, En1993Curve.d)]
    [InlineData(0.36, 0.3, 0.110, S460, false, En1993Curve.c)]
    public void Table62_RolledI(double h, double b, double tf, double ryn, bool strong, En1993Curve expected)
    {
        var r = En1993Imperfections.Curve(I(SteelFabrication.Rolled, h, b, tf), ryn, strong);
        Assert.Equal(expected, r.Curve);
        Assert.Contains("табл. 6.2", r.Reason);
    }

    [Fact]
    public void Table62_RolledI_HbOver12_ThickFlange_Undefined()
    {
        var r = En1993Imperfections.Curve(I(SteelFabrication.Rolled, 0.6, 0.3, 0.11), S355, true);
        Assert.Null(r.Curve);
    }

    [Theory]
    [InlineData(0.040, S355, true, En1993Curve.b)]
    [InlineData(0.040, S460, false, En1993Curve.c)]
    [InlineData(0.045, S355, true, En1993Curve.c)]
    [InlineData(0.045, S460, false, En1993Curve.d)]
    public void Table62_WeldedI(double tf, double ryn, bool strong, En1993Curve expected) =>
        Assert.Equal(expected, En1993Imperfections.Curve(I(SteelFabrication.Welded, 0.8, 0.3, tf), ryn, strong).Curve);

    [Theory]
    [InlineData(SteelProfileKind.Box, SteelFabrication.Rolled, S355, En1993Curve.a)]
    [InlineData(SteelProfileKind.Box, SteelFabrication.Rolled, S460, En1993Curve.a0)]
    [InlineData(SteelProfileKind.Pipe, SteelFabrication.Rolled, S355, En1993Curve.a)]
    [InlineData(SteelProfileKind.Box, SteelFabrication.Bent, S460, En1993Curve.c)]
    [InlineData(SteelProfileKind.Pipe, SteelFabrication.Bent, S355, En1993Curve.c)]
    [InlineData(SteelProfileKind.Box, SteelFabrication.Welded, S460, En1993Curve.b)]
    [InlineData(SteelProfileKind.Channel, SteelFabrication.Rolled, S460, En1993Curve.c)]
    [InlineData(SteelProfileKind.Tee, SteelFabrication.Welded, S355, En1993Curve.c)]
    [InlineData(SteelProfileKind.Rect, SteelFabrication.Rolled, S355, En1993Curve.c)]
    [InlineData(SteelProfileKind.Round, SteelFabrication.Rolled, S355, En1993Curve.c)]
    [InlineData(SteelProfileKind.Angle, SteelFabrication.Rolled, S460, En1993Curve.b)]
    public void Table62_OtherKinds(SteelProfileKind kind, SteelFabrication fab, double ryn, En1993Curve expected)
    {
        var p = new SteelProfile { Kind = kind, Fabrication = fab, H = 0.2, Bf1 = 0.2, Tf1 = 0.01, Tw = 0.01 };
        Assert.Equal(expected, En1993Imperfections.Curve(p, ryn, true).Curve);
        Assert.Equal(expected, En1993Imperfections.Curve(p, ryn, false).Curve);
    }

    [Fact]
    public void Table62_Undefined()
    {
        Assert.Null(En1993Imperfections.Curve(new SteelProfile { Kind = SteelProfileKind.Generic }, S355, true).Curve);
        Assert.Null(En1993Imperfections.Curve(new SteelProfile { Kind = SteelProfileKind.Pipe, Fabrication = SteelFabrication.Welded, H = 0.2, Tw = 0.01 }, S355, true).Curve);
        Assert.Null(En1993Imperfections.Curve(I(SteelFabrication.Bent, 0.3, 0.1, 0.004), S355, true).Curve);
    }

    // Сварной двутавр h = 500, b = 250, t_f = 20 мм по контуру: оси контура x — горизонтальная (сильная).
    static CrossSection WeldedISection(bool rotated, double rynKpa)
    {
        const double h = 0.5, b = 0.25, tf = 0.02, tw = 0.01;
        double[] xs = [-b / 2, b / 2, b / 2, tw / 2, tw / 2, b / 2, b / 2, -b / 2, -b / 2, -tw / 2, -tw / 2, -b / 2, -b / 2];
        double[] ys = [-h / 2, -h / 2, -h / 2 + tf, -h / 2 + tf, h / 2 - tf, h / 2 - tf, h / 2, h / 2, h / 2 - tf, h / 2 - tf, -h / 2 + tf, -h / 2 + tf, -h / 2];
        if (rotated) (xs, ys) = (ys.Select(v => -v).ToArray(), xs);
        MaterialChars Chars(CalcType calc, double ry) => new(calc) { Type = MatType.Steel, Ry = ry, Ru = 1.4 * ry, E = 206_000_000, Fc = -ry, Ft = ry };
        var steel = new Material
        {
            Tag = "S", Type = MatType.Steel,
            MaterialChars = [Chars(CalcType.C, 0.95 * rynKpa), Chars(CalcType.CL, 0.95 * rynKpa), Chars(CalcType.N, rynKpa), Chars(CalcType.NL, rynKpa)],
        };
        var area = new MaterialArea { Tag = "I", Category = AreaCategory.Region, Material = steel, Hull = new Contour(xs, ys, "outer") };
        return new CrossSection { Tag = "I500", Areas = [area] };
    }

    [Fact]
    public void Table62_FromCrossSection_Axes()
    {
        var upright = WeldedISection(rotated: false, S355);
        Assert.Equal(En1993Curve.b, En1993Imperfections.Curve(upright, aboutContourX: true).Curve);   // y–y
        Assert.Equal(En1993Curve.c, En1993Imperfections.Curve(upright, aboutContourX: false).Curve);  // z–z
        var turned = WeldedISection(rotated: true, S355);
        Assert.Equal(En1993Curve.c, En1993Imperfections.Curve(turned, aboutContourX: true).Curve);
        Assert.Equal(En1993Curve.b, En1993Imperfections.Curve(turned, aboutContourX: false).Curve);
        Assert.Null(En1993Imperfections.Curve(new CrossSection { Tag = "пусто" }, true).Curve);
    }

    [Fact]
    public void BowActive_Criterion5326()
    {
        double ei = 2.06e11 * 1e-4, l = 6, ncr = Math.PI * Math.PI * ei / (l * l);
        Assert.Equal(ncr, En1993Imperfections.NcrPinned(ei, l), 6);
        Assert.True(En1993Imperfections.BowActive(0.25 * ncr * 1.01, ei, l, momentEnd: true));
        Assert.False(En1993Imperfections.BowActive(0.25 * ncr * 0.99, ei, l, momentEnd: true));
        Assert.False(En1993Imperfections.BowActive(0.5 * ncr, ei, l, momentEnd: false));
        Assert.False(En1993Imperfections.BowActive(-0.5 * ncr, ei, l, momentEnd: true));
    }
}
