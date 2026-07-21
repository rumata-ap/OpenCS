using CScore.Fem;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public sealed class FemUnitConverterTests
{
    [Fact]
    public void ConvertsNodeForcesAndMomentsBetweenKiloAndBaseUnits()
    {
        Assert.Equal(125_000, FemUnitConverter.KiloNewtonsToNewtons(125), 8);
        Assert.Equal(125, FemUnitConverter.NewtonsToKiloNewtons(125_000), 8);
        Assert.Equal(-2_500, FemUnitConverter.KiloNewtonMetersToNewtonMeters(-2.5), 8);
        Assert.Equal(-2.5, FemUnitConverter.NewtonMetersToKiloNewtonMeters(-2_500), 8);
    }

    [Fact]
    public void ConvertsManualTorsionalStiffnessBetweenKiloAndBaseUnits()
    {
        Assert.Equal(3_000_000, FemUnitConverter.KiloNewtonMetersSquaredToNewtonMetersSquared(3_000), 8);
        Assert.Equal(3_000, FemUnitConverter.NewtonMetersSquaredToKiloNewtonMetersSquared(3_000_000), 8);
    }
}
