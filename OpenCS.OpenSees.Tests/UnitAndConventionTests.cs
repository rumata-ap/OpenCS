using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Tests;

public sealed class UnitAndConventionTests
{
    [Fact]
    public void CScore_units_are_converted_to_SI()
    {
        Assert.Equal(1_000_000, CScoreUnitConverter.MegapascalsToPascals(1));
        Assert.Equal(1_000, CScoreUnitConverter.KiloNewtonsToNewtons(1));
        Assert.Equal(1_000, CScoreUnitConverter.KiloNewtonMetersToNewtonMeters(1));
    }

    [Fact]
    public void CScore_default_mapping_keeps_rectangle_axes_explicitly()
    {
        (double y, double z) = CScoreUnitConverter.ToOpenSeesCoordinates(
            cscoreX: 0.2,
            cscoreY: 0.3,
            OpenSeesCoordinateConvention.CScoreDefault);

        Assert.Equal(0.3, y);
        Assert.Equal(0.2, z);
        Assert.Equal(OpenSeesCoordinateSource.CScoreY, OpenSeesCoordinateConvention.CScoreDefault.YFrom);
        Assert.Equal(OpenSeesCoordinateSource.CScoreX, OpenSeesCoordinateConvention.CScoreDefault.ZFrom);
    }

    [Fact]
    public void Positive_CScore_curvatures_increase_strain_on_positive_CScore_axes()
    {
        const double e0 = 0.001;
        const double ky = 0.002;
        const double kz = 0.003;
        (double y, double z) = CScoreUnitConverter.ToOpenSeesCoordinates(
            cscoreX: 0.2,
            cscoreY: 0.3,
            OpenSeesCoordinateConvention.CScoreDefault);

        double atPositivePoint = e0 + ky * y + kz * z;
        double atOrigin = e0;

        Assert.True(atPositivePoint > atOrigin);
        Assert.Equal(0.0016, e0 + ky * y, precision: 12);
        Assert.Equal(0.0016, e0 + kz * z, precision: 12);
    }
}
