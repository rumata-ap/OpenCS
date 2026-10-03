using CScore.Import;
using CScore.Sp16;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>Параметры СП 16 КЭ из стальной группы SCAD.</summary>
public class ImportedSteelDesignTests
{
    static ScadSteelGroup Group(double gammaC = 0.8, double muXoZ = 2, double muYoZ = 1, double? lengthXoZ = null,
        double? step = 0.5, double stepRatio = 0.5, bool isMember = false, int[]? ids = null) =>
        new(2, "Балки", ids ?? [1, 2], "C255", "", 0, isMember, 2, 1, gammaC, muXoZ, muYoZ, lengthXoZ, null,
            180, 60, 400, step, stepRatio, 0, 1);

    static readonly SteelDesignParams Base = new()
    {
        GammaC = 1, LefX = 9, LefY = 9, LefB = 0, CompressionCategory = CompressionMemberCategory.Bracing, UseGammaRes = true,
    };

    [Fact]
    public void Apply_MuTimesLength_LinearStep_Limits()
    {
        var p = ImportedSteelDesign.Apply(Base, Group(), 3);

        Assert.Equal(0.8, p.GammaC);
        Assert.Equal(6, p.LefX, 12);
        Assert.Equal(3, p.LefY, 12);
        Assert.Equal(0.5, p.LefB, 12);
        Assert.Equal(new SlendernessLimit(180, 60, "стальная группа SCAD 2 «Балки»"), p.CompressionLimit);
        Assert.Equal(400, p.TensionLimit!.Base);
        Assert.True(p.UseGammaRes);                                     // прочие параметры — из проверки
        Assert.Equal(CompressionMemberCategory.Bracing, p.CompressionCategory);
    }

    [Fact]
    public void Apply_GivenLengthAndRatioStep()
    {
        var p = ImportedSteelDesign.Apply(Base, Group(lengthXoZ: 7.5, step: null, stepRatio: 0.25), 4);

        Assert.Equal(7.5, p.LefX, 12);
        Assert.Equal(4, p.LefY, 12);
        Assert.Equal(1, p.LefB, 12);
    }

    [Fact]
    public void Apply_ZeroStep_ElementLength()
    {
        Assert.Equal(3, ImportedSteelDesign.Apply(Base, Group(step: 0), 3).LefB, 12);
        Assert.Equal(3, ImportedSteelDesign.Apply(Base, Group(step: null, stepRatio: 0), 3).LefB, 12);
    }

    [Fact]
    public void Apply_ZeroValues_KeepBase()
    {
        var g = Group(gammaC: 0, muXoZ: 0) with { CompressionLimit = 0, TensionLimit = 0 };
        var p = ImportedSteelDesign.Apply(Base, g, 3);

        Assert.Equal(1, p.GammaC);
        Assert.Equal(9, p.LefX);
        Assert.Null(p.CompressionLimit);
        Assert.Null(p.TensionLimit);
    }

    [Fact]
    public void Length_ElementOrWholeMember()
    {
        double? Len(int id) => id switch { 1 => 2.0, 2 => 3.0, _ => null };

        Assert.Equal(2.0, ImportedSteelDesign.Length(Group(), 1, Len));
        Assert.Equal(5.0, ImportedSteelDesign.Length(Group(isMember: true), 1, Len));
        Assert.Null(ImportedSteelDesign.Length(Group(isMember: true, ids: [1, 3]), 1, Len));
    }
}
