using CScore.Fem;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Загружения и комбинации в дереве схемы: счётчики нагрузок, состав комбинаций, активное загружение.</summary>
public sealed class FemLoadTreeTests
{
    [Fact]
    public void Details_OnlyNonEmpty_AndSelfWeight()
    {
        var lc = new FemLoadCase { Tag = "G", SelfWeightFactor = 1.1 };
        var details = FemLoadTree.Details(lc, new FemLoadCaseCounts(Node: 3, Member: 0, Element: 5, Kinematic: 0, MeshNode: 0));
        Assert.Equal(
            [$"{Loc.S("FemLoadTreeNodal")} — 3", $"{Loc.S("FemLoadTreeArea")} — 5",
             string.Format(Loc.S("FemLoadTreeSelfWeight"), 1.1)],
            details.Select(d => d.Text));
    }

    [Fact]
    public void Terms_WithNumbersAndCoefficients()
    {
        List<FemLoadCase> cases = [new() { Id = 10, Tag = "G" }, new() { Id = 11, Tag = "Снег" }];
        var expression = new FemLoadExpression
        {
            Mode = FemLoadExpressionMode.Sum,
            Terms = [new FemLoadTerm { LoadCaseId = 10, Coefficient = 1.1 }, new FemLoadTerm { LoadCaseId = 11, Coefficient = 1.26 }],
        };
        Assert.Equal(
            [string.Format("{0:0.###} × 1. G", 1.1), string.Format("{0:0.###} × 2. Снег", 1.26)],
            FemLoadTree.Terms(expression, cases).Select(t => t.Text));
    }

    [Fact]
    public void ActiveTag_RememberedOrFirst()
    {
        List<FemLoadCase> cases = [new() { Tag = "G" }, new() { Tag = "Q" }];
        Assert.Equal("Q", FemLoadTree.ActiveTag(cases, "Q"));
        Assert.Equal("G", FemLoadTree.ActiveTag(cases, "удалено"));
        Assert.Equal("G", FemLoadTree.ActiveTag(cases, null));
        Assert.Null(FemLoadTree.ActiveTag([], "Q"));
    }

    [Fact]
    public void Tree_ReadsLoadCasesCountsAndActive()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-load-tree-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Рама" };
            db.SaveFemSchema(schema);
            var node = new FemNode { Id = -1, NodeTag = "1" };
            List<FemLoadCase> cases = [new() { Id = -1, Tag = "G", Sp20Type = "permanent", SelfWeightFactor = 1 },
                new() { Id = -2, Tag = "Q" }];
            var load = new FemNodeLoad { NodeId = -1, LoadCaseId = -2, Fz = -1e3 };
            var definition = new FemLoadDefinition { SchemaId = schema.Id, Tag = "C1" };
            definition.SetExpression(new FemLoadExpression
            {
                Mode = FemLoadExpressionMode.Sum,
                Terms = [new FemLoadTerm { LoadCaseId = -1, Coefficient = 1.1 }, new FemLoadTerm { LoadCaseId = -2, Coefficient = 1.2 }],
            });
            db.SaveFemSchemaEdit(schema.Id, [node], [], [], cases, [load], [], [], definitions: [definition]);

            var counts = db.GetFemLoadCaseCounts(schema.Id);
            var saved = db.GetFemLoadCases(schema.Id);
            Assert.Equal(1, counts[saved[1].Id].Node);
            Assert.False(counts.ContainsKey(saved[0].Id));

            Assert.Null(db.GetFemActiveLoadCaseTag(schema.Id));
            db.SetFemActiveLoadCaseTag(schema.Id, "Q");
            Assert.Equal("Q", db.GetFemActiveLoadCaseTag(schema.Id));

            var tree = new FemSchemaTreeVM(schema, db, []);
            var loads = tree.LoadCasesSubNode;
            Assert.Equal(2, loads.Count);
            Assert.True(loads.IsVisible);
            Assert.Equal(["1. G", "2. Q"], loads.Items.Select(i => i.Label));
            Assert.Equal([false, true], loads.Items.Select(i => i.IsActive));
            Assert.Equal("permanent", loads.Items[0].Sp20Type);
            Assert.Single(loads.Items[1].Details);

            loads.SetActive("G");
            Assert.Equal([true, false], loads.Items.Select(i => i.IsActive));

            var combo = Assert.Single(tree.CombinationsSubNode.Items);
            Assert.Equal("C1", combo.Label);
            Assert.Equal(2, combo.Terms.Count);

            db.SetFemActiveLoadCaseTag(schema.Id, null);
            Assert.Null(db.GetFemActiveLoadCaseTag(schema.Id));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }
    }
}
