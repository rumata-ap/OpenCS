using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;
using CScore;

namespace OpenCS.OpenSees.Tests;

public sealed class PlateSectionOpenSeesMapperTests
{
    [Fact]
    public void Map_CreatesConcreteLayersFromMinusToPlusNormal()
    {
        var section = new PlateSection { H = 0.2, NLayers = 4 };
        var result = PlateSectionOpenSeesMapper.Map(section, ShellFrame.Identity, Resolver());

        var concrete = result.Section.Layers.Where(x => x.Kind == ShellLayerKind.Concrete).ToArray();

        Assert.Equal(4, concrete.Length);
        Assert.Equal(-0.075, concrete[0].CenterZ, 12);
        Assert.Equal(0.075, concrete[^1].CenterZ, 12);
        Assert.All(concrete, layer => Assert.Equal(0.05, layer.Thickness, 12));
    }

    [Fact]
    public void Map_CreatesIndependentXAndYRebarLayers()
    {
        var section = new PlateSection
        {
            H = 0.2,
            NLayers = 2,
            RebarLayers = [
                new PlateRebarLayer { Asx = 0.001, Asy = 0.002, Zsx = -0.07, Zsy = 0.07 }
            ]
        };
        var result = PlateSectionOpenSeesMapper.Map(section, ShellFrame.Identity, Resolver());

        Assert.Contains(result.Section.Layers,
            x => x.Kind == ShellLayerKind.RebarX && x.DirectionDegrees == 0 && x.CenterZ == -0.07);
        Assert.Contains(result.Section.Layers,
            x => x.Kind == ShellLayerKind.RebarY && x.DirectionDegrees == 90 && x.CenterZ == 0.07);
        Assert.Equal(ShellMappingMode.NativeWithExplicitApproximation, result.Section.MappingMode);
    }

    [Fact]
    public void Map_RejectsMissingMaterialResolution()
    {
        var section = new PlateSection { H = 0.2, NLayers = 2 };

        var ex = Assert.Throws<CScoreMappingException>(() =>
            PlateSectionOpenSeesMapper.Map(section, ShellFrame.Identity, new MissingResolver()));

        Assert.Contains("материал", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MapMany_TwoSectionsWithIdenticalConcrete_ShareSingleConcreteMaterialTag()
    {
        var sectionA = new PlateSection { H = 0.2, NLayers = 2 };
        var sectionB = new PlateSection { H = 0.3, NLayers = 2 };

        var batch = PlateSectionOpenSeesMapper.MapMany(
            [(sectionA, ShellFrame.Identity, 1), (sectionB, ShellFrame.Identity, 2)],
            Resolver());

        Assert.Equal(2, batch.Sections.Count);
        Assert.Single(batch.Materials);
        int concreteTag = batch.Materials[0].Tag;
        Assert.All(batch.Sections, section =>
            Assert.All(section.Layers.Where(x => x.Kind == ShellLayerKind.Concrete),
                layer => Assert.Equal(concreteTag, layer.MaterialTag)));
    }

    [Fact]
    public void MapMany_DistinctRebarLayouts_AddOneExtraMaterialNotDuplicateConcrete()
    {
        var sectionA = new PlateSection { H = 0.2, NLayers = 2 };
        var sectionB = new PlateSection
        {
            H = 0.2, NLayers = 2,
            RebarLayers = [new PlateRebarLayer { Asx = 0.001, Zsx = -0.09 }]
        };

        var batch = PlateSectionOpenSeesMapper.MapMany(
            [(sectionA, ShellFrame.Identity, 1), (sectionB, ShellFrame.Identity, 2)], Resolver());

        // concrete (общий для обеих секций) + цепочка армирования (uniaxial сталь + PlateRebar
        // обёртка) — сектор B добавляет ровно одну цепочку, не дублирует бетон.
        Assert.Equal(3, batch.Materials.Count);
    }

    [Fact]
    public void MapMany_RejectsDuplicateSectionTag()
    {
        var section = new PlateSection { H = 0.2, NLayers = 2 };

        var ex = Assert.Throws<CScoreMappingException>(() => PlateSectionOpenSeesMapper.MapMany(
            [(section, ShellFrame.Identity, 5), (section, ShellFrame.Identity, 5)], Resolver()));

        Assert.Contains("tag", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IPlateSectionShellMaterialResolver Resolver() => new TestResolver();

    private sealed class TestResolver : IPlateSectionShellMaterialResolver
    {
        public IReadOnlyList<NativeShellMaterialDefinition> ResolveConcrete(int sourceMaterialId) =>
            [new(1, $"concrete:{sourceMaterialId}", new ElasticIsotropicShellMaterialSpec(30e9, 0.2))];

        public IReadOnlyList<NativeShellMaterialDefinition> ResolveRebar(int sourceMaterialId) =>
        [
            new(5, $"rebar:{sourceMaterialId}:uniaxial", new ElasticUniaxialShellMaterialSpec(200e9)),
            new(2, $"rebar:{sourceMaterialId}:plate", new PlateRebarShellMaterialSpec(5, 0)),
        ];
    }

    private sealed class MissingResolver : IPlateSectionShellMaterialResolver
    {
        public IReadOnlyList<NativeShellMaterialDefinition> ResolveConcrete(int sourceMaterialId) =>
            throw new CScoreMappingException("Не разрешён материал бетона.");

        public IReadOnlyList<NativeShellMaterialDefinition> ResolveRebar(int sourceMaterialId) =>
            throw new CScoreMappingException("Не разрешён материал арматуры.");
    }
}
