using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class StripBeamModelTests
{
    const double Ea = 3.0e5;
    const double Eiy = 2.0e4;
    const double Eiz = 1.5e4;
    const double LengthM = 6.0;

    static double[,] Diagonal() => new double[,]
    {
        { Ea, 0, 0 },
        { 0, Eiy, 0 },
        { 0, 0, Eiz }
    };

    static readonly double[] Stations = [0.0, 0.25, 0.5, 0.75, 1.0];

    static StripLoadSet Uniform(double qx = 0.0, double qy = 0.0, double qz = 0.0) =>
        new([new StripLoad
        {
            Kind = StripLoadKind.DistributedUniform,
            SourceTag = "udl",
            StationStartFraction = 0.0,
            StationEndFraction = 1.0,
            QxKnM = qx,
            QyKnM = qy,
            QzKnM = qz
        }]);

    [Fact]
    public void BuildConstraintMask_PinnedPinnedStartOnly_FixesTransverseAtBothEndsAndAxialAtStart()
    {
        var mask = StripBeamModel.BuildConstraintMask(StripBeamSupportScheme.SimplySupported, 3);

        // Узел 0: u, v, w закреплены; ротации свободны.
        Assert.True(mask[0]);
        Assert.True(mask[1]);
        Assert.True(mask[2]);
        Assert.False(mask[3]);
        Assert.False(mask[4]);

        // Внутренний узел свободен полностью.
        for (int i = 5; i < 10; i++)
            Assert.False(mask[i]);

        // Узел 2: v, w закреплены, u — нет (StartOnly), ротации свободны.
        Assert.False(mask[10]);
        Assert.True(mask[11]);
        Assert.True(mask[12]);
        Assert.False(mask[13]);
        Assert.False(mask[14]);
    }

    [Fact]
    public void BuildConstraintMask_BothEndsAxialRestraint_FixesAxialAtEnd()
    {
        var scheme = new StripBeamSupportScheme(
            StripBeamEndCondition.Pinned, StripBeamEndCondition.Pinned, StripAxialRestraint.BothEnds);

        var mask = StripBeamModel.BuildConstraintMask(scheme, 2);

        Assert.True(mask[0]);
        Assert.True(mask[5]);
    }

    [Fact]
    public void BuildConstraintMask_FixedEnds_AlsoFixRotations()
    {
        var scheme = new StripBeamSupportScheme(
            StripBeamEndCondition.Fixed, StripBeamEndCondition.Fixed, StripAxialRestraint.StartOnly);

        var mask = StripBeamModel.BuildConstraintMask(scheme, 2);

        Assert.True(mask[3]);
        Assert.True(mask[4]);
        Assert.True(mask[8]);
        Assert.True(mask[9]);
    }

    [Fact]
    public void Solve_SimplySupportedUniformTransverse_MatchesAnalyticMoment()
    {
        const double q = 5.0;
        var result = StripBeamModel.Solve(
            Diagonal(), LengthM, Stations, StripBeamSupportScheme.SimplySupported, Uniform(qz: q));

        Assert.True(result.IsCalculable);
        Assert.Equal(0.0, result.StationResultants[0][1], 9);
        Assert.Equal(q * LengthM * LengthM / 8.0, result.StationResultants[2][1], 9);
        Assert.Equal(0.0, result.StationResultants[4][1], 9);

        // Эпюра q·x·(L−x)/2 в промежуточных станциях.
        double x = 0.25 * LengthM;
        Assert.Equal(q * x * (LengthM - x) / 2.0, result.StationResultants[1][1], 9);
    }

    [Fact]
    public void Solve_FixedFixedUniformTransverse_MatchesAnalyticMoment()
    {
        const double q = 5.0;
        var scheme = new StripBeamSupportScheme(
            StripBeamEndCondition.Fixed, StripBeamEndCondition.Fixed, StripAxialRestraint.StartOnly);

        var result = StripBeamModel.Solve(Diagonal(), LengthM, Stations, scheme, Uniform(qz: q));

        Assert.True(result.IsCalculable);
        Assert.Equal(-q * LengthM * LengthM / 12.0, result.StationResultants[0][1], 9);
        Assert.Equal(q * LengthM * LengthM / 24.0, result.StationResultants[2][1], 9);
        Assert.Equal(-q * LengthM * LengthM / 12.0, result.StationResultants[4][1], 9);
    }

    [Fact]
    public void Solve_FixedPinnedUniformTransverse_MatchesProppedCantileverMoment()
    {
        const double q = 5.0;
        var scheme = new StripBeamSupportScheme(
            StripBeamEndCondition.Fixed, StripBeamEndCondition.Pinned, StripAxialRestraint.StartOnly);

        var result = StripBeamModel.Solve(Diagonal(), LengthM, Stations, scheme, Uniform(qz: q));

        Assert.True(result.IsCalculable);
        Assert.Equal(-q * LengthM * LengthM / 8.0, result.StationResultants[0][1], 9);
        Assert.Equal(0.0, result.StationResultants[4][1], 9);

        double x = 0.5 * LengthM;
        double expected = -q * LengthM * LengthM / 8.0 + 5.0 * q * LengthM * x / 8.0 - q * x * x / 2.0;
        Assert.Equal(expected, result.StationResultants[2][1], 9);
    }

    [Fact]
    public void Solve_UniformAxialLoad_StartOnlyRestraint_GivesLinearNormalForce()
    {
        const double q = 3.0;
        var result = StripBeamModel.Solve(
            Diagonal(), LengthM, Stations, StripBeamSupportScheme.SimplySupported, Uniform(qx: q));

        Assert.True(result.IsCalculable);
        Assert.Equal(q * LengthM, result.StationResultants[0][0], 9);
        Assert.Equal(q * LengthM / 2.0, result.StationResultants[2][0], 9);
        Assert.Equal(0.0, result.StationResultants[4][0], 9);
    }

    [Fact]
    public void Solve_UniformInPlaneLoad_MatchesAnalyticMomentAboutZ()
    {
        const double q = 4.0;
        var result = StripBeamModel.Solve(
            Diagonal(), LengthM, Stations, StripBeamSupportScheme.SimplySupported, Uniform(qy: q));

        Assert.True(result.IsCalculable);
        Assert.Equal(0.0, result.StationResultants[0][2], 9);
        Assert.Equal(-q * LengthM * LengthM / 8.0, result.StationResultants[2][2], 9);
    }

    [Fact]
    public void Solve_LengthScaling_MomentScalesWithSquareOfLength()
    {
        const double q = 5.0;
        double[] lengths = [1.0, 10.0];

        foreach (double l in lengths)
        {
            var result = StripBeamModel.Solve(
                Diagonal(), l, Stations, StripBeamSupportScheme.SimplySupported, Uniform(qz: q));

            Assert.True(result.IsCalculable);
            Assert.Equal(q * l * l / 8.0, result.StationResultants[2][1], 9);
        }
    }

    [Fact]
    public void Solve_IrregularStations_StillMatchesAnalyticMoment()
    {
        const double q = 5.0;
        double[] stations = [0.0, 0.05, 0.4, 0.5, 0.93, 1.0];

        var result = StripBeamModel.Solve(
            Diagonal(), LengthM, stations, StripBeamSupportScheme.SimplySupported, Uniform(qz: q));

        Assert.True(result.IsCalculable);
        for (int i = 0; i < stations.Length; i++)
        {
            double x = stations[i] * LengthM;
            Assert.Equal(q * x * (LengthM - x) / 2.0, result.StationResultants[i][1], 8);
        }
    }

    [Fact]
    public void Solve_InteriorStation_LeftAndRightElementsGiveSameResultant()
    {
        const double q = 5.0;
        var tangent = Diagonal();
        var result = StripBeamModel.Solve(
            tangent, LengthM, Stations, StripBeamSupportScheme.SimplySupported, Uniform(qz: q));

        Assert.True(result.IsCalculable);

        // Модель берёт значение с левого элемента; пересчитываем то же сечение с правого.
        const int station = 2;
        var projection = StripLoadConsistentNodalProjection.Project(
            Uniform(qz: q), LengthM, Stations);
        double le = (Stations[station + 1] - Stations[station]) * LengthM;
        var k = StripBeamElement.Stiffness(tangent, le);
        var fe = StripBeamElement.LoadVector(projection.Elements[station]);

        var g = new double[StripBeamElement.DofPerElement];
        int offset = station * StripBeamElement.DofPerNode;
        for (int i = 0; i < StripBeamElement.DofPerElement; i++)
        {
            double sum = -fe[i];
            for (int j = 0; j < StripBeamElement.DofPerElement; j++)
                sum += k[i, j] * result.Displacements[offset + j];
            g[i] = sum;
        }

        Assert.Equal(result.StationResultants[station][0], -g[0], 8);
        Assert.Equal(result.StationResultants[station][1], -g[3], 8);
        Assert.Equal(result.StationResultants[station][2], -g[4], 8);
    }

    [Fact]
    public void Solve_CoupledSectionTangent_DoesNotSplitIntoIndependentProblems()
    {
        // Связь N–My: осевая нагрузка обязана породить изгибные перемещения. Реализация,
        // решающая три независимые скалярные задачи, дала бы здесь строгий ноль.
        var coupled = new double[,]
        {
            { Ea, 0.3 * Eiy, 0 },
            { 0.3 * Eiy, Eiy, 0 },
            { 0, 0, Eiz }
        };

        var result = StripBeamModel.Solve(
            coupled, LengthM, Stations, StripBeamSupportScheme.SimplySupported, Uniform(qx: 3.0));

        Assert.True(result.IsCalculable);

        double maxW = 0.0;
        for (int node = 0; node < Stations.Length; node++)
            maxW = Math.Max(maxW, Math.Abs(result.Displacements[node * StripBeamElement.DofPerNode + 2]));
        Assert.True(maxW > 1e-12, "Связанная матрица сечения обязана дать изгибный отклик.");

        // При шарнирных концах и отсутствии поперечной нагрузки эпюра My тождественно нулевая.
        foreach (var station in result.StationResultants)
            Assert.Equal(0.0, station[1], 8);
    }

    [Fact]
    public void Solve_DegenerateSectionTangent_ReturnsSingularDiagnostic()
    {
        var result = StripBeamModel.Solve(
            new double[3, 3], LengthM, Stations, StripBeamSupportScheme.SimplySupported, Uniform(qz: 5.0));

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_load_recovery_singular_system");
    }

    [Fact]
    public void Solve_InvalidLoadProjection_PropagatesDiagnostics()
    {
        var bad = new StripLoadSet([new StripLoad
        {
            Kind = StripLoadKind.DistributedUniform,
            SourceTag = "bad",
            StationStartFraction = 0.0,
            StationEndFraction = 1.0,
            QzKnM = double.NaN
        }]);

        var result = StripBeamModel.Solve(
            Diagonal(), LengthM, Stations, StripBeamSupportScheme.SimplySupported, bad);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_load_invalid_input");
    }

    [Fact]
    public void Solve_NonFiniteSectionTangent_Throws()
    {
        var tangent = Diagonal();
        tangent[1, 1] = double.NaN;

        Assert.Throws<ArgumentException>(() => StripBeamModel.Solve(
            tangent, LengthM, Stations, StripBeamSupportScheme.SimplySupported, Uniform(qz: 1.0)));
    }
}
