using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class EquivalentBeamLoadRecoveryTests
{
    const double Ea = 3.0e5;
    const double Eiy = 2.0e4;
    const double Eiz = 1.5e4;
    const double LengthM = 6.0;

    static readonly double[] Stations = [0.0, 0.25, 0.5, 0.75, 1.0];

    static double[,] Diagonal() => new double[,]
    {
        { Ea, 0, 0 },
        { 0, Eiy, 0 },
        { 0, 0, Eiz }
    };

    static EquivalentSection Section(double[,]? tangent = null, double lengthM = LengthM) => new()
    {
        IsCalculable = true,
        BeamTangent = tangent ?? Diagonal(),
        ResultFingerprint = "SECTION-FP",
        Strip = new PlateStripBeamAnalogy
        {
            Id = "strip-1",
            Geometry = new PlateStripGeometry { LengthM = lengthM }
        }
    };

    static TargetBeamResultants Target(
        double[]? stations = null, double[]? n = null, double[]? my = null, double[]? mz = null)
    {
        stations ??= Stations;
        return new TargetBeamResultants
        {
            StationFractions = stations,
            N = n ?? new double[stations.Length],
            My = my ?? new double[stations.Length],
            Mz = mz ?? new double[stations.Length]
        };
    }

    static StripLoadSet UniformLoad(double qx = 0.0, double qy = 0.0, double qz = 0.0) =>
        new([new StripLoad
        {
            Kind = StripLoadKind.DistributedUniform,
            SourceTag = "true-load",
            StationStartFraction = 0.0,
            StationEndFraction = 1.0,
            QxKnM = qx,
            QyKnM = qy,
            QzKnM = qz
        }]);

    /// <summary>Целевая эпюра, снятая с прямого прогона балки под известной нагрузкой — так
    /// проверяется именно обращение оператора, а не совпадение двух аналитических формул.</summary>
    static TargetBeamResultants TargetFromForwardRun(
        StripLoadSet loads, StripBeamSupportScheme scheme,
        double[]? stations = null, double[,]? tangent = null, double lengthM = LengthM)
    {
        stations ??= Stations;
        var solve = StripBeamModel.Solve(
            tangent ?? Diagonal(), lengthM, stations, scheme, loads);
        Assert.True(solve.IsCalculable);

        return new TargetBeamResultants
        {
            StationFractions = stations,
            N = solve.StationResultants.Select(s => s[0]).ToArray(),
            My = solve.StationResultants.Select(s => s[1]).ToArray(),
            Mz = solve.StationResultants.Select(s => s[2]).ToArray()
        };
    }

    static LoadRecoveryOptions Options(
        StripLoadSet? equilibriumSource = null,
        LoadRecoveryMode mode = LoadRecoveryMode.WeakEquilibriumProjection,
        double[]? stations = null,
        double lengthM = LengthM,
        double? alpha = null,
        KnownEndActions? endActions = null,
        double accuracyTolerance = 1e-6)
    {
        EquilibriumTarget? equilibrium = null;
        if (equilibriumSource != null)
        {
            var (target, diagnostics) = EquilibriumTarget.FromStripLoadSet(
                equilibriumSource, lengthM, stations ?? Stations);
            Assert.Empty(diagnostics);
            equilibrium = target;
        }

        return new LoadRecoveryOptions
        {
            Mode = mode,
            Alpha = alpha,
            EquilibriumTarget = equilibrium,
            KnownEndActions = endActions,
            AccuracyTolerance = accuracyTolerance
        };
    }

    static double[] Component(RecoveredBeamLoadSet result, int component)
    {
        var layout = new LoadBasisLayout(result.Basis, result.TargetResultants.StationFractions);
        int offset = layout.ComponentOffset(component);
        return result.Coefficients.Skip(offset).Take(layout.FunctionsPerComponent).ToArray();
    }

    // ── Аналитически точное восстановление ──────────────────────────────────────────────

    [Theory]
    [InlineData(StripBeamEndCondition.Pinned, StripBeamEndCondition.Pinned)]
    [InlineData(StripBeamEndCondition.Fixed, StripBeamEndCondition.Fixed)]
    [InlineData(StripBeamEndCondition.Fixed, StripBeamEndCondition.Pinned)]
    public void Recover_UniformTransverseLoad_ReturnsUniformIntensityOnEverySupportScheme(
        StripBeamEndCondition start, StripBeamEndCondition end)
    {
        const double q = 5.0;
        var scheme = new StripBeamSupportScheme(start, end, StripAxialRestraint.StartOnly);
        var loads = UniformLoad(qz: q);

        var result = EquivalentBeamLoadRecovery.Recover(
            Section(), TargetFromForwardRun(loads, scheme), scheme, Options(loads));

        Assert.True(result.IsCalculable, string.Join("; ", result.Diagnostics.Select(d => d.Code)));
        foreach (double value in Component(result, 2))
            Assert.Equal(q, value, 6);
        Assert.True(result.ApproximationError.Relative < 1e-9);
    }

    [Fact]
    public void Recover_MembraneLoad_ReturnsUniformAxialIntensity()
    {
        const double q = 3.0;
        var scheme = StripBeamSupportScheme.SimplySupported;
        var loads = UniformLoad(qx: q);

        var result = EquivalentBeamLoadRecovery.Recover(
            Section(), TargetFromForwardRun(loads, scheme), scheme, Options(loads));

        Assert.True(result.IsCalculable);
        foreach (double value in Component(result, 0))
            Assert.Equal(q, value, 6);
    }

    [Fact]
    public void Recover_InPlaneLoad_ReturnsUniformTransverseInPlaneIntensity()
    {
        const double q = -4.0;
        var scheme = StripBeamSupportScheme.SimplySupported;
        var loads = UniformLoad(qy: q);

        var result = EquivalentBeamLoadRecovery.Recover(
            Section(), TargetFromForwardRun(loads, scheme), scheme, Options(loads));

        Assert.True(result.IsCalculable);
        foreach (double value in Component(result, 1))
            Assert.Equal(q, value, 6);
    }

    [Fact]
    public void Recover_CoupledSectionTangent_StillRecoversExactly()
    {
        var coupled = new double[,]
        {
            { Ea, 0.3 * Eiy, 0 },
            { 0.3 * Eiy, Eiy, 0 },
            { 0, 0, Eiz }
        };
        const double q = 5.0;
        var scheme = StripBeamSupportScheme.SimplySupported;
        var loads = UniformLoad(qz: q, qx: 2.0);
        var target = TargetFromForwardRun(loads, scheme, tangent: coupled);

        var result = EquivalentBeamLoadRecovery.Recover(
            Section(coupled), target, scheme, Options(loads));

        Assert.True(result.IsCalculable);
        foreach (double value in Component(result, 2))
            Assert.Equal(q, value, 6);
        foreach (double value in Component(result, 0))
            Assert.Equal(2.0, value, 6);
    }

    [Fact]
    public void Recover_LengthScaling_IntensitiesAreIdenticalForSameNormalizedDiagram()
    {
        const double q = 5.0;
        var scheme = StripBeamSupportScheme.SimplySupported;

        foreach (double length in new[] { 1.0, 10.0 })
        {
            var loads = UniformLoad(qz: q);
            var target = TargetFromForwardRun(loads, scheme, lengthM: length);
            var result = EquivalentBeamLoadRecovery.Recover(
                Section(lengthM: length), target, scheme,
                Options(loads, stations: Stations, lengthM: length));

            Assert.True(result.IsCalculable);
            foreach (double value in Component(result, 2))
                Assert.Equal(q, value, 6);
        }
    }

    [Fact]
    public void Recover_IrregularStations_StillRecoversUniformLoad()
    {
        double[] stations = [0.0, 0.05, 0.4, 0.5, 0.93, 1.0];
        const double q = 5.0;
        var scheme = StripBeamSupportScheme.SimplySupported;
        var loads = UniformLoad(qz: q);
        var target = TargetFromForwardRun(loads, scheme, stations);

        var result = EquivalentBeamLoadRecovery.Recover(
            Section(), target, scheme, Options(loads, stations: stations));

        Assert.True(result.IsCalculable);
        foreach (double value in Component(result, 2))
            Assert.Equal(q, value, 5);
    }

    // ── End actions ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Recover_ConstantMomentWithEndActions_ReturnsZeroLoad()
    {
        const double moment = 7.0;
        var scheme = StripBeamSupportScheme.SimplySupported;
        var target = Target(my: Stations.Select(_ => moment).ToArray());
        var options = new LoadRecoveryOptions
        {
            KnownEndActions = new KnownEndActions(StartMy: moment, EndMy: moment),
            EquilibriumTarget = new EquilibriumTarget(0, 0, 0, 0, 0),
            AccuracyTolerance = 1e-6
        };

        var result = EquivalentBeamLoadRecovery.Recover(Section(), target, scheme, options);

        Assert.True(result.IsCalculable, string.Join("; ", result.Diagnostics.Select(d => d.Code)));
        foreach (double value in result.Coefficients)
            Assert.Equal(0.0, value, 6);
        Assert.True(result.ApproximationError.Absolute < 1e-6);
    }

    [Fact]
    public void Recover_ConstantMomentWithoutEndActions_ReportsAccuracyViolationAndHint()
    {
        const double moment = 7.0;
        var target = Target(my: Stations.Select(_ => moment).ToArray());

        var result = EquivalentBeamLoadRecovery.Recover(
            Section(), target, StripBeamSupportScheme.SimplySupported,
            new LoadRecoveryOptions
            {
                EquilibriumTarget = new EquilibriumTarget(0, 0, 0, 0, 0),
                AccuracyTolerance = 1e-6
            });

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_load_recovery_accuracy_violation");
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_load_recovery_end_actions_required");
        Assert.NotNull(result.TargetResultants);
        Assert.True(result.RawDerivative.IsDefined);
    }

    // ── Диагностическая производная и режимы ───────────────────────────────────────────

    [Fact]
    public void Recover_SpikeInDiagram_DoesNotAppearInRecoveredLoadButAppearsInRawDerivative()
    {
        const double q = 5.0;
        double[] stations = [0.0, 0.2, 0.4, 0.5, 0.6, 0.8, 1.0];
        var scheme = StripBeamSupportScheme.SimplySupported;
        var loads = UniformLoad(qz: q);
        var clean = TargetFromForwardRun(loads, scheme, stations);

        var spiked = clean.My.ToArray();
        spiked[3] *= 1.35;
        var target = Target(stations, my: spiked);

        var result = EquivalentBeamLoadRecovery.Recover(
            Section(), target, scheme,
            Options(loads, stations: stations, alpha: 0.5, accuracyTolerance: 1.0));

        Assert.True(result.IsCalculable);
        double rawSpike = Math.Abs(result.RawDerivative.Qz[3]);
        double recoveredSpike = Math.Abs(Component(result, 2)[3]);
        Assert.True(rawSpike > 5.0 * q, $"raw={rawSpike:G6}");
        Assert.True(recoveredSpike < 2.0 * q, $"recovered={recoveredSpike:G6}");
    }

    [Fact]
    public void Recover_NoisyDiagram_PreservesResultantDespiteStrongSmoothing()
    {
        var (target, loads, stations) = NoisyUniformCase();

        var result = EquivalentBeamLoadRecovery.Recover(
            Section(), target, StripBeamSupportScheme.SimplySupported,
            Options(loads, stations: stations, alpha: 100.0, accuracyTolerance: 1.0));

        Assert.True(result.IsCalculable);
        foreach (double value in result.EquilibriumResidual)
            Assert.True(Math.Abs(value) < 1e-6, $"невязка равновесия {value:G6}");
        Assert.True(result.ApproximationError.Relative > 1e-6, "сглаживание обязано дать ошибку эпюры");
    }

    [Fact]
    public void Recover_ThreeModes_AgreeOnResultantButDifferInSmoothness()
    {
        var (target, loads, stations) = NoisyUniformCase();
        var scheme = StripBeamSupportScheme.SimplySupported;

        var weak = EquivalentBeamLoadRecovery.Recover(Section(), target, scheme,
            Options(loads, LoadRecoveryMode.WeakEquilibriumProjection, stations, accuracyTolerance: 1.0));
        var smooth = EquivalentBeamLoadRecovery.Recover(Section(), target, scheme,
            Options(loads, LoadRecoveryMode.ConstrainedSmoothFit, stations, alpha: 50.0,
                accuracyTolerance: 1.0));
        var piecewise = EquivalentBeamLoadRecovery.Recover(Section(), target, scheme,
            Options(loads, LoadRecoveryMode.PiecewiseUniformOrLinear, stations, accuracyTolerance: 1.0));
        var rawMode = EquivalentBeamLoadRecovery.Recover(Section(), target, scheme,
            Options(loads, LoadRecoveryMode.RawDerivativeDiagnostic, stations, accuracyTolerance: 1.0));

        foreach (var result in new[] { weak, smooth, piecewise, rawMode })
            Assert.True(result.IsCalculable, string.Join("; ", result.Diagnostics.Select(d => d.Code)));

        Assert.Equal(LoadBasis.PiecewiseConstant, piecewise.Basis);
        Assert.Empty(rawMode.ElementLoadDefinitions);
        Assert.Contains(rawMode.Diagnostics,
            d => d.Code == "plate_strip_load_recovery_raw_derivative_not_design_load");

        // Сглаженная нагрузка обязана быть спокойнее и несглаженной, и raw-производной.
        Assert.True(Roughness(Component(smooth, 2)) < Roughness(Component(weak, 2)));
        Assert.True(Roughness(Component(weak, 2)) < Roughness(rawMode.RawDerivative.Qz));

        foreach (var result in new[] { weak, smooth, piecewise })
            foreach (double value in result.EquilibriumResidual)
                Assert.True(Math.Abs(value) < 1e-6);
    }

    [Fact]
    public void Recover_PointForceDiagram_ReportsAccuracyViolationAndKeepsTargetAndRaw()
    {
        double[] stations = [0.0, 0.25, 0.5, 0.75, 1.0];
        var pointLoad = new StripLoadSet([new StripLoad
        {
            Kind = StripLoadKind.Point,
            SourceTag = "midspan",
            StationFraction = 0.5,
            PzKn = -20.0
        }]);
        var scheme = StripBeamSupportScheme.SimplySupported;
        var target = TargetFromForwardRun(pointLoad, scheme, stations);

        var result = EquivalentBeamLoadRecovery.Recover(
            Section(), target, scheme,
            Options(pointLoad, stations: stations, accuracyTolerance: 1e-9));

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_load_recovery_accuracy_violation");
        Assert.Same(target, result.TargetResultants);
        Assert.True(result.RawDerivative.IsDefined);
    }

    // ── Вырожденные входы ──────────────────────────────────────────────────────────────

    [Fact]
    public void Recover_ZeroTarget_ReturnsZeroLoadWithoutDivisionByZero()
    {
        var result = EquivalentBeamLoadRecovery.Recover(
            Section(), Target(), StripBeamSupportScheme.SimplySupported,
            new LoadRecoveryOptions { EquilibriumTarget = new EquilibriumTarget(0, 0, 0, 0, 0) });

        Assert.True(result.IsCalculable, string.Join("; ", result.Diagnostics.Select(d => d.Code)));
        foreach (double value in result.Coefficients)
            Assert.Equal(0.0, value, 9);
        Assert.Equal(0.0, result.ApproximationError.Absolute, 9);
    }

    [Fact]
    public void Recover_AxialOnlyAndBendingOnlyTargets_BothSolve()
    {
        var scheme = StripBeamSupportScheme.SimplySupported;

        var axialLoads = UniformLoad(qx: 4.0);
        var axialOnly = EquivalentBeamLoadRecovery.Recover(
            Section(), TargetFromForwardRun(axialLoads, scheme), scheme, Options(axialLoads));

        var bendingLoads = UniformLoad(qz: 4.0);
        var bendingOnly = EquivalentBeamLoadRecovery.Recover(
            Section(), TargetFromForwardRun(bendingLoads, scheme), scheme, Options(bendingLoads));

        Assert.True(axialOnly.IsCalculable);
        Assert.True(bendingOnly.IsCalculable);
    }

    [Fact]
    public void Recover_WithoutEquilibriumTarget_WarnsAndStillReportsResidual()
    {
        var loads = UniformLoad(qz: 5.0);
        var scheme = StripBeamSupportScheme.SimplySupported;

        var result = EquivalentBeamLoadRecovery.Recover(
            Section(), TargetFromForwardRun(loads, scheme), scheme,
            new LoadRecoveryOptions { AccuracyTolerance = 1e-6 });

        Assert.True(result.IsCalculable);
        Assert.Contains(result.Diagnostics,
            d => d.Code == "plate_strip_load_recovery_equilibrium_unconstrained" && !d.IsError);
        Assert.Equal(5.0 * LengthM, result.EquilibriumResidual[2], 6);
    }

    [Fact]
    public void Recover_InvalidSection_ReturnsDiagnosticAndKeepsTargetAndRaw()
    {
        var target = Target(my: [0.0, 10.0, 12.0, 10.0, 0.0]);

        var missing = EquivalentBeamLoadRecovery.Recover(
            null, target, StripBeamSupportScheme.SimplySupported, new LoadRecoveryOptions());
        Assert.False(missing.IsCalculable);
        Assert.Contains(missing.Diagnostics, d => d.Code == "plate_strip_load_recovery_invalid_section");
        Assert.Same(target, missing.TargetResultants);

        var stale = Section();
        stale.IsStale = true;
        var staleResult = EquivalentBeamLoadRecovery.Recover(
            stale, target, StripBeamSupportScheme.SimplySupported, new LoadRecoveryOptions());
        Assert.False(staleResult.IsCalculable);
        Assert.Contains(staleResult.Diagnostics, d => d.Code == "plate_strip_load_recovery_invalid_section");

        var degenerate = Section(new double[3, 3]);
        var degenerateResult = EquivalentBeamLoadRecovery.Recover(
            degenerate, target, StripBeamSupportScheme.SimplySupported, new LoadRecoveryOptions());
        Assert.False(degenerateResult.IsCalculable);
        Assert.Contains(degenerateResult.Diagnostics,
            d => d.Code == "plate_strip_load_recovery_invalid_section");
    }

    [Fact]
    public void Recover_InvalidLength_ReturnsDiagnosticAndUndefinedRawDerivative()
    {
        var result = EquivalentBeamLoadRecovery.Recover(
            Section(lengthM: 0.0), Target(my: [0.0, 1.0, 2.0, 1.0, 0.0]),
            StripBeamSupportScheme.SimplySupported, new LoadRecoveryOptions());

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_load_recovery_invalid_length");
        Assert.False(result.RawDerivative.IsDefined);
    }

    [Fact]
    public void Recover_InvalidTargetOrOptions_ReturnsDiagnostics()
    {
        var badTarget = EquivalentBeamLoadRecovery.Recover(
            Section(), Target([0.0, 1.0]), StripBeamSupportScheme.SimplySupported,
            new LoadRecoveryOptions());
        Assert.Contains(badTarget.Diagnostics, d => d.Code == "plate_strip_load_recovery_invalid_target");

        var badOptions = EquivalentBeamLoadRecovery.Recover(
            Section(), Target(), StripBeamSupportScheme.SimplySupported,
            new LoadRecoveryOptions { Alpha = -1.0 });
        Assert.Contains(badOptions.Diagnostics, d => d.Code == "plate_strip_load_recovery_invalid_options");
    }

    [Fact]
    public void Recover_FingerprintIsFilledAndStable()
    {
        var loads = UniformLoad(qz: 5.0);
        var scheme = StripBeamSupportScheme.SimplySupported;
        var target = TargetFromForwardRun(loads, scheme);

        var first = EquivalentBeamLoadRecovery.Recover(Section(), target, scheme, Options(loads));
        var second = EquivalentBeamLoadRecovery.Recover(Section(), target, scheme, Options(loads));

        Assert.False(string.IsNullOrEmpty(first.InputFingerprint));
        Assert.Equal(first.InputFingerprint, second.InputFingerprint);
    }

    // ── Передача на балочную сетку (задача 7) ──────────────────────────────────────────

    [Fact]
    public void Recover_ElementLoadDefinitions_ProjectBackToEquilibriumTarget()
    {
        const double q = 5.0;
        var scheme = StripBeamSupportScheme.SimplySupported;
        var loads = UniformLoad(qz: q, qx: 1.5, qy: -2.0);
        var options = Options(loads);

        var result = EquivalentBeamLoadRecovery.Recover(
            Section(), TargetFromForwardRun(loads, scheme), scheme, options);

        Assert.True(result.IsCalculable);
        Assert.NotEmpty(result.ElementLoadDefinitions);
        Assert.NotEmpty(result.ConsistentNodalLoads);

        var projection = StripLoadConsistentNodalProjection.Project(
            new StripLoadSet(result.ElementLoadDefinitions), LengthM, Stations);

        var expected = options.EquilibriumTarget!;
        Assert.Equal(expected.Fx, projection.TotalForceCheck[0], 6);
        Assert.Equal(expected.Fy, projection.TotalForceCheck[1], 6);
        Assert.Equal(expected.Fz, projection.TotalForceCheck[2], 6);
        Assert.Equal(expected.My, projection.TotalMomentCheck[1], 6);
        Assert.Equal(expected.Mz, projection.TotalMomentCheck[2], 6);
    }

    [Fact]
    public void Recover_ConsistentNodalLoads_MatchProjectionOfElementLoads()
    {
        var loads = UniformLoad(qz: 5.0);
        var scheme = StripBeamSupportScheme.SimplySupported;

        var result = EquivalentBeamLoadRecovery.Recover(
            Section(), TargetFromForwardRun(loads, scheme), scheme, Options(loads));

        var projection = StripLoadConsistentNodalProjection.Project(
            new StripLoadSet(result.ElementLoadDefinitions), LengthM, Stations);

        Assert.Equal(projection.Elements.Count, result.ConsistentNodalLoads.Count);
        for (int i = 0; i < projection.Elements.Count; i++)
            Assert.Equal(projection.Elements[i], result.ConsistentNodalLoads[i]);
    }

    // ── Вспомогательное ────────────────────────────────────────────────────────────────

    static (TargetBeamResultants Target, StripLoadSet Loads, double[] Stations) NoisyUniformCase()
    {
        double[] stations = [0.0, 0.15, 0.3, 0.45, 0.6, 0.75, 0.9, 1.0];
        var loads = UniformLoad(qz: 5.0);
        var clean = TargetFromForwardRun(loads, StripBeamSupportScheme.SimplySupported, stations);

        // Детерминированный «шум» измерения эпюры.
        double[] noise = [0.0, 0.06, -0.05, 0.07, -0.08, 0.05, -0.06, 0.0];
        var my = clean.My.Select((value, i) => value * (1.0 + noise[i])).ToArray();

        return (new TargetBeamResultants
        {
            StationFractions = stations,
            N = clean.N,
            My = my,
            Mz = clean.Mz
        }, loads, stations);
    }

    static double Roughness(IReadOnlyList<double> values)
    {
        double sum = 0.0;
        for (int i = 1; i < values.Count - 1; i++)
        {
            double second = values[i + 1] - 2.0 * values[i] + values[i - 1];
            sum += second * second;
        }
        return sum;
    }
}
