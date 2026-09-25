using CScore.Sp16;
using Xunit;

namespace CScore.Tests.Steel;

/// <summary>Распознавание профиля по полигону: шаблоны OpenCS и полигоны сортамента.</summary>
public class SteelProfileRecognizerTests
{
    const double Mm = 0.001;

    static SteelProfile Recognize(List<(double X, double Y)> outer, List<(double X, double Y)>? hole = null) =>
        SteelProfileRecognizer.Recognize(new PolygonSection(outer, hole == null ? null : [hole]));

    [Fact]
    public void WeldedIBeamTemplate()
    {
        var p = Recognize(TemplatePoints.IBeamPoints(0.300, 0.150, 0.008, 0.012));
        Assert.Equal(SteelProfileKind.IBeam, p.Kind);
        Assert.Equal(SteelFabrication.Welded, p.Fabrication);
        Assert.Equal(0.300, p.H, 6);
        Assert.Equal(0.150, p.Bf1, 6);
        Assert.Equal(0.012, p.Tf1, 6);
        Assert.Equal(0.008, p.Tw, 6);
        Assert.True(p.IsDoublySymmetricIBeam);
        Assert.False(p.Rotated90);
    }

    [Fact]
    public void RolledWideFlangeIBeam_ParallelFlanges()
    {
        // 30Б1 по ГОСТ Р 57837: h=296, b=140, tw=5,8, tf=8,5, r=13 мм.
        var prof = new IBeamProfile("30Б1", 296 * Mm, 140 * Mm, 5.8 * Mm, 8.5 * Mm, 13 * Mm, 0, 41.92e-4);
        var p = Recognize(prof.ToPolygonPoints(nArc: 12));
        Assert.Equal(SteelProfileKind.IBeam, p.Kind);
        Assert.Equal(SteelFabrication.Rolled, p.Fabrication);
        Assert.Equal(8.5 * Mm, p.Tf1, 5);
        Assert.Equal(5.8 * Mm, p.Tw, 5);
        Assert.InRange(p.R, 12 * Mm, 14 * Mm);
    }

    [Fact]
    public void RolledIBeam_SlopedFlanges_MeanThicknessAtMidOutstand()
    {
        // Двутавр 20 по ГОСТ 8239: h=200, b=100, s=5,2, t=8,4, R=9,5, r=4 мм. Полигон OpenCS строит
        // уклон грани (7 %) между началом закругления у стенки и скруглением пера, поэтому средняя
        // толщина посередине свеса полигона ≈ 8,7 мм, а не 8,4 — распознаватель меряет контур как есть.
        var prof = new IBeamProfile("20", 200 * Mm, 100 * Mm, 5.2 * Mm, 8.4 * Mm, 9.5 * Mm, 4 * Mm, 26.8e-4);
        var p = Recognize(prof.ToPolygonPoints(nArc: 12));
        Assert.Equal(SteelProfileKind.IBeam, p.Kind);
        Assert.Equal(SteelFabrication.Rolled, p.Fabrication);
        Assert.InRange(p.Tf1, 8.3 * Mm, 8.8 * Mm);
        Assert.InRange(p.R, 7.5 * Mm, 10.5 * Mm);
    }

    [Fact]
    public void RolledChannel()
    {
        // Швеллер 20П по ГОСТ 8240: h=200, b=76, s=5,2, t=9,0, R=9,5, r=4 мм.
        var prof = new ChannelProfile("20П", 200 * Mm, 76 * Mm, 5.2 * Mm, 9.0 * Mm, 9.5 * Mm, 4 * Mm, 23.4e-4, 0);
        var p = Recognize(prof.ToPolygonPoints(nArc: 12));
        Assert.Equal(SteelProfileKind.Channel, p.Kind);
        Assert.Equal(SteelFabrication.Rolled, p.Fabrication);
        Assert.Equal(76 * Mm, p.Bf1, 5);
        Assert.InRange(p.Tf1, 8.9 * Mm, 9.1 * Mm);
        Assert.Equal(5.2 * Mm, p.Tw, 5);
        Assert.False(p.Flipped);
    }

    [Fact]
    public void TeeTemplate()
    {
        var p = Recognize(TemplatePoints.TeePoints(0.200, 0.150, 0.010, 0.014));
        Assert.Equal(SteelProfileKind.Tee, p.Kind);
        Assert.Equal(0.200, p.Bf1, 6);
        Assert.Equal(0.014, p.Tf1, 6);
        Assert.Equal(0.010, p.Tw, 6);
        Assert.Equal(0.150, p.H, 6);
    }

    [Fact]
    public void RolledEqualAngle()
    {
        // Уголок 100×8 по ГОСТ 8509: R=12, r=4 мм.
        var prof = new AngleProfile("100x8", 100 * Mm, 100 * Mm, 8 * Mm, 8 * Mm, 12 * Mm, 4 * Mm, 15.6e-4, 0, 0);
        var p = Recognize(prof.ToPolygonPoints(nArc: 12));
        Assert.Equal(SteelProfileKind.Angle, p.Kind);
        Assert.Equal(8 * Mm, p.Tw, 5);
        Assert.Equal(100 * Mm, p.H, 5);
    }

    [Fact]
    public void RectAndRound()
    {
        Assert.Equal(SteelProfileKind.Rect, Recognize(TemplatePoints.RectPoints(0.1, 0.02)).Kind);
        var round = Recognize(TemplatePoints.CirclePoints(0.05, 64));
        Assert.Equal(SteelProfileKind.Round, round.Kind);
        Assert.InRange(round.H, 0.0495, 0.0501);
    }

    [Fact]
    public void RoundTube()
    {
        var t = new RoundTubeProfile("159x6", 159 * Mm, 6 * Mm, 0);
        var p = Recognize(t.OuterPoints(12), t.HolePoints(12));
        Assert.Equal(SteelProfileKind.Pipe, p.Kind);
        Assert.InRange(p.H, 158 * Mm, 159.5 * Mm);
        Assert.InRange(p.Tw, 5.9 * Mm, 6.1 * Mm);
    }

    [Fact]
    public void BentRectTube_IsBentBox()
    {
        var t = new RectTubeProfile("200x100x6", 200 * Mm, 100 * Mm, 6 * Mm, 6 * Mm, 6 * Mm, 0);
        var p = Recognize(t.OuterPoints(12), t.HolePoints(12));
        Assert.Equal(SteelProfileKind.Box, p.Kind);
        Assert.Equal(SteelFabrication.Bent, p.Fabrication);
        Assert.Equal(6 * Mm, p.Tw, 5);
        Assert.InRange(p.R, 11 * Mm, 13 * Mm);
    }

    [Fact]
    public void WeldedBox()
    {
        var outer = TemplatePoints.RectPoints(0.300, 0.400);
        var hole = TemplatePoints.RectPoints(0.300 - 2 * 0.010, 0.400 - 2 * 0.016);
        var p = Recognize(outer, hole);
        Assert.Equal(SteelProfileKind.Box, p.Kind);
        Assert.Equal(SteelFabrication.Welded, p.Fabrication);
        Assert.Equal(0.010, p.Tw, 6);
        Assert.Equal(0.016, p.Tf1, 6);
    }

    [Fact]
    public void RotatedIBeam_IsRecognizedWithRotationFlag()
    {
        var pts = TemplatePoints.IBeamPoints(0.300, 0.150, 0.008, 0.012).Select(q => (q.Y, q.X)).ToList();
        var p = Recognize(pts);
        Assert.Equal(SteelProfileKind.IBeam, p.Kind);
        Assert.True(p.Rotated90);
        Assert.Equal(0.300, p.H, 6);
    }

    [Fact]
    public void Section_DerivedDimensions_WeldedIBeam()
    {
        var poly = new PolygonSection(TemplatePoints.IBeamPoints(0.300, 0.150, 0.008, 0.012));
        var s = Sp16Section.FromContour(poly, null, new SteelMaterialProps(240000, 360000, null, null, 2.06e8));
        Assert.Equal(0.276, s.Hef, 6);
        Assert.Equal((0.150 - 0.008) / 2, s.BefTop, 6);
        Assert.Equal(0.276 * 0.008, s.Aw, 9);
        // Ix = (b h³ − (b − tw)(h − 2tf)³)/12.
        double ix = (0.150 * Math.Pow(0.300, 3) - 0.142 * Math.Pow(0.276, 3)) / 12;
        Assert.Equal(ix, s.Ix, 12);
        // S половины сечения относительно нейтральной оси и толщина стенки на оси.
        var (sHalf, t) = s.ShearAtCentroid(true);
        double sExpected = 0.150 * 0.012 * (0.150 - 0.006) + 0.008 * 0.138 * 0.138 / 2;
        Assert.Equal(sExpected, sHalf, 9);
        Assert.Equal(0.008, t, 9);
        Assert.Equal(SectionCurve.b, s.CurveFor(true));
        Assert.Equal(SectionCurve.c, s.CurveFor(false));
    }
}
