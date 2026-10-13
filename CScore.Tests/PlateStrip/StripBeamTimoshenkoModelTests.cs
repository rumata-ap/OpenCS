using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class StripBeamTimoshenkoModelTests
{
    const double Ea = 3.0e5;
    const double Eiy = 2.0e4;
    const double Eiz = 1.5e4;
    const double Gj = 4.0e3;
    const double GaY = 9.0e4;
    const double GaZ = 6.0e4;
    const double L = 6.0;

    static double[,] Diagonal() => new double[,]
    {
        { Ea, 0, 0 },
        { 0, Eiy, 0 },
        { 0, 0, Eiz }
    };

    static StripLoadSet Uniform(double qz) => new([new StripLoad
    {
        Kind = StripLoadKind.DistributedUniform,
        SourceTag = "q",
        StationStartFraction = 0.0,
        StationEndFraction = 1.0,
        QzKnM = qz
    }]);

    static StripLoadSet Point(double fraction, double pz = 0.0, double mx = 0.0, double py = 0.0) => new([new StripLoad
    {
        Kind = StripLoadKind.Point,
        SourceTag = "p",
        StationFraction = fraction,
        PzKn = pz,
        PyKn = py,
        MxKnM = mx
    }]);

    static readonly double[] Two = [0.0, 0.5, 1.0];
    static readonly double[] Five = [0.0, 0.25, 0.5, 0.75, 1.0];
    static readonly StripLoadSet None = new([]);

    static StripBeamTimoshenkoSolveResult Solve(
        double[] stations, StripBeamSupportScheme scheme, StripLoadSet loads,
        double gaZ = GaZ, double gaY = GaY, double gj = Gj,
        KnownEndActions? end = null, StripTorsionRestraint torsion = StripTorsionRestraint.BothEnds,
        IReadOnlyList<StripPrescribedDisplacement>? prescribed = null) =>
        StripBeamTimoshenkoModel.SolveLinear(Diagonal(), gj, gaY, gaZ, L, stations, scheme, loads,
            end, prescribed, torsion);

    [Fact]
    public void RigidShear_MatchesEulerBernoulliModel()
    {
        var loads = Uniform(-12.0);
        var bernoulli = StripBeamModel.Solve(Diagonal(), L, Five, StripBeamSupportScheme.SimplySupported, loads);
        var timoshenko = Solve(Five, StripBeamSupportScheme.SimplySupported, loads,
            gaZ: double.PositiveInfinity, gaY: double.PositiveInfinity);

        Assert.True(timoshenko.IsCalculable);
        for (int node = 0; node < Five.Length; node++)
        {
            Assert.Equal(bernoulli.Displacements[node * 5 + 2], timoshenko.Displacements[node * 6 + 2], 10);
            Assert.Equal(bernoulli.Displacements[node * 5 + 3], timoshenko.Displacements[node * 6 + 4], 10);
            Assert.Equal(bernoulli.StationResultants[node][1], timoshenko.StationResultants[node][1], 8);
        }
    }

    [Fact]
    public void SimplySupported_CentralLoad_AddsShearDeflection()
    {
        const double p = 30.0;
        var result = Solve(Two, StripBeamSupportScheme.SimplySupported, Point(0.5, pz: p));

        double expected = p * L * L * L / (48.0 * Eiy) + p * L / (4.0 * GaZ);
        Assert.True(result.IsCalculable);
        Assert.Equal(expected, Math.Abs(result.Displacements[1 * 6 + 2]), 8);
        // поперечная сила на опорах = P/2, момент в пролёте PL/4
        Assert.Equal(p / 2.0, Math.Abs(result.StationResultants[0][5]), 8);
        Assert.Equal(p * L / 4.0, Math.Abs(result.StationResultants[1][1]), 8);
    }

    [Fact]
    public void FixedFixed_UniformLoad_AddsShearDeflection()
    {
        const double q = 8.0;
        var scheme = new StripBeamSupportScheme(
            StripBeamEndCondition.Fixed, StripBeamEndCondition.Fixed, StripAxialRestraint.StartOnly);
        var result = Solve(Five, scheme, Uniform(q));

        double expected = q * Math.Pow(L, 4) / (384.0 * Eiy) + q * L * L / (8.0 * GaZ);
        Assert.Equal(expected, Math.Abs(result.Displacements[2 * 6 + 2]), 7);
    }

    [Fact]
    public void InPlaneShear_UsesGaY()
    {
        const double p = 20.0;
        var result = Solve(Two, StripBeamSupportScheme.SimplySupported, Point(0.5, py: p));

        double expected = p * L * L * L / (48.0 * Eiz) + p * L / (4.0 * GaY);
        Assert.Equal(expected, Math.Abs(result.Displacements[1 * 6 + 1]), 8);
    }

    [Fact]
    public void CentralTorque_ForkSupports_GivesTL_Over_4GJ()
    {
        const double t = 12.0;
        var result = Solve(Two, StripBeamSupportScheme.SimplySupported, Point(0.5, mx: t));

        Assert.True(result.IsCalculable);
        Assert.Equal(t * L / (4.0 * Gj), Math.Abs(result.Displacements[1 * 6 + 3]), 10);
        Assert.Equal(t / 2.0, Math.Abs(result.StationResultants[0][3]), 8);
    }

    [Fact]
    public void EndTorque_Cantilever_GivesTL_Over_GJ()
    {
        const double t = 5.0;
        var result = Solve(Five, StripBeamSupportScheme.SimplySupported, None,
            end: new KnownEndActions(EndT: t), torsion: StripTorsionRestraint.StartOnly);

        Assert.True(result.IsCalculable);
        double last = result.Displacements[4 * 6 + 3];
        Assert.Equal(t * L / Gj, last, 10);
        Assert.All(result.StationResultants, r => Assert.Equal(t, r[3], 8));
    }

    [Fact]
    public void PrescribedTwist_ProducesTorqueReaction()
    {
        const double theta = 0.002;
        var result = Solve(Two, StripBeamSupportScheme.SimplySupported, None,
            torsion: StripTorsionRestraint.StartOnly,
            prescribed: [new(2, StripBeamTimoshenkoModel.StripTorsionDof, theta)]);

        Assert.True(result.IsCalculable);
        Assert.Equal(theta / 2.0, result.Displacements[1 * 6 + 3], 12);
        Assert.Equal(Gj * theta / L, Math.Abs(result.StationResultants[0][3]), 8);
    }

    [Fact]
    public void FixedRotationPrescribed_SettlementOfFixedEnd_UsesTimoshenkoCorrection()
    {
        // Осадка заделки: M = 6EIδ/L² / (1+Φ), Φ = 12EI/(GA L²)
        const double delta = -0.01;
        var scheme = new StripBeamSupportScheme(
            StripBeamEndCondition.Fixed, StripBeamEndCondition.Fixed, StripAxialRestraint.StartOnly);
        var result = Solve(Two, scheme, None, prescribed: [new(2, 2, delta)]);

        double phi = 12.0 * Eiy / (GaZ * L * L);
        double expected = 6.0 * Eiy * Math.Abs(delta) / (L * L) / (1.0 + phi);
        Assert.True(result.IsCalculable);
        Assert.Equal(expected, Math.Abs(result.StationResultants[0][1]), 8);
    }
}
