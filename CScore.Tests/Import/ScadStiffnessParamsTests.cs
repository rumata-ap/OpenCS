using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>Строки ApiGetRigid модели «Краеведческий музей» (срез 0, 01.10).</summary>
public class ScadStiffnessParamsTests
{
    [Fact]
    public void Ge_ShellThicknessInLengthUnits()
    {
        var r = ScadStiffnessParams.Parse(1,
            "GE 8.829e+09  0.2  0.22 RO 24525 TMP 1e-05 1e-05 Name File", "Плиты",
            lengthUnitM: 1, sectionUnitM: 1);

        Assert.Equal(ScadStiffnessKind.Shell, r.Kind);
        Assert.Equal(0.22, r.ThicknessM);
        Assert.Equal("Плиты", r.Name);
        Assert.Null(r.BarRect);
        Assert.StartsWith("GE 8.829e+09", r.Text);
    }

    [Fact]
    public void Ge_LengthUnitCentimeters_ConvertsToMeters()
    {
        var r = ScadStiffnessParams.Parse(1, "GE 8.829e+09 0.2 22 RO 24525", null, 0.01, 0.01);
        Assert.Equal(0.22, r.ThicknessM);
    }

    [Fact]
    public void S0_WithNu_BarRectBalongY1HalongZ1()
    {
        var r = ScadStiffnessParams.Parse(13,
            "S0 1.7658e+10  0.4  0.9 NU 0.2 RO 8829 Shift 0 0 0 Material \"{…}\" Name File", "Балки1",
            lengthUnitM: 1, sectionUnitM: 1);

        Assert.Equal(ScadStiffnessKind.Bar, r.Kind);
        Assert.Equal(new LiraBarRect(0.4, 0.9), r.BarRect);
        Assert.Null(r.ThicknessM);
        Assert.Equal("Балки1", r.Name);
    }

    [Fact]
    public void S0_WithoutNu_BarRect()
    {
        var r = ScadStiffnessParams.Parse(6, "S0 8.829e+09  0.6  1 RO 14715 Name File", null, 1, 1);
        Assert.Equal(new LiraBarRect(0.6, 1.0), r.BarRect);
        Assert.Null(r.Name);
    }

    [Fact]
    public void S0_SectionUnitCentimeters()
    {
        var r = ScadStiffnessParams.Parse(6, "S0 900000 40 90 NU 0.2", "Стойка", 1, 0.01);
        Assert.Equal(new LiraBarRect(0.4, 0.9), r.BarRect);
    }

    [Theory]
    [InlineData("STZ RUSSIAN pu_typep 13 Name File")]
    [InlineData("STZ ASCHM d3 1 Name File")]
    public void Stz_BarWithoutShape(string body)
    {
        var r = ScadStiffnessParams.Parse(20, body, "Ферма", 1, 1);
        Assert.Equal(ScadStiffnessKind.Bar, r.Kind);
        Assert.Null(r.BarRect);
    }

    [Theory]
    [InlineData("SPRING 1e+06 0 0 0 0 0 Type 51")]
    [InlineData("SPRING 1 1 1 1 1 1 Type 100")]
    [InlineData("")]
    public void SpringAndEmpty_Other(string body)
    {
        var r = ScadStiffnessParams.Parse(18, body, "", 1, 1);
        Assert.Equal(ScadStiffnessKind.Other, r.Kind);
        Assert.Null(r.ThicknessM);
        Assert.Null(r.BarRect);
        Assert.Null(r.Name);
    }

    [Fact]
    public void BarRect_FromSavedRecord()
    {
        var saved = new LiraStiffnessRecord(6, ScadStiffnessParams.ScadKindCode, "Колонны",
            "S0 900000 60 60 NU 0.2", 0.01);
        Assert.Equal(new LiraBarRect(0.6, 0.6), ScadStiffnessParams.BarRect(saved));

        var lira = saved with { KindCode = 0 };
        Assert.Null(ScadStiffnessParams.BarRect(lira));
    }

    [Fact]
    public void TxtParser_StiffnessesStillParsed()
    {
        const string text =
            "(0;Version=21;SubVersion=1/1;\"T\";/)" +
            "(1/10 1 1 2 /)" +
            "(3/1  S0 900000 0.4 0.9 NU 0.2 RO 0.9 Name \"Стойка\"/" +
            "2 GE 1.8e+06 0.2 0.22 RO 2.5 Name \"Плита\"/)" +
            "(4/0 0 0/1 0 0/)";

        var r = ScadTextParser.ParseText(text);
        Assert.True(r.Success, r.Error);
        var bar = r.Data!.Stiffnesses.Single(s => s.Id == 1);
        Assert.Equal("Стойка", bar.Name);
        Assert.Equal(new LiraBarRect(0.4, 0.9), bar.BarRect);
        var shell = r.Data.Stiffnesses.Single(s => s.Id == 2);
        Assert.Equal(0.22, shell.ThicknessM);
        Assert.Equal("Плита", shell.Name);
    }
}
