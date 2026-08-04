using CScore.Import;
using Xunit;

namespace CScore.Tests;

public class UnitTokensTests
{
    [Theory]
    [InlineData("Н", 9.80665, 1e-3)]
    [InlineData("н", 9.80665, 1e-3)]
    [InlineData("кг", 9.80665, 9.80665e-3)]
    [InlineData("кгс", 9.80665, 9.80665e-3)]
    [InlineData("т", 9.80665, 9.80665)]
    [InlineData("тс", 9.80665, 9.80665)]
    [InlineData("кН", 9.80665, 1.0)]
    [InlineData("МН", 9.80665, 1000.0)]
    public void ForceToKn_KnownTokens(string token, double tonFactor, double expected)
        => Assert.Equal(expected, UnitTokens.ForceToKn(token, tonFactor)!.Value, 9);

    [Fact]
    public void ForceToKn_UnknownToken_ReturnsNull()
        => Assert.Null(UnitTokens.ForceToKn("г", 9.80665));

    [Theory]
    [InlineData("мм", 0.001)]
    [InlineData("см", 0.01)]
    [InlineData("дм", 0.1)]
    [InlineData("м", 1.0)]
    public void LengthToM_KnownTokens(string token, double expected)
        => Assert.Equal(expected, UnitTokens.LengthToM(token)!.Value, 9);

    [Fact]
    public void ParseCompoundToKnBase_SimpleStress_TmSquared()
    {
        // т/м2 → кН/м² на единицу; при tonFactor=9.80665 → 9.80665
        double? scale = UnitTokens.ParseCompoundToKnBase("т/м2", 9.80665);
        Assert.Equal(9.80665, scale!.Value, 6);
    }

    [Fact]
    public void ParseCompoundToKnBase_KgPerCm2()
    {
        // кг/см2 → кПа: (tonFactor/1000) / (0.01^2) = tonFactor*10
        double? scale = UnitTokens.ParseCompoundToKnBase("кг/см2", 9.80665);
        Assert.Equal(98.0665, scale!.Value, 4);
    }

    [Fact]
    public void ParseCompoundToKnBase_MomentWithParens_LengthCancels()
    {
        // (т*м)/м → тот же tonFactor (числитель*1 / знаменатель*1)
        double? scale = UnitTokens.ParseCompoundToKnBase("(т*м)/м", 9.80665);
        Assert.Equal(9.80665, scale!.Value, 6);
    }

    [Fact]
    public void ParseCompoundToKnBase_DistributedForce_TPerM()
    {
        double? scale = UnitTokens.ParseCompoundToKnBase("т/м", 9.80665);
        Assert.Equal(9.80665, scale!.Value, 6);
    }

    [Fact]
    public void ParseCompoundToKnBase_Bimoment_TmM()
    {
        double? scale = UnitTokens.ParseCompoundToKnBase("т*м*м", 9.80665);
        Assert.Equal(9.80665, scale!.Value, 6);
    }

    [Fact]
    public void ParseCompoundToKnBase_UnknownUnit_ReturnsNull()
        => Assert.Null(UnitTokens.ParseCompoundToKnBase("абракадабра/м2", 9.80665));
}
