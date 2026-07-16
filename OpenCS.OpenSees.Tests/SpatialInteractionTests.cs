using OpenCS.OpenSees.Analysis;

namespace OpenCS.OpenSees.Tests;

public sealed class SpatialInteractionTests
{
    [Fact]
    public void Request_generates_full_turn_without_duplicate_360()
    {
        SectionSpatialInteractionRequest request = new()
        {
            AxialForcesN = [-100_000, 0, 100_000],
            AngleStepDegrees = 45,
            MaxCurvature = 0.01,
            Increments = 20
        };

        request.Validate();

        Assert.Equal(8, request.GenerateAnglesDegrees().Count);
        Assert.Equal(0, request.GenerateAnglesDegrees()[0]);
        Assert.Equal(315, request.GenerateAnglesDegrees()[^1]);
    }

    [Fact]
    public void Request_rejects_non_dividing_angle_step()
    {
        SectionSpatialInteractionRequest request = new()
        {
            AxialForcesN = [0],
            AngleStepDegrees = 7,
            MaxCurvature = 0.01,
            Increments = 20
        };

        Assert.Throws<ArgumentException>(() => request.Validate());
    }

    [Fact]
    public void Spatial_point_maps_zero_and_ninety_degrees_to_Mx_and_My()
    {
        SpatialSectionAnalysisRequest zero = SpatialSectionAnalysisRequest.At(0, 0, 0.01, 20);
        SpatialSectionAnalysisRequest ninety = SpatialSectionAnalysisRequest.At(0, 90, 0.01, 20);

        Assert.Equal(0.01, zero.CurvatureMxAtMax, 12);
        Assert.Equal(0, zero.CurvatureMyAtMax, 12);
        Assert.Equal(0, ninety.CurvatureMxAtMax, 12);
        Assert.Equal(0.01, ninety.CurvatureMyAtMax, 12);
    }

    [Fact]
    public void Request_rejects_empty_nonfinite_and_duplicate_axial_forces()
    {
        Assert.Throws<ArgumentException>(() => new SectionSpatialInteractionRequest
        {
            AxialForcesN = []
        }.Validate());

        Assert.Throws<ArgumentException>(() => new SectionSpatialInteractionRequest
        {
            AxialForcesN = [double.NaN]
        }.Validate());

        Assert.Throws<ArgumentException>(() => new SectionSpatialInteractionRequest
        {
            AxialForcesN = [0, 0]
        }.Validate());
    }
}
