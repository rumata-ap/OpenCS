using CScore.Sp63Punching;
using Xunit;

namespace CScore.Tests.Sp63Punching;

/// <summary>
/// Крестообразная поперечная арматура (п. 8.1.48, рис. 8.11г, п. 8.1.52) и ряды в пирамиде.
/// Эталон — Пособие ППКЗД 2025-03-31-1, пример № 1 (их X — наша Y).
/// </summary>
public class PunchingCrossReinforcementTests
{
    const double Rbt = 1050;     // кПа, B25
    const double Tf = 9.80665;   // кН в тс
    const double H0 = 0.541;

    static PunchingVariantResult V(PunchingResult r, string id) => r.Variants.Single(v => v.Variant.Id == id);

    static PunchingShearReinforcement PosobieCross(double bar = 0.010)
        => new(300_000, 0, 0,
            Rows: new PunchingStirrupRows(Math.PI * bar * bar / 4, 0.200, 0.150, 6),
            // Наша Y — их X: лучи вдоль нашей Y шириной 900 мм у длинных граней, вдоль X — 300 мм у торцов.
            Cross: new PunchingCrossArms(new PunchingCrossArm(0.300, 0.150), new PunchingCrossArm(0.900, 0.150)));

    static PunchingInput Posobie(PunchingShearReinforcement? sw = null, IReadOnlyList<PunchingSlabEdge>? edges = null)
        => new(new PunchingLoadArea.Rectangle(0.8, 0.25), H0, Rbt,
            new PunchingForces(286 * Tf, 1.56 * Tf, 2.34 * Tf, MomentsAreMloc: true),
            sw ?? PosobieCross(), edges, Options: new PunchingOptions(Pylon: PylonMode.Off));

    [Fact]
    public void Posobie2025_Example1_CrossReinforced()
    {
        var r = PunchingChecker.Check(Posobie());
        var v = V(r, "closed");
        Assert.Equal(4.264, v.Geometry.U, 12);
        var sw = v.Sw!;
        Assert.True(sw.Cross);
        Assert.Equal(3 * Math.PI * 0.01 * 0.01 / 4, sw.AswLine, 12);           // 3 стержня в полосе ±0,5h0
        Assert.Equal(471.238898, sw.QX, 5);                                     // 48,03 тс/м
        Assert.Equal(471.238898, sw.QY, 5);
        Assert.Equal(0.6, sw.Armed!.LX, 12);                                    // их 2·Lswy = 2·0,3
        Assert.Equal(1.8, sw.Armed.LY, 12);                                     // их 2·Lswx = 2·0,9
        Assert.Equal(904.778684, v.Fsw, 5);                                     // 92,21 тс (у них qsw округлено)
        Assert.True(v.SwCounted);
        // Их Wswx = 0,58 м² — наша Wswy; их Wswy = 0,73 м² — наша Wswx.
        Assert.Equal(0.8 * 471.238898 * 0.7232780025, v.Mswx, 4);              // 28,05 тс·м у них
        Assert.Equal(0.8 * 471.238898 * 0.5835080537, v.Mswy, 4);              // 22,28 тс·м
        // Кисп = 0,8430 + 0,0176 = 0,861 (у них 0,86 — с полным Mloc,x = 2,34 вместо 1,17, см. спеку среза 4).
        Assert.Equal(0.86059, v.Utilization, 4);
        Assert.Equal(PunchingStatus.ReinforcementRequired, r.Status);
    }

    [Fact]
    public void Posobie2025_Example1_BeyondCross_Octagon()
    {
        var r = PunchingChecker.Check(Posobie());
        var v = V(r, "outer_closed");
        Assert.Equal(PunchingZone.BeyondReinforcement, v.Variant.Zone);
        Assert.Equal(8, v.Variant.Contour.Elements.Count());
        Assert.Equal(9.0924321, v.Geometry.U, 6);                    // 9,092 м
        Assert.Equal(7.8021864, v.Geometry.Ibx, 6);                  // их Iby = 7,805 (катеты округлены)
        Assert.Equal(9.6337684, v.Geometry.Iby, 6);                  // их Ibx = 9,637
        Assert.Equal(5.7987264, v.Wbx, 6);                           // их Wby = 5,80
        Assert.Equal(5.9449357, v.Wby, 6);                           // их Wbx = 5,95
        Assert.Equal(0.548745, v.Utilization, 5);                     // у них 0,54
        Assert.False(v.SwCounted);
        Assert.Equal("closed", r.Governing!.Variant.Id);
    }

    [Fact]
    public void Rows_InPyramid_CountedBetweenThirdAndFullH0()
    {
        var c = PunchingSwLayout.CountRows(new PunchingStirrupRows(1e-4, 0.2, 0.15, 6), H0);
        Assert.Equal(3, c.Counted);               // 200, 350, 500 ≤ 541
        Assert.Equal(0, c.NearFace);
        Assert.False(c.WorstWindow);

        var near = PunchingSwLayout.CountRows(new PunchingStirrupRows(1e-4, 0.1, 0.15, 6), H0);
        Assert.Equal(1, near.NearFace);           // 100 < 180,3
        Assert.Equal(2, near.Counted);            // 250, 400; 100 — ближе h0/3, 550 > 541
    }

    [Fact]
    public void Rows_StepAboveThirdH0_WorstPyramid()
    {
        // sd = 0,25 > h0/3: ряды 0,2; 0,45; 0,7; 0,95 — окно h0 = 0,541, сдвиг до h0/2 — минимум 2 ряда.
        var c = PunchingSwLayout.CountRows(new PunchingStirrupRows(1e-4, 0.2, 0.25, 4), H0);
        Assert.True(c.WorstWindow);
        Assert.Equal(2, c.Counted);
    }

    [Fact]
    public void Uniform_WithRows_AswFromBand()
    {
        var sw = new PunchingShearReinforcement(300_000, 0, 0.15, Rows: new PunchingStirrupRows(7.85e-5, 0.2, 0.15, 6));
        var r = PunchingChecker.Check(Posobie(sw));
        var v = V(r, "closed");
        double q = 300_000 * 3 * 7.85e-5 / 0.15;
        Assert.Equal(q, v.Sw!.Qsw, 9);
        Assert.Equal(0.8 * q * 4.264, v.Fsw, 9);
        Assert.Equal(0.2 + 5 * 0.15, sw.EffectiveZoneExtent!.Value, 12);
        Assert.Contains(r.Variants, x => x.Variant.Id == "outer_closed");
        Assert.DoesNotContain(r.Warnings, w => w.Code == "sw_detailing_not_checked");
    }

    [Fact]
    public void Cross_AtEdge_ArmTowardEdgeOff_OpenVariantsUseArmedParts()
    {
        // Край плиты у грани −y (наша Y — длинная сторона луча 900 мм): луч в −y выключен.
        var sw = PosobieCross() with
        {
            Cross = new PunchingCrossArms(new PunchingCrossArm(0.300, 0.150),
                new PunchingCrossArm(0.900, 0.150, Positive: true, Negative: false))
        };
        var edge = new PunchingSlabEdge(new PlanPoint(0, -0.125 - 0.1), new PlanPoint(0, 1));
        var r = PunchingChecker.Check(Posobie(sw, [edge]));
        var open = V(r, "open_edge_0");
        Assert.Equal(0.9, open.Sw!.Armed!.LY, 9);                // только луч +y
        Assert.True(open.Sw.Armed.LX > 0);                       // лучи вдоль X пересекают ветви контура
        Assert.Contains(r.Variants, x => x.Variant.Id == "outer_open_edge_0");
        Assert.All(r.Variants.Where(x => x.Variant.Zone == PunchingZone.BeyondReinforcement), x => Assert.False(x.SwCounted));
    }

    [Fact]
    public void Cross_ArmsWiderThanContourSide_CornerSplitByDiagonal()
    {
        // Квадрат 0,4, h0 = 0,2: сторона контура 0,6; лучи шириной 1,0 — участки не двоятся в углах.
        var sw = new PunchingShearReinforcement(300_000, 1e-4, 0,
            Cross: new PunchingCrossArms(new PunchingCrossArm(1.0, 0.1), new PunchingCrossArm(1.0, 0.1)));
        var input = new PunchingInput(new PunchingLoadArea.Rectangle(0.4, 0.4), 0.2, Rbt, new PunchingForces(500, 0, 0), sw);
        var v = V(PunchingChecker.Check(input), "closed");
        Assert.Equal(v.Geometry.U, v.Sw!.Armed!.LX + v.Sw.Armed.LY, 9);
        Assert.Equal(v.Sw.Armed.LX, v.Sw.Armed.LY, 9);
    }

    [Fact]
    public void Cross_PylonBroken_ArmedPartsOfUContours()
    {
        // Пилон 0,25×1,2, h0 = 0,2: Lmax > 2Lmin + h0 — разорванный контур; лучи вдоль Y у торцов.
        var sw = new PunchingShearReinforcement(300_000, 1e-4, 0,
            Cross: new PunchingCrossArms(null, new PunchingCrossArm(0.3, 0.1)));
        var input = new PunchingInput(new PunchingLoadArea.Rectangle(0.25, 1.2), 0.2, Rbt, new PunchingForces(500, 0, 0), sw);
        var r = PunchingChecker.Check(input);
        var pylon = V(r, "pylon_broken");
        Assert.Equal(0.6, pylon.Sw!.Armed!.LY, 9);
        Assert.Equal(0, pylon.Sw.Armed.LX, 12);
    }

    [Fact]
    public void Cross_GeometryCache_SameResult()
    {
        var input = Posobie();
        var cache = PunchingGeometryCache.Prepare(input);
        var a = PunchingChecker.Check(input);
        var b = PunchingChecker.Check(input, includeTrace: false, geometry: cache);
        Assert.Equal(a.Utilization, b.Utilization, 12);
        Assert.Equal(V(a, "closed").Mswx, V(b, "closed").Mswx, 12);
    }

    [Fact]
    public void Cross_Trace_HasArmedLengthsAndSectionModuli()
    {
        var v = V(PunchingChecker.Check(Posobie()), "closed");
        var ids = v.Trace.Select(s => s.StepId).ToList();
        Assert.Contains("sp63.punching.closed.asw", ids);
        Assert.Contains("sp63.punching.closed.lswx", ids);
        Assert.Contains("sp63.punching.closed.lswy", ids);
        Assert.Contains("sp63.punching.closed.wswx", ids);
        var lx = v.Trace.Single(s => s.StepId.EndsWith(".lswx"));
        Assert.Equal(1.8, lx.Values["L"].Value, 9);   // Lsw,x — участки под лучами вдоль Y (рис. 8.11г)
    }

    [Fact]
    public void Cross_Scene_BarsAndArmedRoundTrip()
    {
        var input = Posobie();
        var scene = PunchingScene.From(input, PunchingChecker.Check(input));
        Assert.True(scene.SwCross);
        // Лучи: 2 × (3 линии) + 2 × (7 линий) по 6 рядов.
        Assert.Equal(6 * (2 * 3 + 2 * 7), scene.SwBars!.Count);
        var back = PunchingSceneJson.Read(System.Text.Json.JsonDocument.Parse(PunchingSceneJson.ToJson(scene)).RootElement);
        Assert.True(back.SwCross);
        Assert.Equal(scene.SwBars.Count, back.SwBars!.Count);
        var closed = back.Variants.Single(v => v.Id == "closed");
        Assert.Equal(2.4, closed.Armed!.Sum(e => e.Length), 9);
        var json = PunchingResultJson.Write(PunchingChecker.Check(input));
        Assert.Contains("\"layout\":\"cross\"", json);
        var sw = System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("details")[0].GetProperty("sw");
        Assert.Equal(1.8, sw.GetProperty("lsw_x").GetDouble(), 9);
    }

    [Fact]
    public void Detailing_Posobie_AllRequirementsMet()
    {
        var r = PunchingChecker.Check(Posobie());
        var d = r.Detailing!.ToDictionary(c => c.Code);
        Assert.Equal(PunchingDetailingState.Ok, d["first_row_min"].State);
        Assert.Equal(H0 / 3, d["first_row_min"].Limit, 12);              // 180,3 мм
        Assert.Equal(PunchingDetailingState.Ok, d["first_row_max"].State);
        Assert.Equal(PunchingDetailingState.Ok, d["row_step"].State);
        Assert.Equal(PunchingDetailingState.Ok, d["zone_width"].State);   // 950 ≥ 811,5
        Assert.Equal((0.25 + H0) / 4, d["line_step_x"].Limit, 12);        // их swy ≤ 790/4 = 197,5 мм
        Assert.Equal((0.8 + H0) / 4, d["line_step_y"].Limit, 12);        // их swx ≤ 1341/4 = 335,25 мм
        Assert.Equal(PunchingDetailingState.NotChecked, d["anchorage"].State);
        Assert.DoesNotContain(r.Warnings, w => w.Code == "sw_detailing_violated");
        Assert.Equal(3, r.Rows!.Counted);
    }

    [Fact]
    public void Detailing_StepUpToHalfH0_AllowedWithPyramidBars()
    {
        var sw = PosobieCross() with { Rows = new PunchingStirrupRows(7.85e-5, 0.2, 0.25, 4) };
        var r = PunchingChecker.Check(Posobie(sw));
        Assert.Equal(PunchingDetailingState.Allowed, r.Detailing!.Single(c => c.Code == "row_step").State);
        Assert.Contains(r.Warnings, w => w.Code == "sw_detailing_violated");
        Assert.True(r.Rows!.WorstWindow);
        Assert.Equal(2 * 7.85e-5, V(r, "closed").Sw!.AswLine, 12);
    }

    [Fact]
    public void Detailing_WithoutRows_NotChecked()
    {
        var sw = new PunchingShearReinforcement(300_000, 2e-4, 0.1);
        var r = PunchingChecker.Check(Posobie(sw));
        Assert.Empty(r.Detailing!);
        Assert.Contains(r.Warnings, w => w.Code == "sw_detailing_not_checked");
    }
}