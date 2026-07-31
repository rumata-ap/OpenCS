using CScore;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Tests;

public sealed class NativeShellMaterialMapperTests
{
    private static MaterialChars ConcreteChars() => new()
    {
        E = 30_000_000, Fc = -17_000, Ft = 1_150, Ec0 = -0.002, Ec2 = -0.0035,
    };

    private static MaterialChars SteelChars() => new()
    {
        E = 200_000_000, Ft = 355_000, Ru = 500_000, Et2 = 0.05,
    };

    [Fact]
    public void MapConcrete_ReturnsPositiveFtAndFcDespiteNegativeSourceFc()
    {
        IReadOnlyList<NativeShellMaterialDefinition> chain =
            NativeShellMaterialMapper.MapConcrete(ConcreteChars(), "concrete:1");

        var damage = Assert.IsType<PlasticDamageConcretePlaneStressShellMaterialSpec>(chain[0].Spec);
        Assert.True(damage.Fc > 0, "Fc бетона в shell-материале должен быть положительным.");
        Assert.True(damage.Ft > 0, "Ft бетона в shell-материале должен быть положительным.");
        Assert.Equal(0.2, damage.Nu, 6);
    }

    [Fact]
    public void MapConcrete_WrapsWithPlateFromPlaneStressDependingOnFirstElement()
    {
        IReadOnlyList<NativeShellMaterialDefinition> chain =
            NativeShellMaterialMapper.MapConcrete(ConcreteChars(), "concrete:1");

        Assert.Equal(2, chain.Count);
        var wrapper = Assert.IsType<PlateFromPlaneStressShellMaterialSpec>(chain[1].Spec);
        Assert.Equal(chain[0].Tag, wrapper.BaseMaterialTag);
    }

    [Fact]
    public void MapConcrete_OutOfPlaneShearModulusMatchesElasticFormula()
    {
        IReadOnlyList<NativeShellMaterialDefinition> chain =
            NativeShellMaterialMapper.MapConcrete(ConcreteChars(), "concrete:1");

        var damage = (PlasticDamageConcretePlaneStressShellMaterialSpec)chain[0].Spec;
        var wrapper = (PlateFromPlaneStressShellMaterialSpec)chain[1].Spec;
        double expectedG = damage.E / (2 * (1 + damage.Nu));

        Assert.Equal(expectedG, wrapper.OutOfPlaneShearModulus, 3);
    }

    [Fact]
    public void MapRebar_Steel02_MatchesNativeMaterialMapperNumericValues()
    {
        MaterialChars chars = SteelChars();

        var expected = (Steel02Spec)NativeMaterialMapper.Map(
            chars, MatType.ReSteelF, considerConcreteTension: false,
            MainMaterialModelKind.Steel02, SteelModelKind.Steel02,
            isReinforcement: true, steelHardeningRatioOverride: null)!;

        IReadOnlyList<NativeShellMaterialDefinition> chain = NativeShellMaterialMapper.MapRebar(
            chars, MatType.ReSteelF, SteelModelKind.Steel02, steelHardeningRatioOverride: null, "rebar:1");

        var actual = Assert.IsType<Steel02UniaxialShellMaterialSpec>(chain[0].Spec);
        Assert.Equal(expected.Fy, actual.Fy, 6);
        Assert.Equal(expected.E0, actual.E0, 6);
        Assert.Equal(expected.B, actual.B, 6);
        Assert.Equal(expected.R0, actual.R0, 6);
    }

    [Fact]
    public void MapRebar_WrapsWithPlateRebarDependingOnUniaxialSteel()
    {
        IReadOnlyList<NativeShellMaterialDefinition> chain = NativeShellMaterialMapper.MapRebar(
            SteelChars(), MatType.ReSteelF, SteelModelKind.Steel02, null, "rebar:1");

        Assert.Equal(2, chain.Count);
        var plateRebar = Assert.IsType<PlateRebarShellMaterialSpec>(chain[1].Spec);
        Assert.Equal(chain[0].Tag, plateRebar.UniaxialMaterialTag);
    }
}
