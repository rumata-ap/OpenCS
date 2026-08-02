using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Tcl;

namespace OpenCS.OpenSees.Tests;

public sealed class NativeShellMaterialSpecTests
{
    // TclNumber.Format использует G17 (round-trip) — не всегда даёт "чистую" десятичную запись
    // (0.2 -> "0.20000000000000001"), поэтому ожидаемые строки строятся тем же форматтером,
    // а не хардкодятся вручную.
    private static string Tcl(double value) => TclNumber.Format(value);


    [Fact]
    public void PlasticDamageConcretePlaneStress_EmitsExpectedTcl()
    {
        var spec = new PlasticDamageConcretePlaneStressShellMaterialSpec(
            3.0e10, 0.2, 3.0e6, 3.0e7, 0.6, 0.5, 2.0, 0.14);

        Assert.Equal(
            "nDMaterial PlasticDamageConcretePlaneStress 1 30000000000 " + Tcl(0.2) + " 3000000 30000000 " +
            Tcl(0.6) + " 0.5 2 " + Tcl(0.14),
            spec.ToTcl(1));
    }

    [Fact]
    public void PlasticDamageConcretePlaneStress_RejectsNegativeFc()
    {
        var spec = new PlasticDamageConcretePlaneStressShellMaterialSpec(
            3.0e10, 0.2, 3.0e6, -3.0e7, 0.6, 0.5, 2.0, 0.14);

        Assert.Throws<InvalidOperationException>(() => spec.ToTcl(1));
    }

    [Fact]
    public void PlateFromPlaneStress_DependsOnBaseMaterialTag()
    {
        var spec = new PlateFromPlaneStressShellMaterialSpec(7, 1.25e10);

        Assert.Equal(7, spec.DependsOnMaterialTag);
        Assert.Equal("nDMaterial PlateFromPlaneStress 2 7 12500000000", spec.ToTcl(2));
    }

    [Fact]
    public void PlateFromPlaneStress_WithDependencyTag_RewritesBaseMaterialTag()
    {
        var spec = new PlateFromPlaneStressShellMaterialSpec(7, 1.25e10);

        var rewritten = spec.WithDependencyTag(42);

        Assert.Equal(42, Assert.IsType<PlateFromPlaneStressShellMaterialSpec>(rewritten).BaseMaterialTag);
    }

    [Fact]
    public void PlateRebar_WithDependencyTag_RewritesUniaxialMaterialTag()
    {
        var spec = new PlateRebarShellMaterialSpec(5, 90);

        var rewritten = spec.WithDependencyTag(42);

        Assert.Equal(42, Assert.IsType<PlateRebarShellMaterialSpec>(rewritten).UniaxialMaterialTag);
        Assert.Equal(90, Assert.IsType<PlateRebarShellMaterialSpec>(rewritten).AngleDegrees);
    }

    [Fact]
    public void Steel02Uniaxial_EmitsExpectedTcl()
    {
        var spec = new Steel02UniaxialShellMaterialSpec(4.0e8, 2.0e11, 0.01, 18, 0.925, 0.15);

        Assert.Equal(
            "uniaxialMaterial Steel02 3 400000000 200000000000 " + Tcl(0.01) + " 18 " + Tcl(0.925) + " " + Tcl(0.15),
            spec.ToTcl(3));
    }

    [Fact]
    public void ElasticIsotropic_HasNoDependency()
    {
        var spec = new ElasticIsotropicShellMaterialSpec(30e9, 0.2);

        Assert.Null(spec.DependsOnMaterialTag);
        Assert.Throws<InvalidOperationException>(() => spec.WithDependencyTag(1));
    }

    [Fact]
    public void ElasticIsotropic_DeclaresRequiredStressAndStrainCapabilities()
    {
        var spec = new ElasticIsotropicShellMaterialSpec(30e9, 0.2);

        NativeResponseCapability stress = Assert.Single(spec.Capabilities, c => c.ResponseName == "stress");
        Assert.True(stress.IsRequired);
        Assert.Equal(5, stress.ComponentCount);
        Assert.Equal("Pa", stress.Unit);
        Assert.Equal("stress", stress.TclQueryContract);
        Assert.True(spec.HasResponse("strain"));
    }

    [Fact]
    public void NativeShellMaterialSpec_DeclaresUnitsComponentOrderAndConjugatePair()
    {
        var spec = new ElasticIsotropicShellMaterialSpec(30e9, 0.2);

        NativeResponseCapability stress = Assert.Single(spec.Capabilities, c => c.ResponseName == "stress");
        NativeResponseCapability strain = Assert.Single(spec.Capabilities, c => c.ResponseName == "strain");

        Assert.Equal("Pa", stress.Unit);
        Assert.Equal("1", strain.Unit);
        Assert.Equal(["sigma_x", "sigma_y", "tau_xy", "tau_xz", "tau_yz"], stress.ComponentNames);
        Assert.Equal(["epsilon_x", "epsilon_y", "gamma_xy", "gamma_xz", "gamma_yz"], strain.ComponentNames);
        Assert.Contains(stress.ConjugatePairs, pair => pair is { StressResponse: "stress", StrainResponse: "strain" });
    }

    [Fact]
    public void PlasticDamageConcrete_DoesNotFakeDamageCrackOrEnergyCapabilities()
    {
        var spec = new PlasticDamageConcretePlaneStressShellMaterialSpec(
            3.0e10, 0.2, 3.0e6, 3.0e7, 0.6, 0.5, 2.0, 0.14);

        Assert.True(spec.HasResponse("stress"));
        Assert.True(spec.HasResponse("strain"));
        Assert.False(spec.HasResponse("tangent"));
        Assert.False(spec.HasResponse("damage"));
        Assert.False(spec.HasResponse("crack"));
        Assert.False(spec.HasResponse("energy"));
    }

    [Fact]
    public void PlateRebar_DeclaresStressStrainWithoutEnergyCapability()
    {
        var spec = new PlateRebarShellMaterialSpec(5, 45);

        Assert.True(spec.HasResponse("stress"));
        Assert.True(spec.HasResponse("strain"));
        Assert.False(spec.HasResponse("energy"));
        Assert.Contains(spec.Capabilities, c => c.ResponseName == "stress" && c.Warnings.Count > 0);
    }

    [Fact]
    public void UniaxialMaterials_DeclareSingleComponentStressStrainWithPair()
    {
        var spec = new Steel01UniaxialShellMaterialSpec(4.0e8, 2.0e11, 0.01);

        NativeResponseCapability stress = Assert.Single(spec.Capabilities, c => c.ResponseName == "stress");
        NativeResponseCapability strain = Assert.Single(spec.Capabilities, c => c.ResponseName == "strain");

        Assert.True(stress.IsRequired);
        Assert.Equal(1, stress.ComponentCount);
        Assert.Equal("Pa", stress.Unit);
        Assert.Equal(1, strain.ComponentCount);
        Assert.Equal("1", strain.Unit);
        Assert.Equal(["stress"], stress.ComponentNames);
        Assert.Equal(["strain"], strain.ComponentNames);
        Assert.Contains(stress.ConjugatePairs, pair => pair is { StressResponse: "stress", StrainResponse: "strain" });
    }
}
