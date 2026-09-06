using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class TargetBeamResultantsTests
{
    static TargetBeamResultants Make(double[] stations, double[]? n = null,
        double[]? my = null, double[]? mz = null) => new()
    {
        StationFractions = stations,
        N = n ?? new double[stations.Length],
        My = my ?? new double[stations.Length],
        Mz = mz ?? new double[stations.Length]
    };

    [Fact]
    public void Validate_ValidTarget_ReturnsNoDiagnostics()
    {
        Assert.Empty(Make([0.0, 0.5, 1.0]).Validate());
    }

    [Fact]
    public void Validate_TooFewStations_ReturnsDiagnostic()
    {
        Assert.Contains(Make([0.0, 1.0]).Validate(),
            d => d.Code == "plate_strip_load_recovery_invalid_target");
    }

    [Fact]
    public void Validate_MismatchedArrayLengths_ReturnsDiagnostic()
    {
        var target = new TargetBeamResultants
        {
            StationFractions = [0.0, 0.5, 1.0],
            N = [0.0, 0.0],
            My = [0.0, 0.0, 0.0],
            Mz = [0.0, 0.0, 0.0]
        };

        Assert.Contains(target.Validate(), d => d.Code == "plate_strip_load_recovery_invalid_target");
    }

    [Fact]
    public void Validate_StationsNotStartingAtZeroOrEndingAtOne_ReturnsDiagnostic()
    {
        Assert.NotEmpty(Make([0.1, 0.5, 1.0]).Validate());
        Assert.NotEmpty(Make([0.0, 0.5, 0.9]).Validate());
    }

    [Fact]
    public void Validate_UnsortedStations_ReturnsDiagnostic()
    {
        Assert.NotEmpty(Make([0.0, 0.7, 0.3, 1.0]).Validate());
    }

    [Fact]
    public void Validate_NonFiniteValues_ReturnsDiagnostic()
    {
        Assert.NotEmpty(Make([0.0, 0.5, 1.0], my: [0.0, double.NaN, 0.0]).Validate());
    }

    [Fact]
    public void ToRowVector_OrdersStationBlocksWithNMyMz()
    {
        var target = Make([0.0, 0.5, 1.0], n: [1, 2, 3], my: [4, 5, 6], mz: [7, 8, 9]);

        Assert.Equal(new double[] { 1, 4, 7, 2, 5, 8, 3, 6, 9 }, target.ToRowVector());
    }
}

public sealed class EquilibriumTargetTests
{
    static readonly double[] Stations = [0.0, 0.25, 0.6, 1.0];
    const double LengthM = 8.0;

    static StripLoadSet MixedLoads() => new([
        new StripLoad
        {
            Kind = StripLoadKind.DistributedUniform,
            SourceTag = "udl",
            StationStartFraction = 0.0,
            StationEndFraction = 1.0,
            QzKnM = -2.0,
            QyKnM = 1.5,
            QxKnM = 0.5
        },
        new StripLoad
        {
            Kind = StripLoadKind.Point,
            SourceTag = "point",
            StationFraction = 0.4,
            PzKn = -5.0
        }]);

    [Fact]
    public void FromStripLoadSet_MatchesProjectionTotalsExactly()
    {
        var loads = MixedLoads();
        var projection = StripLoadConsistentNodalProjection.Project(loads, LengthM, Stations);

        var (target, diagnostics) = EquilibriumTarget.FromStripLoadSet(loads, LengthM, Stations);

        Assert.Empty(diagnostics);
        Assert.NotNull(target);
        Assert.Equal(projection.TotalForceCheck[0], target!.Fx);
        Assert.Equal(projection.TotalForceCheck[1], target.Fy);
        Assert.Equal(projection.TotalForceCheck[2], target.Fz);
        Assert.Equal(projection.TotalMomentCheck[1], target.My);
        Assert.Equal(projection.TotalMomentCheck[2], target.Mz);
    }

    [Fact]
    public void FromStripLoadSet_PointOnInteriorStation_InheritsRightElementRule()
    {
        var loads = new StripLoadSet([new StripLoad
        {
            Kind = StripLoadKind.Point,
            SourceTag = "on-station",
            StationFraction = 0.25,
            PzKn = -4.0
        }]);

        var (target, _) = EquilibriumTarget.FromStripLoadSet(loads, LengthM, Stations);

        Assert.NotNull(target);
        Assert.Equal(-4.0, target!.Fz, 9);
        Assert.Equal(-0.25 * LengthM * -4.0, target.My, 9);
    }

    [Fact]
    public void FromStripLoadSet_InvalidProjection_ReturnsDiagnosticsAndNoTarget()
    {
        var (target, diagnostics) = EquilibriumTarget.FromStripLoadSet(
            new StripLoadSet([]), -1.0, Stations);

        Assert.Null(target);
        Assert.Contains(diagnostics, d => d.Code == "plate_strip_load_invalid_length");
    }
}

public sealed class LoadRecoveryFingerprintTests
{
    static readonly double[] Stations = [0.0, 0.5, 1.0];

    static TargetBeamResultants Target() => new()
    {
        StationFractions = Stations,
        N = [1.0, 2.0, 3.0],
        My = [0.0, 4.0, 0.0],
        Mz = [0.0, 0.0, 0.0]
    };

    static string Compute(
        TargetBeamResultants? target = null,
        double lengthM = 6.0,
        string sectionFingerprint = "SECTION",
        StripBeamSupportScheme? scheme = null,
        LoadRecoveryOptions? options = null) =>
        LoadRecoveryFingerprint.Compute(
            target ?? Target(), lengthM, sectionFingerprint,
            scheme ?? StripBeamSupportScheme.SimplySupported,
            options ?? new LoadRecoveryOptions());

    [Fact]
    public void Compute_SameInput_IsDeterministic()
    {
        Assert.Equal(Compute(), Compute());
    }

    [Fact]
    public void Compute_ChangedLength_ChangesFingerprint()
    {
        Assert.NotEqual(Compute(), Compute(lengthM: 6.5));
    }

    [Fact]
    public void Compute_ChangedSectionFingerprint_ChangesFingerprint()
    {
        Assert.NotEqual(Compute(), Compute(sectionFingerprint: "OTHER"));
    }

    [Fact]
    public void Compute_ChangedSupportScheme_ChangesFingerprint()
    {
        var fixedEnds = new StripBeamSupportScheme(
            StripBeamEndCondition.Fixed, StripBeamEndCondition.Pinned, StripAxialRestraint.StartOnly);
        var bothEnds = new StripBeamSupportScheme(
            StripBeamEndCondition.Pinned, StripBeamEndCondition.Pinned, StripAxialRestraint.BothEnds);

        Assert.NotEqual(Compute(), Compute(scheme: fixedEnds));
        Assert.NotEqual(Compute(), Compute(scheme: bothEnds));
    }

    [Fact]
    public void Compute_ChangedTargetStationOrValue_ChangesFingerprint()
    {
        var movedStation = new TargetBeamResultants
        {
            StationFractions = [0.0, 0.4, 1.0],
            N = [1.0, 2.0, 3.0],
            My = [0.0, 4.0, 0.0],
            Mz = [0.0, 0.0, 0.0]
        };
        var changedValue = new TargetBeamResultants
        {
            StationFractions = Stations,
            N = [1.0, 2.0, 3.0],
            My = [0.0, 4.5, 0.0],
            Mz = [0.0, 0.0, 0.0]
        };

        Assert.NotEqual(Compute(), Compute(movedStation));
        Assert.NotEqual(Compute(), Compute(changedValue));
    }

    [Fact]
    public void Compute_ChangedOptions_ChangesFingerprint()
    {
        var baseline = Compute();

        Assert.NotEqual(baseline, Compute(options: new LoadRecoveryOptions
        {
            Mode = LoadRecoveryMode.ConstrainedSmoothFit
        }));
        Assert.NotEqual(baseline, Compute(options: new LoadRecoveryOptions
        {
            Basis = LoadBasis.PiecewiseConstant
        }));
        Assert.NotEqual(baseline, Compute(options: new LoadRecoveryOptions { Alpha = 0.5 }));
        Assert.NotEqual(baseline, Compute(options: new LoadRecoveryOptions
        {
            StationWeights = [1.0, 2.0, 1.0]
        }));
        Assert.NotEqual(baseline, Compute(options: new LoadRecoveryOptions
        {
            ResultantTolerance = 1e-5
        }));
        Assert.NotEqual(baseline, Compute(options: new LoadRecoveryOptions
        {
            AccuracyTolerance = 1e-2
        }));
        Assert.NotEqual(baseline, Compute(options: new LoadRecoveryOptions
        {
            KnownEndActions = new KnownEndActions(StartMy: 3.0)
        }));
        Assert.NotEqual(baseline, Compute(options: new LoadRecoveryOptions
        {
            EquilibriumTarget = new EquilibriumTarget(0, 0, -10, 0, 0)
        }));
    }
}

public sealed class LoadRecoveryOptionsTests
{
    [Fact]
    public void EffectiveBasisAndAlpha_FollowModeDefaults()
    {
        Assert.Equal(LoadBasis.PiecewiseLinear,
            new LoadRecoveryOptions().EffectiveBasis);
        Assert.Equal(LoadBasis.PiecewiseConstant,
            new LoadRecoveryOptions { Mode = LoadRecoveryMode.PiecewiseUniformOrLinear }.EffectiveBasis);
        Assert.Equal(0.0,
            new LoadRecoveryOptions { Mode = LoadRecoveryMode.PiecewiseUniformOrLinear }.EffectiveAlpha);
        Assert.Equal(0.25, new LoadRecoveryOptions { Alpha = 0.25 }.EffectiveAlpha);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void Validate_InvalidAlpha_ReturnsDiagnostic(double alpha)
    {
        Assert.Contains(new LoadRecoveryOptions { Alpha = alpha }.Validate(3),
            d => d.Code == "plate_strip_load_recovery_invalid_options");
    }

    [Fact]
    public void Validate_InvalidWeights_ReturnsDiagnostic()
    {
        Assert.NotEmpty(new LoadRecoveryOptions { StationWeights = [1.0, 1.0] }.Validate(3));
        Assert.NotEmpty(new LoadRecoveryOptions { StationWeights = [1.0, 0.0, 1.0] }.Validate(3));
        Assert.NotEmpty(new LoadRecoveryOptions { StationWeights = [1.0, double.NaN, 1.0] }.Validate(3));
    }

    [Fact]
    public void Validate_InvalidTolerancesOrEndActions_ReturnsDiagnostic()
    {
        Assert.NotEmpty(new LoadRecoveryOptions { ResultantTolerance = 0.0 }.Validate(3));
        Assert.NotEmpty(new LoadRecoveryOptions { AccuracyTolerance = double.NaN }.Validate(3));
        Assert.NotEmpty(new LoadRecoveryOptions
        {
            KnownEndActions = new KnownEndActions(StartMy: double.NaN)
        }.Validate(3));
    }

    [Fact]
    public void Validate_ValidOptions_ReturnsNoDiagnostics()
    {
        Assert.Empty(new LoadRecoveryOptions { StationWeights = [1.0, 2.0, 1.0] }.Validate(3));
    }
}

public sealed class RawDerivativeResultTests
{
    [Fact]
    public void Compute_ParabolicMoment_ReturnsUniformTransverseLoad()
    {
        // My = q·x·(L−x)/2 при x = L·s  ⇒  qz = −(1/L²)·d²My/ds² = q.
        const double q = 5.0;
        const double lengthM = 6.0;
        double[] stations = [0.0, 0.25, 0.5, 0.75, 1.0];
        var my = stations.Select(s =>
        {
            double x = s * lengthM;
            return q * x * (lengthM - x) / 2.0;
        }).ToArray();

        var target = new TargetBeamResultants
        {
            StationFractions = stations,
            N = new double[stations.Length],
            My = my,
            Mz = new double[stations.Length]
        };

        var raw = RawDerivativeResult.Compute(target, lengthM);

        Assert.True(raw.IsDefined);
        foreach (double value in raw.Qz)
            Assert.Equal(q, value, 9);
    }

    [Fact]
    public void Compute_LinearNormalForce_ReturnsUniformAxialLoad()
    {
        // N = q·(L−x) ⇒ qx = −(1/L)·dN/ds = q.
        const double q = 3.0;
        const double lengthM = 4.0;
        double[] stations = [0.0, 0.3, 1.0];
        var n = stations.Select(s => q * (lengthM - s * lengthM)).ToArray();

        var raw = RawDerivativeResult.Compute(new TargetBeamResultants
        {
            StationFractions = stations,
            N = n,
            My = new double[stations.Length],
            Mz = new double[stations.Length]
        }, lengthM);

        foreach (double value in raw.Qx)
            Assert.Equal(q, value, 9);
    }

    [Fact]
    public void Compute_ScalesWithLength()
    {
        double[] stations = [0.0, 0.5, 1.0];
        var target = new TargetBeamResultants
        {
            StationFractions = stations,
            N = [0.0, 0.0, 0.0],
            My = [0.0, 1.0, 0.0],
            Mz = [0.0, 0.0, 0.0]
        };

        var shortStrip = RawDerivativeResult.Compute(target, 1.0);
        var longStrip = RawDerivativeResult.Compute(target, 10.0);

        Assert.Equal(shortStrip.Qz[1] / 100.0, longStrip.Qz[1], 12);
    }

    [Fact]
    public void Compute_IrregularStations_StillExactForQuadratic()
    {
        const double lengthM = 5.0;
        double[] stations = [0.0, 0.07, 0.5, 0.91, 1.0];
        var my = stations.Select(s => 2.0 * s * s * lengthM * lengthM).ToArray();

        var raw = RawDerivativeResult.Compute(new TargetBeamResultants
        {
            StationFractions = stations,
            N = new double[stations.Length],
            My = my,
            Mz = new double[stations.Length]
        }, lengthM);

        // d²My/ds² = 4·L², qz = −4.
        foreach (double value in raw.Qz)
            Assert.Equal(-4.0, value, 9);
    }
}
