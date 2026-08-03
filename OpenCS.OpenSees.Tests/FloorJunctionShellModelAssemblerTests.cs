using System.Linq;
using CScore;
using CScore.Fem;
using CScore.Planar;
using CScore.PlateRebar;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;
using Xunit;

namespace OpenCS.OpenSees.Tests;

/// <summary>Каркасные тесты сборщика двух shell-моделей стыка плиты и стены: проверяют
/// детерминированный remap тегов, перевод exact pairs в equalDOF и блокирующие диагностики.</summary>
public sealed class FloorJunctionShellModelAssemblerTests
{
    [Fact]
    public void Assemble_ProducesDisjointNodeAndElementTags()
    {
        var (plateSnapshot, wallSnapshot) = Snapshots();
        var mapping = ConformingMapping();

        var result = FloorJunctionShellModelAssembler.Assemble(
            plateSnapshot, PlateRegion(), PlateSection(),
            wallSnapshot, WallRegion(), PlateSection(),
            new ConcreteOnlyResolver(), mapping);

        Assert.True(result.IsCalculable, Diagnostics(result));
        var nodeTags = result.Model.Nodes.Select(node => node.Tag).ToArray();
        Assert.Equal(nodeTags.Length, nodeTags.Distinct().Count());
        var elementTags = result.Model.Elements.Select(element => element.Tag).ToArray();
        Assert.Equal(elementTags.Length, elementTags.Distinct().Count());
    }

    [Fact]
    public void Assemble_RemapsWallNodesByPlateCountOffset()
    {
        var (plateSnapshot, wallSnapshot) = Snapshots();
        var result = FloorJunctionShellModelAssembler.Assemble(
            plateSnapshot, PlateRegion(), PlateSection(),
            wallSnapshot, WallRegion(), PlateSection(),
            new ConcreteOnlyResolver(), ConformingMapping());

        Assert.Equal(6, result.PlateNodeIndexToTag.Count);
        Assert.Equal(1, result.PlateNodeIndexToTag[0]);
        Assert.Equal(7, result.WallNodeIndexToTag[0]); // plate nodes 1..6, wall nodes 7..
    }

    [Fact]
    public void Assemble_TranslatesExactPairsToAssemblyTagsAndAddsEqualDofPlateMasterWallSlave()
    {
        var (plateSnapshot, wallSnapshot) = Snapshots();
        var result = FloorJunctionShellModelAssembler.Assemble(
            plateSnapshot, PlateRegion(), PlateSection(),
            wallSnapshot, WallRegion(), PlateSection(),
            new ConcreteOnlyResolver(), ConformingMapping());

        Assert.NotEmpty(result.JunctionPairs);
        foreach (var (plateTag, wallTag) in result.JunctionPairs)
        {
            Assert.Contains(result.Model.EqualDofConstraints,
                constraint => constraint.MasterNode == plateTag &&
                              constraint.SlaveNode == wallTag &&
                              constraint.Dofs.SequenceEqual([1, 2, 3, 4, 5, 6]));
        }
    }

    [Fact]
    public void Assemble_RejectsSlaveDofConflict()
    {
        // Искусственный mapping: один wall-узел в двух парах -> конфликт slave DOF.
        var (plateSnapshot, wallSnapshot) = Snapshots();
        var mapping = CopyMapping(
            ConformingMapping(),
            exactNodePairs:
            [
                new PlanarConnectionNodePair(4, 4, 0),
                new PlanarConnectionNodePair(5, 4, 0)
            ]);

        var result = FloorJunctionShellModelAssembler.Assemble(
            plateSnapshot, PlateRegion(), PlateSection(),
            wallSnapshot, WallRegion(), PlateSection(),
            new ConcreteOnlyResolver(), mapping);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "floor_junction_slave_dof_conflict");
    }

    [Fact]
    public void Assemble_RejectsNonConformingMapping()
    {
        var (plateSnapshot, wallSnapshot) = Snapshots();
        var mapping = CopyMapping(
            ConformingMapping(), meshMode: PlanarConnectionMeshMode.EmbeddedLocus);

        var result = FloorJunctionShellModelAssembler.Assemble(
            plateSnapshot, PlateRegion(), PlateSection(),
            wallSnapshot, WallRegion(), PlateSection(),
            new ConcreteOnlyResolver(), mapping);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "floor_junction_mesh_mode_unsupported");
    }

    [Fact]
    public void Assemble_MappingWarning_DoesNotBlockCalculation()
    {
        // Информационные mapping-сообщения (например про smeared rebar) не должны
        // превращаться в блокирующие ошибки сборки.
        var (plateSnapshot, wallSnapshot) = Snapshots();

        var result = FloorJunctionShellModelAssembler.Assemble(
            plateSnapshot, PlateRegion(), RebarPlateSection(),
            wallSnapshot, WallRegion(), PlateSection(),
            new RebarCapableResolver(), ConformingMapping());

        Assert.True(result.IsCalculable, Diagnostics(result));
        Assert.Contains(result.Diagnostics,
            d => d.Code == "floor_junction_mapping_warning" && !d.IsError);
        Assert.DoesNotContain(result.Diagnostics,
            d => d.Code == "floor_junction_tag_collision" && d.Message.Contains("mapping"));
    }

    [Fact]
    public void Assemble_RejectsMappingWithErrorDiagnostics()
    {
        var (plateSnapshot, wallSnapshot) = Snapshots();
        var mapping = CopyMapping(
            ConformingMapping(),
            diagnostics: [new FemValidationDiagnostic("floor_junction_mapping_error", "mapping failed")]);

        var result = FloorJunctionShellModelAssembler.Assemble(
            plateSnapshot, PlateRegion(), PlateSection(),
            wallSnapshot, WallRegion(), PlateSection(),
            new ConcreteOnlyResolver(), mapping);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "floor_junction_mapping_error" && d.IsError);
    }

    [Fact]
    public void Assemble_RejectsNonCalculableSnapshot()
    {
        var (plateSnapshot, wallSnapshot) = Snapshots();
        var result = FloorJunctionShellModelAssembler.Assemble(
            plateSnapshot, PlateRegion(), PlateSection(),
            WithIsCalculable(wallSnapshot, isCalculable: false), WallRegion(), PlateSection(),
            new ConcreteOnlyResolver(), ConformingMapping());

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "floor_junction_snapshot_not_calculable");
    }

    [Fact]
    public void Assemble_ReportsMissingExactPairNodes()
    {
        var (plateSnapshot, wallSnapshot) = Snapshots();
        var mapping = CopyMapping(
            ConformingMapping(), exactNodePairs: [new(99, 99, 0)]);

        var result = FloorJunctionShellModelAssembler.Assemble(
            plateSnapshot, PlateRegion(), PlateSection(),
            wallSnapshot, WallRegion(), PlateSection(),
            new ConcreteOnlyResolver(), mapping);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "floor_junction_connection_mapping_missing");
    }

    // --- fixtures ---

    static string Diagnostics(FloorJunctionShellAssemblyResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));

    static PlanarConnectionMeshMapping ConformingMapping() => new()
    {
        ConnectionId = 7,
        ConnectionFingerprint = "fp",
        MeshMode = PlanarConnectionMeshMode.ConformingPartition,
        SideASnapshotId = 110,
        SideAFingerprint = "snapshot-a",
        SideBSnapshotId = 120,
        SideBFingerprint = "snapshot-b",
        SideA = new PlanarConnectionSideMapping
        {
            RegionId = 10,
            Orientation = PlanarConnectionOrientation.Forward,
            OrderedNodeIndices = [4, 5]
        },
        SideB = new PlanarConnectionSideMapping
        {
            RegionId = 20,
            Orientation = PlanarConnectionOrientation.Forward,
            OrderedNodeIndices = [4, 5]
        },
        ExactNodePairs = [new(4, 4, 0), new(5, 5, 0)]
    };

    static PlanarConnectionMeshMapping CopyMapping(
        PlanarConnectionMeshMapping source,
        PlanarConnectionMeshMode? meshMode = null,
        IReadOnlyList<PlanarConnectionNodePair>? exactNodePairs = null,
        IReadOnlyList<FemValidationDiagnostic>? diagnostics = null) => new()
    {
        ConnectionId = source.ConnectionId,
        ConnectionFingerprint = source.ConnectionFingerprint,
        MeshMode = meshMode ?? source.MeshMode,
        SideASnapshotId = source.SideASnapshotId,
        SideAFingerprint = source.SideAFingerprint,
        SideBSnapshotId = source.SideBSnapshotId,
        SideBFingerprint = source.SideBFingerprint,
        SideA = source.SideA,
        SideB = source.SideB,
        ExactNodePairs = exactNodePairs ?? source.ExactNodePairs,
        Diagnostics = diagnostics ?? source.Diagnostics
    };

    static PlanarMeshSnapshot WithIsCalculable(PlanarMeshSnapshot source, bool isCalculable) => new()
    {
        Id = source.Id,
        RegionId = source.RegionId,
        InputFingerprint = source.InputFingerprint,
        IsCalculable = isCalculable,
        Settings = source.Settings,
        Provenance = source.Provenance,
        Diagnostics = source.Diagnostics,
        Nodes = source.Nodes,
        Elements = source.Elements,
        BoundaryMappings = source.BoundaryMappings,
        MeshFormatVersion = source.MeshFormatVersion,
        EntityProvenance = source.EntityProvenance,
        ConstraintMappings = source.ConstraintMappings,
    };

    static (PlanarMeshSnapshot Plate, PlanarMeshSnapshot Wall) Snapshots()
    {
        var plate = new PlanarMeshSnapshot
        {
            Id = 110,
            RegionId = 10,
            InputFingerprint = "snapshot-a",
            IsCalculable = true,
            Nodes =
            [
                new(0, 0, 0, 0, 0, 0), new(1, 4, 0, 4, 0, 0),
                new(2, 4, 4, 4, 4, 0), new(3, 0, 4, 0, 4, 0),
                new(4, 2, 1, 2, 1, 0), new(5, 2, 3, 2, 3, 0)
            ],
            Elements =
            [
                new(0, PlanarMeshElementKind.Quadrangle4, [0, 4, 5, 3]),
                new(1, PlanarMeshElementKind.Quadrangle4, [4, 1, 2, 5])
            ]
        };
        var wall = new PlanarMeshSnapshot
        {
            Id = 120,
            RegionId = 20,
            InputFingerprint = "snapshot-b",
            IsCalculable = true,
            Nodes =
            [
                new(0, 0, 0, 2, 0, 0), new(1, 4, 0, 2, 4, 0),
                new(2, 4, 3, 2, 4, 3), new(3, 0, 3, 2, 0, 3),
                new(4, 1, 0, 2, 1, 0), new(5, 3, 0, 2, 3, 0)
            ],
            Elements =
            [
                new(0, PlanarMeshElementKind.Quadrangle4, [0, 4, 5, 3]),
                new(1, PlanarMeshElementKind.Quadrangle4, [4, 1, 2, 5])
            ]
        };
        return (plate, wall);
    }

    static PlanarRegion PlateRegion()
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] });
        region.Id = 10;
        return region;
    }

    static PlanarRegion WallRegion(Frame3D? frame = null)
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 4, 4, 0], Y = [0, 0, 3, 3] },
            frame: frame ?? new Frame3D(new(2, 0, 0), new(0, 1, 0), new(0, 0, 1), new(1, 0, 0)));
        region.Id = 20;
        return region;
    }

    static PlateSection PlateSection() =>
        new() { H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2 };

    static PlateSection RebarPlateSection() => new()
    {
        H = 0.2, NLayers = 4, ConcreteMaterialId = 1, RebarMaterialId = 2,
        RebarLayers = [new PlateRebarLayer { Asx = 0.001, Zsx = -0.09 }]
    };

    sealed class ConcreteOnlyResolver : IPlateSectionShellMaterialResolver
    {
        public IReadOnlyList<NativeShellMaterialDefinition> ResolveConcrete(int sourceMaterialId) =>
            [new(1, $"concrete:{sourceMaterialId}", new ElasticIsotropicShellMaterialSpec(30e9, 0.2))];

        public IReadOnlyList<NativeShellMaterialDefinition> ResolveRebar(int sourceMaterialId) =>
            throw new NotSupportedException("Тест не использует армирование.");
    }

    sealed class RebarCapableResolver : IPlateSectionShellMaterialResolver
    {
        public IReadOnlyList<NativeShellMaterialDefinition> ResolveConcrete(int sourceMaterialId) =>
            [new(1, $"concrete:{sourceMaterialId}", new ElasticIsotropicShellMaterialSpec(30e9, 0.2))];

        public IReadOnlyList<NativeShellMaterialDefinition> ResolveRebar(int sourceMaterialId) =>
        [
            new(500, $"rebar:{sourceMaterialId}:uniaxial", new ElasticUniaxialShellMaterialSpec(200e9)),
            new(2, $"rebar:{sourceMaterialId}:plate", new PlateRebarShellMaterialSpec(500, 0)),
        ];
    }
}
