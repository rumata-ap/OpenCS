using CScore;
using CScore.Fem;
using CScore.Planar;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Линейные связи в дереве схемы: типы по параметрам, грани и длина, потерявшие грань — отдельной строкой.</summary>
public sealed class FemLineSupportTreeTests
{
    static readonly FemSchema Schema = new() { Tag = "Плита" };

    /// <summary>Пластина «P» 6 × 4 м в плоскости XY.</summary>
    static (FemMember Member, PlanarRegion Region) Plate() => (
        new FemMember { ElemTag = "P", ElemType = "shell", PlanarRegionId = 1 },
        new PlanarRegion { Id = 1, Contours = [new Contour([0, 6, 6, 0, 0], [0, 0, 4, 4, 0], "P") { Type = ContourType.Hull }] });

    static FemLineSupport On(FemPlateEdge edge, FemLineSupport s)
    {
        s.AssignEdge(edge);
        return s;
    }

    [Fact]
    public void LineGroups_ByParameters_WithLength_AndLost()
    {
        var (member, region) = Plate();
        var edges = FemLineSupportGeometry.EdgesByMember([member], [region]);
        var e = edges["P"];
        var spring = new FemLineSupport();
        spring.SetStiffnesses([0, 0, 5e7, 0, 0, 0]);
        var lost = On(e[0], new FemLineSupport { Mask = 4 });
        lost.MemberTag = "удалён";

        var groups = FemBoundaryTree.LineGroups(Schema,
        [
            On(e[0], new FemLineSupport { Mask = 0b111, FixEdgeRotation = true }),
            On(e[2], new FemLineSupport { Mask = 0b111, FixEdgeRotation = true }),
            On(e[1], new FemLineSupport { Mask = FemBoundaryDofs.All }),
            On(e[3], spring),
            lost,
        ], edges);

        Assert.Equal(4, groups.Count);
        // Заделка, шарнирное опирание, упругая, потерявшие грань.
        Assert.Equal(FemBoundaryTree.SupportLabel(FemBoundaryDofs.All), groups[0].Label);
        Assert.Equal(Loc.S("FemLineSupportPresetPlate"), groups[1].Label);
        Assert.Equal(2, groups[1].Count);
        Assert.Equal(12, groups[1].LengthM, 9);
        Assert.Equal($"Kz′ = 50000 {Loc.S("FemBoundaryUnitKLine")}", groups[2].Label);
        Assert.True(groups[3].IsLost);
        Assert.False(groups[3].IsEditable);
        Assert.True(groups[1].IsEditable);

        Assert.Equal(new[] { e[0].Key, e[2].Key }, groups[1].Targets().PlateEdges);
        Assert.Equal(new[] { lost.Key }, groups[3].Targets().PlateEdges);
        Assert.Equal(0b111, groups[1].LineSupport!.Mask);
    }

    [Fact]
    public void LineLabel_EdgeRotationAndSprings()
    {
        var s = new FemLineSupport { Mask = 4, FixEdgeRotation = true };
        Assert.Equal($"Tz; {Loc.S("FemBoundaryLineEdgeRotation")}", FemBoundaryTree.LineLabel(s));
        var k = new FemLineSupport();
        k.SetStiffnesses([0, 0, 0, 0, 0, 2e4]);
        Assert.Equal($"Kuz′ = 20 {Loc.S("FemBoundaryUnitKrLine")}", FemBoundaryTree.LineLabel(k));
    }

    [Fact]
    public void Tree_ReadsLineSupportsFromDatabase()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-line-tree-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Плита" };
            db.SaveFemSchema(schema);
            var (member, region) = Plate();
            member.SchemaId = schema.Id;
            member.PlanarRegionId = db.AddPlanarRegion(region, schema.Id);
            db.SaveFemMember(member);
            region.Id = member.PlanarRegionId.Value;
            var edges = FemLineSupportGeometry.Edges(member, region);
            db.ReplaceFemLineSupports(schema.Id, edges.Select(x => On(x, new FemLineSupport { Mask = 0b111, FixEdgeRotation = true })).ToList());

            var tree = new FemSchemaTreeVM(schema, db, []);
            tree.ReloadMeshSnapshot();
            var line = tree.BoundarySubNode.LineSupports;
            Assert.True(tree.BoundarySubNode.IsVisible);
            Assert.True(line.IsVisible);
            Assert.Equal(4, line.Count);
            var group = Assert.Single(line.Groups);
            Assert.Equal(20, group.LengthM, 9);
            Assert.Equal(4, line.Targets().PlateEdges.Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }
    }
}
