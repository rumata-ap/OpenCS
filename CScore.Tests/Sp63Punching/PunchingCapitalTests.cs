using System.Text.Json;
using CScore.Sp63Punching;
using Xunit;

namespace CScore.Tests.Sp63Punching;

/// <summary>
/// Капитель: у колонны — h0 + hc, за капителью — грузовая площадь по грани капители и h0 плиты (пп. 8.1.46–8.1.52).
/// Ручной пример: колонна 0,4×0,4, h = 0,2, h0 = 0,17, капитель 1,2×1,2, hc = 0,15, Rbt = 1050 кПа.
/// </summary>
public class PunchingCapitalTests
{
    const double Rbt = 1050;

    static PunchingInput Node(double f, PunchingCapital? cap, double mx = 0, double my = 0, double q = 0,
        PunchingOptions? options = null, PunchingShearReinforcement? sw = null)
        => new(new PunchingLoadArea.Rectangle(0.4, 0.4), 0.17, Rbt,
            new PunchingForces(f, mx, my, CounterPressure: q), sw, Options: options, H: 0.2, Capital: cap);

    static PunchingCapital Cap(double bx = 1.2, double by = 1.2, double hc = 0.15, double dx = 0, double dy = 0)
        => new(new PunchingLoadArea.Rectangle(bx, by), hc, new PlanPoint(dx, dy));

    static PunchingVariantResult V(PunchingResult r, string id) => r.Variants.Single(v => v.Variant.Id == id);

    [Fact]
    public void TwoChecks_ColumnWithCapitalDepth_AndBeyondCapitalWithSlabDepth()
    {
        var r = PunchingChecker.Check(Node(900, Cap()));
        var col = V(r, "closed");
        var cap = V(r, "cap_closed");
        Assert.Equal(PunchingZone.Main, col.Variant.Zone);
        Assert.Equal(PunchingZone.BeyondCapital, cap.Variant.Zone);

        // У колонны: h0к = 0,17 + 0,15 = 0,32; u = 4·(0,4 + 0,32) = 2,88.
        Assert.Equal(0.32, col.H0, 12);
        Assert.Equal(2.88, col.Geometry.U, 12);
        Assert.Equal(Rbt * 2.88 * 0.32, col.Fb, 9);
        // За капителью: h0 = 0,17; u = 4·(1,2 + 0,17) = 5,48.
        Assert.Equal(0.17, cap.H0, 12);
        Assert.Equal(5.48, cap.Geometry.U, 12);
        Assert.Equal(Rbt * 5.48 * 0.17, cap.Fb, 9);

        Assert.Equal(900 / (Rbt * 2.88 * 0.32), col.Utilization, 9);
        Assert.Equal(900 / (Rbt * 5.48 * 0.17), cap.Utilization, 9);
        Assert.True(col.Governs);
        Assert.Equal(col.Utilization, r.Utilization, 12);
        Assert.Equal(PunchingStatus.ConcreteSufficient, r.Status);
        Assert.Contains(col.Trace, s => s.TitleKey == "Punching_Trace_CapitalColumn");
        Assert.Contains(cap.Trace, s => s.TitleKey == "Punching_Trace_CapitalBeyond");
    }

    [Fact]
    public void FCapital_GoesOnlyToBeyondCapitalVariants()
    {
        // Сила у колонны 900, за капителью — своя 850 (у МКЭ — со своим основанием пирамиды).
        var input = Node(900, Cap()) with { Forces = new PunchingForces(900, 0, 0, FCapital: 850) };
        var r = PunchingChecker.Check(input, includeTrace: true);
        Assert.Equal(900, V(r, "closed").Fred, 12);
        Assert.Equal(850, V(r, "cap_closed").Fred, 12);
        Assert.Equal(850 / (Rbt * 5.48 * 0.17), V(r, "cap_closed").Utilization, 9);
        Assert.Contains(V(r, "cap_closed").Trace, s => s.TitleKey == "Punching_Trace_FCapital");
        Assert.DoesNotContain(V(r, "closed").Trace, s => s.TitleKey == "Punching_Trace_FCapital");
        // Отпор вычитается из своей силы каждой проверки.
        var q = PunchingChecker.Check(input with { Forces = new PunchingForces(900, 0, 0, CounterPressure: 10, FCapital: 850) });
        Assert.Equal(850 - 10 * 2.3716, V(q, "cap_closed").Fred, 9);
        Assert.Equal(900 - 10 * 1.0816, V(q, "closed").Fred, 9);
    }

    [Fact]
    public void CounterPressure_EachCheckUsesItsOwnPyramidBase()
    {
        var r = PunchingChecker.Check(Node(900, Cap(), q: 10));
        // Основание у колонны: (0,4 + 2·0,32)² = 1,0816; за капителью: (1,2 + 2·0,17)² = 2,3716.
        Assert.Equal(900 - 10 * 1.0816, V(r, "closed").Fred, 9);
        Assert.Equal(900 - 10 * 2.3716, V(r, "cap_closed").Fred, 9);
        Assert.Equal(2.3716, V(r, "cap_closed").BaseArea, 9);
        Assert.Equal(900 - 10 * 1.0816, r.Fred, 9);
    }

    [Fact]
    public void OffsetCapital_EccentricityFromColumnCentre()
    {
        // Капитель 1,6×1,2 смещена на 0,2 по X: центр контура за капителью — в (0,2; 0), e = 0,2.
        var o = new PunchingOptions(EccentricityMode.Loading, EccentricityMode.Loading);
        var r = PunchingChecker.Check(Node(500, Cap(1.6, 1.2, dx: 0.2), options: o));
        var cap = V(r, "cap_closed");
        Assert.Equal(0.2, cap.Geometry.Xc, 9);
        Assert.Equal(500 * 0.2, cap.MyStar, 9);
        Assert.Equal(0, V(r, "closed").MyStar, 9);
    }

    [Fact]
    public void SmallCapital_ColumnContourOutside_UsesSlabDepthAndWarns()
    {
        // Выступ 0,1 < h0к/2 = 0,16: у колонны — h0 плиты.
        var r = PunchingChecker.Check(Node(300, Cap(0.6, 0.6)));
        Assert.Equal(0.17, V(r, "closed").H0, 12);
        Assert.Contains(r.Warnings, w => w.Code == "capital_too_small");
    }

    [Fact]
    public void BeyondCapitalFails_StatusNotEnsured_DesignFlagsIt()
    {
        // F = 1500: у колонны нужна арматура (Fb = 967,7), за капителью Fb = 978,2 — не проходит по бетону.
        var input = Node(1500, Cap());
        var r = PunchingChecker.Check(input);
        Assert.Equal(PunchingStatus.NotEnsured, r.Status);
        Assert.Contains(r.Warnings, w => w.Code == "capital_beyond_fails");
        var d = PunchingDesign.Solve(input);
        Assert.True(d.CapitalBeyondFails);
        Assert.Equal(PunchingDesignState.ByCalculation, d.State);
    }

    [Fact]
    public void GeometryCache_GivesSameResult()
    {
        var input = Node(900, Cap(1.6, 1.2, dx: 0.2), mx: 50, my: 30);
        var a = PunchingChecker.Check(input, includeTrace: false);
        var b = PunchingChecker.Check(input, includeTrace: false, geometry: PunchingGeometryCache.Prepare(input));
        Assert.Equal(a.Variants.Select(v => (v.Variant.Id, v.Utilization)), b.Variants.Select(v => (v.Variant.Id, v.Utilization)));
    }

    [Fact]
    public void Validation_CapitalMustContainColumn_NoWall_PositiveHeight()
    {
        Assert.Throws<ArgumentException>(() => PunchingChecker.Check(Node(300, Cap(0.3, 0.6))));
        Assert.Throws<ArgumentException>(() => PunchingChecker.Check(Node(300, Cap(hc: 0))));
        var round = new PunchingCapital(new PunchingLoadArea.Circle(1.2), 0.15, new PlanPoint(0.1, 0));
        Assert.Throws<ArgumentException>(() => PunchingChecker.Check(Node(300, round)));
    }

    [Fact]
    public void RoundColumn_RoundCapital()
    {
        // Колонна D = 0,4, капитель D = 1,2: u = π(0,4 + 0,32) и π(1,2 + 0,17).
        var input = new PunchingInput(new PunchingLoadArea.Circle(0.4), 0.17, Rbt, new PunchingForces(500, 0, 0),
            H: 0.2, Capital: new PunchingCapital(new PunchingLoadArea.Circle(1.2), 0.15));
        var r = PunchingChecker.Check(input);
        Assert.Equal(Math.PI * 0.72, V(r, "closed").Geometry.U, 9);
        Assert.Equal(Math.PI * 1.37, V(r, "cap_closed").Geometry.U, 9);
    }

    [Fact]
    public void SceneJson_RoundTripsCapital()
    {
        var input = Node(900, Cap(1.6, 1.2, dx: 0.2));
        var scene = PunchingScene.From(input, PunchingChecker.Check(input));
        Assert.Equal(0.32, scene.H0, 12);
        var back = PunchingSceneJson.Read(JsonDocument.Parse(PunchingSceneJson.ToJson(scene)).RootElement);
        Assert.Equal(4, back.CapitalOutline().Count);
        Assert.Equal(1.0, back.CapitalOutline().Max(p => p.X), 9);
        Assert.Contains(back.Variants, v => v.Zone == PunchingZone.BeyondCapital);
    }

    [Fact]
    public void BeyondReinforcementContour_OutsideCapital_UsesSlabDepth()
    {
        // Зона арматуры 0,5 от грани: контур за ней — на 0,5 + 0,16 от грани колонны, капитель 1,6 (выступ 0,6) — выходит.
        var sw = new PunchingShearReinforcement(Rsw: 170000, Asw: 2 * 0.785e-4, Sw: 0.1, ZoneExtent: 0.5);
        var r = PunchingChecker.Check(Node(1000, Cap(1.6, 1.6), sw: sw));
        Assert.Equal(0.17, V(r, "outer_closed").H0, 12);
        Assert.Contains(r.Warnings, w => w.Code == "capital_sw_beyond");

        // Зона 0,2: контур на 0,36 от грани — в капители, h0к.
        var r2 = PunchingChecker.Check(Node(1000, Cap(1.6, 1.6), sw: sw with { ZoneExtent = 0.2 }));
        Assert.Equal(0.32, V(r2, "outer_closed").H0, 12);
        Assert.DoesNotContain(r2.Warnings, w => w.Code == "capital_sw_beyond");
    }

    [Fact]
    public void ShearReinforcement_CountsOnlyAtColumn()
    {
        var sw = new PunchingShearReinforcement(Rsw: 170000, Asw: 2 * 0.785e-4, Sw: 0.1);
        var r = PunchingChecker.Check(Node(1000, Cap(1.6, 1.6), sw: sw));
        Assert.NotNull(V(r, "closed").Sw);
        Assert.Null(V(r, "cap_closed").Sw);
    }
}
