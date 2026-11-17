using System.Text.Json;
using CScore.Sp63Punching;
using Xunit;

namespace CScore.Tests.Sp63Punching;

/// <summary>Геометрия контура и правила пп. 8.1.46–8.1.52 СП 63 на ручных примерах.</summary>
public class PunchingCoreTests
{
    const double Rbt = 1050; // кПа, B25

    static PunchingInput Rect(double bx, double by, double h0, double f, double mx = 0, double my = 0,
        IReadOnlyList<PunchingSlabEdge>? edges = null, PunchingOptions? options = null,
        PunchingShearReinforcement? sw = null, IReadOnlyList<PunchingHole>? holes = null,
        PunchingForces? forces = null)
        => new(new PunchingLoadArea.Rectangle(bx, by), h0, Rbt, forces ?? new PunchingForces(f, mx, my), sw, edges, holes,
            Options: options);

    static PunchingVariantResult V(PunchingResult r, string id) => r.Variants.Single(v => v.Variant.Id == id);

    static PunchingSlabEdge Bottom(double y) => new(new PlanPoint(0, y), new PlanPoint(0, 1));

    [Fact]
    public void ClosedRectangle_MatchesSp63Formulas()
    {
        // 0,3×0,5, h0 = 0,2: Lx = 0,5, Ly = 0,7.
        var r = PunchingChecker.Check(Rect(0.3, 0.5, 0.2, 100));
        var v = V(r, "closed");
        double lx = 0.5, ly = 0.7;
        Assert.Equal(2 * (lx + ly), v.Geometry.U, 12);
        Assert.Equal(2 * lx * Math.Pow(ly / 2, 2) + 2 * Math.Pow(ly, 3) / 12, v.Geometry.Ibx, 12);
        Assert.Equal(2 * ly * Math.Pow(lx / 2, 2) + 2 * Math.Pow(lx, 3) / 12, v.Geometry.Iby, 12);
        Assert.Equal(Rbt * 2.4 * 0.2, v.Fb, 9);
        Assert.Equal(100 / v.Fb, v.Utilization, 12);
        Assert.Equal(PunchingStatus.ConcreteSufficient, r.Status);
    }

    [Fact]
    public void Circle_SectionModulusMatchesSp63Formula()
    {
        double d = 0.4, h0 = 0.165;
        var r = PunchingChecker.Check(new PunchingInput(new PunchingLoadArea.Circle(d), h0, Rbt, new PunchingForces(100, 10, 0)));
        var v = V(r, "closed");
        Assert.Equal(Math.PI * (d + h0), v.Geometry.U, 12);
        Assert.Equal(Math.PI * Math.Pow(d + h0, 2) / 4, v.Wbx, 12);   // п. 8.1.51
        Assert.Equal(Math.PI * Math.Pow(d + h0, 2) / 4, v.Wby, 12);
    }

    [Fact]
    public void Ring_SameAsCircleByOuterDiameter()
    {
        var f = new PunchingForces(300, 20, 10);
        var a = PunchingChecker.Check(new PunchingInput(new PunchingLoadArea.Circle(0.5), 0.18, Rbt, f));
        var b = PunchingChecker.Check(new PunchingInput(new PunchingLoadArea.Ring(0.5, 0.1), 0.18, Rbt, f));
        Assert.Equal(a.Utilization, b.Utilization, 12);
    }

    [Fact]
    public void CircleAtChamferedCorner_TangentLegsAndArc_PropertiesMatchNumericIntegration()
    {
        // Края: снизу y = −0,3 и скос под 135° к нему (нормаль (−1, 1)/√2 смотрит в плиту) через (0,35; 0).
        var edges = new List<PunchingSlabEdge>
        {
            Bottom(-0.3),
            new(new PlanPoint(0.35, 0), new PlanPoint(-1, 1))
        };
        var input = new PunchingInput(new PunchingLoadArea.Circle(0.3), 0.2, Rbt, new PunchingForces(100, 0, 0),
            Edges: edges);
        var r = PunchingChecker.Check(input);
        var v = r.Variants.Single(x => x.Variant.Kind == PunchingVariantKind.OpenCorner);
        var els = v.Variant.Contour.Elements.ToList();
        Assert.Equal(3, els.Count);
        Assert.IsType<LineElement>(els[0]);
        Assert.IsType<ArcElement>(els[1]);
        Assert.IsType<LineElement>(els[2]);
        // Концы ветвей лежат на краях, ветви идут по нормалям к краям.
        Assert.True(Math.Abs(edges.Min(e => Math.Abs(e.SignedDistance(els[0].Start)))) < 1e-12);
        Assert.True(Math.Abs(edges.Min(e => Math.Abs(e.SignedDistance(els[2].End)))) < 1e-12);

        // Численная проверка интегралов по контуру.
        var pts = new List<(PlanPoint P, double Ds)>();
        foreach (var e in els)
        {
            const int n = 20000;
            for (int i = 0; i < n; i++)
            {
                double t = (i + 0.5) / n;
                PlanPoint p = e switch
                {
                    LineElement l => l.A + t * (l.B - l.A),
                    ArcElement a => a.Center + a.R * PlanPoint.FromAngle(a.Phi1 + t * a.Theta),
                    _ => throw new InvalidOperationException()
                };
                pts.Add((p, e.Length / n));
            }
        }
        double u = pts.Sum(x => x.Ds);
        double xc = pts.Sum(x => x.P.X * x.Ds) / u, yc = pts.Sum(x => x.P.Y * x.Ds) / u;
        double ibx = pts.Sum(x => Math.Pow(x.P.Y - yc, 2) * x.Ds);
        double iby = pts.Sum(x => Math.Pow(x.P.X - xc, 2) * x.Ds);
        Assert.Equal(u, v.Geometry.U, 9);
        Assert.Equal(xc, v.Geometry.Xc, 7);
        Assert.Equal(yc, v.Geometry.Yc, 7);
        Assert.Equal(ibx, v.Geometry.Ibx, 7);
        Assert.Equal(iby, v.Geometry.Iby, 7);
    }

    [Fact]
    public void RotatedRectangleWithRotatedEdge_SameAsAxisAligned()
    {
        double a = Math.PI / 6;
        PlanPoint Rot(double x, double y) => new(x * Math.Cos(a) - y * Math.Sin(a), x * Math.Sin(a) + y * Math.Cos(a));
        var plain = PunchingChecker.Check(Rect(0.4, 0.6, 0.2, 400, edges: new[] { Bottom(-0.35) }));
        var poly = new PunchingLoadArea.Polygon(new[] { Rot(-0.2, -0.3), Rot(0.2, -0.3), Rot(0.2, 0.3), Rot(-0.2, 0.3) });
        var rotated = PunchingChecker.Check(new PunchingInput(poly, 0.2, Rbt, new PunchingForces(400, 0, 0),
            Edges: new[] { new PunchingSlabEdge(Rot(0, -0.35), Rot(0, 1)) }));
        Assert.Equal(plain.Variants.Count, rotated.Variants.Count);
        foreach (var v in plain.Variants)
        {
            var w = V(rotated, v.Variant.Id);
            Assert.Equal(v.Geometry.U, w.Geometry.U, 10);
            Assert.Equal(v.Geometry.Ibx + v.Geometry.Iby, w.Geometry.Ibx + w.Geometry.Iby, 10);
            Assert.Equal(v.Utilization, w.Utilization, 10);
        }
    }

    [Fact]
    public void PolygonAwayFromOrigin_SameAsRectangle()
    {
        var poly = new PunchingLoadArea.Polygon(new PlanPoint[] { new(1, 2), new(1.4, 2), new(1.4, 2.6), new(1, 2.6) });
        var a = PunchingChecker.Check(Rect(0.4, 0.6, 0.2, 400, 30, 20));
        var b = PunchingChecker.Check(new PunchingInput(poly, 0.2, Rbt, new PunchingForces(400, 30, 20)));
        Assert.Equal(a.Utilization, b.Utilization, 12);
        Assert.Equal(V(a, "closed").MxStar, V(b, "closed").MxStar, 12);
    }

    [Fact]
    public void Edge_FarerThanHalfH0_ChecksClosedAndOpen_GoverningIsWorst()
    {
        // c = 0,15 > h0/2 = 0,0825: замкнутый помещается.
        var r = PunchingChecker.Check(Rect(0.25, 0.8, 0.165, 600, 30, 20, edges: new[] { Bottom(-(0.4 + 0.15)) }));
        var ids = r.Variants.Select(v => v.Variant.Id).ToList();
        Assert.Contains("closed", ids);
        Assert.Contains("open_edge_0", ids);
        Assert.Equal(r.Variants.Max(v => v.Utilization), r.Utilization);
        // Незамкнутый: Ly = hk + h0/2 + c.
        var open = V(r, "open_edge_0");
        Assert.Equal(0.25 + 0.165 + 2 * (0.8 + 0.0825 + 0.15), open.Geometry.U, 12);
    }

    [Fact]
    public void Edge_CloserThanHalfH0_OnlyOpen()
    {
        var r = PunchingChecker.Check(Rect(0.25, 0.8, 0.165, 600, edges: new[] { Bottom(-0.45) }));
        Assert.Equal(new[] { "open_edge_0" }, r.Variants.Select(v => v.Variant.Id));
        Assert.Contains(r.Warnings, w => w.Code == "closed_not_fit");
    }

    [Fact]
    public void Pylon_Applicability_Boundary()
    {
        double h0 = 0.165, bk = 0.25;
        var at = PunchingChecker.Check(Rect(bk, 2 * bk + h0, h0, 300));
        var over = PunchingChecker.Check(Rect(bk, 2 * bk + h0 + 0.001, h0, 300));
        Assert.DoesNotContain(at.Variants, v => v.Variant.Kind == PunchingVariantKind.PylonBroken);
        Assert.Contains(over.Variants, v => v.Variant.Kind == PunchingVariantKind.PylonBroken);
    }

    [Fact]
    public void Pylon_AlongX_SameAsAlongY()
    {
        var y = PunchingChecker.Check(Rect(0.25, 0.8, 0.165, 600, 30, 20));
        var x = PunchingChecker.Check(Rect(0.8, 0.25, 0.165, 600, 20, 30));
        Assert.Equal(V(y, "pylon_broken").Utilization, V(x, "pylon_broken").Utilization, 12);
    }

    [Fact]
    public void Pylon_AtEdge_Warns_AndWideCminWarns()
    {
        var atEdge = PunchingChecker.Check(Rect(0.25, 0.8, 0.165, 600, edges: new[] { Bottom(-0.6) }));
        Assert.Contains(atEdge.Warnings, w => w.Code == "pylon_at_edge_not_checked");
        var wide = PunchingChecker.Check(Rect(0.4, 1.6, 0.165, 600));
        Assert.Contains(wide.Warnings, w => w.Code == "pylon_cmin_h0_out_of_range");
    }

    [Fact]
    public void Reinforcement_CappedAtTwiceConcrete()
    {
        var sw = new PunchingShearReinforcement(170_000, 10 * Math.PI * 0.012 * 0.012 / 4, 0.05);
        var r = PunchingChecker.Check(Rect(0.4, 0.4, 0.2, 500, 10, 10, sw: sw));
        var v = V(r, "closed");
        Assert.True(v.SwCounted);
        Assert.Equal(2 * v.Fb, v.Fult, 12);
        Assert.Equal(2 * v.Mbx, v.Mxult, 12);
    }

    [Fact]
    public void MomentPart_LimitedToHalfForcePart()
    {
        var r = PunchingChecker.Check(Rect(0.4, 0.4, 0.2, 100, 200, 0));
        var v = V(r, "closed");
        Assert.Equal(0.5 * v.KF, v.KM, 12);
        Assert.Equal(1.5 * v.KF, v.Utilization, 12);
        Assert.Contains(r.Warnings, w => w.Code == "m_limited_half_kf");
    }

    [Fact]
    public void MomentsAreMloc_HalvesMoments()
    {
        var opt = new PunchingOptions(EccentricityMode.Ignore, EccentricityMode.Ignore);
        var full = PunchingChecker.Check(Rect(0.4, 0.4, 0.2, 500, 40, 20, options: opt));
        var half = PunchingChecker.Check(Rect(0.4, 0.4, 0.2, 500, options: opt,
            forces: new PunchingForces(500, 40, 20, MomentsAreMloc: true)));
        Assert.Equal(V(full, "closed").MxStar / 2, V(half, "closed").MxStar, 12);
        Assert.Equal(V(full, "closed").MyStar / 2, V(half, "closed").MyStar, 12);
    }

    [Fact]
    public void CounterPressure_SubtractedOverPyramidBase()
    {
        var r = PunchingChecker.Check(Rect(0.4, 0.4, 0.2, 500, forces: new PunchingForces(500, 0, 0, CounterPressure: 100)));
        Assert.Equal(0.8 * 0.8, r.BaseArea, 12);
        Assert.Equal(500 - 64, r.Fred, 9);
        var none = PunchingChecker.Check(Rect(0.4, 0.4, 0.2, 50, forces: new PunchingForces(50, 0, 0, CounterPressure: 100)));
        Assert.Equal(0, none.Utilization);
        Assert.Contains(none.Warnings, w => w.Code == "fred_nonpositive");
    }

    [Fact]
    public void AutoEccentricity_SignFollowsLoadSide()
    {
        var edge = new[] { Bottom(-0.25) };
        var auto = new PunchingOptions(EccentricityMode.Auto, EccentricityMode.Auto);
        var above = PunchingChecker.Check(Rect(0.4, 0.4, 0.2, 500, edges: edge, options: auto,
            forces: new PunchingForces(500, 10, 0, LoadSide: PunchingLoadSide.FromAbove)));
        var below = PunchingChecker.Check(Rect(0.4, 0.4, 0.2, 500, edges: edge, options: auto,
            forces: new PunchingForces(500, 10, 0, LoadSide: PunchingLoadSide.FromBelow)));
        var va = V(above, "open_edge_0");
        double yc = va.Geometry.Yc;
        Assert.True(yc > 0);
        Assert.Equal(Math.Abs(10 + 500 * yc), va.MxStar, 12);
        Assert.Equal(Math.Abs(10 - 500 * yc), V(below, "open_edge_0").MxStar, 12);
    }

    [Fact]
    public void StressSide_GivesNotSmallerModulus_AndNeedsAuto()
    {
        var edge = new[] { Bottom(-0.25) };
        var f = new PunchingForces(500, 30, 0, LoadSide: PunchingLoadSide.FromAbove);
        var min = PunchingChecker.Check(Rect(0.4, 0.4, 0.2, 500, edges: edge, forces: f,
            options: new PunchingOptions(EccentricityMode.Auto, EccentricityMode.Auto)));
        var side = PunchingChecker.Check(Rect(0.4, 0.4, 0.2, 500, edges: edge, forces: f,
            options: new PunchingOptions(EccentricityMode.Auto, EccentricityMode.Auto, Wb: SectionModulusMode.StressSide)));
        Assert.True(V(side, "open_edge_0").Wbx >= V(min, "open_edge_0").Wbx);
        var noAuto = PunchingChecker.Check(Rect(0.4, 0.4, 0.2, 500, edges: edge, forces: f,
            options: new PunchingOptions(Wb: SectionModulusMode.StressSide)));
        Assert.Contains(noAuto.Warnings, w => w.Code == "wb_stress_side_needs_auto");
        Assert.Equal(V(min, "open_edge_0").Wbx, V(noAuto, "open_edge_0").Wbx, 12);
    }

    [Fact]
    public void BeyondReinforcement_OuterContourConcreteOnly()
    {
        var sw = new PunchingShearReinforcement(170_000, 4 * Math.PI * 0.008 * 0.008 / 4, 0.06, ZoneExtent: 0.3);
        var r = PunchingChecker.Check(Rect(0.4, 0.4, 0.2, 500, sw: sw));
        var outer = V(r, "outer_closed");
        Assert.Equal(PunchingZone.BeyondReinforcement, outer.Variant.Zone);
        Assert.Equal(4 * (0.4 + 2 * 0.4), outer.Geometry.U, 12);   // смещение 0,3 + h0/2
        Assert.Equal(0, outer.Fsw);
        Assert.Equal(Rbt * 4.8 * 0.2, outer.Fult, 9);
    }

    [Fact]
    public void Hole_Within6h0_CutsContourBetweenTangents()
    {
        // Колонна 0,4×0,4, h0 = 0,2; отверстие 0,2×0,2 по оси на 2h0 от грани (y = 0,6…0,8).
        var hole = new PunchingHole(new PlanPoint[] { new(-0.1, 0.6), new(0.1, 0.6), new(0.1, 0.8), new(-0.1, 0.8) });
        var r = PunchingChecker.Check(Rect(0.4, 0.4, 0.2, 300, holes: new[] { hole }));
        var v = V(r, "closed");
        // Касательные через (±0,1; 0,6) пересекают верх контура y = 0,3 в x = ±0,05.
        Assert.Equal(4 * 0.6 - 0.1, v.Geometry.U, 12);
        Assert.Equal(0.1, v.Variant.Excluded.Sum(e => e.Length), 12);
        Assert.Contains(r.Warnings, w => w.Code == "hole_excluded");

        var far = new PunchingHole(new PlanPoint[] { new(-0.1, 1.6), new(0.1, 1.6), new(0.1, 1.8), new(-0.1, 1.8) });
        var r2 = PunchingChecker.Check(Rect(0.4, 0.4, 0.2, 300, holes: new[] { far }));
        Assert.Equal(2.4, V(r2, "closed").Geometry.U, 12);
    }

    [Fact]
    public void Hole_FlushWithPylonFace_CutsContourBetweenTangents()
    {
        // Вопрос из чата 17.11: отверстие вплотную к грани пилона. Пилон 1,2×0,3, h0 = 0,2; отверстие 0,3×0,3
        // у длинной грани (x = 0,2…0,5, y = 0,15…0,45) — касается площади, расстояние 0.
        var hole = new PunchingHole(new PlanPoint[] { new(0.2, 0.15), new(0.5, 0.15), new(0.5, 0.45), new(0.2, 0.45) });
        var r = PunchingChecker.Check(Rect(1.2, 0.3, 0.2, 300, holes: new[] { hole }));
        var v = V(r, "closed");
        // Контур 1,4×0,5. Касательные из центра: через (0,5; 0,15) — y = 0,3x, выходит на правую сторону x = 0,7 при
        // y = 0,21; через (0,2; 0,45) — y = 2,25x, на верхнюю y = 0,25 при x = 1/9. Вычет: 0,7 − 1/9 сверху и 0,04 справа.
        double cut = 0.7 - 1.0 / 9 + 0.04;
        Assert.Equal(cut, v.Variant.Excluded.Sum(e => e.Length), 9);
        Assert.Equal(2 * (1.4 + 0.5) - cut, v.Geometry.U, 9);
        Assert.Contains(r.Warnings, w => w.Code == "hole_excluded");
        // Остальные контуры у пилона (в т.ч. по СП 430) тоже теряют часть у отверстия, расчёт конечен.
        Assert.All(r.Variants.Where(x => x.Variant.Zone == PunchingZone.Main), x =>
        {
            Assert.True(x.Variant.Excluded.Sum(e => e.Length) > 0, x.Variant.Id);
            Assert.True(double.IsFinite(x.Utilization) && x.Utilization > 0, x.Variant.Id);
        });
    }

    [Fact]
    public void Hole_InfluenceZoneIs6hFullThickness_Not6h0()
    {
        // П. 8.1.46: «на расстоянии менее 6h» — полная толщина. Отверстие на 1,4 м от грани: 7h0 при h0 = 0,2,
        // но меньше 6h = 1,44 при h = 0,24 — вырезает часть контура.
        var far = new PunchingHole(new PlanPoint[] { new(-0.1, 1.6), new(0.1, 1.6), new(0.1, 1.8), new(-0.1, 1.8) });
        var input = Rect(0.4, 0.4, 0.2, 300, holes: new[] { far }) with { H = 0.24 };
        var r = PunchingChecker.Check(input);
        Assert.True(V(r, "closed").Geometry.U < 2.4 - 1e-6);
        Assert.Contains(r.Warnings, w => w.Code == "hole_excluded" && w.Text.Contains("6h:"));
        Assert.NotNull(PunchingContourBuilder.HoleTangents(input, far.Polygon));

        // При h = 0,23 (6h = 1,38 < 1,4) — не влияет.
        var r2 = PunchingChecker.Check(input with { H = 0.23 });
        Assert.Equal(2.4, V(r2, "closed").Geometry.U, 12);
    }

    [Fact]
    public void ConcavePolygon_UsesHullAndWarns()
    {
        var l = new PunchingLoadArea.Polygon(new PlanPoint[] { new(0, 0), new(0.6, 0), new(0.6, 0.2), new(0.2, 0.2), new(0.2, 0.6), new(0, 0.6) });
        var r = PunchingChecker.Check(new PunchingInput(l, 0.2, Rbt, new PunchingForces(300, 0, 0)));
        Assert.Contains(r.Warnings, w => w.Code == "concave_area_hull");
        Assert.Single(r.Variants);
    }

    [Fact]
    public void CustomContour_WithInactiveEdge_EqualsOpenEdge()
    {
        double h0 = 0.165, yE = -0.45;
        var auto = PunchingChecker.Check(Rect(0.25, 0.8, h0, 600, 30, 20, edges: new[] { Bottom(yE) }));
        var open = V(auto, "open_edge_0");
        double x = 0.125 + h0 / 2, yt = 0.4 + h0 / 2;
        var custom = new PunchingCustomContour(new PlanPoint[] { new(-x, yE), new(x, yE), new(x, yt), new(-x, yt) }, new[] { 0 });
        var r = PunchingChecker.Check(Rect(0.25, 0.8, h0, 600, 30, 20,
            forces: null, options: null) with { CustomContours = new[] { custom }, Edges = new[] { Bottom(yE) } });
        var c = V(r, "custom_0");
        Assert.Equal(open.Geometry.U, c.Geometry.U, 12);
        Assert.Equal(open.Geometry.Ibx, c.Geometry.Ibx, 12);
        Assert.Equal(open.Utilization, c.Utilization, 12);
    }

    /// <summary>
    /// Заданный основной контур (контур ЛИРЫ для сверки): контуры СП 63 на h0/2 и пилона не строятся, контур за
    /// поперечной арматурой — по-прежнему; без контуров пользователя опция не действует.
    /// </summary>
    [Fact]
    public void CustomReplacesMain_SkipsSp63MainContours_KeepsOuter()
    {
        var sw = new PunchingShearReinforcement(170_000, 4 * Math.PI * 0.008 * 0.008 / 4, 0.06, ZoneExtent: 0.3);
        // Контур «ЛИРЫ» — квадрат 0,62×0,62 (чуть больше OpenCS 0,6×0,6).
        var lira = new PunchingCustomContour(new PlanPoint[] { new(-0.31, -0.31), new(0.31, -0.31), new(0.31, 0.31), new(-0.31, 0.31) }, []);
        var opt = new PunchingOptions(CustomReplacesMain: true);
        var r = PunchingChecker.Check(Rect(0.4, 0.4, 0.2, 400, options: opt, sw: sw) with { CustomContours = [lira] });
        Assert.DoesNotContain(r.Variants, v => v.Variant.Id == "closed");
        Assert.Equal(4 * 0.62, V(r, "custom_0").Geometry.U, 12);
        Assert.Contains(r.Variants, v => v.Variant.Id == "outer_closed");
        Assert.Contains(r.Warnings, w => w.Code == "main_contour_custom");

        // Пилон 1,2×0,3: разорванного контура тоже нет.
        var pylon = PunchingChecker.Check(Rect(1.2, 0.3, 0.2, 400, options: opt) with { CustomContours = [lira] });
        Assert.DoesNotContain(pylon.Variants, v => v.Variant.Kind is PunchingVariantKind.PylonBroken or PunchingVariantKind.Closed);

        // Без контуров пользователя — как обычно.
        var plain = PunchingChecker.Check(Rect(0.4, 0.4, 0.2, 400, options: opt));
        Assert.Contains(plain.Variants, v => v.Variant.Id == "closed");
        Assert.DoesNotContain(plain.Warnings, w => w.Code == "main_contour_custom");
    }

    [Theory]
    [InlineData(0.9, PunchingStatus.ConcreteSufficient)]
    [InlineData(1.5, PunchingStatus.ReinforcementRequired)]
    [InlineData(2.5, PunchingStatus.NotEnsured)]
    public void Status_ByConcreteAndMaxReinforcement(double share, PunchingStatus expected)
    {
        double fb = Rbt * 2.4 * 0.2;
        var r = PunchingChecker.Check(Rect(0.4, 0.4, 0.2, share * fb));
        Assert.Equal(expected, r.Status);
        Assert.Equal(share, r.UtilizationConcreteOnly, 12);
        Assert.Equal(share / 2, r.UtilizationMaxReinforcement, 12);
    }

    /// <summary>
    /// Пособие по расчёту и конструированию зон продавливания (ППКЗД 2025-03-31-1, Кочнев, Куковякин),
    /// пример № 1 до установки арматуры: пилон 250×800 на фундаментной плите, h0 = 541 мм, B25,
    /// N = 286 тс, Mloc = 2,34 и 1,56 тс·м. В Пособии большая сторона a = 800 — вдоль X,
    /// его «Mx» и «Wbx» отвечают нашим My и Wby (момент относительно оси Y).
    /// </summary>
    [Fact]
    public void Posobie2025_Example1_ConcreteOnly()
    {
        const double tf = 9.80665;
        var input = new PunchingInput(new PunchingLoadArea.Rectangle(0.8, 0.25), 0.541, Rbt,
            new PunchingForces(286 * tf, 1.56 * tf, 2.34 * tf, MomentsAreMloc: true),
            Options: new PunchingOptions(Pylon: PylonMode.Off));
        var v = V(PunchingChecker.Check(input), "closed");
        Assert.Equal(4.264, v.Geometry.U, 12);
        Assert.Equal(1.113135939, v.Geometry.Iby, 9);       // их Ibx = 1 113 135 939 мм³
        Assert.Equal(1.660158, v.Wby, 6);                    // их Wbx = 1 660 158 мм²
        Assert.Equal(0.502, v.Geometry.Ibx, 3);               // их Iby ≈ 0,50 м³ (у них 65,92 округлено)
        Assert.Equal(1.17 * tf, v.MyStar, 9);                // 0,5·Mloc
        Assert.Equal(1.18, v.Utilization, 2);                // 1,16 + 0,01 + 0,01
    }

    [Fact]
    public void Galyautdinov_ReducedPerimeter()
    {
        var g = GalyautdinovPerimeter.Compute(0.25, 0.8, 0.165);
        Assert.Equal(2.0837441, g.URed, 6);
        Assert.Equal(0.7708, g.Chi, 4);
        var sq = GalyautdinovPerimeter.Compute(0.2, 0.2, 0.165);
        Assert.Equal(1.0, sq.Chi, 12);
        Assert.Equal(1.4498575, sq.URed, 5);   // 1449,8575 мм — округлённое значение ручного счёта
        Assert.Equal(1.0, GalyautdinovPerimeter.ReinforcementFactor(0.01), 12);
    }

    [Fact]
    public void PylonReduced_InformationalByDefault()
    {
        var opt = new PunchingOptions(PylonReducedPerimeter: true);
        var r = PunchingChecker.Check(Rect(0.25, 0.8, 0.165, 600, 30, 20, options: opt));
        var red = V(r, "pylon_reduced");
        Assert.False(red.Governs);
        Assert.Equal(600 / (Rbt * 2.0837441 * 0.165), red.Utilization, 5);
        Assert.Equal(Math.Max(V(r, "closed").Utilization, V(r, "pylon_broken").Utilization), r.Utilization, 12);

        var gov = PunchingChecker.Check(Rect(0.25, 0.8, 0.165, 600, 0, 0,
            options: opt with { PylonReducedGoverns = true }));
        Assert.Equal("pylon_reduced", gov.Governing!.Variant.Id);
    }

    [Fact]
    public void Json_HasPassedRatioAndDetails()
    {
        var r = PunchingChecker.Check(Rect(0.25, 0.8, 0.165, 600, 30, 20));
        var doc = JsonDocument.Parse(PunchingResultJson.Write(r)).RootElement;
        Assert.Equal(r.Passed, doc.GetProperty("passed").GetBoolean());
        Assert.Equal(r.Utilization, doc.GetProperty("ratio").GetDouble(), 12);
        Assert.Equal(r.Variants.Count, doc.GetProperty("details").GetArrayLength());
        Assert.Equal("pylon_broken", doc.GetProperty("governing").GetString());
    }

    [Fact]
    public void Trace_HasNormReferencesForEachVariant()
    {
        var sw = new PunchingShearReinforcement(170_000, 2 * Math.PI * 0.006 * 0.006 / 4, 0.05);
        var r = PunchingChecker.Check(Rect(0.25, 0.8, 0.165, 600, 30, 20, sw: sw));
        foreach (var v in r.Variants)
        {
            Assert.Contains(v.Trace, s => s.CodeReference!.Contains("8.99"));
            Assert.Contains(v.Trace, s => s.CodeReference!.Contains("8.88"));
            Assert.Contains(v.Trace, s => s.CodeReference!.Contains("8.91"));
            Assert.Contains(v.Trace, s => s.CodeReference == "(8.96)");
        }
    }
}
