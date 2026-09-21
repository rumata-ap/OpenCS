using CScore;
using CScore.Abaqus;

using Xunit;

namespace CScore.Tests;

/// <summary>Проверки экспорта стали и арматуры в Abaqus (*Elastic + *Plastic).</summary>
public sealed class AbaqusSteelExportTests
{
    const double SteelE = 206_000_000.0; // кПа
    const double RebarE = 200_000_000.0; // кПа

    /// <summary>С245 по СП 16.13330.2017: Ry = 240 МПа, Ru = 360 МПа (группа 1 табл. В.9).</summary>
    static Material SteelC245() => Build(MatType.Steel, calc => new MaterialChars(calc)
    {
        Type = MatType.Steel, Fc = -240_000, Ft = 240_000, Ry = 240_000, Ru = 360_000, E = SteelE,
        Ec2 = -0.025, Et2 = 0.025,
    });

    /// <summary>A400 (физический предел текучести), Rs = 348 МПа — из «Арматура стальная_C.csv».</summary>
    static Material RebarA400(double rsc = 348_000) => Build(MatType.ReSteelF, calc => new MaterialChars(calc)
    {
        Type = MatType.ReSteelF, Fc = -rsc, Ft = 348_000, E = RebarE, Ec2 = -0.0035, Et2 = 0.025,
    });

    /// <summary>A600 (условный предел текучести), Rs = 522 МПа, εs2 = 0.015.</summary>
    static Material RebarA600() => Build(MatType.ReSteelU, calc => new MaterialChars(calc)
    {
        Type = MatType.ReSteelU, Fc = -522_000, Ft = 522_000, E = RebarE, Ec2 = -0.0035, Et2 = 0.015,
    });

    static Material Build(MatType type, Func<CalcType, MaterialChars> chars) => new()
    {
        Tag = type.ToString(),
        Type = type,
        MaterialChars = [chars(CalcType.C), chars(CalcType.CL), chars(CalcType.N), chars(CalcType.NL)],
    };

    static (double Stress, double PlasticStrain) Expected(double eps, double sigMpa, double eMpa)
    {
        double trueStress = sigMpa * (1.0 + eps);
        return (trueStress, Math.Log(1.0 + eps) - trueStress / eMpa);
    }

    [Fact]
    public void SteelSp16_PointsFollowTableB9AndStopAtUltimate()
    {
        var result = AbaqusSteelCurveGenerator.Generate(SteelC245(), AbaqusSteelOptions.Default());

        double eMpa = SteelE / 1000.0;
        double epsY = 240.0 / eMpa;
        var nominal = new (double Eps, double Sig)[]
        {
            (0.8 * epsY, 0.8 * 240.0),
            (1.7 * epsY, 240.0),
            (14.0 * epsY, 240.0),
            (141.6 * epsY, 1.653 * 240.0),
        };

        Assert.Equal(eMpa, result.ElasticModulus, 9);
        Assert.Equal(0.3, result.PoissonRatio, 12);
        Assert.Equal(nominal.Length, result.Plastic.Count);
        for (int i = 0; i < nominal.Length; i++)
        {
            var (stress, plastic) = Expected(nominal[i].Eps, nominal[i].Sig, eMpa);
            Assert.Equal(stress, result.Plastic[i].Stress, 9);
            if (i > 0)
                Assert.Equal(plastic, result.Plastic[i].PlasticStrain, 12);
        }
        Assert.Equal(0.0, result.Plastic[0].PlasticStrain, 15);
        Assert.Contains(result.Warnings, w => w.Contains("Нисходящая"));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("сжатии"));
    }

    [Fact]
    public void SteelSp16_WithoutPlateau_DropsPlateauPoint()
    {
        var options = AbaqusSteelOptions.Default() with { HasYieldPlateau = false };

        var result = AbaqusSteelCurveGenerator.Generate(SteelC245(), options);

        Assert.Equal(3, result.Plastic.Count);
        Assert.Equal(1.653 * 240.0, result.Plastic[^1].NominalStress, 9);
    }

    [Fact]
    public void RebarA400_TwoPointsFromYieldToEpsS2()
    {
        var result = AbaqusSteelCurveGenerator.Generate(RebarA400(), AbaqusSteelOptions.Default());

        double eMpa = RebarE / 1000.0;
        Assert.Equal(2, result.Plastic.Count);
        Assert.Equal(348.0 * (1.0 + 348.0 / eMpa), result.Plastic[0].Stress, 9);
        Assert.Equal(0.0, result.Plastic[0].PlasticStrain, 15);
        var (stress, plastic) = Expected(0.025, 348.0, eMpa);
        Assert.Equal(stress, result.Plastic[1].Stress, 9);
        Assert.Equal(plastic, result.Plastic[1].PlasticStrain, 12);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void RebarA600_StartsAtProportionalLimit()
    {
        var result = AbaqusSteelCurveGenerator.Generate(RebarA600(), AbaqusSteelOptions.Default());

        double eMpa = RebarE / 1000.0;
        Assert.Equal(3, result.Plastic.Count);
        Assert.Equal(0.9 * 522.0 * (1.0 + 0.9 * 522.0 / eMpa), result.Plastic[0].Stress, 9);
        Assert.Equal(1.1 * 522.0, result.Plastic[1].NominalStress, 9);
        Assert.Equal(0.015, result.Plastic[2].NominalStrain, 12);
        Assert.True(result.Plastic[1].PlasticStrain > 0.0);
        Assert.True(result.Plastic[2].PlasticStrain > result.Plastic[1].PlasticStrain);
    }

    [Fact]
    public void KpaMKN_ScalesStressesOnly()
    {
        var mpa = AbaqusSteelCurveGenerator.Generate(RebarA400(), AbaqusSteelOptions.Default());
        var kpa = AbaqusSteelCurveGenerator.Generate(RebarA400(),
            AbaqusSteelOptions.ForProfile(AbaqusCdpUnitProfile.KpaMKN));

        Assert.Equal(RebarE, kpa.ElasticModulus, 6);
        for (int i = 0; i < mpa.Plastic.Count; i++)
        {
            Assert.Equal(mpa.Plastic[i].Stress * 1000.0, kpa.Plastic[i].Stress, 6);
            Assert.Equal(mpa.Plastic[i].PlasticStrain, kpa.Plastic[i].PlasticStrain, 12);
        }
    }

    [Fact]
    public void AsymmetricRebar_WarnsAndExportsTension()
    {
        var result = AbaqusSteelCurveGenerator.Generate(RebarA400(rsc: 330_000), AbaqusSteelOptions.Default());

        Assert.Contains(result.Warnings, w => w.Contains("сжатии"));
        Assert.Equal(348.0, result.Plastic[0].NominalStress, 9);
    }

    [Fact]
    public void NormativeCalcType_UsesNormativeChars()
    {
        var material = Build(MatType.ReSteelF, calc => new MaterialChars(calc)
        {
            Type = MatType.ReSteelF, E = RebarE, Et2 = 0.025, Ec2 = -0.0035,
            Fc = calc is CalcType.N or CalcType.NL ? -400_000 : -348_000,
            Ft = calc is CalcType.N or CalcType.NL ? 400_000 : 348_000,
        });
        var options = AbaqusSteelOptions.Default() with { CalcType = CalcType.N };

        var result = AbaqusSteelCurveGenerator.Generate(material, options);

        Assert.Equal(400.0, result.Plastic[0].NominalStress, 9);
    }

    [Fact]
    public void Concrete_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            AbaqusSteelCurveGenerator.Generate(TestMaterials.Concrete(), AbaqusSteelOptions.Default()));
    }

    [Fact]
    public void Keyword_ContainsElasticAndPlasticBlocks()
    {
        var data = AbaqusSteelCurveGenerator.Generate(RebarA400(),
            AbaqusSteelOptions.Default() with { MaterialName = "A400 C" });

        var lines = AbaqusSteelKeywordSerializer.ToKeyword(data)
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("*Material, name=A400_C", lines[0]);
        Assert.Equal("*Elastic", lines[1]);
        Assert.Equal("200000, 0.29999999999999999", lines[2]);
        Assert.Equal("*Plastic", lines[3]);
        Assert.Equal(4 + data.Plastic.Count, lines.Length);
        Assert.EndsWith(", 0", lines[4]);
    }

    [Fact]
    public void Tsv_HasElasticAndPlasticSections()
    {
        var data = AbaqusSteelCurveGenerator.Generate(RebarA400(), AbaqusSteelOptions.Default());

        var lines = AbaqusSteelKeywordSerializer.ToTsv(data)
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("[Elastic]", lines[0]);
        Assert.Equal("[Plastic]", lines[3]);
        Assert.Equal("TrueStress\tPlasticStrain", lines[4]);
        Assert.Equal(5 + data.Plastic.Count, lines.Length);
    }
}
