using CScore.Fem;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>ГУ в дереве схемы: типы узловых связей, строка-мозаика переменных пружин, упругое основание, шарниры и
/// жёсткие тела отдельными ветками, правка типа связи группой.</summary>
public sealed class FemBoundaryTreeTests
{
    static readonly FemSchema Schema = new() { Tag = "Схема" };
    static readonly string Scad = FemLoadOrigin.Import("scad");

    static FemNode Node(string tag, int mask) => new() { NodeTag = tag, DofMask = mask };

    static FemMeshNodeSupport Support(string tag, int mask, string origin) => new() { NodeTag = tag, Mask = mask, Origin = origin };

    static FemSpring Spring(string tag, double kz, string kind = FemSpringTargetKinds.MeshNode)
    {
        var s = new FemSpring { NodeTag = tag, TargetKind = kind, Origin = Scad };
        s.SetStiffnesses([0, 0, kz, 0, 0, 0]);
        return s;
    }

    [Fact]
    public void NodalSupports_GroupByMaskStiffnessAndOrigin_BothLevelsTogether()
    {
        var groups = FemBoundaryTree.NodalGroups(Schema,
            [Node("1", 63), Node("2", 63), Node("3", 0), Node("4", 4)],
            [
                Support("10", 63, FemLoadOrigin.Manual),
                Support("11", 7, Scad),
                Support("12", 7, Scad),
                Support("13", 0, FemLoadOrigin.Manual), // отметка ручного узла без закреплений
                Support("20", 3, Scad),
            ],
            [Spring("20", 5e6), Spring("21", 5e6), Spring("22", 5e6)]);

        // Жёсткие связи (заделки, шарниры, прочие маски), затем упругие; узел 20 — закрепление и пружина одной связью.
        string kz = $"Kz = 5000 {Loc.S("FemBoundaryUnitK")}";
        Assert.Equal(
            [FemBoundaryTree.SupportLabel(63), FemBoundaryTree.SupportLabel(7), "Tz", kz, "Tx Ty; " + kz],
            groups.Select(g => g.Label));
        Assert.Equal([3, 2, 1, 2, 1], groups.Select(g => g.Count));
        Assert.Equal(Loc.S("FemBoundaryOriginManual"), groups[0].OriginText);
        Assert.Equal("SCAD", groups[1].OriginText);
        Assert.All(groups, g => Assert.True(g.IsEditable));
        Assert.Equal(3, groups[4].Mask);
        Assert.Equal(5e6, groups[4].Stiffnesses[2]);

        var fixedTargets = groups[0].Targets();
        Assert.Equal(["1", "2"], fixedTargets.NodeTags);
        Assert.Equal(["10"], fixedTargets.MeshNodeTags);
        Assert.Equal(["21", "22"], groups[3].Targets().MeshNodeTags);
    }

    [Fact]
    public void ManyStiffnessSets_BecomeOneMosaicRow()
    {
        var springs = Enumerable.Range(1, FemBoundaryTree.MaxExactGroups + 1)
            .Select(i => Spring(i.ToString(), 1e6 * i)).ToList();

        var groups = FemBoundaryTree.NodalGroups(Schema, [], [], springs);

        var row = Assert.Single(groups);
        Assert.True(row.IsMosaic);
        Assert.False(row.IsEditable);
        Assert.Equal(springs.Count, row.Count);
        Assert.Equal(string.Format(Loc.S("FemBoundaryVariableSprings"), "Kz"), row.Label);
        Assert.Equal(springs.Select(s => s.NodeTag), row.Targets().MeshNodeTags);
    }

    [Fact]
    public void ManyRigidBodies_AreGroupedByKindAndMask()
    {
        var bodies = Enumerable.Range(1, FemBoundaryTree.MaxRigidBodyItems + 1).Select(i =>
        {
            var b = new FemRigidBody { MasterNodeTag = $"m{i}", Kind = i % 2 == 0 ? FemRigidBodyKinds.Coupled : FemRigidBodyKinds.Rigid };
            b.SetSlaveNodeTags([$"s{i}"]);
            return b;
        }).ToList();

        var groups = FemBoundaryTree.RigidBodyItems(Schema, bodies);

        Assert.Equal([26, 25], groups.Select(g => g.Count));
        Assert.Contains("m1", groups[0].Targets().MeshNodeTags);
        Assert.Contains("s1", groups[0].Targets().MeshNodeTags);
        Assert.Single(FemBoundaryTree.RigidBodyItems(Schema, bodies.Take(1)));
    }

    [Fact]
    public void ReleaseLabel_ListsLocalForcesPerEnd()
    {
        Assert.Equal("I: —; J: My Mz", FemBoundaryTree.ReleaseLabel(0, 0b11_0000));
        Assert.Equal("I: N Qy Qz T My Mz; J: —", FemBoundaryTree.ReleaseLabel(FemBoundaryDofs.All, 0));
    }

    [Fact]
    public void Tree_ReadsBoundaryFromDatabase_AndHidesEmptyBranches()
    {
        string path = TempDbPath();
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "SCAD", SourceType = "scad" };
            db.SaveFemSchema(schema);

            var tree = new FemSchemaTreeVM(schema, db, []);
            var bc = tree.BoundarySubNode;
            Assert.False(bc.IsVisible);
            Assert.Contains(bc.Releases, tree.SubNodes);
            Assert.Contains(bc.RigidBodies, tree.SubNodes);
            Assert.DoesNotContain(bc.Releases, bc.Children);

            db.SaveFemMeshSnapshot(schema.Id,
                [MeshNode("1", 0), MeshNode("2", 1), MeshNode("3", 2), MeshNode("4", 3)],
                [Bar("1", "[1,2]"), Bar("2", "[2,3]"), Bar("3", "[3,4]"), Shell("4", "[1,2,3,4]")]);
            var body = new FemRigidBody { MasterNodeTag = "1", SourceElemTag = "100" };
            body.SetSlaveNodeTags(["2", "3"]);
            db.SaveFemBoundary(schema.Id, Scad,
                [new FemMeshNodeSupport { NodeTag = "1", Mask = FemBoundaryDofs.All }],
                [Spring("4", 1e6)], [body],
                new Dictionary<string, FemElementBoundaryProps>
                {
                    ["1"] = new(null, 0b11_0000, null),
                    ["2"] = new(null, 0b11_0000, null),
                    ["3"] = new(0b10_0000, 0b10_0000, null),
                    ["4"] = new(null, null, 2e7),
                });

            tree.ReloadMeshSnapshot();
            Assert.True(bc.IsVisible);
            Assert.Equal(2, bc.NodalSupports.Count);
            Assert.Equal(2, bc.NodalSupports.Groups.Count);
            Assert.Equal(1, bc.Foundation.Count);
            Assert.Empty(bc.Foundation.Groups);
            Assert.Equal(3, bc.Releases.Count);
            Assert.Equal(1, bc.RigidBodies.Count);

            var pair = bc.Releases.Groups.Single(g => g.Count == 2);
            Assert.Equal("I: —; J: My Mz", pair.Label);
            Assert.Equal(["1", "2"], pair.Targets().MeshElemTags);
            Assert.Equal(["1", "2", "3"], bc.RigidBodies.Groups.Single().Targets().MeshNodeTags);

            // Правка типа связи группой: узлы становятся ручными с новыми значениями.
            var fixedGroup = bc.NodalSupports.Groups.First(g => g.Mask == FemBoundaryDofs.All);
            db.SetFemMeshNodesBoundary(schema.Id, fixedGroup.Targets().MeshNodeTags, 4, [0, 0, 0, 0, 0, 3e3]);
            tree.ReloadMeshSnapshot();
            var edited = bc.NodalSupports.Groups.Single(g => g.Mask == 4);
            Assert.Equal(Loc.S("FemBoundaryOriginManual"), edited.OriginText);
            Assert.Equal(3e3, edited.Stiffnesses[5]);

            // «Удалить связь со всех узлов»: без закреплений и пружин узел уходит из узловых связей.
            int before = bc.NodalSupports.Count;
            var editedTags = edited.Targets().MeshNodeTags.ToList();
            db.SetFemMeshNodesBoundary(schema.Id, editedTags, 0, new double[6]);
            tree.ReloadMeshSnapshot();
            Assert.Equal(before - edited.Count, bc.NodalSupports.Count);
            Assert.DoesNotContain(bc.NodalSupports.Groups, g => g.Mask == 4);
            db.SetFemMeshNodesBoundary(schema.Id, editedTags, 4, [0, 0, 0, 0, 0, 3e3]);
            tree.ReloadMeshSnapshot();

            // Повторный перенос без ГУ: ручная связь остаётся, импортные ветки скрываются.
            db.SaveFemBoundary(schema.Id, Scad, [], [], [], new Dictionary<string, FemElementBoundaryProps>());
            tree.ReloadMeshSnapshot();
            Assert.Equal(1, bc.NodalSupports.Count);
            Assert.False(bc.Foundation.IsVisible);
            Assert.False(bc.Releases.IsVisible);
            Assert.False(bc.RigidBodies.IsVisible);
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
            var nodal = tree.BoundarySubNode.NodalSupports;
            Assert.Equal(2, nodal.Count);
            Assert.Equal([["1"], ["2"]], nodal.Groups.Select(g => g.Targets().NodeTags));
            Assert.All(nodal.Groups, g => Assert.Equal(Loc.S("FemBoundaryOriginManual"), g.OriginText));
            Assert.False(tree.BoundarySubNode.Foundation.IsVisible);
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
