using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class StripParentEndActionsTests
{
    const double Width = 2.0;
    const double Length = 6.0;
    const double E = 30_000.0;
    const double H = 0.3;
    const double Q = 10.0;

    static StripSupportDerivationResult Derivation(
        StripEndActionComponents start, StripEndActionComponents end,
        StripBeamSupportScheme? scheme = null) =>
        new(true, scheme ?? StripBeamSupportScheme.SimplySupported,
            new StripEndDerivation([], StripBeamEndCondition.Pinned, start, StripSupportKind.Column),
            new StripEndDerivation([], StripBeamEndCondition.Pinned, end, StripSupportKind.Column),
            []);

    static readonly StripEndActionComponents All =
        StripEndActionComponents.N | StripEndActionComponents.My | StripEndActionComponents.Mz;

    [Fact]
    public void Mask_ZeroesComponentsNotTransferred()
    {
        var actions = StripParentEndActions.From(
            [1, 2, 3], [4, 5, 6],
            Derivation(StripEndActionComponents.My | StripEndActionComponents.Mz, StripEndActionComponents.N));

        Assert.Equal(new KnownEndActions(0, 2, 3, 4, 0, 0), actions);
    }

    [Theory]
    [InlineData(false, 3, 0.0)]
    [InlineData(true, 2, 0.0)]
    [InlineData(true, 3, double.NaN)]
    public void TryFrom_InvalidDiagram_ReturnsDiagnosticWithoutThrowing(bool present, int length, double value)
    {
        double[]? diagram = present ? Enumerable.Repeat(value, length).ToArray() : null;

        bool ok = StripParentEndActions.TryFrom(
            diagram, [0, 0, 0], Derivation(All, All), out var actions, out var diagnostics);

        Assert.False(ok);
        Assert.Null(actions);
        Assert.Contains(diagnostics, d => d.Code == "plate_strip_parent_end_actions_invalid");
    }

    [Fact]
    public void TryFrom_NotCalculableDerivation_ReturnsDiagnostic()
    {
        var failed = new StripSupportDerivationResult(false, null, null, null, []);

        Assert.False(StripParentEndActions.TryFrom([0, 0, 0], [0, 0, 0], failed, out _, out var diagnostics));
        Assert.Single(diagnostics);
        Assert.Throws<ArgumentException>(() => StripParentEndActions.From([0, 0, 0], [0, 0, 0], failed));
    }

    /// <summary>Сквозная проверка знаков: эпюра защемлённой балки, перенесённая концевыми
    /// значениями на шарнирную балку, воспроизводит защемлённую во всех станциях. Если бы
    /// конвенция KnownEndActions расходилась с эпюрой, концевые моменты удвоились бы или
    /// обнулились.</summary>
    [Fact]
    public void FixedDiagram_AppliedToPinnedBeam_ReproducesFixedBeam()
    {
        var stations = Stations(8);
        var loads = UniformLoad();
        var tangent = NonlinearStripSection.Evaluate(Width, [Linear(), Linear()], BeamStrainState.Zero).K;
        var fixedScheme = new StripBeamSupportScheme(
            StripBeamEndCondition.Fixed, StripBeamEndCondition.Fixed, StripAxialRestraint.StartOnly);

        var fixedBeam = StripBeamModel.Solve(tangent, Length, stations, fixedScheme, loads);
        var actions = StripParentEndActions.From(
            fixedBeam.StationResultants[0], fixedBeam.StationResultants[^1], Derivation(All, All));
        var pinned = StripBeamModel.Solve(
            tangent, Length, stations, StripBeamSupportScheme.SimplySupported, loads, actions);

        double qL2 = Q * Length * Length;
        Assert.Equal(qL2 / 12.0, Math.Abs(fixedBeam.StationResultants[0][1]), 6);
        Assert.Equal(qL2 / 24.0, Math.Abs(fixedBeam.StationResultants[4][1]), 6);
        AssertSameDiagram(fixedBeam.StationResultants, pinned.StationResultants);

        var nonlinear = StripBeamNonlinearModel.Solve(
            StripSectionSourceGrid.Uniform([Linear(), Linear()], stations.Count - 1),
            Width, Length, stations, StripBeamSupportScheme.SimplySupported, loads, actions);
        Assert.True(nonlinear.IsCalculable, string.Join("; ", nonlinear.Diagnostics.Select(d => d.Message)));
        AssertSameDiagram(fixedBeam.StationResultants, nonlinear.StationResultants);
    }

    /// <summary>Концевые станции эпюры родителя смещены сэмплингом (усреднение по элементу у линии
    /// опоры): подгонка по внутренним станциям обязана восстановить точные концевые моменты.</summary>
    [Fact]
    public void TryFit_IgnoresBiasedEndStationsAndRecoversEndMoments()
    {
        var stations = Stations(8);
        var loads = UniformLoad();
        var tangent = NonlinearStripSection.Evaluate(Width, [Linear(), Linear()], BeamStrainState.Zero).K;
        var fixedScheme = new StripBeamSupportScheme(
            StripBeamEndCondition.Fixed, StripBeamEndCondition.Fixed, StripAxialRestraint.StartOnly);
        var parent = StripBeamModel.Solve(tangent, Length, stations, fixedScheme, loads).StationResultants
            .Select(r => (double[])r.Clone()).ToArray();
        parent[0][1] *= 0.8;
        parent[^1][1] *= 0.8;

        bool ok = StripParentEndActions.TryFit(stations, parent, loads, Length, Derivation(All, All),
            out var actions, out var diagnostics);

        Assert.True(ok, string.Join("; ", diagnostics.Select(d => d.Message)));
        double qL2 = Q * Length * Length;
        Assert.Equal(qL2 / 12.0, Math.Abs(actions!.StartMy), 6);
        Assert.Equal(qL2 / 12.0, Math.Abs(actions.EndMy), 6);
        Assert.Equal(Math.Sign(parent[1][1]), Math.Sign(actions.StartMy));
        Assert.Contains(diagnostics, d => d.Code == "plate_strip_parent_end_actions_fitted" && !d.IsError);
    }

    [Fact]
    public void TryFit_AppliesTransferMask()
    {
        var stations = Stations(4);
        var parent = stations.Select(_ => new[] { 1.0, 5.0, 2.0 }).ToArray();

        StripParentEndActions.TryFit(stations, parent, new StripLoadSet([]), Length,
            Derivation(StripEndActionComponents.None, StripEndActionComponents.N), out var actions, out _);

        Assert.Equal(new KnownEndActions(EndN: 1.0), actions);
    }

    [Theory]
    [InlineData(2)] // одна внутренняя станция
    [InlineData(1)] // ни одной
    public void TryFit_TooFewInteriorStations_ReturnsDiagnostic(int elements)
    {
        var stations = Stations(elements);
        var parent = stations.Select(_ => new double[3]).ToArray();

        Assert.False(StripParentEndActions.TryFit(stations, parent, new StripLoadSet([]), Length,
            Derivation(All, All), out var actions, out var diagnostics));
        Assert.Null(actions);
        Assert.Contains(diagnostics, d => d.Code == "plate_strip_parent_end_actions_invalid");
    }

    [Fact]
    public void TryFit_DiagramCountMismatch_ReturnsDiagnostic()
    {
        Assert.False(StripParentEndActions.TryFit(Stations(4), [new double[3]], new StripLoadSet([]), Length,
            Derivation(All, All), out _, out var diagnostics));
        Assert.Contains(diagnostics, d => d.Code == "plate_strip_parent_end_actions_invalid");
    }

    static void AssertSameDiagram(double[][] expected, double[][] actual)
    {
        double scale = expected.Max(s => Math.Abs(s[1]));
        for (int s = 0; s < expected.Length; s++)
            Assert.True(Math.Abs(expected[s][1] - actual[s][1]) <= 1e-9 * scale,
                $"Станция {s}: ожидалось My={expected[s][1]:G10}, получено {actual[s][1]:G10}.");
    }

    static List<double> Stations(int elementCount) =>
        Enumerable.Range(0, elementCount + 1).Select(i => (double)i / elementCount).ToList();

    static StripLoadSet UniformLoad() =>
        new([new StripLoad { SourceTag = "q", Kind = StripLoadKind.DistributedUniform, QzKnM = -Q }]);

    static ConstantLinearPlateSectionResponse Linear()
    {
        var a = new double[3, 3];
        var b = new double[3, 3];
        var d = new double[3, 3];
        var ass = new double[2, 2];
        a[0, 0] = a[1, 1] = E * H;
        d[0, 0] = d[1, 1] = E * H * H * H / 12.0;
        ass[0, 0] = ass[1, 1] = 400.0;
        return new ConstantLinearPlateSectionResponse(a, b, d, ass, "linear");
    }
}
