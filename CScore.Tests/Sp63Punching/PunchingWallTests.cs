using System.Text.Json;
using System.Text.RegularExpressions;
using CScore.Sp63Punching;
using Xunit;

namespace CScore.Tests.Sp63Punching;

/// <summary>
/// Срез 5а: плиты у торцов и углов стен по СП 430.1325800.2018, п. 6.3.8 (рис. 6.1, 6.2 ред. с Изм. № 1).
/// Геометрия контуров сверена с рисунками нормы и Пособия ППКЗД 2025 (рис. 4.9–4.11), числа — ручным расчётом.
/// </summary>
public class PunchingWallTests
{
    const double Rbt = 1050;            // кПа, B25
    const double A1 = 0.2, H0 = 0.18;   // стена 200 мм, h0 = 180 мм
    const double D = H0 / 2;

    static PunchingInput End(double f, double dM = 0, double my = 0, IReadOnlyList<PunchingSlabEdge>? edges = null,
        PunchingShearReinforcement? sw = null, IReadOnlyList<PunchingHole>? holes = null, double q = 0)
        => new(new PunchingLoadArea.Rectangle(1, 1), H0, Rbt, new PunchingForces(f, dM, my, CounterPressure: q), sw, edges, holes,
            Wall: new PunchingWall(PunchingWallKind.End, A1));

    static PunchingInput Corner(double f, double a2, double mx = 0, double my = 0, double q = 0)
        => new(new PunchingLoadArea.Rectangle(1, 1), H0, Rbt, new PunchingForces(f, mx, my, CounterPressure: q),
            Wall: new PunchingWall(PunchingWallKind.Corner, A1, a2));

    static PunchingVariantResult V(PunchingResult r, string id) => r.Variants.Single(v => v.Variant.Id == id);

    static (PlanPoint A, PlanPoint B) Seg(ContourElement e) => e is LineElement l ? (l.A, l.B) : throw new InvalidOperationException();

    // ---------- Геометрия ----------

    [Fact]
    public void EndContour_EqualsPylonBrokenEnd()
    {
        // Пилон 1200×200 вдоль X (cmax > 2·cmin + h0): П у торца x = +0,6 — тот же, что у торца стены, сдвинутый на 0,6.
        double l = 1.2;
        var pylon = PunchingChecker.Check(new PunchingInput(new PunchingLoadArea.Rectangle(l, A1), H0, Rbt, new PunchingForces(100, 0, 0)));
        var pylonEnd = V(pylon, "pylon_broken").Variant.Contour.Elements.Select(Seg)
            .Where(s => s.A.X > 0 && s.B.X > 0).Select(s => (A: s.A - new PlanPoint(l / 2, 0), B: s.B - new PlanPoint(l / 2, 0))).ToList();
        var wall = V(PunchingChecker.Check(End(100)), "wall_end").Variant.Contour.Elements.Select(Seg).ToList();

        Assert.Equal(3, pylonEnd.Count);
        Assert.Equal(3, wall.Count);
        foreach (var w in wall)
            Assert.Contains(pylonEnd, p =>
                (Close(p.A, w.A) && Close(p.B, w.B)) || (Close(p.A, w.B) && Close(p.B, w.A)));

        static bool Close(PlanPoint a, PlanPoint b) => (a - b).Length < 1e-12;
    }

    [Fact]
    public void EndContour_ThreeEqualSidesAPlusH0()
    {
        // Рис. 6.1, а: торцевой участок и ветви равны a = a1 + h0.
        var g = V(PunchingChecker.Check(End(100)), "wall_end").Geometry;
        double a = A1 + H0;
        Assert.Equal(3 * a, g.U, 12);
        // Центр тяжести: торцевой отрезок при x = h0/2, ветви — середины при x = h0/2 − a/2.
        Assert.Equal((a * D + 2 * a * (D - a / 2)) / (3 * a), g.Xc, 12);
        Assert.Equal(0, g.Yc, 12);
        // Ibx = 2(a/2)³/3 (торцевой) + 2·a·(a/2)² (ветви).
        Assert.Equal(2 * Math.Pow(a / 2, 3) / 3 + 2 * a * Math.Pow(a / 2, 2), g.Ibx, 12);
    }

    [Fact]
    public void CornerContour_LengthAndCentroid()
    {
        // Рис. 6.2 (ред. с Изм. № 1): наружный Г с ветвями h0/2 + a1 + a2, диагональ длиной √(a1² + a2²).
        double a2 = 0.25, leg = A1 + a2;
        var g = V(PunchingChecker.Check(Corner(100, a2)), "wall_corner").Geometry;
        double diag = Math.Sqrt(A1 * A1 + a2 * a2);
        Assert.Equal(2 * (D + leg) + diag, g.U, 12);

        // Центр тяжести вручную: ветвь x = −d (середина y = (leg − d)/2), ветвь y = −d, диагональ — середина концов.
        double lLeg = leg + D, mid = (leg - D) / 2;
        double dx = (a2 + D + (a2 + D + A1)) / 2, dy = (A1 + D + a2 + (A1 + D)) / 2;
        double u = 2 * lLeg + diag;
        Assert.Equal((lLeg * -D + lLeg * mid + diag * dx) / u, g.Xc, 12);
        Assert.Equal((lLeg * mid + lLeg * -D + diag * dy) / u, g.Yc, 12);
    }

    [Fact]
    public void EdgeBehindEnd_ContourOnePrime_LengthsAPlusC()
    {
        // Рис. 6.1, б: край на c = 0,05 за торцом (ближе h0/2) — П обрезан, 1′ — два отрезка a1 + c.
        double c = 0.05;
        var r = PunchingChecker.Check(End(100, edges: [new PunchingSlabEdge(new PlanPoint(c, 0), new PlanPoint(-1, 0))]));
        var prime = V(r, "wall_end_edge_0");
        Assert.Equal(PunchingVariantKind.WallEndEdge, prime.Variant.Kind);
        Assert.Equal(2, prime.Variant.Contour.Pieces.Count);
        Assert.Equal(2 * (A1 + c), prime.Geometry.U, 12);
        foreach (var (a, b) in prime.Variant.Contour.Elements.Select(Seg))
        {
            Assert.Equal(A1 / 2 + D, Math.Abs(a.Y), 12);
            Assert.Equal(-A1, Math.Min(a.X, b.X), 12);
            Assert.Equal(c, Math.Max(a.X, b.X), 12);
        }
        // П без торцевого участка: ветви до края.
        Assert.Equal(2 * (A1 + H0 - (D - c)), V(r, "wall_end").Geometry.U, 12);
        Assert.Contains(r.Warnings, w => w.Code == "wall_contour_clipped");
        // 1′ короче — определяет он.
        Assert.Equal("wall_end_edge_0", r.Variants[r.GoverningIndex].Variant.Id);
    }

    [Fact]
    public void EdgeFarBehindEnd_BothContoursKept_WorstGoverns()
    {
        // Край на 0,6 м: П целый, 1′ = 2(a1 + 0,6) = 1,6 м > u(П) = 1,14 м — определяет П.
        var r = PunchingChecker.Check(End(100, edges: [new PunchingSlabEdge(new PlanPoint(0.6, 0), new PlanPoint(-1, 0))]));
        Assert.Equal(3 * (A1 + H0), V(r, "wall_end").Geometry.U, 12);
        Assert.Equal(2 * (A1 + 0.6), V(r, "wall_end_edge_0").Geometry.U, 12);
        Assert.Equal("wall_end", r.Variants[r.GoverningIndex].Variant.Id);
        Assert.DoesNotContain(r.Warnings, w => w.Code == "wall_contour_clipped");
    }

    [Fact]
    public void EdgeAlongWall_NoContourOnePrime()
    {
        // Край параллелен стене (не за торцом) — 1′ не строится.
        var r = PunchingChecker.Check(End(100, edges: [new PunchingSlabEdge(new PlanPoint(0, -0.5), new PlanPoint(0, 1))]));
        Assert.DoesNotContain(r.Variants, v => v.Variant.Kind == PunchingVariantKind.WallEndEdge);
    }

    [Fact]
    public void NoClosedOrPylonContours_AtWall()
    {
        var r = PunchingChecker.Check(End(100) with { Options = new PunchingOptions(PylonReducedPerimeter: true) });
        Assert.Equal(new[] { "wall_end" }, r.Variants.Select(v => v.Variant.Id));
        var c = PunchingChecker.Check(Corner(100, 0.2));
        Assert.Equal(new[] { "wall_corner" }, c.Variants.Select(v => v.Variant.Id));
    }

    // ---------- Ручной расчёт ----------

    [Fact]
    public void End_HandCalc_HalfDeltaMOnly()
    {
        // a = a1 + h0 = 0,38 м; u = 3a = 1,14 м.
        // Fb = Rbt·u·h0 = 1050·1,14·0,18 = 215,46 кН (8.88), (8.89).
        // Ibx = 2·0,19³/3 + 2·0,38·0,19² = 0,0045727 + 0,027436 = 0,0320087 м³; ymax = 0,19 → Wbx = 0,168467 м².
        // Mbx = Rbt·Wbx·h0 = 1050·0,168467·0,18 = 31,840 кН·м (8.94).
        // ΔM = 10 → M = ½ΔM = 5 кН·м; My = 7 не учитывается (момент только из плоскости стены).
        // K = F/Fb + M/Mbx = 150/215,46 + 5/31,84 = 0,6962 + 0,1570 = 0,8532.
        var r = PunchingChecker.Check(End(150, dM: 10, my: 7));
        var v = V(r, "wall_end");
        double a = A1 + H0, u = 3 * a;
        double ibx = 2 * Math.Pow(a / 2, 3) / 3 + 2 * a * Math.Pow(a / 2, 2), wbx = ibx / (a / 2);
        double fb = Rbt * u * H0, mbx = Rbt * wbx * H0;
        Assert.Equal(fb, v.Fb, 9);
        Assert.Equal(215.46, v.Fb, 9);
        Assert.Equal(wbx, v.Wbx, 12);
        Assert.Equal(mbx, v.Mbx, 9);
        Assert.Equal(5, v.MxStar, 12);
        Assert.Equal(0, v.MyStar, 12);
        Assert.Equal(150 / fb + 5 / mbx, v.Utilization, 12);
        Assert.Equal(0.8532, v.Utilization, 4);
        Assert.Equal(PunchingStatus.ConcreteSufficient, r.Status);
    }

    [Fact]
    public void End_DeltaMAlreadyMarkedAsMloc_SameResult()
    {
        var a = PunchingChecker.Check(End(150, dM: 10));
        var input = End(150, dM: 10);
        var b = PunchingChecker.Check(input with { Forces = input.Forces with { MomentsAreMloc = true } });
        Assert.Equal(a.Utilization, b.Utilization, 15);
    }

    [Fact]
    public void Corner_HandCalc_ForceOnly()
    {
        // a1 = 0,2, a2 = 0,25: u = 2(0,09 + 0,45) + √(0,04 + 0,0625) = 1,08 + 0,320156 = 1,400156 м.
        // Fb = 1050·1,400156·0,18 = 264,63 кН; K = F/Fb = 200/264,63 = 0,7558; моменты не учитываются.
        var r = PunchingChecker.Check(Corner(200, 0.25, mx: 40, my: 30));
        var v = V(r, "wall_corner");
        double u = 2 * (D + A1 + 0.25) + Math.Sqrt(A1 * A1 + 0.25 * 0.25);
        Assert.Equal(Rbt * u * H0, v.Fb, 9);
        Assert.Equal(0, v.MxStar, 12);
        Assert.Equal(0, v.MyStar, 12);
        Assert.Equal(200 / (Rbt * u * H0), v.Utilization, 12);
        Assert.Equal(0.7558, v.Utilization, 4);
    }

    [Fact]
    public void End_RowsOfStirrups_FswAlongWholeContour()
    {
        // Ряды Ø12 A240 (Rsw = 170 МПа), шаг по контуру 100 мм, s1 = 60, sd = 60, 4 ряда.
        var sw = new PunchingShearReinforcement(170_000, 0, 0.10, Rows: new PunchingStirrupRows(Math.PI * 0.012 * 0.012 / 4, 0.06, 0.06, 4));
        var r = PunchingChecker.Check(End(300, dM: 10, sw: sw));
        var v = V(r, "wall_end");
        Assert.True(v.SwCounted);
        Assert.True(r.Rows!.Counted > 0);
        Assert.Equal(0.8 * v.Sw!.Qsw * v.Geometry.U, v.Fsw, 9);   // (8.90)
        // Контур за зоной арматуры: П на отступе ext + h0/2, стороны a1 + 2d.
        double ext = 0.06 + 3 * 0.06, d = ext + D;
        var outer = V(r, "outer_wall_end");
        Assert.Equal(PunchingZone.BeyondReinforcement, outer.Variant.Zone);
        Assert.Equal(3 * (A1 + 2 * d), outer.Geometry.U, 12);
        // Стержни на рисунке — по П каждого ряда.
        var bars = PunchingSwLayout.BarPoints(End(300, sw: sw));
        Assert.NotEmpty(bars);
        Assert.All(bars, p => Assert.True(p.X <= 0.06 + 3 * 0.06 + 1e-9));
    }

    [Fact]
    public void Cross_AtWall_IgnoredWithWarning()
    {
        var sw = new PunchingShearReinforcement(170_000, 0, 0.10,
            Cross: new PunchingCrossArms(new PunchingCrossArm(0.3, 0.1), new PunchingCrossArm(0.3, 0.1)));
        var r = PunchingChecker.Check(End(150, sw: sw));
        Assert.Contains(r.Warnings, w => w.Code == "wall_sw_cross_ignored");
        Assert.Equal(0, V(r, "wall_end").Fsw);
        // Через кэш геометрии пакета — то же предупреждение.
        var input = End(150, sw: sw);
        var cached = PunchingChecker.Check(input, false, PunchingGeometryCache.Prepare(input));
        Assert.Contains(cached.Warnings, w => w.Code == "wall_sw_cross_ignored");
    }

    [Fact]
    public void HoleAtEnd_CutsEndSegmentByTangents()
    {
        // Отверстие 200×200 за торцом: x ∈ [0,15; 0,35], |y| ≤ 0,1. Касательные из центра площадки a1×a1 (−0,1; 0)
        // через углы (0,15; ±0,1): наклон 0,1/0,25 = 0,4; на торцевом участке x = 0,09 вырезается |y| ≤ 0,4·0,19 = 0,076.
        var hole = new PunchingHole([new(0.15, -0.1), new(0.35, -0.1), new(0.35, 0.1), new(0.15, 0.1)]);
        var v = V(PunchingChecker.Check(End(100, holes: [hole])), "wall_end");
        Assert.Equal(3 * (A1 + H0) - 2 * 0.076, v.Geometry.U, 12);
        Assert.Equal(2 * 0.076, v.Variant.Excluded.Sum(e => e.Length), 12);
    }

    [Fact]
    public void PyramidBase_EndAndCorner_CounterPressure()
    {
        // Торец: основание (a1 + 2h0)², угол: (a1 + a2 + 2h0)² − a1·a2/2.
        double q = 20;
        var e = PunchingChecker.Check(End(150, q: q));
        Assert.Equal(Math.Pow(A1 + 2 * H0, 2), e.BaseArea, 12);
        Assert.Equal(150 - q * e.BaseArea, e.Fred, 12);
        var c = PunchingChecker.Check(Corner(150, 0.25, q: q));
        Assert.Equal(Math.Pow(A1 + 0.25 + 2 * H0, 2) - A1 * 0.25 / 2, c.BaseArea, 12);
        // Край за торцом обрезает основание.
        var cut = PunchingChecker.Check(End(150, q: q, edges: [new PunchingSlabEdge(new PlanPoint(0.05, 0), new PlanPoint(-1, 0))]));
        Assert.Equal((A1 + H0 + 0.05) * (A1 + 2 * H0), cut.BaseArea, 12);
    }

    [Fact]
    public void LoadAreaFromCaller_IgnoredAtWall()
    {
        var a = PunchingChecker.Check(End(150, dM: 10));
        var b = PunchingChecker.Check(End(150, dM: 10) with { LoadArea = new PunchingLoadArea.Circle(0.9) });
        Assert.Equal(a.Utilization, b.Utilization, 15);
        Assert.Throws<ArgumentException>(() => PunchingChecker.Check(End(150) with { Wall = new PunchingWall(PunchingWallKind.End, 0) }));
        Assert.Throws<ArgumentException>(() => PunchingChecker.Check(Corner(150, 0.2) with { Wall = new PunchingWall(PunchingWallKind.Corner, 0.2, -0.1) }));
    }

    // ---------- Сцена, JSON, трассировка ----------

    [Fact]
    public void Scene_WallVariants_RoundTripThroughJson()
    {
        var input = End(150, dM: 10, edges: [new PunchingSlabEdge(new PlanPoint(0.3, 0), new PlanPoint(-1, 0))]);
        var scene = PunchingScene.From(input, PunchingChecker.Check(input));
        Assert.Equal(PunchingWallGeometry.AreaPolygon(input.Wall!, H0), scene.AreaPolygon);
        Assert.Equal(new PlanPoint(-A1 / 2, 0), scene.AreaCentroid);
        using var doc = JsonDocument.Parse(PunchingSceneJson.ToJson(scene));
        var back = PunchingSceneJson.Read(doc.RootElement);
        Assert.Equal(scene.Variants.Select(v => v.Kind), back.Variants.Select(v => v.Kind));
        Assert.Contains(back.Variants, v => v.Kind == PunchingVariantKind.WallEnd);
        Assert.Contains(back.Variants, v => v.Kind == PunchingVariantKind.WallEndEdge);
    }

    [Fact]
    public void Trace_WallStepsSubstituteAllTokens()
    {
        var token = new Regex(@"\{(?<n>[A-Za-z0-9_]+)\}");
        foreach (var r in new[] { PunchingChecker.Check(End(150, dM: 10)), PunchingChecker.Check(Corner(150, 0.25)) })
        {
            var v = r.Variants[r.GoverningIndex];
            var ids = v.Trace.Select(s => s.StepId.Split('.').Last()).ToList();
            Assert.Contains("wall", ids);
            Assert.DoesNotContain("ex", ids);
            Assert.DoesNotContain("my", ids);
            foreach (var s in v.Trace)
                foreach (Match m in token.Matches((s.SubstitutionLatexTemplate ?? "") + (s.ResultLatexTemplate ?? "")))
                    Assert.True(s.Values.ContainsKey(m.Groups["n"].Value), $"{s.StepId}: {m.Value}");
        }
        var end = PunchingChecker.Check(End(150, dM: 10));
        var wall = end.Variants[0].Trace.Single(s => s.StepId.EndsWith(".wall"));
        Assert.Equal(10, wall.Values["dM"].Value, 12);
        Assert.Equal(5, wall.Values["Mx"].Value, 12);
    }
}
