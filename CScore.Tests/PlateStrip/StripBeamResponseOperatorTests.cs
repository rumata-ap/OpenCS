using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class LoadBasisLayoutTests
{
    static readonly double[] Stations = [0.0, 0.25, 0.6, 1.0];

    [Fact]
    public void Layout_FunctionCountsAndOffsets_FollowBasisContract()
    {
        var constant = new LoadBasisLayout(LoadBasis.PiecewiseConstant, Stations);
        var linear = new LoadBasisLayout(LoadBasis.PiecewiseLinear, Stations);

        Assert.Equal(3, constant.FunctionsPerComponent);   // n−1
        Assert.Equal(9, constant.CoefficientCount);
        Assert.Equal(4, linear.FunctionsPerComponent);     // n
        Assert.Equal(12, linear.CoefficientCount);

        Assert.Equal(0, linear.ComponentOffset(0));
        Assert.Equal(4, linear.ComponentOffset(1));
        Assert.Equal(8, linear.ComponentOffset(2));
    }

    [Fact]
    public void CoefficientPositions_AreStationsForHatsAndMidpointsForConstants()
    {
        Assert.Equal(Stations, new LoadBasisLayout(LoadBasis.PiecewiseLinear, Stations)
            .CoefficientPositions());
        Assert.Equal(new[] { 0.125, 0.425, 0.8 },
            new LoadBasisLayout(LoadBasis.PiecewiseConstant, Stations).CoefficientPositions());
    }

    [Fact]
    public void ToLoads_PiecewiseConstant_EmitsUniformLoadPerInterval()
    {
        var layout = new LoadBasisLayout(LoadBasis.PiecewiseConstant, Stations);
        var coefficients = new double[9];
        coefficients[layout.ComponentOffset(2) + 1] = -3.0;   // qz на втором интервале

        var loads = layout.ToLoads(coefficients, "test");

        var load = Assert.Single(loads);
        Assert.Equal(StripLoadKind.DistributedUniform, load.Kind);
        Assert.Equal(0.25, load.StationStartFraction);
        Assert.Equal(0.6, load.StationEndFraction);
        Assert.Equal(-3.0, load.QzKnM);
        Assert.Equal(0.0, load.QzEndKnM);
        load.Validate();
    }

    [Fact]
    public void ToLoads_PiecewiseLinear_EmitsLinearLoadWithHatEndpoints()
    {
        var layout = new LoadBasisLayout(LoadBasis.PiecewiseLinear, Stations);
        var coefficients = new double[12];
        coefficients[layout.ComponentOffset(2) + 1] = 4.0;    // hat-функция станции 1

        var loads = layout.ToLoads(coefficients, "test");

        Assert.Equal(2, loads.Count);
        Assert.Equal(StripLoadKind.DistributedLinear, loads[0].Kind);
        Assert.Equal(0.0, loads[0].QzKnM);
        Assert.Equal(4.0, loads[0].QzEndKnM);
        Assert.Equal(4.0, loads[1].QzKnM);
        Assert.Equal(0.0, loads[1].QzEndKnM);
        foreach (var load in loads)
            load.Validate();
    }

    [Fact]
    public void ToLoads_WrongCoefficientCount_Throws()
    {
        var layout = new LoadBasisLayout(LoadBasis.PiecewiseLinear, Stations);
        Assert.Throws<ArgumentException>(() => layout.ToLoads(new double[5], "test"));
    }
}

public sealed class StripBeamResponseOperatorTests
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

    static StripBeamResponseOperator Build(
        LoadBasis basis = LoadBasis.PiecewiseLinear,
        LoadRecoveryMode mode = LoadRecoveryMode.WeakEquilibriumProjection,
        double[,]? tangent = null,
        KnownEndActions? endActions = null) =>
        StripBeamResponseOperatorBuilder.Build(
            tangent ?? Diagonal(), LengthM, new LoadBasisLayout(basis, Stations),
            StripBeamSupportScheme.SimplySupported, mode, endActions);

    [Fact]
    public void Build_ResponseColumns_MatchDirectBeamRunPerComponent()
    {
        var layout = new LoadBasisLayout(LoadBasis.PiecewiseLinear, Stations);
        var operatorResult = Build();

        Assert.True(operatorResult.IsCalculable);

        // Отдельная проверка каждой компоненты ловит перестановку qy/qz и ошибочный знак момента.
        for (int component = 0; component < 3; component++)
        {
            int column = layout.ComponentOffset(component) + 2;
            var unit = new StripLoadSet(layout.UnitLoads(column));
            var direct = StripBeamModel.Solve(
                Diagonal(), LengthM, Stations, StripBeamSupportScheme.SimplySupported, unit);

            Assert.True(direct.IsCalculable);
            for (int station = 0; station < Stations.Length; station++)
            for (int row = 0; row < 3; row++)
                Assert.Equal(direct.StationResultants[station][row],
                    operatorResult.Response[3 * station + row, column], 9);
        }
    }

    [Fact]
    public void Build_ResponseIsLinear_SuperpositionMatchesSingleRun()
    {
        var layout = new LoadBasisLayout(LoadBasis.PiecewiseLinear, Stations);
        var operatorResult = Build();

        var coefficients = new double[layout.CoefficientCount];
        for (int i = 0; i < coefficients.Length; i++)
            coefficients[i] = 0.5 + 0.25 * i - 0.1 * i * i;

        var combined = StripBeamModel.Solve(
            Diagonal(), LengthM, Stations, StripBeamSupportScheme.SimplySupported,
            new StripLoadSet(layout.ToLoads(coefficients, "combined")));

        Assert.True(combined.IsCalculable);
        for (int station = 0; station < Stations.Length; station++)
        for (int row = 0; row < 3; row++)
        {
            double superposed = 0.0;
            for (int column = 0; column < layout.CoefficientCount; column++)
                superposed += operatorResult.Response[3 * station + row, column] * coefficients[column];

            Assert.Equal(combined.StationResultants[station][row], superposed, 6);
        }
    }

    [Fact]
    public void Build_Constraints_ReproduceUniformLoadResultants()
    {
        var layout = new LoadBasisLayout(LoadBasis.PiecewiseConstant, Stations);
        var operatorResult = Build(LoadBasis.PiecewiseConstant);

        // Единичная qz на всех интервалах = равномерная нагрузка 1 кН/м по всей полосе.
        double totalFz = 0.0;
        double totalMy = 0.0;
        for (int interval = 0; interval < layout.IntervalCount; interval++)
        {
            int column = layout.ComponentOffset(2) + interval;
            totalFz += operatorResult.Constraints[2, column];
            totalMy += operatorResult.Constraints[3, column];
        }

        Assert.Equal(LengthM, totalFz, 9);                        // q·L
        Assert.Equal(-LengthM * LengthM / 2.0, totalMy, 9);       // -q·L²/2
    }

    [Fact]
    public void Build_BaseResponse_ReproducesConstantMomentFromEndActions()
    {
        const double moment = 7.0;
        var operatorResult = Build(endActions: new KnownEndActions(StartMy: moment, EndMy: moment));

        Assert.True(operatorResult.IsCalculable);
        for (int station = 0; station < Stations.Length; station++)
            Assert.Equal(moment, operatorResult.BaseResponse[3 * station + 1], 8);
    }

    [Fact]
    public void Build_BaseResponse_IsZeroWithoutEndActions()
    {
        var operatorResult = Build();
        Assert.All(operatorResult.BaseResponse, value => Assert.Equal(0.0, value));
    }

    [Fact]
    public void Build_SmoothingOperator_IsSpacingAwareFirstDifference()
    {
        double[] irregular = [0.0, 0.1, 1.0];
        var layout = new LoadBasisLayout(LoadBasis.PiecewiseLinear, irregular);
        var operatorResult = StripBeamResponseOperatorBuilder.Build(
            Diagonal(), LengthM, layout, StripBeamSupportScheme.SimplySupported,
            LoadRecoveryMode.WeakEquilibriumProjection, null);

        Assert.True(operatorResult.IsCalculable);
        double h1 = 0.1 * LengthM;
        Assert.Equal(-1.0 / h1, operatorResult.Smoothing[0, 0], 12);
        Assert.Equal(1.0 / h1, operatorResult.Smoothing[0, 1], 12);

        double h2 = 0.9 * LengthM;
        Assert.Equal(-1.0 / h2, operatorResult.Smoothing[1, 1], 12);
        Assert.Equal(1.0 / h2, operatorResult.Smoothing[1, 2], 12);
    }

    [Fact]
    public void Build_SmoothingOperator_SecondDifferenceForSmoothFit()
    {
        var operatorResult = Build(mode: LoadRecoveryMode.ConstrainedSmoothFit);
        var layout = new LoadBasisLayout(LoadBasis.PiecewiseLinear, Stations);

        // n−2 строки на компоненту, равномерная сетка: [1, −2, 1]/h².
        Assert.Equal(3 * (layout.FunctionsPerComponent - 2), operatorResult.Smoothing.GetLength(0));
        double h = 0.25 * LengthM;
        Assert.Equal(1.0 / (h * h), operatorResult.Smoothing[0, 0], 10);
        Assert.Equal(-2.0 / (h * h), operatorResult.Smoothing[0, 1], 10);
        Assert.Equal(1.0 / (h * h), operatorResult.Smoothing[0, 2], 10);
    }

    [Fact]
    public void Build_SmoothingOperator_IsEmptyForUnregularizedModes()
    {
        Assert.Equal(0, Build(mode: LoadRecoveryMode.PiecewiseUniformOrLinear)
            .Smoothing.GetLength(0));
    }

    [Fact]
    public void Build_DegenerateSection_ReturnsDiagnosticInsteadOfThrowing()
    {
        var result = Build(tangent: new double[3, 3]);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_load_recovery_singular_system");
    }
}
