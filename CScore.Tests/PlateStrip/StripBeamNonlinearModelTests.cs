using System.Linq;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

/// <summary>Срез 7, Task 4: нелинейная балочная задача полосы.</summary>
public sealed class StripBeamNonlinearModelTests
{
    const double Width = 2.0;
    const double Length = 6.0;
    const double E = 30_000.0;
    const double H = 0.3;

    /// <summary>Нагрузка нелинейных сценариев. Момент в середине пролёта q·L²/8 = 1.08 кН·м
    /// лежит между упругим пределом секции W·Ry = (2·0.3²/6)·30 = 0.9 и пластическим ≈1.35:
    /// сечение заходит в пластику, но не пластифицируется целиком (иначе касательная вырождается
    /// физически корректно, и проверять на такой нагрузке нечего).</summary>
    const double NonlinearQ = 0.24;

    [Theory]
    [InlineData(StripBeamEndCondition.Pinned, StripBeamEndCondition.Pinned, StripAxialRestraint.StartOnly)]
    [InlineData(StripBeamEndCondition.Fixed, StripBeamEndCondition.Fixed, StripAxialRestraint.StartOnly)]
    [InlineData(StripBeamEndCondition.Fixed, StripBeamEndCondition.Pinned, StripAxialRestraint.StartOnly)]
    [InlineData(StripBeamEndCondition.Pinned, StripBeamEndCondition.Pinned, StripAxialRestraint.BothEnds)]
    public void LinearSource_MatchesLinearModel_OnEverySupportScheme(
        StripBeamEndCondition start, StripBeamEndCondition end, StripAxialRestraint axial)
    {
        var scheme = new StripBeamSupportScheme(start, end, axial);
        var stations = Stations(4);
        var loads = UniformLoad();
        var source = Linear();
        var tangent = NonlinearStripSection.Evaluate(Width, [source, source], BeamStrainState.Zero).K;

        var linear = StripBeamModel.Solve(tangent, Length, stations, scheme, loads);
        var nonlinear = StripBeamNonlinearModel.Solve(
            Grid(source, stations.Count - 1), Width, Length, stations, scheme, loads);

        Assert.True(linear.IsCalculable);
        Assert.True(nonlinear.IsCalculable, string.Join("; ", nonlinear.Diagnostics.Select(d => d.Message)));
        for (int i = 0; i < linear.Displacements.Length; i++)
            Assert.Equal(linear.Displacements[i], nonlinear.Displacements[i], 9);
        for (int s = 0; s < linear.StationResultants.Length; s++)
        for (int c = 0; c < 3; c++)
            Assert.Equal(linear.StationResultants[s][c], nonlinear.StationResultants[s][c], 8);
    }

    [Fact]
    public void LinearSource_ConvergesInOneSolve()
    {
        var stations = Stations(4);
        var result = StripBeamNonlinearModel.Solve(
            Grid(Linear(), stations.Count - 1), Width, Length, stations,
            StripBeamSupportScheme.SimplySupported, UniformLoad());

        Assert.True(result.IsCalculable);
        Assert.Equal(1, result.TotalIterations);
        Assert.Equal(1.0, result.AchievedLoadFactor, 12);
    }

    /// <summary>Страж правила sampling: снятие эпюры с точек квадратуры дало бы на шарнирной
    /// опоре fixed-end moment qL²/12 вместо нуля.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void SimplySupportedUniformLoad_HasZeroMomentAtSupports(int elementCount)
    {
        const double q = 10.0; // кН/м
        var stations = Stations(elementCount);
        var result = StripBeamNonlinearModel.Solve(
            Grid(Linear(), elementCount), Width, Length, stations,
            StripBeamSupportScheme.SimplySupported, UniformLoad(q));

        Assert.True(result.IsCalculable);
        double wrongIfSampledAtGaussPoints = q * Length * Length / 12.0;

        Assert.Equal(0.0, result.StationResultants[0][1], 6);
        Assert.Equal(0.0, result.StationResultants[^1][1], 6);
        Assert.NotEqual(wrongIfSampledAtGaussPoints, Math.Abs(result.StationResultants[0][1]), 3);
    }

    [Fact]
    public void SimplySupportedUniformLoad_HasAnalyticMidspanMoment()
    {
        const double q = 10.0;
        var stations = Stations(8);
        var result = StripBeamNonlinearModel.Solve(
            Grid(Linear(), 8), Width, Length, stations,
            StripBeamSupportScheme.SimplySupported, UniformLoad(q));

        Assert.True(result.IsCalculable);
        double expected = q * Length * Length / 8.0;
        Assert.Equal(expected, Math.Abs(result.StationResultants[4][1]), 6);
    }

    [Fact]
    public void MonotonicHardeningSource_IsIndependentOfLoadStepCount()
    {
        var stations = Stations(4);
        var grid = Grid(Nonlinear(), 4);

        var single = StripBeamNonlinearModel.Solve(
            grid, Width, Length, stations, StripBeamSupportScheme.SimplySupported, UniformLoad(NonlinearQ),
            null, new StripNewtonOptions(LoadSteps: 1));
        var stepped = StripBeamNonlinearModel.Solve(
            grid, Width, Length, stations, StripBeamSupportScheme.SimplySupported, UniformLoad(NonlinearQ),
            null, new StripNewtonOptions(LoadSteps: 10));

        Assert.True(single.IsCalculable, string.Join("; ", single.Diagnostics.Select(d => d.Message)));
        Assert.True(stepped.IsCalculable, string.Join("; ", stepped.Diagnostics.Select(d => d.Message)));
        for (int i = 0; i < single.Displacements.Length; i++)
            Assert.Equal(single.Displacements[i], stepped.Displacements[i], 6);
    }

    [Fact]
    public void NonlinearSource_IsSofterThanLinear_ButKeepsStaticallyDeterminateMoment()
    {
        const double q = NonlinearQ;
        var stations = Stations(6);
        var scheme = StripBeamSupportScheme.SimplySupported;

        var linear = StripBeamNonlinearModel.Solve(
            Grid(LinearLike(), 6), Width, Length, stations, scheme, UniformLoad(q));
        var nonlinear = StripBeamNonlinearModel.Solve(
            Grid(Nonlinear(), 6), Width, Length, stations, scheme, UniformLoad(q));

        Assert.True(linear.IsCalculable);
        Assert.True(nonlinear.IsCalculable, string.Join("; ", nonlinear.Diagnostics.Select(d => d.Message)));

        double linearMid = Math.Abs(linear.Displacements[3 * StripBeamElement.DofPerNode + 2]);
        double nonlinearMid = Math.Abs(nonlinear.Displacements[3 * StripBeamElement.DofPerNode + 2]);
        Assert.True(nonlinearMid > linearMid,
            $"Прогиб после излома обязан вырасти: {nonlinearMid} vs {linearMid}.");

        // Момент статически определимой схемы определяется равновесием, а не жёсткостью.
        double expected = q * Length * Length / 8.0;
        Assert.Equal(expected, Math.Abs(nonlinear.StationResultants[3][1]), 5);
    }

    [Fact]
    public void ModifiedNewton_ReachesSameResultWithMoreIterations()
    {
        var stations = Stations(4);
        var grid = Grid(Nonlinear(), 4);

        var full = StripBeamNonlinearModel.Solve(
            grid, Width, Length, stations, StripBeamSupportScheme.SimplySupported, UniformLoad(NonlinearQ),
            null, new StripNewtonOptions(ModifiedNewton: false));
        var modified = StripBeamNonlinearModel.Solve(
            grid, Width, Length, stations, StripBeamSupportScheme.SimplySupported, UniformLoad(NonlinearQ),
            null, new StripNewtonOptions(ModifiedNewton: true, MaxIterations: 200));

        Assert.True(full.IsCalculable);
        Assert.True(modified.IsCalculable, string.Join("; ", modified.Diagnostics.Select(d => d.Message)));
        for (int i = 0; i < full.Displacements.Length; i++)
            Assert.Equal(full.Displacements[i], modified.Displacements[i], 6);
        Assert.True(modified.TotalIterations >= full.TotalIterations);
    }

    /// <summary>Ловушка Среза 6, первая половина: при тождественно нулевой осевой компоненте
    /// масштаб не должен схлопываться на абсолютный пол.</summary>
    [Fact]
    public void PureBending_WithoutAxialComponent_Converges()
    {
        var stations = Stations(4);
        var result = StripBeamNonlinearModel.Solve(
            Grid(Linear(), 4), Width, Length, stations,
            StripBeamSupportScheme.SimplySupported, UniformLoad(10.0));

        Assert.True(result.IsCalculable);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "plate_strip_nonlinear_singular_tangent");
    }

    /// <summary>Ловушка Среза 6, вторая половина: при большой длине немасштабированная норма
    /// позволила бы моментным компонентам замаскировать несходимость осевой задачи.</summary>
    [Fact]
    public void LongStrip_WithAxialAndBendingLoad_ConvergesOnBothComponents()
    {
        const double length = 20.0;
        var stations = Stations(6);
        var loads = new StripLoadSet(
        [
            new StripLoad { SourceTag = "q", Kind = StripLoadKind.DistributedUniform, QzKnM = -10.0 },
            new StripLoad { SourceTag = "n", Kind = StripLoadKind.DistributedUniform, QxKnM = 5.0 }
        ]);

        var source = Linear();
        var tangent = NonlinearStripSection.Evaluate(Width, [source, source], BeamStrainState.Zero).K;
        var linear = StripBeamModel.Solve(tangent, length, stations, StripBeamSupportScheme.SimplySupported, loads);
        var nonlinear = StripBeamNonlinearModel.Solve(
            Grid(source, 6), Width, length, stations, StripBeamSupportScheme.SimplySupported, loads);

        Assert.True(nonlinear.IsCalculable, string.Join("; ", nonlinear.Diagnostics.Select(d => d.Message)));
        for (int s = 0; s < linear.StationResultants.Length; s++)
            Assert.Equal(linear.StationResultants[s][0], nonlinear.StationResultants[s][0], 7);
    }

    [Fact]
    public void SourceGridMismatch_IsRejected()
    {
        var stations = Stations(4);
        var result = StripBeamNonlinearModel.Solve(
            Grid(Linear(), 3), Width, Length, stations,
            StripBeamSupportScheme.SimplySupported, UniformLoad());

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_source_grid_shape_mismatch");
    }

    [Fact]
    public void NonConvergingSource_ReportsDiagnosticInsteadOfThrowing()
    {
        var stations = Stations(4);
        var grid = Grid(Softening(), 4);

        var result = StripBeamNonlinearModel.Solve(
            grid, Width, Length, stations, StripBeamSupportScheme.SimplySupported, UniformLoad(4000.0),
            null, new StripNewtonOptions(MaxIterations: 8, MaxStepHalvings: 1));

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_nonlinear_not_converged");
        Assert.True(result.AchievedLoadFactor < 1.0);
    }

    [Fact]
    public void OutOfBoundsSource_BecomesDiagnostic()
    {
        var stations = Stations(1);
        var grid = StripSectionSourceGrid.Uniform([new ThrowingSource(), new ThrowingSource()], 1);

        var result = StripBeamNonlinearModel.Solve(
            grid, Width, Length, stations, StripBeamSupportScheme.SimplySupported, UniformLoad());

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_nonlinear_state_out_of_bounds");
    }

    [Fact]
    public void NonzeroInitialForces_AreReportedAsWarning()
    {
        var stations = Stations(1);
        var grid = StripSectionSourceGrid.Uniform([new PrestressedSource(), new PrestressedSource()], 1);

        var result = StripBeamNonlinearModel.Solve(
            grid, Width, Length, stations, StripBeamSupportScheme.SimplySupported, UniformLoad());

        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_nonlinear_nonzero_initial_forces");
        Assert.All(result.Diagnostics.Where(d => d.Code == "plate_strip_nonlinear_nonzero_initial_forces"),
            d => Assert.False(d.IsError));
    }

    [Fact]
    public void InvalidArguments_Throw()
    {
        var stations = Stations(1);
        var grid = Grid(Linear(), 1);
        var scheme = StripBeamSupportScheme.SimplySupported;

        Assert.Throws<ArgumentNullException>(() => StripBeamNonlinearModel.Solve(
            null!, Width, Length, stations, scheme, UniformLoad()));
        Assert.Throws<ArgumentOutOfRangeException>(() => StripBeamNonlinearModel.Solve(
            grid, 0.0, Length, stations, scheme, UniformLoad()));
        Assert.Throws<ArgumentOutOfRangeException>(() => StripBeamNonlinearModel.Solve(
            grid, Width, 0.0, stations, scheme, UniformLoad()));
        Assert.Throws<ArgumentException>(() => StripBeamNonlinearModel.Solve(
            grid, Width, Length, [0.0], scheme, UniformLoad()));
        Assert.Throws<ArgumentOutOfRangeException>(() => StripBeamNonlinearModel.Solve(
            grid, Width, Length, stations, scheme, UniformLoad(), null, new StripNewtonOptions(LoadSteps: 0)));
    }

    static StripSectionSourceGrid Grid(IPlateSectionResponse source, int elementCount) =>
        StripSectionSourceGrid.Uniform([source, source], elementCount);

    static List<double> Stations(int elementCount)
    {
        var stations = new List<double>(elementCount + 1);
        for (int i = 0; i <= elementCount; i++)
            stations.Add((double)i / elementCount);
        return stations;
    }

    static StripLoadSet UniformLoad(double qz = 10.0) =>
        new([new StripLoad { SourceTag = "q", Kind = StripLoadKind.DistributedUniform, QzKnM = -qz }]);

    static ConstantLinearPlateSectionResponse Linear()
    {
        var a = new double[3, 3];
        var b = new double[3, 3];
        var d = new double[3, 3];
        var ass = new double[2, 2];
        a[0, 0] = E * H;
        a[1, 1] = E * H;
        d[0, 0] = E * H * H * H / 12.0;
        d[1, 1] = E * H * H * H / 12.0;
        ass[0, 0] = ass[1, 1] = 400.0;
        return new ConstantLinearPlateSectionResponse(a, b, d, ass, "linear");
    }

    /// <summary>Линейный источник с той же начальной жёсткостью, что у нелинейного, — эталон
    /// сравнения «до/после излома».</summary>
    static IPlateSectionResponse LinearLike()
    {
        var source = Nonlinear();
        var tangent = source.Tangent(ShellStrainState.Zero);
        return new ConstantLinearPlateSectionResponse(tangent.A, tangent.B, tangent.D, tangent.As, "linear-like");
    }

    static PlateSectionLiveResponse Nonlinear() => Live(ry: 30.0, hardeningRatio: 0.1);

    /// <summary>Источник с падающей ветвью — Ньютон на нём законно расходится.</summary>
    static PlateSectionLiveResponse Softening() => Live(ry: 3.0, hardeningRatio: 0.0);

    static PlateSectionLiveResponse Live(double ry, double hardeningRatio)
    {
        MaterialChars Ch(CalcType ct) => new(ct)
        {
            E = E, Ry = ry, Ru = ry * (1.0 + hardeningRatio), Ft = ry, Fc = -ry,
            Ec2 = -0.05, Et2 = 0.05, Type = MatType.ReSteelF,
        };
        var m = new Material { Id = 1, E = E, Type = MatType.ReSteelF, Tag = "bilinear" };
        m.MaterialChars = [Ch(CalcType.C), Ch(CalcType.CL), Ch(CalcType.N), Ch(CalcType.NL)];
        var diagram = m.GetDiagramms(DiagrammType.L2)![CalcType.C];
        var section = new PlateSection { H = H, NLayers = 12, TensionConcrete = true, PlateModel = "layered" };
        return new PlateSectionLiveResponse(section, diagram, diagram);
    }

    sealed class ThrowingSource : IPlateSectionResponse
    {
        public EquivalentSectionSourceKind SourceKind => EquivalentSectionSourceKind.ConstantLinear;
        public string Fingerprint => "throwing";
        public PlateResultants Forces(ShellStrainState state) =>
            throw new ArgumentOutOfRangeException(nameof(state), "Состояние вне рабочих границ источника.");
        public PlateShellTangentResult Tangent(ShellStrainState state) =>
            throw new ArgumentOutOfRangeException(nameof(state), "Состояние вне рабочих границ источника.");
    }

    /// <summary>Источник с ненулевыми усилиями в нулевом состоянии (аналог преднапряжения).</summary>
    sealed class PrestressedSource : IPlateSectionResponse
    {
        public EquivalentSectionSourceKind SourceKind => EquivalentSectionSourceKind.ConstantLinear;
        public string Fingerprint => "prestressed";

        public PlateResultants Forces(ShellStrainState state) =>
            new(E * H * state.Eps0x + 100.0, 0, 0, E * H * H * H / 12.0 * state.Kx, 0, 0);

        public PlateShellTangentResult Tangent(ShellStrainState state)
        {
            var a = new double[3, 3];
            var b = new double[3, 3];
            var d = new double[3, 3];
            var ass = new double[2, 2];
            a[0, 0] = E * H;
            d[0, 0] = E * H * H * H / 12.0;
            ass[0, 0] = ass[1, 1] = 400.0;
            var forces = Forces(state);
            return new PlateShellTangentResult
            {
                Nx = forces.Nx, Ny = forces.Ny, Nxy = forces.Nxy,
                Mx = forces.Mx, My = forces.My, Mxy = forces.Mxy,
                A = a, B = b, D = d, As = ass,
            };
        }
    }
}
