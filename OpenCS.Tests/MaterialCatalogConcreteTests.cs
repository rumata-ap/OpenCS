using CScore;
using OpenCS.Services;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Бетон из справочника по рецепту: те же числа, что даёт окно справочника (γb2, γb3, kE, влажность).</summary>
public sealed class MaterialCatalogConcreteTests
{
    [Fact]
    public void Default_SameAsHeavyConcrete()
    {
        var recipe = MaterialCatalog.CreateConcrete(new ConcreteRecipe("B25"))!;
        var old = MaterialCatalog.CreateHeavyConcrete("B25")!;
        Assert.Equal(old.Tag, recipe.Tag);
        Assert.Equal("Бетон тяжелый по СП 63.13330", recipe.Description);
        foreach (var ct in new[] { CalcType.C, CalcType.CL, CalcType.N, CalcType.NL })
        {
            Assert.Equal(old.GetChars(ct)!.Fc, recipe.GetChars(ct)!.Fc, 6);
            Assert.Equal(old.GetChars(ct)!.E, recipe.GetChars(ct)!.E, 6);
            Assert.Equal(old.GetChars(ct)!.Ec1, recipe.GetChars(ct)!.Ec1, 12);
        }
    }

    [Fact]
    public void PlainAndTallLayer_ScaleDesignStrengthOnly()
    {
        var basic = MaterialCatalog.CreateConcrete(new ConcreteRecipe("B25"))!;
        var m = MaterialCatalog.CreateConcrete(new ConcreteRecipe("B25", Plain: true, TallLayer: true))!;
        Assert.Equal(basic.C!.Fc * 0.9 * 0.85, m.C!.Fc, 6);
        Assert.Equal(basic.CL!.Fc * 0.9 * 0.85, m.CL!.Fc, 6);
        Assert.Equal(basic.N!.Fc, m.N!.Fc, 6);
        Assert.Equal(0.6 * m.C.Fc / m.C.E, m.C.Ec1, 12);
        Assert.Contains("γb2 = 0,9", m.Description);
        Assert.Contains("γb3 = 0,85", m.Description);
    }

    [Fact]
    public void HeatTreated_OnlyForFineGrainedA()
    {
        var heavy = MaterialCatalog.CreateConcrete(new ConcreteRecipe("B25", HeatTreated: true))!;
        Assert.Equal(MaterialCatalog.CreateConcrete(new ConcreteRecipe("B25"))!.N!.E, heavy.N!.E, 6);

        var fine = MaterialCatalog.CreateConcrete(new ConcreteRecipe("B25", ConcreteKind.FineA))!;
        var fineHeat = MaterialCatalog.CreateConcrete(new ConcreteRecipe("B25", ConcreteKind.FineA, HeatTreated: true))!;
        Assert.Equal(fine.N!.E * 0.89, fineHeat.N!.E, 6);
        Assert.Contains("E × 0,89", fineHeat.Description);
        Assert.StartsWith("Бетон мелкозернистый группы А", fine.Description);
    }

    [Fact]
    public void Dampness_PicksNlTable()
    {
        var dry = MaterialCatalog.CreateConcrete(new ConcreteRecipe("B25", Dampness: Dampness.ниже_40))!;
        var normal = MaterialCatalog.CreateConcrete(new ConcreteRecipe("B25"))!;
        Assert.NotEqual(normal.NL!.Ec2, dry.NL!.Ec2);
    }

    [Fact]
    public void UnknownClass_Null() => Assert.Null(MaterialCatalog.CreateConcrete(new ConcreteRecipe("B999")));
}
