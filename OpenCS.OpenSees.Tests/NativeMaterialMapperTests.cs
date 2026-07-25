using CScore;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public sealed class NativeMaterialMapperTests
{
    // Реальные характеристики B25 из OpenCS/DataSource/Бетон_тяжелый_C.csv (кПа/безразмерные).
    static MaterialChars ConcreteB25() => new()
    {
        Type = MatType.Concrete,
        Fc = -14_500, Ft = 1_050, E = 30_000_000,
        Ec0 = -0.002, Ec2 = -0.0035, Et0 = 0.0001, Et2 = 0.00015
    };

    // Реальные характеристики A500 из OpenCS/DataSource/Арматура стальная_C.csv.
    static MaterialChars RebarA500() => new()
    {
        Type = MatType.ReSteelF,
        Fc = -435_000, Ft = 435_000, Ry = 0, Ru = 0, E = 200_000_000,
        Ec2 = -0.0035, Et0 = 0.002175, Et2 = 0.025
    };

    // Реальные характеристики С235 из OpenCS/DataSource/Конструкционная_сталь_C_ГОСТ_27772-2015.csv.
    static MaterialChars StructuralSteelC235() => new()
    {
        Type = MatType.Steel,
        Fc = -230_000, Ft = 230_000, Ry = 230_000, Ru = 350_000, E = 206_000_000,
        Ec2 = -0.025, Et0 = 0.001117, Et2 = 0.025
    };

    [Fact]
    public void Map_ConcreteWithTension_Concrete0102_ReturnsConcrete02WithCorrectSignsAndUnits()
    {
        var spec = NativeMaterialMapper.Map(
            ConcreteB25(), MatType.Concrete, considerConcreteTension: true,
            ConcreteModelKind.Concrete0102, SteelModelKind.Steel02, steelHardeningRatioOverride: null);

        var concrete02 = Assert.IsType<Concrete02Spec>(spec);
        Assert.Equal(-14_500_000, concrete02.Fpc, 3);
        Assert.Equal(-0.002, concrete02.Epsc0, 6);
        Assert.Equal(-14_500_000 * 0.2, concrete02.Fpcu, 3);
        Assert.Equal(-0.0035, concrete02.EpsU, 6);
        Assert.Equal(0.1, concrete02.Lambda, 6);
        Assert.Equal(1_050_000, concrete02.Ft, 3);
        Assert.Equal(1_050_000 / 0.002, concrete02.Ets, 3);
    }

    [Fact]
    public void Map_ConcreteWithoutTension_Concrete0102_ReturnsConcrete01()
    {
        var spec = NativeMaterialMapper.Map(
            ConcreteB25(), MatType.Concrete, considerConcreteTension: false,
            ConcreteModelKind.Concrete0102, SteelModelKind.Steel02, steelHardeningRatioOverride: null);

        var concrete01 = Assert.IsType<Concrete01Spec>(spec);
        Assert.Equal(-14_500_000, concrete01.Fpc, 3);
        Assert.Equal(-0.002, concrete01.Epsc0, 6);
        Assert.Equal(-14_500_000 * 0.2, concrete01.Fpcu, 3);
        Assert.Equal(-0.0035, concrete01.EpsU, 6);
    }

    [Fact]
    public void Map_ConcreteWithTension_Concrete04_ReturnsConcrete04WithCorrectSignsAndUnits()
    {
        var spec = NativeMaterialMapper.Map(
            ConcreteB25(), MatType.Concrete, considerConcreteTension: true,
            ConcreteModelKind.Concrete04, SteelModelKind.Steel02, steelHardeningRatioOverride: null);

        var concrete04 = Assert.IsType<Concrete04Spec>(spec);
        Assert.Equal(-14_500_000, concrete04.Fc, 3);
        Assert.Equal(-0.002, concrete04.Ec0, 6);
        Assert.Equal(-0.0035, concrete04.Ecu, 6);
        Assert.Equal(30_000_000_000, concrete04.Ec, 3);
        Assert.Equal(1_050_000, concrete04.Fct);
        Assert.Equal(0.00015, concrete04.Et);
        Assert.Equal(0.1, concrete04.Beta);
    }

    [Fact]
    public void Map_ConcreteWithoutTension_Concrete04_ReturnsConcrete04WithNullTensionFields()
    {
        var spec = NativeMaterialMapper.Map(
            ConcreteB25(), MatType.Concrete, considerConcreteTension: false,
            ConcreteModelKind.Concrete04, SteelModelKind.Steel02, steelHardeningRatioOverride: null);

        var concrete04 = Assert.IsType<Concrete04Spec>(spec);
        Assert.Equal(-14_500_000, concrete04.Fc, 3);
        Assert.Equal(-0.002, concrete04.Ec0, 6);
        Assert.Equal(-0.0035, concrete04.Ecu, 6);
        Assert.Equal(30_000_000_000, concrete04.Ec, 3);
        Assert.Null(concrete04.Fct);
        Assert.Null(concrete04.Et);
        Assert.Null(concrete04.Beta);
    }

    [Fact]
    public void Map_StructuralSteel_UsesRyAndDerivesHardeningFromRuAndEt2()
    {
        var spec = NativeMaterialMapper.Map(
            StructuralSteelC235(), MatType.Steel, considerConcreteTension: true,
            ConcreteModelKind.Concrete04, SteelModelKind.Steel02, steelHardeningRatioOverride: null);

        var steel02 = Assert.IsType<Steel02Spec>(spec);
        Assert.Equal(230_000_000, steel02.Fy, 3);
        Assert.Equal(206_000_000_000, steel02.E0, 3);
        // b = ((Ru-Ry)/(Et2 - Ry/E)) / E, зажатое в [0.001, 0.05]
        double expectedB = (350_000_000 - 230_000_000) / (0.025 - 230_000.0 / 206_000_000) / 206_000_000_000;
        Assert.Equal(Math.Clamp(expectedB, 0.001, 0.05), steel02.B, 6);
        Assert.Equal(18, steel02.R0, 6);
        Assert.Equal(0.925, steel02.CR1, 6);
        Assert.Equal(0.15, steel02.CR2, 6);
    }

    [Fact]
    public void Map_Rebar_UsesFtAsFyAndFallsBackToDefaultHardening()
    {
        // Ry=Ru=0 у арматуры (реальные данные CSV) — секущая формула недоступна, откат на дефолт.
        var spec = NativeMaterialMapper.Map(
            RebarA500(), MatType.ReSteelF, considerConcreteTension: true,
            ConcreteModelKind.Concrete04, SteelModelKind.Steel01, steelHardeningRatioOverride: null);

        var steel01 = Assert.IsType<Steel01Spec>(spec);
        Assert.Equal(435_000_000, steel01.Fy, 3);
        Assert.Equal(200_000_000_000, steel01.E0, 3);
        Assert.Equal(0.01, steel01.B, 6);
    }

    [Fact]
    public void Map_SteelHardeningOverride_TakesPrecedenceOverDerivation()
    {
        var spec = NativeMaterialMapper.Map(
            StructuralSteelC235(), MatType.Steel, considerConcreteTension: true,
            ConcreteModelKind.Concrete04, SteelModelKind.Steel01, steelHardeningRatioOverride: 0.02);

        var steel01 = Assert.IsType<Steel01Spec>(spec);
        Assert.Equal(0.02, steel01.B, 6);
    }

    [Fact]
    public void Map_CustomMaterialType_ReturnsNull()
    {
        MaterialChars chars = ConcreteB25();
        chars.Type = MatType.Custom;

        var spec = NativeMaterialMapper.Map(
            chars, MatType.Custom, considerConcreteTension: true,
            ConcreteModelKind.Concrete04, SteelModelKind.Steel02, steelHardeningRatioOverride: null);

        Assert.Null(spec);
    }

    [Fact]
    public void Map_NullChars_ReturnsNull()
    {
        var spec = NativeMaterialMapper.Map(
            null, MatType.Concrete, considerConcreteTension: true,
            ConcreteModelKind.Concrete04, SteelModelKind.Steel02, steelHardeningRatioOverride: null);

        Assert.Null(spec);
    }
}
