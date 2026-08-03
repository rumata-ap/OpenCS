using CScore;
using CScore.Planar;
using CScore.Planar.Fragments;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;
using Xunit;

namespace OpenCS.OpenSees.Tests;

/// <summary>Тесты сборщика составной модели многоэтажной колонны: N независимых shell-снапшотов
/// перекрытий + балочные сегменты между их anchor-узлами.</summary>
public sealed class MultiStoryColumnShellModelAssemblerTests
{
    [Fact]
    public void Assemble_RemapsNodeAndElementTagsSequentiallyAcrossLevels()
    {
        var levels = ThreeLevelSnapshots();

        var result = MultiStoryColumnShellModelAssembler.Assemble(
            levels, [], ColumnBaseFixity.None, "PDelta", "forceBeamColumn",
            new ConcreteOnlyResolver());

        Assert.True(result.IsCalculable, Diagnostics(result));
        // Каждый уровень даёт 6 узлов (см. LevelSnapshot ниже): 1..6, 7..12, 13..18.
        Assert.Equal(1, result.NodeIndexToTagByLevel["level-1"][0]);
        Assert.Equal(7, result.NodeIndexToTagByLevel["level-2"][0]);
        Assert.Equal(13, result.NodeIndexToTagByLevel["level-3"][0]);
        var nodeTags = result.Model.Nodes.Select(node => node.Tag).ToArray();
        Assert.Equal(18, nodeTags.Length);
        Assert.Equal(nodeTags.Length, nodeTags.Distinct().Count());
        var elementTags = result.Model.Elements.Select(element => element.Tag).ToArray();
        Assert.Equal(elementTags.Length, elementTags.Distinct().Count());
    }

    // --- fixtures (переиспользуются в Tasks 6-9) ---

    internal static (ColumnFloorLevel Level, PlanarMeshSnapshot Snapshot)[] ThreeLevelSnapshots() =>
    [
        LevelSnapshot("level-1", 1, originZ: 0),
        LevelSnapshot("level-2", 2, originZ: 3),
        LevelSnapshot("level-3", 3, originZ: 6)
    ];

    internal static (ColumnFloorLevel Level, PlanarMeshSnapshot Snapshot) LevelSnapshot(
        string id, int regionId, double originZ)
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] },
            frame: new Frame3D(
                new PlanarVector3(0, 0, originZ), new PlanarVector3(1, 0, 0),
                new PlanarVector3(0, 1, 0), new PlanarVector3(0, 0, 1)));
        region.Id = regionId;
        var level = new ColumnFloorLevel
        {
            Id = id,
            PlateRegion = region,
            PlateSection = new PlateSection { H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 },
            ColumnAnchorLocalXY = (2, 2)
        };
        var snapshot = new PlanarMeshSnapshot
        {
            Id = regionId * 100,
            RegionId = regionId,
            InputFingerprint = $"snapshot-{id}",
            IsCalculable = true,
            Nodes =
            [
                new(0, 0, 0, 0, 0, originZ), new(1, 4, 0, 4, 0, originZ),
                new(2, 4, 4, 4, 4, originZ), new(3, 0, 4, 0, 4, originZ),
                new(4, 2, 1, 2, 1, originZ), new(5, 2, 2, 2, 2, originZ)
            ],
            Elements =
            [
                new(0, PlanarMeshElementKind.Quadrangle4, [0, 4, 5, 3]),
                new(1, PlanarMeshElementKind.Quadrangle4, [4, 1, 2, 5])
            ],
            ConstraintMappings =
            [
                new PlanarConstraintMeshMapping { ConstraintObjectId = "anchor", PointNodeIndices = [5] }
            ]
        };
        return (level, snapshot);
    }

    internal static string Diagnostics(MultiStoryColumnShellAssemblyResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));

    internal sealed class ConcreteOnlyResolver : IPlateSectionShellMaterialResolver
    {
        public System.Collections.Generic.IReadOnlyList<NativeShellMaterialDefinition> ResolveConcrete(int sourceMaterialId) =>
            [new(1, $"concrete:{sourceMaterialId}", new ElasticIsotropicShellMaterialSpec(30e9, 0.2))];

        public System.Collections.Generic.IReadOnlyList<NativeShellMaterialDefinition> ResolveRebar(int sourceMaterialId) =>
            throw new NotSupportedException("Тест не использует армирование плиты.");
    }
}
