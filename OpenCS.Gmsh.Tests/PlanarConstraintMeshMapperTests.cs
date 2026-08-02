using CScore;
using CScore.Planar;
using OpenCS.Gmsh.Mapping;
using OpenCS.Gmsh.Parsing;
using Xunit;

namespace OpenCS.Gmsh.Tests;

public sealed class PlanarConstraintMeshMapperTests
{
    [Fact]
    public void Map_ProducesExactPointOrderedCurveAndRegionMappings()
    {
        var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] });
        region.ConstraintObjects =
        [
            PlanarConstraintObject.Point("point-1", new(0, 0),
                new PlanarStructuralFacet(PlanarStructuralKind.None), new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint)),
            PlanarConstraintObject.Curve("curve-1", [new(1, 1), new(3, 1)],
                new PlanarStructuralFacet(PlanarStructuralKind.None), new PlanarMeshFacet(PlanarMeshKind.EmbeddedCurve)),
            PlanarConstraintObject.Region("region-1", [new(1, 2), new(3, 2), new(3, 3), new(1, 3)],
                new PlanarStructuralFacet(PlanarStructuralKind.None), new PlanarMeshFacet(PlanarMeshKind.EmbeddedRegion))
        ];

        var document = new GmshMsh41Document
        {
            Nodes =
            [
                new(10, 0, 0, 0), new(11, 1, 1, 0), new(12, 2, 1, 0), new(13, 3, 1, 0),
                new(14, 1, 2, 0), new(15, 3, 2, 0), new(16, 3, 3, 0), new(17, 1, 3, 0)
            ],
            Elements =
            [
                new(100, 0, 1, 15, [10], 3001, "constraint:point-1:point"),
                new(101, 1, 20, 1, [11, 12], 3002, "constraint:curve-1:curve"),
                new(102, 1, 21, 1, [12, 13], 3002, "constraint:curve-1:curve"),
                new(103, 2, 30, 2, [14, 15, 16], 3003, "constraint:region-1:region"),
                new(104, 2, 30, 2, [14, 16, 17], 3003, "constraint:region-1:region")
            ]
        };
        var nodes = document.Nodes.Select((node, index) => new PlanarMeshNode(index, node.X, node.Y, node.X, node.Y, node.Z)).ToArray();
        var elements = document.Elements.Where(element => element.ElementType is 2 or 3)
            .Select((element, index) => new PlanarMeshElement(index, PlanarMeshElementKind.Triangle3,
                element.RawNodeIds.Select(raw => Array.IndexOf(document.Nodes.Select(node => node.RawId).ToArray(), raw)).ToArray())).ToArray();

        var result = PlanarConstraintMeshMapper.Map(region, document, nodes, elements);

        Assert.True(result.IsCalculable);
        var point = Assert.Single(result.Mappings, mapping => mapping.ConstraintObjectId == "point-1");
        Assert.Equal([0], point.PointNodeIndices);
        var curve = Assert.Single(result.Mappings, mapping => mapping.ConstraintObjectId == "curve-1");
        Assert.Equal([new PlanarMeshEdge(1, 2), new PlanarMeshEdge(2, 3)], curve.OrderedCurveEdges);
        var mappedRegion = Assert.Single(result.Mappings, mapping => mapping.ConstraintObjectId == "region-1");
        Assert.Equal([4, 5, 6, 7], mappedRegion.RegionNodeIndices);
        Assert.Equal([0, 1], mappedRegion.RegionElementIndices);
    }

    [Fact]
    public void Map_RejectsCurveWithoutExactEndpointNode()
    {
        var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] });
        region.ConstraintObjects =
        [PlanarConstraintObject.Curve("curve-1", [new(1, 1), new(3, 1)],
            new PlanarStructuralFacet(PlanarStructuralKind.None), new PlanarMeshFacet(PlanarMeshKind.EmbeddedCurve))];
        var document = new GmshMsh41Document
        {
            Nodes = [new(10, 1.1, 1, 0), new(11, 2, 1, 0)],
            Elements = [new(101, 1, 20, 1, [10, 11], 3002, "constraint:curve-1:curve")]
        };
        var nodes = document.Nodes.Select((node, index) => new PlanarMeshNode(index, node.X, node.Y, node.X, node.Y, node.Z)).ToArray();

        var result = PlanarConstraintMeshMapper.Map(region, document, nodes, []);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "planar_constraint_curve_endpoint_missing");
    }

    [Fact]
    public void Map_CarriesDerivedSourceProvenanceIntoSnapshotMapping()
    {
        var region = PlanarRegion.CreateFromContour(new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] });
        var constraint = PlanarConstraintObject.Point(
            "derived:point",
            new(1, 1),
            new PlanarStructuralFacet(
                PlanarStructuralKind.EmbeddedMember,
                new PlanarMasterReference("fem-member", "10"),
                PlanarDofMask.UX | PlanarDofMask.UY | PlanarDofMask.UZ),
            new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint));
        constraint.IsDerived = true;
        constraint.SourceReferences =
        [new PlanarSourceReference(10, "bar", [21], ["e21"], [1, 2], ["1", "2"] )];
        constraint.StructuralRelations =
        [new PlanarStructuralRelation(10, "bar", [21], ["e21"],
            new PlanarMasterReference("fem-member", "10"), PlanarStructuralKind.EmbeddedMember,
            PlanarDofMask.UX | PlanarDofMask.UY | PlanarDofMask.UZ)];
        var document = new GmshMsh41Document
        {
            Nodes = [new(10, 1, 1, 0)],
            Elements = [new(100, 0, 1, 15, [10], 3001, "constraint:derived:point:point")]
        };
        PlanarMeshNode[] nodes = [new(0, 1, 1, 1, 1, 0)];

        var result = PlanarConstraintMeshMapper.Map(region, document, nodes, [], [constraint]);

        var mapping = Assert.Single(result.Mappings);
        Assert.Equal([21], mapping.SourceReferences.Single().ElementIds);
        Assert.Equal(PlanarStructuralKind.EmbeddedMember, mapping.StructuralRelations.Single().Kind);
    }
}
