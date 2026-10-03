using CScore.Import;
using CScore.ParametricRc;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>Параметрическое ЖБ-сечение проекта по профилю жёсткости стержня импортированной схемы.</summary>
public class RcSectionBuilderTests
{
    static ImportedBarProfile Brus(double b = 0.3, double h = 0.5) =>
        new(7, ImportedBarMaterial.Concrete, ImportedBarShape.Rectangle, b, h, "Брус 30 X 50");

    [Fact]
    public void Build_RectangleWithMaterialsWithoutRebar()
    {
        var (definition, reason) = RcSectionBuilder.Build(Brus(), concreteId: 3, rebarId: 4, "B25 A500");

        Assert.Null(reason);
        Assert.Equal(ParametricRcShape.Rectangle, definition!.Shape);
        Assert.Equal(0.3, definition.WidthM, 9);
        Assert.Equal(0.5, definition.HeightM, 9);
        Assert.Equal(3, definition.ConcreteMaterialId);
        Assert.Equal(4, definition.LongitudinalMaterialId);
        Assert.Null(definition.UpperRebar);
        Assert.Null(definition.LowerRebar);
        Assert.Equal("Брус 300×500 B25 A500", definition.Tag);
    }

    [Fact]
    public void Generate_ConcreteOnly_BalongX_HalongY()
    {
        // Оси как у источников проверки (LiraBarSectionBuilder): x ‖ Y1 — ширина B, y ‖ Z1 — высота H.
        var (definition, _) = RcSectionBuilder.Build(Brus(0.3, 0.5), 3, 4, "B25 A500");
        var result = ParametricRcSectionGenerator.Generate(definition!);

        Assert.Empty(result.Diagnostics);
        var area = Assert.Single(result.Section.Areas);
        Assert.Equal(AreaCategory.Region, area.Category);
        Assert.Equal(3, area.MaterialId);
        Assert.Equal(-0.15, area.Hull.X.Min(), 9);
        Assert.Equal(0.15, area.Hull.X.Max(), 9);
        Assert.Equal(-0.25, area.Hull.Y.Min(), 9);
        Assert.Equal(0.25, area.Hull.Y.Max(), 9);
    }

    [Fact]
    public void Key_SameForRoundingNoise_DifferentForMaterials()
    {
        var a = RcSectionBuilder.Key(Brus(0.3, 0.5), 3, 4);
        Assert.Equal(a, RcSectionBuilder.Key(Brus(0.30000001, 0.49999999), 3, 4));
        Assert.NotEqual(a, RcSectionBuilder.Key(Brus(0.3, 0.5), 3, 5));
        Assert.NotEqual(a, RcSectionBuilder.Key(Brus(0.5, 0.3), 3, 4));
    }

    [Fact]
    public void Matches_OwnDefinition_NotEditedOne()
    {
        var key = RcSectionBuilder.Key(Brus(), 3, 4);
        var (definition, _) = RcSectionBuilder.Build(Brus(), 3, 4, "B25 A500");

        Assert.True(RcSectionBuilder.Matches(definition!, key));
        Assert.True(RcSectionBuilder.Matches(definition! with { Tag = "Переименовано" }, key));
        Assert.False(RcSectionBuilder.Matches(definition! with { ConcreteMaterialId = 9 }, key));
        Assert.False(RcSectionBuilder.Matches(definition! with { HeightM = 0.6 }, key));
        Assert.False(RcSectionBuilder.Matches(definition! with
        {
            LowerRebar = ParametricLongitudinalLayer.Physical(3, 0.016, -0.2),
        }, key));
    }

    [Fact]
    public void Build_NotConcreteOrNoSize_Reason()
    {
        Assert.Contains("не железобетонный",
            RcSectionBuilder.Build(Brus() with { Material = ImportedBarMaterial.Steel }, 3, 4, "").Reason);
        Assert.Contains("размеры сечения не заданы",
            RcSectionBuilder.Build(Brus(0, 0.5), 3, 4, "").Reason);
    }
}
