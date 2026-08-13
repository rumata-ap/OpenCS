using CScore;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;
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
    public void Adapter_UsesConfiguredZeroHardeningOnlyForNestedRebar()
    {
        var (section, concrete, steel) = CrossSectionFixtures.RectangularSection();

        var model = CrossSectionToOpenSeesAdapter.Build(
            section,
            CalcType.C,
            CrossSectionFixtures.Materials(concrete, steel),
            customPool: null,
            options: new CrossSectionToOpenSeesAdapter.Options { SteelHardeningModulusPa = 0 });

        var rebar = Assert.Single(model.Materials, material => material.SourceId == steel.Id.ToString());
        Assert.Equal(0, TailSlope(rebar.PositiveEnvelope, positive: true), 12);
        Assert.Equal(0, TailSlope(rebar.NegativeEnvelope, positive: false), 12);
    }

    [Fact]
    public void Adapter_UsesConfiguredZeroHardeningForNativeNestedRebar()
    {
        var (section, concrete, steel) = CrossSectionFixtures.RectangularSection();

        var model = CrossSectionToOpenSeesAdapter.Build(
            section,
            CalcType.C,
            CrossSectionFixtures.Materials(concrete, steel),
            customPool: null,
            options: new CrossSectionToOpenSeesAdapter.Options
            {
                MaterialSource = MaterialSource.Native,
                SteelHardeningModulusPa = 0
            });

        Assert.Equal(0, Assert.IsType<Steel02Spec>(
            Assert.Single(model.Materials, material => material.SourceId == steel.Id.ToString()).Native).B, 12);
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

        CScoreMappingException exception = Assert.Throws<CScoreMappingException>(() =>
            CrossSectionToOpenSeesAdapter.Build(
                section,
                CalcType.C,
                CrossSectionFixtures.Materials(concrete, steel),
                customPool: null,
                options: new CrossSectionToOpenSeesAdapter.Options()));

        Assert.Contains("OpenSees", exception.Message);
        Assert.Contains("фибровой сетки", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("контур", exception.Message, StringComparison.OrdinalIgnoreCase);

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

    [Fact]
    public void Adapter_UsesNativeMaterialWhenMaterialSourceIsNative()
    {
        var (section, concrete, steel) = CrossSectionFixtures.RectangularSection();

        var model = CrossSectionToOpenSeesAdapter.Build(
            section,
            CalcType.C,
            CrossSectionFixtures.Materials(concrete, steel),
            customPool: null,
            options: new CrossSectionToOpenSeesAdapter.Options { MaterialSource = MaterialSource.Native });

        foreach (var material in model.Materials)
        {
            Assert.NotNull(material.Native);
            Assert.Empty(material.PositiveEnvelope);
            Assert.Empty(material.NegativeEnvelope);
        }
    }

    [Fact]
    public void Adapter_FallsBackToTranslatedDiagramForCustomMaterialInNativeMode()
    {
        var (section, concrete, steel) = CrossSectionFixtures.RectangularSection();

        // Material.ResolveCustomDiagramms(pool) требует Type==Custom + запись в CustomDiagramIds,
        // указывающую на Diagramm.Id в пуле (см. CScore/Material.cs).
        concrete.BaseType = MatType.Concrete;
        concrete.Type = MatType.Custom;
        concrete.CustomDiagramIds[CalcType.C] = 42;
        Diagramm customDiagram = new(
            new CSmath.LSpline([-0.002, 0], [-2_000, 0]),
            new CSmath.LSpline([0, 0.001], [0, 1_500]),
            DiagrammType.Custom, MatType.Custom, "custom")
        { Id = 42 };

        var model = CrossSectionToOpenSeesAdapter.Build(
            section,
            CalcType.C,
            CrossSectionFixtures.Materials(concrete, steel),
            customPool: [customDiagram],
            options: new CrossSectionToOpenSeesAdapter.Options { MaterialSource = MaterialSource.Native });

        var concreteMaterial = model.Materials.First(m => m.SourceId == concrete.Id.ToString());
        Assert.Null(concreteMaterial.Native);
        Assert.NotEmpty(concreteMaterial.PositiveEnvelope);
        Assert.Contains(concreteMaterial.Warnings, w => w.Contains("нативная", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Adapter_UsesSeparateMainAndRebarNativeModels()
    {
        var (section, concrete, steel) = CrossSectionFixtures.RectangularSection();

        var model = CrossSectionToOpenSeesAdapter.Build(
            section,
            CalcType.C,
            CrossSectionFixtures.Materials(concrete, steel),
            customPool: null,
            options: new CrossSectionToOpenSeesAdapter.Options
            {
                MaterialSource = MaterialSource.Native,
                MainMaterialModel = MainMaterialModelKind.Concrete04,
                SteelModel = SteelModelKind.Steel01
            });

        var concreteDefinition = Assert.Single(model.Materials, m => m.SourceId == concrete.Id.ToString());
        var rebarDefinition = Assert.Single(model.Materials, m => m.SourceId == steel.Id.ToString());
        Assert.IsType<Concrete04Spec>(concreteDefinition.Native);
        Assert.IsType<Steel01Spec>(rebarDefinition.Native);
    }

    [Fact]
    public void Adapter_UsesMainSteelModelForHostlessSteelArea()
    {
        var (_, _, steel) = CrossSectionFixtures.RectangularSection();
        var steelArea = new MaterialArea
        {
            Id = 10,
            Tag = "main-steel-area",
            Material = steel,
            MaterialId = steel.Id,
            DiagrammType = DiagrammType.L2,
            Fibers = [new Fiber { X = 0.2, Y = 0.3, Area = 0.01, TypeFiber = FiberType.poly }]
        };
        var section = new CrossSection { Areas = [steelArea] };

        var model = CrossSectionToOpenSeesAdapter.Build(
            section,
            CalcType.C,
            new Dictionary<int, Material> { [steel.Id] = steel },
            customPool: null,
            options: new CrossSectionToOpenSeesAdapter.Options
            {
                MaterialSource = MaterialSource.Native,
                MainMaterialModel = MainMaterialModelKind.Steel01,
                SteelModel = SteelModelKind.Steel02,
                SteelHardeningModulusPa = 0
            });

        var native = Assert.IsType<Steel01Spec>(Assert.Single(model.Materials).Native);
        Assert.NotEqual(0, native.B);
    }

    [Fact]
    public void Adapter_RejectsConcreteModelForHostlessSteelArea()
    {
        var (_, _, steel) = CrossSectionFixtures.RectangularSection();
        var steelArea = new MaterialArea
        {
            Id = 10,
            Tag = "main-steel-area",
            Material = steel,
            MaterialId = steel.Id,
            DiagrammType = DiagrammType.L2,
            Fibers = [new Fiber { X = 0.2, Y = 0.3, Area = 0.01, TypeFiber = FiberType.poly }]
        };
        var section = new CrossSection { Areas = [steelArea] };

        var exception = Assert.Throws<CScoreMappingException>(() =>
            CrossSectionToOpenSeesAdapter.Build(
                section,
                CalcType.C,
                new Dictionary<int, Material> { [steel.Id] = steel },
                customPool: null,
                options: new CrossSectionToOpenSeesAdapter.Options
                {
                    MaterialSource = MaterialSource.Native,
                    MainMaterialModel = MainMaterialModelKind.Concrete04
                }));

        Assert.Contains("стальной", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Adapter_uses_configured_sp63_descending_branch()
    {
        var (section, concrete, steel) = CrossSectionFixtures.RectangularSection();
        section.Areas[0].DiagrammType = DiagrammType.SP63;
        foreach (var calc in Enum.GetValues<CalcType>())
            concrete.GetChars(calc)!.Et1 = 0.00005;

        var options = new CrossSectionToOpenSeesAdapter.Options
        {
            ConsiderConcreteTension = false,
            Sp63EtaMin = 0.2
        };

        var model = CrossSectionToOpenSeesAdapter.Build(
            section,
            CalcType.C,
            CrossSectionFixtures.Materials(concrete, steel),
            customPool: null,
            options: options);

        var actualMinStrain = Assert.Single(model.Materials, m => m.SourceId == concrete.Id.ToString())
            .NegativeEnvelope.Min(point => point.Strain);
        var expectedMinStrain = concrete.GetDiagramms(DiagrammType.SP63, 0.2)![CalcType.C].Ic!.X.Min();

        Assert.Equal(expectedMinStrain, actualMinStrain, 12);
    }

    private static double TailSlope(IReadOnlyList<EnvelopePoint> points, bool positive)
    {
        List<EnvelopePoint> ordered = points.OrderBy(point => point.Strain).ToList();
        EnvelopePoint first = positive ? ordered[^2] : ordered[0];
        EnvelopePoint second = positive ? ordered[^1] : ordered[1];
        return (second.StressPa - first.StressPa) / (second.Strain - first.Strain);
    }
}
