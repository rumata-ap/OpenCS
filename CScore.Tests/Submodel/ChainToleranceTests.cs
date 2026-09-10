using CScore.Planar;
using CScore.Submodel;
using Xunit;

namespace CScore.Tests.Submodel;

public sealed class ChainToleranceTests
{
    [Fact]
    public void CharacteristicSize_IsSumOfLengths_NotBoundingBoxDiagonal()
    {
        var segments = new List<BeamSegmentInput>
        {
            RigidTransformFixture.Segment("1", 0, 5),
            RigidTransformFixture.Segment("2", 105, 110)
        };

        Assert.Equal(10.0, SegmentInputValidation.CharacteristicSize(segments), 9);
    }

    [Fact]
    public void CharacteristicSize_IsInvariantUnderRigidMotion()
    {
        var segments = new List<BeamSegmentInput> { RigidTransformFixture.Segment("1", 0, 4) };

        Assert.Equal(
            SegmentInputValidation.CharacteristicSize(segments),
            SegmentInputValidation.CharacteristicSize(RigidTransformFixture.Apply(segments)), 9);
    }

    [Fact]
    public void Resolve_TakesMaxOfAbsoluteAndRelativeParts()
    {
        var resolved = ChainTolerances.Default.Resolve(40.0);

        Assert.Equal(0.004, resolved.NodeCoincidenceM, 9);
        Assert.Equal(0.008, resolved.LineDistanceM, 9);
        Assert.Equal(0.04, resolved.GapM, 9);
        Assert.Equal(0.5, resolved.AngularDeg, 9);
    }

    [Fact]
    public void Resolve_FallsBackToAbsolutePartsOnZeroCharacteristicSize()
    {
        var resolved = ChainTolerances.Default.Resolve(0.0);

        Assert.Equal(0.001, resolved.NodeCoincidenceM, 9);
        Assert.Equal(0.010, resolved.GapM, 9);
    }

    [Fact]
    public void Resolve_ThrowsOnNegativeCharacteristicSize() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ChainTolerances.Default.Resolve(-1.0));

    [Fact]
    public void Resolve_ThrowsOnNegativeToleranceComponent()
    {
        var broken = ChainTolerances.Default with { NodeCoincidenceAbsM = -0.001 };

        Assert.Throws<ArgumentOutOfRangeException>(() => broken.Resolve(1.0));
    }

    [Fact]
    public void GapDefault_IsLargerThanNodeCoincidence_SoGapIsReachable()
    {
        var resolved = ChainTolerances.Default.Resolve(5.0);

        Assert.True(resolved.GapM > resolved.NodeCoincidenceM);
    }

    [Fact]
    public void Filter_DropsNonFiniteAndZeroLengthSegments_WithDiagnostics()
    {
        var segments = new List<BeamSegmentInput>
        {
            RigidTransformFixture.Segment("ok", 0, 1),
            RigidTransformFixture.Segment("zero", 2, 2),
            new("nan", new PlanarVector3(double.NaN, 0, 0), new PlanarVector3(1, 0, 0),
                0, BetaSource.Member, "m1"),
            new("inf", new PlanarVector3(0, 0, 0), new PlanarVector3(double.PositiveInfinity, 0, 0),
                0, BetaSource.Member, "m1"),
            new("beta", new PlanarVector3(0, 0, 0), new PlanarVector3(1, 0, 0),
                double.NaN, BetaSource.Member, "m1")
        };

        var (valid, diagnostics) = SegmentInputValidation.Filter(segments);

        Assert.Single(valid);
        Assert.Equal("ok", valid[0].SourceKey);
        Assert.Contains(diagnostics, d => d.Code == "chain_segment_degenerate" && d.SourceKeys!.Contains("zero"));
        Assert.Contains(diagnostics, d => d.Code == "chain_input_invalid" && d.SourceKeys!.Contains("nan"));
        Assert.Contains(diagnostics, d => d.Code == "chain_input_invalid" && d.SourceKeys!.Contains("inf"));
        Assert.Contains(diagnostics, d => d.Code == "chain_input_invalid" && d.SourceKeys!.Contains("beta"));
        Assert.All(diagnostics, d => Assert.True(d.IsError));
    }
}
