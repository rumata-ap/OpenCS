using CScore;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;

namespace OpenCS.OpenSees.Tests;

public sealed class PlateSectionShellMaterialResolverTests
{
    private static Material ConcreteMaterial(int id) => BuildMaterial(id, MatType.Concrete,
        new MaterialChars { E = 30_000_000, Fc = -17_000, Ft = 1_150, Ec0 = -0.002, Ec2 = -0.0035 });

    private static Material RebarMaterial(int id) => BuildMaterial(id, MatType.ReSteelF,
        new MaterialChars { E = 200_000_000, Ft = 355_000, Ru = 500_000, Et2 = 0.05 });

    // Material() — параметрless конструктор + object initializer; C — публичное свойство
    // (не метод), клонирует MaterialChars и проставляет TypeCalc=CalcType.C при установке
    // (см. CScore/Material.cs:111-129). 8-арг конструктор Material(id,tag,desc,matType,
    // c,cl,n,nl) требует все 4 CalcType сразу — избыточно для теста с одним calc.
    private static Material BuildMaterial(int id, MatType type, MaterialChars chars) =>
        new() { Id = id, Tag = $"mat{id}", Type = type, C = chars };

    [Fact]
    public void ResolveConcrete_BuildsDamageWrapperChain()
    {
        var resolver = new PlateSectionShellMaterialResolver(
            id => id == 1 ? ConcreteMaterial(1) : null, CalcType.C, SteelModelKind.Steel02, null);

        IReadOnlyList<NativeShellMaterialDefinition> chain = resolver.ResolveConcrete(1);

        Assert.Equal(2, chain.Count);
        Assert.IsType<PlasticDamageConcretePlaneStressShellMaterialSpec>(chain[0].Spec);
        Assert.IsType<PlateFromPlaneStressShellMaterialSpec>(chain[1].Spec);
    }

    [Fact]
    public void ResolveRebar_BuildsSteelPlateRebarChain()
    {
        var resolver = new PlateSectionShellMaterialResolver(
            id => id == 2 ? RebarMaterial(2) : null, CalcType.C, SteelModelKind.Steel02, null);

        IReadOnlyList<NativeShellMaterialDefinition> chain = resolver.ResolveRebar(2);

        Assert.Equal(2, chain.Count);
        Assert.IsType<Steel02UniaxialShellMaterialSpec>(chain[0].Spec);
        Assert.IsType<PlateRebarShellMaterialSpec>(chain[1].Spec);
    }

    [Fact]
    public void ResolveConcrete_MissingMaterial_ThrowsCScoreMappingException()
    {
        var resolver = new PlateSectionShellMaterialResolver(_ => null, CalcType.C, SteelModelKind.Steel02, null);

        var ex = Assert.Throws<CScoreMappingException>(() => resolver.ResolveConcrete(99));
        Assert.Contains("99", ex.Message);
    }
}
