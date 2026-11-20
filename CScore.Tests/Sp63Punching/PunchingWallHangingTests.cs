using System.Text.Json;
using CScore.Sp63Punching;
using Xunit;

namespace CScore.Tests.Sp63Punching;

/// <summary>
/// Плита, подвешенная к торцу вышележащей стены (СП 430.1325800.2018, п. 6.3.8, рис. 6.3): П-контур на h0/2 от крайних
/// подвешивающих стержней. Ветви вдоль стены — a1 (по рисунку) или a1 + h0 (как у торца, рис. 6.1). Числа — ручной расчёт.
/// </summary>
public class PunchingWallHangingTests
{
    const double Rbt = 1050;            // кПа, B25
    const double A1 = 0.2, H0 = 0.18;   // стена 200 мм, h0 = 180 мм
    const double Bs = 0.1, Cs = 0.05;   // ряды стержней на ±50 мм от оси стены, крайний стержень в 50 мм от торца

    static PunchingInput Hanging(double f, double dM = 0, PunchingHangerLegs legs = PunchingHangerLegs.WallThickness,
        IReadOnlyList<PunchingSlabEdge>? edges = null, double q = 0, double bs = Bs, double cs = Cs)
        => new(new PunchingLoadArea.Rectangle(1, 1), H0, Rbt, new PunchingForces(f, dM, 0, CounterPressure: q), Edges: edges,
            Wall: new PunchingWall(PunchingWallKind.End, A1, Hanger: new PunchingHanger(bs, cs, legs)));

    static PunchingVariantResult V(PunchingResult r, string id) => r.Variants.Single(v => v.Variant.Id == id);

    [Fact]
    public void Contour_FromOuterBars_LegsWallThickness_HandCalc()
    {
        // W = bs/2 + h0/2 = 0,05 + 0,09 = 0,14; торцевой отрезок при x = −cs + h0/2 = 0,04, длина 2W = 0,28;
        // ветви a1 = 0,2 от x = 0,04 до −0,16. u = 0,28 + 2·0,2 = 0,68 м.
        // Fb = 1050·0,68·0,18 = 128,52 кН. Ibx = 2·0,14³/3 + 2·0,2·0,14² = 0,0018293 + 0,00784 = 0,0096693 м³,
        // Wbx = Ibx/0,14 = 0,069067 м², Mbx = 1050·0,069067·0,18 = 13,054 кН·м.
        // F = 60, ΔM = 4 → M = 2: K = 60/128,52 + 2/13,054 = 0,4669 + 0,1532 = 0,6201.
        var r = PunchingChecker.Check(Hanging(60, dM: 4));
        var v = V(r, "wall_hanging");
        Assert.Equal(PunchingVariantKind.WallHanging, v.Variant.Kind);
        Assert.Equal(0.68, v.Geometry.U, 12);
        Assert.Equal((0.28 * 0.04 + 0.4 * -0.06) / 0.68, v.Geometry.Xc, 12);
        Assert.Equal(0, v.Geometry.Yc, 12);
        double ibx = 2 * Math.Pow(0.14, 3) / 3 + 2 * 0.2 * 0.14 * 0.14;
        Assert.Equal(ibx, v.Geometry.Ibx, 12);
        Assert.Equal(128.52, v.Fb, 9);
        Assert.Equal(Rbt * ibx / 0.14 * H0, v.Mbx, 9);
        Assert.Equal(2, v.MxStar, 12);
        Assert.Equal(0, v.MyStar, 12);
        Assert.Equal(0.6201, v.Utilization, 4);
        Assert.DoesNotContain(r.Variants, x => x.Variant.Id == "wall_end");
    }

    [Fact]
    public void Contour_LegsAsWallEnd_APlusH0()
    {
        // Ветви a1 + h0 = 0,38: u = 0,28 + 2·0,38 = 1,04 м.
        var v = V(PunchingChecker.Check(Hanging(60, legs: PunchingHangerLegs.AsWallEnd)), "wall_hanging");
        Assert.Equal(1.04, v.Geometry.U, 12);
        Assert.Equal(60 / (Rbt * 1.04 * H0), v.Utilization, 12);
    }

    [Fact]
    public void BarsAtWallFaces_AsWallEnd_SameAsPlainEnd()
    {
        // Стержни на гранях и в торце (bs = a1, cs = 0) с ветвями a1 + h0 — тот же контур, что у обычного торца.
        var plain = PunchingChecker.Check(new PunchingInput(new PunchingLoadArea.Rectangle(1, 1), H0, Rbt, new PunchingForces(100, 6, 0),
            Wall: new PunchingWall(PunchingWallKind.End, A1)));
        var wall = new PunchingWall(PunchingWallKind.End, A1, Hanger: new PunchingHanger(A1 - 1e-12, 0, PunchingHangerLegs.AsWallEnd));
        var hung = PunchingChecker.Check(new PunchingInput(new PunchingLoadArea.Rectangle(1, 1), H0, Rbt, new PunchingForces(100, 6, 0), Wall: wall));
        Assert.Equal(V(plain, "wall_end").Geometry.U, V(hung, "wall_hanging").Geometry.U, 9);
        Assert.Equal(V(plain, "wall_end").Utilization, V(hung, "wall_hanging").Utilization, 9);
        Assert.Equal(plain.BaseArea, hung.BaseArea, 9);
    }

    [Fact]
    public void PyramidBase_FromBars()
    {
        // Основание на h0 от стержней: W = 0,05 + 0,18 = 0,23, торец x = 0,13, ветви a1 + h0/2·2 = 0,38 → x0 = −0,25.
        double q = 20;
        var r = PunchingChecker.Check(Hanging(60, q: q));
        Assert.Equal(0.38 * 0.46, r.BaseArea, 12);
        Assert.Equal(60 - q * 0.38 * 0.46, r.Fred, 12);
    }

    [Fact]
    public void EdgeBehindEnd_ContourOnePrime_FromBarRows()
    {
        // Край за торцом в 0,3 м: контур 1′ — две линии y = ±0,14 от x = −cs − a1 = −0,25 до края, по 0,55 м.
        var r = PunchingChecker.Check(Hanging(60, edges: [new PunchingSlabEdge(new PlanPoint(0.3, 0), new PlanPoint(-1, 0))]));
        Assert.Equal(2 * 0.55, V(r, "wall_end_edge_0").Geometry.U, 12);
        Assert.Contains(r.Variants, x => x.Variant.Id == "wall_hanging");
    }

    [Fact]
    public void SingleRow_OnWallAxis()
    {
        // bs = 0: W = h0/2, торцевой отрезок h0.
        var v = V(PunchingChecker.Check(Hanging(60, bs: 0)), "wall_hanging");
        Assert.Equal(H0 + 2 * A1, v.Geometry.U, 12);
    }

    [Fact]
    public void BarsOutsideWall_Rejected()
    {
        Assert.Throws<ArgumentException>(() => PunchingChecker.Check(Hanging(60, bs: A1)));
        Assert.Throws<ArgumentException>(() => PunchingChecker.Check(Hanging(60, cs: -0.01)));
    }

    [Fact]
    public void Scene_HangerRods_JsonRoundTrip_TraceFigure63()
    {
        var input = Hanging(60, dM: 4);
        var r = PunchingChecker.Check(input);
        var scene = PunchingScene.From(input, r);
        Assert.Equal([new PlanPoint(-Cs, -Bs / 2), new PlanPoint(-Cs, Bs / 2)], scene.HangerRods);
        Assert.Equal(new PlanPoint(0, 0), scene.AxesCenter);
        using var doc = JsonDocument.Parse(PunchingSceneJson.ToJson(scene));
        var back = PunchingSceneJson.Read(doc.RootElement);
        Assert.Equal(scene.HangerRods, back.HangerRods);
        Assert.Contains(back.Variants, v => v.Kind == PunchingVariantKind.WallHanging);

        var step = V(r, "wall_hanging").Trace.Single(s => s.StepId.EndsWith(".wall"));
        Assert.Contains("рис. 6.3", step.CodeReference);
        Assert.Equal(2, step.Values["Mx"].Value, 12);
        Assert.DoesNotContain(V(r, "wall_hanging").Trace, s => s.StepId.EndsWith(".ex") || s.StepId.EndsWith(".my"));

        // Обычный торец стержней на рисунке не имеет.
        var plain = PunchingScene.FromInput(input with { Wall = new PunchingWall(PunchingWallKind.End, A1) });
        Assert.Null(plain.HangerRods);
    }
}
