using CScore.Import;
using CScore.ParametricSteel;
using CScore.Sp16;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>МК-сечение по стальному профилю жёсткости импортированной схемы.</summary>
public class SteelSectionBuilderTests
{
    static ImportedBarProfile Profile(ImportedSteelShape s, int num = 1) =>
        new(num, ImportedBarMaterial.Steel, ImportedBarShape.SteelSection, s.B, s.H, "Сталь", s);

    static ImportedSteelShape IBeam25B1() => new(SteelProfileKind.IBeam, SteelFabrication.Rolled,
        0.248, 0.124, 0.005, 0.008, 0.012, 0, 0, "СТО АСЧМ 20-93", "25Б1", 32.68, 3537, 254.8);

    sealed class Catalog(ParametricSteelCatalogRef? result) : ISteelCatalogLookup
    {
        public ImportedSteelShape? Asked;
        public ParametricSteelCatalogRef? Find(ImportedSteelShape shape) { Asked = shape; return result; }
    }

    [Fact]
    public void IBeam_DimensionsTagAndArea()
    {
        var r = SteelSectionBuilder.Build(Profile(IBeam25B1()), materialId: 5, catalog: null);

        Assert.Null(r.Reason);
        Assert.Null(r.Warning); // контур со скруглениями даёт A сортамента в пределах 1 %
        var d = r.Definition!;
        Assert.Equal(SteelProfileKind.IBeam, d.Kind);
        Assert.Equal(SteelFabrication.Rolled, d.Fabrication);
        Assert.Equal(0.248, d.H);
        Assert.Equal(0.124, d.Bf1);
        Assert.Equal(0.005, d.Tw);
        Assert.Equal(0.008, d.Tf1);
        Assert.Equal(0.012, d.R1);
        Assert.Equal(5, d.MaterialId);
        Assert.False(d.Rotated90);
        Assert.Null(d.Catalog);
        Assert.Equal("25Б1 СТО АСЧМ 20-93", d.Tag);
    }

    [Fact]
    public void Catalog_RefFromLookup()
    {
        var reference = new ParametricSteelCatalogRef("Двутавры", "Двутавp нормальный (Б) по СТО АСЧМ 20-93", "25Б1", 6.7e-8);
        var catalog = new Catalog(reference);

        var d = SteelSectionBuilder.Build(Profile(IBeam25B1()), 5, catalog).Definition!;

        Assert.Equal(reference, d.Catalog);
        Assert.Equal(IBeam25B1(), catalog.Asked);
        Assert.Equal(6.7e-8, ParametricSteelSectionGenerator.ToSteelProfile(d).ItReference);
    }

    [Fact]
    public void SquareTube_PipeAngleChannel_Build()
    {
        ImportedSteelShape[] shapes =
        [
            new(SteelProfileKind.Box, SteelFabrication.Bent, 0.1, 0.1, 0.004, 0.004, 0.004, 0, 0, "ГОСТ 30245-2012", "100x4", 14.95),
            new(SteelProfileKind.Pipe, SteelFabrication.Welded, 0.114, 0, 0.004, 0, 0, 0, 0, "ГОСТ 10704-91", "114x4", 13.82),
            new(SteelProfileKind.Angle, SteelFabrication.Rolled, 0.05, 0.05, 0.005, 0.005, 0.0055, 0.0018, 0, "ГОСТ 8509-93", "L50x5", 4.8),
            new(SteelProfileKind.Channel, SteelFabrication.Rolled, 0.1, 0.046, 0.0045, 0.0076, 0.007, 0.003, 0.1, "ГОСТ 8240-97", "10У", 10.9),
        ];
        foreach (var s in shapes)
        {
            var r = SteelSectionBuilder.Build(Profile(s), 3, null);
            Assert.Null(r.Reason);
            Assert.True(r.Warning == null, $"{s.Name}: {r.Warning}");
            Assert.Equal(s.Kind, r.Definition!.Kind);
            Assert.Equal(s.Fabrication, r.Definition.Fabrication);
            Assert.NotNull(ParametricSteelSectionGenerator.Generate(r.Definition).Profile);
        }
    }

    [Fact]
    public void AreaMismatch_Warning()
    {
        var s = IBeam25B1() with { ACm2 = 40 };

        var r = SteelSectionBuilder.Build(Profile(s), 5, null);

        Assert.NotNull(r.Definition);
        Assert.Contains("против 40 см² по сортаменту", r.Warning);
    }

    [Fact]
    public void MirroredAngle_Flipped()
    {
        var s = new ImportedSteelShape(SteelProfileKind.Angle, SteelFabrication.Rolled, 0.05, 0.05, 0.005, 0.005,
            0.0055, 0.0018, 0, "ГОСТ 8509-93", "LN50x5", 4.8, Flipped: true);

        var r = SteelSectionBuilder.Build(Profile(s), 5, null);

        Assert.True(r.Definition!.Flipped);
        Assert.NotEqual(SteelSectionBuilder.Key(Profile(s with { Flipped = false }), 5), SteelSectionBuilder.Key(Profile(s), 5));
    }

    [Fact]
    public void NotSteel_OrNotBuildable_Reason()
    {
        var concrete = new ImportedBarProfile(1, ImportedBarMaterial.Concrete, ImportedBarShape.Rectangle, 0.3, 0.5, "Брус");
        var bad = IBeam25B1() with { Tf = 0.2 };

        Assert.Contains("не стальной", SteelSectionBuilder.Build(concrete, 5, null).Reason);
        Assert.Null(SteelSectionBuilder.Key(concrete, 5));
        Assert.Contains("не строится", SteelSectionBuilder.Build(Profile(bad), 5, null).Reason);
    }

    [Fact]
    public void Matches_SameDimensionsAndMaterial_IgnoresTagAndCatalog()
    {
        var key = SteelSectionBuilder.Key(Profile(IBeam25B1()), 5)!.Value;
        var d = SteelSectionBuilder.Build(Profile(IBeam25B1()), 5, null).Definition!;

        Assert.True(SteelSectionBuilder.Matches(d with { Tag = "другое", Catalog = new("a", "b", "c") }, key));
        Assert.True(SteelSectionBuilder.Matches(d with { H = 0.24804 }, key));
        Assert.False(SteelSectionBuilder.Matches(d with { H = 0.2482 }, key));
        Assert.False(SteelSectionBuilder.Matches(d with { MaterialId = 6 }, key));
        Assert.False(SteelSectionBuilder.Matches(d with { Rotated90 = true }, key));
        Assert.False(SteelSectionBuilder.Matches(d with { Fabrication = SteelFabrication.Welded }, key));
    }
}
