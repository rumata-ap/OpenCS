using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.Planar.Fragments;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tests.Fixtures;
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

    [Fact]
    public void Assemble_DeduplicatesIdenticalSectionsAndMaterialsAcrossLevels()
    {
        // Все 3 уровня используют одинаковый frame-relative фингерпринт секции (тот же
        // локальный frame ориентации), одинаковую PlateSection и один resolver ->
        // один материал и одна секция на всех уровнях.
        var levels = ThreeLevelSnapshotsSameOrientation();

        var result = MultiStoryColumnShellModelAssembler.Assemble(
            levels, [], ColumnBaseFixity.None, "PDelta", "forceBeamColumn",
            new ConcreteOnlyResolver());

        Assert.True(result.IsCalculable, Diagnostics(result));
        Assert.Single(result.Model.Materials);
        Assert.Single(result.Model.Sections);
        Assert.All(result.Model.Elements, element =>
            Assert.Equal(result.Model.Sections[0].Tag, element.SectionTag));
    }

    [Fact]
    public void Assemble_KeepsDifferentSectionsSeparateAcrossLevels()
    {
        var levels = ThreeLevelSnapshots();
        levels[1].Level.PlateSection = new PlateSection
            { H = 0.3, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 };

        var result = MultiStoryColumnShellModelAssembler.Assemble(
            levels, [], ColumnBaseFixity.None, "PDelta", "forceBeamColumn",
            new ConcreteOnlyResolver());

        Assert.True(result.IsCalculable, Diagnostics(result));
        Assert.True(result.Model.Sections.Count >= 2);
    }

    [Fact]
    public void Assemble_ReportsSectionAndMaterialProvenance()
    {
        var levels = ThreeLevelSnapshots();

        var result = MultiStoryColumnShellModelAssembler.Assemble(
            levels, [], ColumnBaseFixity.None, "PDelta", "forceBeamColumn",
            new ConcreteOnlyResolver());

        Assert.True(result.IsCalculable, Diagnostics(result));
        Assert.NotEmpty(result.SectionProvenance);
        Assert.NotEmpty(result.MaterialProvenance);
        Assert.Contains(result.MaterialProvenance.Values,
            value => value.Contains("level-1|", StringComparison.Ordinal));
    }

    internal static (ColumnFloorLevel Level, PlanarMeshSnapshot Snapshot)[] ThreeLevelSnapshotsSameOrientation()
    {
        var levels = ThreeLevelSnapshots();
        // Одинаковый locally-flat frame (без поворота) на всех уровнях -> одинаковый
        // frame-relative фингерпринт секции.
        return levels;
    }

    [Fact]
    public void Assemble_AddsColumnSegmentSectionsWithTagsAfterShellRange()
    {
        var levels = ThreeLevelSnapshots();
        var (section, concrete, steel) = CrossSectionFixtures.RectangularSection();
        var materials = CrossSectionFixtures.Materials(concrete, steel);
        var segments = new List<ColumnSegment>
        {
            new() { Id = "seg-1", Section = section, GJ = 1000 },
            new() { Id = "seg-2", Section = section, GJ = 1000 }
        };

        var result = MultiStoryColumnShellModelAssembler.Assemble(
            levels, segments, ColumnBaseFixity.None, "PDelta", "forceBeamColumn",
            new ConcreteOnlyResolver(), CalcType.C, id => materials.GetValueOrDefault(id));

        Assert.True(result.IsCalculable, Diagnostics(result));
        Assert.Equal(2, result.Model.NonlinearBeamSections.Count);
        int shellMaxSectionTag = result.Model.Sections.Count == 0 ? 0 : result.Model.Sections.Max(s => s.Tag);
        int shellMaxMaterialTag = result.Model.Materials.Count == 0 ? 0 : result.Model.Materials.Max(m => m.Tag);
        Assert.All(result.Model.NonlinearBeamSections.Keys, tag => Assert.True(tag > shellMaxSectionTag));
        Assert.All(result.Model.NonlinearBeamSections.Values, section =>
            Assert.All(section.Materials, material => Assert.True(material.Tag > shellMaxMaterialTag)));
    }

    [Fact]
    public void Assemble_ResolvesAnchorNodeTagsAndBuildsBeamElementsBetweenLevels()
    {
        var levels = ThreeLevelSnapshots();
        var (section, concrete, steel) = CrossSectionFixtures.RectangularSection();
        var materials = CrossSectionFixtures.Materials(concrete, steel);
        var segments = new List<ColumnSegment>
        {
            new() { Id = "seg-1", Section = section, GJ = 1000 },
            new() { Id = "seg-2", Section = section, GJ = 1000 }
        };

        var result = MultiStoryColumnShellModelAssembler.Assemble(
            levels, segments, ColumnBaseFixity.None, "PDelta", "forceBeamColumn",
            new ConcreteOnlyResolver(), CalcType.C, id => materials.GetValueOrDefault(id));

        Assert.True(result.IsCalculable, Diagnostics(result));
        Assert.Equal(3, result.AnchorNodeTagByLevel.Count);
        int anchor1 = result.AnchorNodeTagByLevel["level-1"];
        int anchor2 = result.AnchorNodeTagByLevel["level-2"];
        int anchor3 = result.AnchorNodeTagByLevel["level-3"];
        Assert.Equal(2, result.Model.NonlinearBeamElements.Count);
        Assert.Contains(result.Model.NonlinearBeamElements,
            beam => beam.NodeI == anchor1 && beam.NodeJ == anchor2);
        Assert.Contains(result.Model.NonlinearBeamElements,
            beam => beam.NodeI == anchor2 && beam.NodeJ == anchor3);
        // Новых узлов у колонны не создаётся — концы сегментов совпадают с anchor-узлами уровней.
        Assert.Equal(18, result.Model.Nodes.Count);
    }

    [Fact]
    public void Assemble_FixedBaseSupportFixesAllSixDofsOnBottomAnchorNode()
    {
        var levels = ThreeLevelSnapshots();
        var (section, concrete, steel) = CrossSectionFixtures.RectangularSection();
        var materials = CrossSectionFixtures.Materials(concrete, steel);
        var segments = new List<ColumnSegment>
        {
            new() { Id = "seg-1", Section = section, GJ = 1000 },
            new() { Id = "seg-2", Section = section, GJ = 1000 }
        };

        var result = MultiStoryColumnShellModelAssembler.Assemble(
            levels, segments, ColumnBaseFixity.Fixed, "PDelta", "forceBeamColumn",
            new ConcreteOnlyResolver(), CalcType.C, id => materials.GetValueOrDefault(id));

        Assert.True(result.IsCalculable, Diagnostics(result));
        int bottomAnchor = result.AnchorNodeTagByLevel["level-1"];
        var bottomNode = result.Model.Nodes.Single(node => node.Tag == bottomAnchor);
        Assert.All(bottomNode.Fixed, flag => Assert.True(flag));
        int topAnchor = result.AnchorNodeTagByLevel["level-3"];
        var topNode = result.Model.Nodes.Single(node => node.Tag == topAnchor);
        Assert.All(topNode.Fixed, flag => Assert.False(flag));
    }

    [Fact]
    public void Assemble_ReportsMissingAnchorNode()
    {
        var levels = ThreeLevelSnapshots();
        // Убираем ConstraintMappings у первого уровня -> anchor не резолвится. ConstraintMappings
        // init-only (PlanarMeshSnapshot — обычный класс, не record) -> пересобираем snapshot целиком.
        var withoutAnchor = new PlanarMeshSnapshot
        {
            Id = levels[0].Snapshot.Id,
            RegionId = levels[0].Snapshot.RegionId,
            InputFingerprint = levels[0].Snapshot.InputFingerprint,
            IsCalculable = levels[0].Snapshot.IsCalculable,
            Nodes = levels[0].Snapshot.Nodes,
            Elements = levels[0].Snapshot.Elements,
            ConstraintMappings = []
        };
        levels[0] = (levels[0].Level, withoutAnchor);

        var result = MultiStoryColumnShellModelAssembler.Assemble(
            levels, [], ColumnBaseFixity.None, "PDelta", "forceBeamColumn",
            new ConcreteOnlyResolver());

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "multistory_column_anchor_node_missing");
    }

    [Fact]
    public void Assemble_RejectsColumnSectionTagCollisionWithShellSection()
    {
        // Искусственно совпадающие теги невозможно получить через нормальный allocator (он
        // всегда продолжает нумерацию) — тест защитного пути через прямой вызов
        // CheckTagCollisions, как и у FloorJunctionShellModelAssemblerTests.
        var diagnostics = CheckTagCollisions(
            nodes: [new NormalizedShellNode(1, 0, 0, 0, [false, false, false, false, false, false], null)],
            elements: [],
            materials: [],
            sections: [],
            beamSections: new Dictionary<int, OpenSeesSectionModel>
            {
                [1] = new OpenSeesSectionModel { Materials = [], Fibers = [] }
            },
            shellSectionTags: [1]);

        Assert.Contains(diagnostics, d => d.Code == "multistory_column_tag_collision");
    }

    static IReadOnlyList<FemValidationDiagnostic> CheckTagCollisions(
        IReadOnlyList<NormalizedShellNode> nodes,
        IReadOnlyList<NormalizedShellElement> elements,
        IReadOnlyList<NativeShellMaterialDefinition> materials,
        IReadOnlyList<RCShellLayeredSection> sections,
        IReadOnlyDictionary<int, OpenSeesSectionModel> beamSections,
        IReadOnlyList<int> shellSectionTags)
    {
        var method = typeof(MultiStoryColumnShellModelAssembler).GetMethod(
            "CheckTagCollisions", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("CheckTagCollisions не найден.");
        return (IReadOnlyList<FemValidationDiagnostic>)method.Invoke(
            null, [nodes, elements, materials, sections, beamSections, shellSectionTags])!;
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
