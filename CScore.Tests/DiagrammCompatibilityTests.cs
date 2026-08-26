using CScore;
using Xunit;

namespace CScore.Tests;

/// <summary>
/// Совместимость типа диаграммы и типа материала.
/// Регресс: группа арматуры создаётся с дефолтным <see cref="DiagrammType.L2"/>,
/// а для арматуры с условным пределом текучести (A600…A1000, <see cref="MatType.ReSteelU"/>)
/// двухлинейной диаграммы не существует — построение падало с
/// ArgumentException «Диаграмма и материал не совместимы».
/// </summary>
public class DiagrammCompatibilityTests
{
    static Material Rebar(MatType type) => new()
    {
        Id = 1,
        Tag = type == MatType.ReSteelU ? "A1000" : "A500",
        Type = type,
        E = 200_000_000.0,
        MaterialChars =
        [
            Chars(CalcType.C, type),
            Chars(CalcType.CL, type),
            Chars(CalcType.N, type),
            Chars(CalcType.NL, type)
        ]
    };

    static MaterialChars Chars(CalcType calc, MatType type) => new()
    {
        Type = type,
        TypeCalc = calc,
        Fc = -830_000,
        Ft = 830_000,
        E = 200_000_000,
        Ec2 = -0.0035,
        Et2 = 0.025
    };

    [Theory]
    [InlineData(MatType.Concrete, DiagrammType.L3)]
    [InlineData(MatType.ReSteelF, DiagrammType.L2)]
    [InlineData(MatType.ReSteelU, DiagrammType.L3)]
    [InlineData(MatType.Steel, DiagrammType.L2)]
    public void Default_ReturnsSupportedType(MatType matType, DiagrammType expected)
    {
        Assert.Equal(expected, DiagrammCompatibility.Default(matType));
        Assert.True(DiagrammCompatibility.IsCompatible(matType, expected));
    }

    [Theory]
    [InlineData(MatType.ReSteelU, DiagrammType.L2)]
    [InlineData(MatType.ReSteelF, DiagrammType.L3)]
    [InlineData(MatType.Steel, DiagrammType.SP63)]
    [InlineData(MatType.Concrete, DiagrammType.SP16)]
    public void IsCompatible_RejectsUnsupportedPairs(MatType matType, DiagrammType diagramm)
        => Assert.False(DiagrammCompatibility.IsCompatible(matType, diagramm));

    [Fact]
    public void Custom_IsAlwaysCompatible()
    {
        Assert.True(DiagrammCompatibility.IsCompatible(MatType.Custom, DiagrammType.Custom));
        Assert.True(DiagrammCompatibility.IsCompatible(MatType.ReSteelU, DiagrammType.Custom));
    }

    /// <summary>
    /// Группа арматуры A1000 с дефолтным L2 (так её сохраняет редактор групп)
    /// должна строиться, а не падать: тип диаграммы приводится к допустимому L3.
    /// </summary>
    [Fact]
    public void ResolveAndBuildDiagramms_ReSteelUWithL2_CoercedToL3()
    {
        var material = Rebar(MatType.ReSteelU);
        var area = new MaterialArea
        {
            Id = 1,
            Tag = "300х500_А1000",
            Category = AreaCategory.RebarGroup,
            Material = material,
            MaterialId = material.Id,
            DiagrammType = DiagrammType.L2
        };
        area.Fibers.Add(Fiber.CreatePoint(0.020, 0.0, -0.2));

        area.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);

        Assert.Equal(DiagrammType.L3, area.DiagrammType);
        Assert.Equal(4, area.Diagramms.Count);
        Assert.All(area.Diagramms.Values, d => Assert.Equal(DiagrammType.L3, d.Type));
    }

    /// <summary>Совместимый тип диаграммы не подменяется.</summary>
    [Fact]
    public void ResolveAndBuildDiagramms_ReSteelFWithL2_Kept()
    {
        var material = Rebar(MatType.ReSteelF);
        var area = new MaterialArea
        {
            Id = 1,
            Tag = "300х500_А500",
            Category = AreaCategory.RebarGroup,
            Material = material,
            MaterialId = material.Id,
            DiagrammType = DiagrammType.L2
        };
        area.Fibers.Add(Fiber.CreatePoint(0.020, 0.0, -0.2));

        area.ResolveAndBuildDiagramms(rebarDifferentialDiagram: false);

        Assert.Equal(DiagrammType.L2, area.DiagrammType);
        Assert.All(area.Diagramms.Values, d => Assert.Equal(DiagrammType.L2, d.Type));
    }
}
