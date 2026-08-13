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
            Native = new Concrete04Spec(Fc: -14_500_000, Ec0: -0.002, Ecu: -0.0035, Ec: 30_000_000_000, Fct: null, Et: null, Beta: null)
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
    [InlineData(0.0, -0.002, -0.0035, 30_000_000_000)]      // Fc == 0
    [InlineData(-14_500_000, 0.0, -0.0035, 30_000_000_000)] // Ec0 >= 0
    [InlineData(-14_500_000, -0.002, -0.001, 30_000_000_000)] // Ecu >= Ec0 (не более отрицательное)
    [InlineData(-14_500_000, -0.002, -0.0035, 0.0)]         // Ec <= 0
    public void Validate_RejectsInvalidConcrete04SpecWithoutTension(double fc, double ec0, double ecu, double ec)
    {
        var model = ModelWithMaterial(new OpenSeesMaterialDefinition
        {
            Tag = 1,
            Native = new Concrete04Spec(fc, ec0, ecu, ec, Fct: null, Et: null, Beta: null)
        });

        Assert.Throws<ArgumentException>(() => OpenSeesSectionModelValidator.Validate(model));
    }

    [Fact]
    public void Validate_RejectsConcrete04SpecWithNonPositiveTension()
    {
        var model = ModelWithMaterial(new OpenSeesMaterialDefinition
        {
            Tag = 1,
            Native = new Concrete04Spec(
                Fc: -14_500_000, Ec0: -0.002, Ecu: -0.0035, Ec: 30_000_000_000,
                Fct: 0, Et: 0.00015, Beta: 0.1)
        });

        Assert.Throws<ArgumentException>(() => OpenSeesSectionModelValidator.Validate(model));
    }

    [Theory]
    [InlineData(0.0, 200_000_000_000, 0.01)]   // Fy <= 0
    [InlineData(435_000_000, 0.0, 0.01)]       // E0 <= 0
    [InlineData(435_000_000, 200_000_000_000, -0.001)] // b < 0
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

    [Fact]
    public void Validate_AllowsZeroHardeningForSteel01AndSteel02()
    {
        OpenSeesSectionModelValidator.Validate(ModelWithMaterial(new OpenSeesMaterialDefinition
        {
            Tag = 1,
            Native = new Steel01Spec(435_000_000, 200_000_000_000, 0)
        }));

        OpenSeesSectionModelValidator.Validate(ModelWithMaterial(new OpenSeesMaterialDefinition
        {
            Tag = 2,
            Native = new Steel02Spec(435_000_000, 200_000_000_000, 0, 18, 0.925, 0.15)
        }));
    }
}
