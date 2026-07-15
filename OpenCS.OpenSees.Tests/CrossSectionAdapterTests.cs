using CScore;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Tests.Fixtures;

namespace OpenCS.OpenSees.Tests;

public sealed class CrossSectionAdapterTests
{
    [Fact]
    public void Adapter_preserves_prepared_fibers_and_maps_XY_to_ZY()
    {
        var (section, concrete, steel) = CrossSectionFixtures.RectangularSection();

        var model = CrossSectionToOpenSeesAdapter.Build(
            section,
            CalcType.C,
            CrossSectionFixtures.Materials(concrete, steel),
            customPool: null,
            options: new CrossSectionToOpenSeesAdapter.Options());

        Assert.Equal(3, model.Fibers.Count);
        Assert.Equal((0.3, 0.2), (model.Fibers[0].Y, model.Fibers[0].Z));
        Assert.Equal(0.01, model.Fibers[0].AreaM2);
        Assert.Equal((0.0002), model.Fibers[2].AreaM2);
        Assert.NotEqual(model.Fibers[0].MaterialTag, model.Fibers[2].MaterialTag);
    }

    [Fact]
    public void Adapter_deduplicates_material_tags_by_source_and_diagram_selection()
    {
        var (section, concrete, steel) = CrossSectionFixtures.RectangularSection();
        section.Areas.Add(new MaterialArea
        {
            Id = 3,
            Tag = "second-concrete-area",
            Material = concrete,
            MaterialId = concrete.Id,
            DiagrammType = DiagrammType.L2,
            Fibers = [new Fiber { X = 0.4, Y = 0.4, Area = 0.01, TypeFiber = FiberType.tri }]
        });

        var model = CrossSectionToOpenSeesAdapter.Build(
            section,
            CalcType.C,
            CrossSectionFixtures.Materials(concrete, steel),
            customPool: null,
            options: new CrossSectionToOpenSeesAdapter.Options());

        Assert.Equal(2, model.Materials.Count);
        Assert.Equal(model.Fibers[0].MaterialTag, model.Fibers[1].MaterialTag);
        Assert.Equal(model.Fibers[0].MaterialTag, model.Fibers[3].MaterialTag);
        Assert.Equal(new[] { 1, 2 }, model.Materials.Select(material => material.Tag));
    }

    [Fact]
    public void Rebar_with_HostArea_uses_steel_diagram_instead_of_differential_diagram()
    {
        var (section, concrete, steel) = CrossSectionFixtures.RectangularSection();

        var model = CrossSectionToOpenSeesAdapter.Build(
            section,
            CalcType.C,
            CrossSectionFixtures.Materials(concrete, steel),
            customPool: null,
            options: new CrossSectionToOpenSeesAdapter.Options());

        int steelTag = model.Fibers[2].MaterialTag;
        var steelDefinition = Assert.Single(model.Materials, material => material.Tag == steelTag);

        Assert.Contains(steelDefinition.PositiveEnvelope, point => point.StressPa > 100_000_000);
        Assert.DoesNotContain(steelDefinition.Warnings, warning => warning.Contains("differential", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Adapter_reports_area_and_fiber_for_invalid_prepared_fiber()
    {
        var (section, concrete, steel) = CrossSectionFixtures.RectangularSection();
        section.Areas[0].Fibers[1].Area = 0;

        CScoreMappingException exception = Assert.Throws<CScoreMappingException>(() =>
            CrossSectionToOpenSeesAdapter.Build(
                section,
                CalcType.C,
                CrossSectionFixtures.Materials(concrete, steel),
                customPool: null,
                options: new CrossSectionToOpenSeesAdapter.Options()));

        Assert.Contains("concrete-area", exception.Message);
        Assert.Contains("fiber 1", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Adapter_rejects_missing_material_empty_fibers_and_missing_requested_diagram()
    {
        var (section, concrete, steel) = CrossSectionFixtures.RectangularSection();
        section.Areas[0].Fibers = [];

        Assert.Throws<CScoreMappingException>(() => CrossSectionToOpenSeesAdapter.Build(
            section,
            CalcType.C,
            CrossSectionFixtures.Materials(concrete, steel),
            customPool: null,
            options: new CrossSectionToOpenSeesAdapter.Options()));

        section.Areas[0].Fibers = [new Fiber { X = 0, Y = 0, Area = 0.01 }];
        section.Areas[0].Material = null;

        Assert.Throws<CScoreMappingException>(() => CrossSectionToOpenSeesAdapter.Build(
            section,
            CalcType.C,
            new Dictionary<int, Material>(),
            customPool: null,
            options: new CrossSectionToOpenSeesAdapter.Options()));

        section.Areas[0].Material = concrete;
        Material incomplete = new() { Id = 99, Type = MatType.Concrete };
        section.Areas[0].Material = incomplete;
        section.Areas[0].MaterialId = incomplete.Id;

        Assert.Throws<CScoreMappingException>(() => CrossSectionToOpenSeesAdapter.Build(
            section,
            CalcType.C,
            new Dictionary<int, Material> { [incomplete.Id] = incomplete, [steel.Id] = steel },
            customPool: null,
            options: new CrossSectionToOpenSeesAdapter.Options()));
    }
}
