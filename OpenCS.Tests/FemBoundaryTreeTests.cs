using CScore.Fem;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Подузел «Граничные условия» дерева схемы: группировка, счётчики, объекты групп, видимость.</summary>
public sealed class FemBoundaryTreeTests
{
    static readonly FemSchema Schema = new() { Tag = "Схема" };

    static FemNode Node(string tag, int mask) => new() { NodeTag = tag, DofMask = mask };

    static FemMeshNodeSupport Support(string tag, int mask, string origin) => new() { NodeTag = tag, Mask = mask, Origin = origin };

    [Fact]
    public void Supports_GroupByMaskAndOrigin_BothLevelsTogether()
    {
        var groups = FemBoundaryTree.SupportGroups(Schema,
            [Node("1", 63), Node("2", 63), Node("3", 0), Node("4", 4)],
            [
                Support("10", 63, FemLoadOrigin.Manual),
                Support("11", 7, FemLoadOrigin.Import("scad")),
                Support("12", 7, FemLoadOrigin.Import("scad")),
                Support("13", 0, FemLoadOrigin.Manual), // отметка ручного узла без закреплений
            ]);

        // Заделки, затем шарниры, затем прочие маски; внутри — сначала ручные.
        Assert.Equal([(63, 3), (7, 2), (4, 1)], groups.Select(g => (Mask(g.Label), g.Count)));
        Assert.Equal(FemBoundaryTree.SupportLabel(63), groups[0].Label);
        Assert.Equal("Tz", groups[2].Label);
        Assert.Equal(Loc.S("FemBoundaryOriginManual"), groups[0].OriginText);
        Assert.Equal("SCAD", groups[1].OriginText);

        var fixedTargets = groups[0].Targets();
        Assert.Equal(["1", "2"], fixedTargets.NodeTags);
        Assert.Equal(["10"], fixedTargets.MeshNodeTags);
        Assert.Equal(["11", "12"], groups[1].Targets().MeshNodeTags);

        static int Mask(string label) =>
            label == FemBoundaryTree.SupportLabel(63) ? 63 : label == FemBoundaryTree.SupportLabel(7) ? 7 : 4;
    }

    [Fact]
    public void Springs_GroupByStiffnessSet()
    {
        FemSpring Spring(string tag, string kind, double kz, double kux = 0)
        {
            var s = new FemSpring { NodeTag = tag, TargetKind = kind, Origin = FemLoadOrigin.Import("scad") };
            s.SetStiffnesses([0, 0, kz, kux, 0, 0]);
            return s;
        }

        var groups = FemBoundaryTree.SpringGroups(Schema,
        [
            Spring("1", FemSpringTargetKinds.MeshNode, 5e6),
            Spring("2", FemSpringTargetKinds.MeshNode, 5e6),
            Spring("3", FemSpringTargetKinds.Node, 5e6),
            Spring("4", FemSpringTargetKinds.MeshNode, 5e6, 2e4),
            Spring("5", FemSpringTargetKinds.MeshNode, 0),
        ]);

        Assert.Equal([3, 1], groups.Select(g => g.Count));
        Assert.StartsWith("Kz = 5000 ", groups[0].Label);
        Assert.Contains("Kux = 20 ", groups[1].Label);
        Assert.Equal(["3"], groups[0].Targets().NodeTags);
        Assert.Equal(["1", "2"], groups[0].Targets().MeshNodeTags);
    }

    [Fact]
    public void ReleaseLabel_ListsLocalForcesPerEnd()
    {
        Assert.Equal("I: —; J: My Mz", FemBoundaryTree.ReleaseLabel(0, 0b11_0000));
        Assert.Equal("I: N Qy Qz T My Mz; J: —", FemBoundaryTree.ReleaseLabel(FemBoundaryDofs.All, 0));
    }

    [Fact]
    public void Tree_ReadsBoundaryFromDatabase_AndHidesEmptyCategories()
    {
        string path = TempDbPath();
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "SCAD", SourceType = "scad" };
            db.SaveFemSchema(schema);

            var tree = new FemSchemaTreeVM(schema, db, []);
            Assert.False(tree.BoundarySubNode.IsVisible);

            db.SaveFemMeshSnapshot(schema.Id,
                [MeshNode("1", 0), MeshNode("2", 1), MeshNode("3", 2), MeshNode("4", 3)],
                [Bar("1", "[1,2]"), Bar("2", "[2,3]"), Bar("3", "[3,4]"), Shell("4", "[1,2,3,4]")]);
            var spring = new FemSpring { NodeTag = "4", TargetKind = FemSpringTargetKinds.MeshNode };
            spring.SetStiffnesses([0, 0, 1e6, 0, 0, 0]);
            var body = new FemRigidBody { MasterNodeTag = "1", SourceElemTag = "100" };
            body.SetSlaveNodeTags(["2", "3"]);
            db.SaveFemBoundary(schema.Id, FemLoadOrigin.Import("scad"),
                [new FemMeshNodeSupport { NodeTag = "1", Mask = FemBoundaryDofs.All }],
                [spring], [body],
                new Dictionary<string, FemElementBoundaryProps>
                {
                    ["1"] = new(null, 0b11_0000, null),
                    ["2"] = new(null, 0b11_0000, null),
                    ["3"] = new(0b10_0000, 0b10_0000, null),
                    ["4"] = new(null, null, 2e7),
                });

            tree.ReloadMeshSnapshot();
            var bc = tree.BoundarySubNode;
            Assert.True(bc.IsVisible);
            Assert.Equal(1, bc.Supports.Count);
            Assert.Equal(1, bc.Springs.Count);
            Assert.Equal(3, bc.Releases.Count);
            Assert.Equal(1, bc.Foundation.Count);
            Assert.Equal(1, bc.RigidBodies.Count);
            Assert.All(bc.Children, c => Assert.True(c.IsVisible));

            Assert.Equal([2, 1], bc.Releases.Groups.Select(g => g.Count).OrderDescending());
            var pair = bc.Releases.Groups.Single(g => g.Count == 2);
            Assert.Equal("I: —; J: My Mz", pair.Label);
            Assert.Equal(["1", "2"], pair.Targets().MeshElemTags);
            Assert.Equal(["4"], bc.Foundation.Groups.Single().Targets().MeshElemTags);
            Assert.StartsWith("C1 = 20000 ", bc.Foundation.Groups.Single().Label);
            Assert.Equal(["1", "2", "3"], bc.RigidBodies.Groups.Single().Targets().MeshNodeTags);
            Assert.Equal(["1", "2", "3"], bc.Releases.Targets().MeshElemTags.Order());

            // Повторный перенос без ГУ: подузлы и сам узел скрываются.
            db.SaveFemBoundary(schema.Id, FemLoadOrigin.Import("scad"), [], [], [], new Dictionary<string, FemElementBoundaryProps>());
            tree.ReloadMeshSnapshot();
            Assert.False(bc.IsVisible);
            Assert.All(bc.Children, c => Assert.Equal(0, c.Count));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void Tree_ShowsSupportsOfStructuralNodes()
    {
        string path = TempDbPath();
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Рама" };
            db.SaveFemSchema(schema);
            db.SaveFemTopology(schema.Id, [Node("1", 63), Node("2", 7), Node("3", 0)], [], []);

            var tree = new FemSchemaTreeVM(schema, db, []);
            var supports = tree.BoundarySubNode.Supports;
            Assert.Equal(2, supports.Count);
            Assert.Equal([["1"], ["2"]], supports.Groups.Select(g => g.Targets().NodeTags));
            Assert.All(supports.Groups, g => Assert.Equal(Loc.S("FemBoundaryOriginManual"), g.OriginText));
            Assert.False(tree.BoundarySubNode.Springs.IsVisible);
        }
        finally
        {
            Delete(path);
        }
    }

    static FemMeshNode MeshNode(string tag, double x) => new() { NodeTag = tag, X = x, Origin = FemMember.MeshSourceImported };

    static FemElement Bar(string tag, string nodes) =>
        new() { ElemTag = tag, ElemType = "beam", NodeIdsJson = nodes, Origin = FemMember.MeshSourceImported };

    static FemElement Shell(string tag, string nodes) =>
        new() { ElemTag = tag, ElemType = "shell", NodeIdsJson = nodes, Origin = FemMember.MeshSourceImported };

    static string TempDbPath()
        => Path.Combine(Path.GetTempPath(), $"opencs-boundary-tree-{Guid.NewGuid():N}.db");

    static void Delete(string path)
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
    }
}
