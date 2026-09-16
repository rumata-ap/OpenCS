using CScore;
using CScore.Abaqus;

using Xunit;

namespace CScore.Tests;

/// <summary>Проверки доменного экспорта бетонного CDP-материала.</summary>
public sealed class AbaqusCdpExportTests
{
    [Fact]
    public void DefaultOptions_UseMpaMmNAndStructureHelperDefaults()
    {
        var options = AbaqusCdpOptions.Default();

        Assert.Equal(CalcType.C, options.CalcType);
        Assert.Equal(AbaqusCdpUnitProfile.MpaMmN, options.UnitSystem.Profile);
        Assert.Equal(0.001, options.UnitSystem.StressScaleFromOpenCsKpa, 12);
        Assert.Equal(35.0, options.DilationAngleDegrees, 12);
        Assert.Equal(0.1, options.Eccentricity, 12);
        Assert.Equal(1.16, options.Fb0Fc0, 12);
        Assert.Equal(0.667, options.Kc, 12);
        Assert.Equal(0.0001, options.Viscosity, 12);
        Assert.Equal(0.2, options.PoissonRatio, 12);
        Assert.Equal(0.0726, options.FractureEnergy, 12);
        Assert.Equal(10.0, options.ElementLength, 12);
        Assert.Equal(0.4, options.InitialCompressionStressRatio, 12);
        Assert.Equal(0.05, options.CompressionEtaMin, 12);
        Assert.Equal("Concrete-CDP", options.MaterialName);
    }

    [Theory]
    [InlineData(AbaqusCdpUnitProfile.MpaMmN, 0.001, "MPa", "mm", "N", "N/mm", 0.0726, 10.0)]
    [InlineData(AbaqusCdpUnitProfile.KpaMKN, 1.0, "kPa", "m", "kN", "kN/m", 0.0726, 0.01)]
    [InlineData(AbaqusCdpUnitProfile.PaMN, 1000.0, "Pa", "m", "N", "N/m", 72.6, 0.01)]
    public void UnitProfile_ContainsConsistentOutputUnits(
        AbaqusCdpUnitProfile profile,
        double stressScale,
        string stressUnit,
        string lengthUnit,
        string forceUnit,
        string energyUnit,
        double fractureEnergy,
        double elementLength)
    {
        var options = AbaqusCdpOptions.ForProfile(profile);

        Assert.Equal(stressScale, options.UnitSystem.StressScaleFromOpenCsKpa, 12);
        Assert.Equal(stressUnit, options.UnitSystem.StressUnit);
        Assert.Equal(lengthUnit, options.UnitSystem.LengthUnit);
        Assert.Equal(forceUnit, options.UnitSystem.ForceUnit);
        Assert.Equal(energyUnit, options.UnitSystem.EnergyUnit);
        Assert.Equal(fractureEnergy, options.FractureEnergy, 10);
        Assert.Equal(elementLength, options.ElementLength, 10);
    }

    [Fact]
    public void Generate_UsesSelectedCalcTypeAndConvertsStressOnly()
    {
        var material = TestMaterials.Concrete();
        var options = AbaqusCdpOptions.Default() with { CalcType = CalcType.C };

        var result = AbaqusCdpCurveGenerator.Generate(material, options);

        Assert.Equal(31500.0, result.ElasticModulus, 6);
        Assert.Equal(14.5, result.Compression.Max(point => point.Stress), 6);
        Assert.True(result.Compression[0].Stress > 0.0);
        Assert.Equal(0.0, result.Compression[0].AbaqusStrain, 12);
    }

    [Fact]
    public void Compression_FirstPointIsPositiveStressWithZeroInelasticStrain()
    {
        var options = AbaqusCdpOptions.Default();
        var points = AbaqusCdpCompressionBuilder.Build(TestMaterials.Concrete(), options);

        Assert.Equal(0.4 * 14.5, points[0].Stress, 6);
        Assert.Equal(points[0].Stress / (1.05 * 30_000.0), points[0].TotalStrain, 12);
        Assert.Equal(0.0, points[0].AbaqusStrain, 12);
        Assert.Equal(0.0, points[0].Damage, 12);
        Assert.True(points[0].Stress > 0.0);
    }

    [Fact]
    public void Compression_DamageIsZeroThroughPeakAndNondecreasingAfterPeak()
    {
        var points = AbaqusCdpCompressionBuilder.Build(
            TestMaterials.Concrete(), AbaqusCdpOptions.Default());
        int peak = points.Select((point, index) => (point, index))
            .MaxBy(item => item.point.Stress).index;

        Assert.All(points.Take(peak + 1), point => Assert.Equal(0.0, point.Damage, 10));
        for (int i = peak + 1; i < points.Count; i++)
            Assert.True(points[i].Damage + 1e-12 >= points[i - 1].Damage);
        Assert.InRange(points[^1].Damage, 0.0, 0.999);
    }

    [Fact]
    public void Compression_UsesSameInelasticAbscissaForHardeningAndDamage()
    {
        var points = AbaqusCdpCompressionBuilder.Build(
            TestMaterials.Concrete(), AbaqusCdpOptions.Default());

        Assert.True(points.Count >= 3);
        Assert.True(points.Zip(points.Skip(1), (left, right) =>
            right.AbaqusStrain > left.AbaqusStrain).All(value => value));
        Assert.DoesNotContain(points, point => point.Stress <= 0.0);
        Assert.All(points, point => Assert.InRange(point.Damage, 0.0, 0.999));
    }

    [Fact]
    public void Tension_HasInitialPointAndFortyEqualCrackWidthIntervals()
    {
        var result = AbaqusCdpCurveGenerator.Generate(
            TestMaterials.Concrete(), AbaqusCdpOptions.Default());
        var points = result.Tension;

        Assert.Equal(41, points.Count);
        Assert.Equal(1.05, points[0].Stress, 8);
        Assert.Equal(0.0, points[0].AbaqusStrain, 12);
        Assert.Equal(0.0, points[0].Damage, 12);
        Assert.True(points.Zip(points.Skip(1), (left, right) =>
            right.AbaqusStrain > left.AbaqusStrain).All(value => value));
    }

    [Fact]
    public void Tension_UsesTrueAbaqusCrackingStrainAndStructureHelperTail()
    {
        var result = AbaqusCdpCurveGenerator.Generate(
            TestMaterials.Concrete(), AbaqusCdpOptions.Default());
        var last = result.Tension[^1];

        Assert.Equal(last.TotalStrain - last.ElasticStrain, last.AbaqusStrain, 12);
        Assert.InRange(last.Stress / result.Tension[0].Stress, 0.0273, 0.0275);
        Assert.InRange(last.Damage, 0.0, 0.999);
    }

    [Fact]
    public void Tension_UsesFractureEnergyFormulaAndPlasticStrainFormula()
    {
        var options = AbaqusCdpOptions.Default();
        var result = AbaqusCdpCurveGenerator.Generate(TestMaterials.Concrete(), options);
        var first = result.Tension[0];
        var last = result.Tension[^1];
        double wu = 5.14 * options.FractureEnergy / first.Stress;

        Assert.Equal(wu / options.ElementLength,
            last.TotalStrain - first.Stress / result.ElasticModulus, 8);
        Assert.Equal(last.AbaqusStrain - last.Damage * last.Stress
            / ((1.0 - last.Damage) * result.ElasticModulus), last.PlasticStrain, 12);
    }

    [Fact]
    public void Profiles_ProduceEquivalentDimensionlessCurves()
    {
        var material = TestMaterials.Concrete();
        var mpa = AbaqusCdpCurveGenerator.Generate(
            material, AbaqusCdpOptions.ForProfile(AbaqusCdpUnitProfile.MpaMmN));
        var kpa = AbaqusCdpCurveGenerator.Generate(
            material, AbaqusCdpOptions.ForProfile(AbaqusCdpUnitProfile.KpaMKN));
        var pa = AbaqusCdpCurveGenerator.Generate(
            material, AbaqusCdpOptions.ForProfile(AbaqusCdpUnitProfile.PaMN));

        Assert.Equal(mpa.Tension.Count, kpa.Tension.Count);
        Assert.Equal(mpa.Tension.Count, pa.Tension.Count);
        for (int i = 0; i < mpa.Tension.Count; i++)
        {
            Assert.Equal(mpa.Tension[i].AbaqusStrain, kpa.Tension[i].AbaqusStrain, 12);
            Assert.Equal(mpa.Tension[i].AbaqusStrain, pa.Tension[i].AbaqusStrain, 12);
            Assert.Equal(mpa.Tension[i].Damage, kpa.Tension[i].Damage, 12);
            Assert.Equal(mpa.Tension[i].Damage, pa.Tension[i].Damage, 12);
        }
    }

    [Fact]
    public void Generate_RejectsNonConcreteMaterial()
    {
        Assert.Throws<ArgumentException>(() =>
            AbaqusCdpCurveGenerator.Generate(
                TestMaterials.Rebar(), AbaqusCdpOptions.Default()));
    }

    [Fact]
    public void Generate_RejectsMissingCalculationCharacteristics()
    {
        var options = AbaqusCdpOptions.Default() with { CalcType = (CalcType)99 };

        Assert.Throws<ArgumentException>(() =>
            AbaqusCdpCurveGenerator.Generate(TestMaterials.Concrete(), options));
    }

    [Fact]
    public void Generate_RejectsNonPositiveFractureEnergyOrLength()
    {
        var material = TestMaterials.Concrete();

        Assert.Throws<ArgumentException>(() =>
            AbaqusCdpCurveGenerator.Generate(material,
                AbaqusCdpOptions.Default() with { FractureEnergy = 0.0 }));
        Assert.Throws<ArgumentException>(() =>
            AbaqusCdpCurveGenerator.Generate(material,
                AbaqusCdpOptions.Default() with { ElementLength = -1.0 }));
    }

    [Fact]
    public void Generate_RejectsInvalidUnitScale()
    {
        var units = AbaqusCdpUnitSystem.Custom("psi", "in", "lbf", 0.0);
        var options = AbaqusCdpOptions.Default() with { UnitSystem = units };

        Assert.Throws<ArgumentException>(() =>
            AbaqusCdpCurveGenerator.Generate(TestMaterials.Concrete(), options));
    }

    [Fact]
    public void Generate_RejectsNonFiniteOptions()
    {
        var options = AbaqusCdpOptions.Default() with { Viscosity = double.NaN };

        Assert.Throws<ArgumentException>(() =>
            AbaqusCdpCurveGenerator.Generate(TestMaterials.Concrete(), options));
    }

    [Fact]
    public void Generate_RejectsInvalidCdpRanges()
    {
        var material = TestMaterials.Concrete();

        Assert.Throws<ArgumentException>(() =>
            AbaqusCdpCurveGenerator.Generate(material,
                AbaqusCdpOptions.Default() with { Kc = 0.5 }));
        Assert.Throws<ArgumentException>(() =>
            AbaqusCdpCurveGenerator.Generate(material,
                AbaqusCdpOptions.Default() with { PoissonRatio = 0.5 }));
    }

    [Theory]
    [InlineData("E")]
    [InlineData("Fc")]
    [InlineData("Ft")]
    public void Generate_RejectsInvalidConcreteCharacteristic(string characteristic)
    {
        var material = TestMaterials.Concrete();
        var chars = material.C!;
        if (characteristic == "E") chars.E = 0.0;
        if (characteristic == "Fc") chars.Fc = 0.0;
        if (characteristic == "Ft") chars.Ft = 0.0;

        Assert.Throws<ArgumentException>(() =>
            AbaqusCdpCurveGenerator.Generate(material, AbaqusCdpOptions.Default()));
    }
}
