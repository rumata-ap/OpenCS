using CScore;
using CScore.Sp63Shear;
using Xunit;

namespace CScore.Tests.Sp63Shear;

/// <summary>Перебор наклонных сечений и сборка результата.</summary>
public sealed class ShearInclinedCheckerTests
{
    const double H0 = 0.55;
    const double B = 0.30;
    const double Rbt = 1_050.0;

    [Fact]
    public void Check_ConstantProfile_CriticalProjectionMatchesAnalyticalOptimum()
    {
        // C* = h0·√(φb2·Rbt·b/(φsw·qsw)) при qsw = 400 кН/м попадает внутрь [h0; 2h0]
        var input = Input(qsw: 400.0);
        var profile = new ConstantProfile(q: 150.0, m: 0.0, n: 0.0, supportDistance: 0.0);

        var result = ShearInclinedChecker.Check(input, profile, Geometry(), direction: -1);

        double analytical = H0 * Math.Sqrt(
            ShearFormulas.PhiB2 * Rbt * B / (ShearFormulas.PhiSw * 400.0));
        var detail = result.Details.Single(d => d.Formula == "8.56");

        Assert.InRange(detail.Variables["C"], analytical - 2.0 * input.ProjectionStepOrAuto(),
                                              analytical + 2.0 * input.ProjectionStepOrAuto());
    }

    [Fact]
    public void Check_ProducesFiveDetailsWhenReinforcementPresent()
    {
        var result = ShearInclinedChecker.Check(
            Input(), new ConstantProfile(150.0, 80.0, 0.0, 0.0), Geometry(), direction: -1);

        Assert.Contains(result.Details, d => d.Formula == "8.55");
        Assert.Contains(result.Details, d => d.Formula == "8.56");
        Assert.Contains(result.Details, d => d.Formula == "8.60");
        Assert.Contains(result.Details, d => d.Formula == "8.63");
        Assert.Contains(result.Details, d => d.Formula == "8.63s");
    }

    [Fact]
    public void Check_MomentChecksSkipped_WhenCheckMomentIsFalse()
    {
        var input = Input() with { CheckMoment = false };

        var result = ShearInclinedChecker.Check(
            input, new ConstantProfile(150.0, 80.0, 0.0, 0.0), Geometry(), direction: -1);

        Assert.DoesNotContain(result.Details, d => d.Formula.StartsWith("8.63"));
    }

    [Fact]
    public void Check_StripCheckUsesAppliedShearAtStation()
    {
        var result = ShearInclinedChecker.Check(
            Input(), new ConstantProfile(150.0, 0.0, 0.0, 0.0), Geometry(), direction: -1);

        var strip = result.Details.Single(d => d.Formula == "8.55");
        Assert.Equal(150.0, strip.Applied, 6);
        Assert.Equal(ShearFormulas.PhiB1 * 14_500.0 * B * H0, strip.Allowable, 6);
    }

    [Fact]
    public void Check_UniformLoad_ScansAllStationsAndReportsWorst()
    {
        // Q убывает от опоры, поэтому худшей будет ближайшая к опоре стоянка, для которой
        // наклонное сечение ещё помещается до опоры, то есть s ≥ h0.
        var input = Input(qsw: 400.0);
        var profile = new UniformLoadProfile(
            q0: 300.0, m0: 0.0, n0: 0.0, distributedLoad: 40.0, supportDistance: 5.0);

        var result = ShearInclinedChecker.Check(input, profile, Geometry(), direction: -1);

        Assert.True(result.Stations.Count > 5);
        var worst = result.Details.Single(d => d.Formula == "8.56");
        double worstStation = worst.Variables["s"];
        Assert.True(worstStation >= H0 - 1e-9);
        double firstChecked = result.Stations.Where(s => !double.IsNaN(s.Eta)).Min(s => s.S);
        Assert.Equal(firstChecked, worstStation, 6);
        // Стоянки ближе h0 к опоре исключены из (8.56), но попадают в (8.60)
        Assert.All(result.Stations.Where(s => s.S < H0 - 1e-9),
            s => Assert.True(double.IsNaN(s.Eta)));
    }

    [Fact]
    public void Check_TensionPhiNNonPositive_GivesInfiniteUtilization()
    {
        var input = Input() with { Kind = ElementKind.Other };
        var profile = new ConstantProfile(q: 150.0, m: 0.0, n: 500_000.0, supportDistance: 0.0);

        var result = ShearInclinedChecker.Check(input, profile, Geometry(), direction: -1);

        Assert.True(double.IsPositiveInfinity(result.Utilization));
    }

    [Fact]
    public void ProjectionCurve_CoversRangeAndIsMonotonicInStirrupPart()
    {
        var curve = ShearInclinedChecker.ProjectionCurve(
            Input(qsw: 400.0), new ConstantProfile(150.0, 0.0, 0.0, 0.0),
            Geometry(), station: 0.0, direction: -1);

        Assert.Equal(H0, curve[0].C, 6);
        Assert.Equal(2.0 * H0, curve[^1].C, 6);
        Assert.True(curve[^1].Qsw > curve[0].Qsw);
        Assert.True(curve[^1].Qb < curve[0].Qb);
    }

    [Fact]
    public void Check_AutoDirection_TakesWorstOfBoth()
    {
        // Момент растёт в положительном направлении: auto должен выбрать более опасный случай
        var input = Input(qsw: 400.0);
        var profile = new UniformLoadProfile(
            q0: 200.0, m0: 100.0, n0: 0.0, distributedLoad: 40.0, supportDistance: 4.0);

        var auto = ShearInclinedChecker.Check(input, profile, Geometry(), direction: 0);
        var backward = ShearInclinedChecker.Check(input, profile, Geometry(), direction: -1);
        var forward = ShearInclinedChecker.Check(input, profile, Geometry(), direction: +1);

        Assert.Equal(Math.Max(backward.Utilization, forward.Utilization), auto.Utilization, 9);
    }

    [Fact]
    public void Check_MomentChangesSign_SwitchesTensionSidePerStation()
    {
        // M(s) = −100 + 100·s: знак меняется в s = 1.
        var pair = AsymmetricPair();
        var input = Input(qsw: 400.0);
        var profile = new UniformLoadProfile(
            q0: 100.0, m0: -100.0, n0: 0.0, distributedLoad: 0.0, supportDistance: 4.0);

        var result = ShearInclinedChecker.Check(input, profile, pair, direction: -1);

        Assert.Contains(result.Stations, s => s.S < 1.0 && !s.TensionOnPositiveSide);
        Assert.Contains(result.Stations, s => s.S > 1.0 && s.TensionOnPositiveSide);
        Assert.Contains(result.Warnings, w => w.Contains("знак", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Check_MomentChangesSign_UsesWorkingDepthOfTheStationsSide()
    {
        var pair = AsymmetricPair();
        var input = Input(qsw: 400.0);
        var profile = new UniformLoadProfile(
            q0: 100.0, m0: -100.0, n0: 0.0, distributedLoad: 0.0, supportDistance: 4.0);

        var result = ShearInclinedChecker.Check(input, profile, pair, direction: -1);

        // Qb ∝ h0²: при растянутом верхе (h0 = 0,40) он меньше, чем при растянутом низе.
        // Стоянки, для которых (8.56) не выполнялась (ближе h0 к опоре), пропускаются.
        double bottom = result.Stations
            .First(s => !s.TensionOnPositiveSide && !double.IsNaN(s.Qb)).Qb;
        double top = result.Stations
            .First(s => s.TensionOnPositiveSide && !double.IsNaN(s.Qb)).Qb;
        Assert.True(top < bottom);
    }

    [Fact]
    public void Check_StationCloserToSupportThanH0_SkipsFullShearCheck()
    {
        // Стоянка в 0,2 м от опоры: наклонное сечение с C ≥ h0 = 0,55 м не помещается
        var input = Input(qsw: 400.0) with { StationStep = 0.2 };
        var profile = new UniformLoadProfile(
            q0: 300.0, m0: 0.0, n0: 0.0, distributedLoad: 40.0, supportDistance: 5.0);

        var result = ShearInclinedChecker.Check(input, profile, Geometry(), direction: -1);
        var near = result.Stations.First(s => Math.Abs(s.S - 0.2) < 1e-9);

        Assert.True(double.IsNaN(near.Eta));                    // (8.56) не выполнялась
        Assert.True(double.IsNaN(near.CriticalC));
        Assert.Contains(result.Warnings, w => w.Contains("опор", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Details, d => d.Formula == "8.60");   // приопорная проверка осталась
    }

    [Fact]
    public void Check_StationResult_KeepsShearAndMomentCriticalValuesApart()
    {
        var input = Input(qsw: 400.0);
        var profile = new ConstantProfile(q: 150.0, m: 80.0, n: 0.0, supportDistance: 0.0);

        var result = ShearInclinedChecker.Check(input, profile, Geometry(), direction: -1);
        var station = result.Stations[0];

        Assert.Equal(80.0, station.MomentApplied, 6);
        Assert.False(double.IsNaN(station.CriticalCMoment));
        Assert.True(station.Ms > 0.0);
        // Msw растёт с C, поэтому по моменту опасна наименьшая проекция C = h0,
        // а по поперечной силе — внутренний оптимум C* ≈ 0,69 м: величины разные
        Assert.Equal(H0, station.CriticalCMoment, 6);
        Assert.NotEqual(station.CriticalC, station.CriticalCMoment, 6);
    }

    static ShearInclinedInput Input(double qsw = 200.0) => new(
        B: B, H0: H0, Rb: 14_500.0, Rbt: Rbt, Qsw: qsw, Sw: 0.15, Ns: 435.0,
        Kind: ElementKind.BendingUnstressed, AnchorageFactor: 1.0,
        StationStep: 0.0, ProjectionStep: 0.0,
        MomentZoneLength: 0.0, BarCutoffs: [], CheckMoment: true, PhiNOverride: null);

    static InclinedSectionGeometryPair Geometry() => new(Side(H0, true), Side(H0, false));

    /// <summary>Разная арматура сверху и снизу: h0 = 0,40 сверху и 0,55 снизу.</summary>
    static InclinedSectionGeometryPair AsymmetricPair() => new(Side(0.40, true), Side(0.55, false));

    static InclinedSectionGeometry Side(double h0, bool tensionOnPositive) => new(
        B: B, H0: h0, Ns: 435.0, As: 0.001, Rb: 14_500.0, Rbt: Rbt,
        Ab: 0.18, AsTotal: 0.0015, Eb: 24_000_000.0, Eb0: 0.002, Ebt0: 0.0001,
        Plane: ShearPlane.Vy, TensionOnPositiveSide: tensionOnPositive, Warnings: []);
}
