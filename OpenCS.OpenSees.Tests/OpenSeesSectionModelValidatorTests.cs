using OpenCS.OpenSees.Model;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public sealed class OpenSeesSectionModelValidatorTests
{
    static OpenSeesSectionModel ModelWithMaterial(OpenSeesMaterialDefinition material) => new()
    {
        Materials = [material],
        Fibers = [new OpenSeesFiber(0, 0, 0.01, material.Tag)],
        GJ = 1e6
    };

    [Fact]
    public void Validate_AllowsEmptyEnvelopesWhenNativeIsSet()
    {
        var model = ModelWithMaterial(new OpenSeesMaterialDefinition
        {
            Tag = 1,
            PositiveEnvelope = [],
            NegativeEnvelope = [],
            Native = new Concrete01Spec(Fpc: -14_500_000, Epsc0: -0.002, Fpcu: -14_500_000, EpsU: -0.0035)
        });

        OpenSeesSectionModelValidator.Validate(model);
    }

    [Fact]
    public void Validate_StillRejectsEmptyEnvelopesWhenNativeIsNull()
    {
        var model = ModelWithMaterial(new OpenSeesMaterialDefinition
        {
            Tag = 1,
            PositiveEnvelope = [],
            NegativeEnvelope = []
        });

        Assert.Throws<ArgumentException>(() => OpenSeesSectionModelValidator.Validate(model));
    }

    [Theory]
    [InlineData(0.0, -0.002, -14_500_000, -0.0035)]      // Fpc == 0
    [InlineData(-14_500_000, 0.0, -14_500_000, -0.0035)] // Epsc0 >= 0
    [InlineData(-14_500_000, -0.002, -14_500_000, -0.001)] // EpsU >= Epsc0 (не более отрицательное)
    public void Validate_RejectsInvalidConcrete01Spec(double fpc, double epsc0, double fpcu, double epsU)
    {
        var model = ModelWithMaterial(new OpenSeesMaterialDefinition
        {
            Tag = 1,
            Native = new Concrete01Spec(fpc, epsc0, fpcu, epsU)
        });

        Assert.Throws<ArgumentException>(() => OpenSeesSectionModelValidator.Validate(model));
    }

    [Fact]
    public void Validate_RejectsConcrete02SpecWithNonPositiveTension()
    {
        var model = ModelWithMaterial(new OpenSeesMaterialDefinition
        {
            Tag = 1,
            Native = new Concrete02Spec(
                Fpc: -14_500_000, Epsc0: -0.002, Fpcu: -14_500_000, EpsU: -0.0035,
                Lambda: 0.1, Ft: 0, Ets: 1_000_000)
        });

        Assert.Throws<ArgumentException>(() => OpenSeesSectionModelValidator.Validate(model));
    }

    [Theory]
    [InlineData(0.0, 200_000_000_000, 0.01)]   // Fy <= 0
    [InlineData(435_000_000, 0.0, 0.01)]       // E0 <= 0
    [InlineData(435_000_000, 200_000_000_000, 0.0)]  // b <= 0
    [InlineData(435_000_000, 200_000_000_000, 1.0)]  // b >= 1
    public void Validate_RejectsInvalidSteel01Spec(double fy, double e0, double b)
    {
        var model = ModelWithMaterial(new OpenSeesMaterialDefinition
        {
            Tag = 1,
            Native = new Steel01Spec(fy, e0, b)
        });

        Assert.Throws<ArgumentException>(() => OpenSeesSectionModelValidator.Validate(model));
    }
}
