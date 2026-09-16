using System.Globalization;
using CScore.Abaqus;

using Xunit;

namespace CScore.Tests;

/// <summary>Проверяет стабильность keyword- и TSV-представления CDP.</summary>
public sealed class AbaqusCdpSerializationTests
{
    [Fact]
    public void Keyword_ContainsOnlyAbaqusHeadersAndNumericTables()
    {
        var data = AbaqusCdpCurveGenerator.Generate(
            TestMaterials.Concrete("B25 / test"),
            AbaqusCdpOptions.Default() with { MaterialName = "B25 / test" });

        string text = AbaqusCdpKeywordSerializer.ToKeyword(data);

        Assert.Contains("*Material, name=B25_test", text);
        Assert.Contains("*Elastic\r\n", text);
        Assert.Contains("*Concrete Damaged Plasticity\r\n", text);
        Assert.Contains("*Concrete Compression Hardening\r\n", text);
        Assert.Contains("*Concrete Compression Damage\r\n", text);
        Assert.Contains("*Concrete Tension Stiffening, type=STRAIN\r\n", text);
        Assert.Contains("*Concrete Tension Damage, type=STRAIN\r\n", text);
        Assert.DoesNotContain("InelasticStrain", text);
        Assert.DoesNotContain("CrackingStrain", text);
        Assert.DoesNotContain("Stress\t", text);
        Assert.DoesNotContain("...", text);

        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(lines, line => line.StartsWith("14.5", StringComparison.Ordinal));
        Assert.All(lines.Where(line => !line.StartsWith('*')), line =>
        {
            var values = line.Split(',', StringSplitOptions.TrimEntries);
            Assert.True(values.Length is 2 or 5);
            Assert.All(values, value => Assert.True(double.TryParse(
                value, NumberStyles.Float, CultureInfo.InvariantCulture, out _), value));
        });
    }

    [Fact]
    public void Tsv_HasExplicitSectionsHeadersAndInvariantNumbers()
    {
        var data = AbaqusCdpCurveGenerator.Generate(
            TestMaterials.Concrete(), AbaqusCdpOptions.Default());

        string text = AbaqusCdpKeywordSerializer.ToTsv(data);

        Assert.Contains("[Elastic]\r\nModulus\tPoissonRatio\r\n", text);
        Assert.Contains("[Compression Hardening]\r\nStress\tInelasticStrain\r\n", text);
        Assert.Contains("[Compression Damage]\r\nDamage\tInelasticStrain\r\n", text);
        Assert.Contains("[Tension Stiffening]\r\nStress\tCrackingStrain\r\n", text);
        Assert.Contains("[Tension Damage]\r\nDamage\tCrackingStrain\r\n", text);
        Assert.DoesNotContain("\n\n", text);
        Assert.DoesNotContain(',', text);
        Assert.DoesNotContain("...", text);
    }

    [Theory]
    [InlineData(" B25 / heavy concrete ", "B25_heavy_concrete")]
    [InlineData("///", "Concrete-CDP")]
    [InlineData("Concrete-CDP.v1", "Concrete-CDP.v1")]
    public void SanitizeMaterialName_ProducesSafeIdentifier(string input, string expected)
    {
        Assert.Equal(expected, AbaqusCdpKeywordSerializer.SanitizeMaterialName(input));
    }
}
