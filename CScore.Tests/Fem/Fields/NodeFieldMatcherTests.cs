using CScore.Fem.Fields;
using Xunit;

namespace CScore.Tests.Fem.Fields;

public class NodeFieldMatcherTests
{
    [Fact]
    public void Match_PicksNearest_ReportsConflictsAndOrphans()
    {
        var nodes = new List<FieldNode> { new("1", new(0, 0, -16.3)), new("2", new(5, 0, -16.3)), new("3", new(10, 0, -16.3)) };
        var points = new List<FieldPoint>
        {
            new(new(0.3, 0.2, 0), [100]),     // → 1
            new(new(4.9, 0, 0), [200]),       // → 2, ближе
            new(new(5.4, 0, 0), [210]),       // → 2, конфликт, отброшена
            new(new(20, 0, 0), [300]),        // без узла
        };
        var r = NodeFieldMatcher.Match(points, hasZ: false, nodes, tolerance: 0.5);
        var byTag = r.Targets.ToDictionary(t => t.Tag);
        Assert.Equal(FieldHow.Matched, byTag["1"].How);
        Assert.Equal(100, byTag["1"].Values![0]);
        Assert.Equal(200, byTag["2"].Values![0]);
        Assert.Equal(FieldHow.None, byTag["3"].How);
        Assert.Contains(r.Diagnostics, d => d.Code == "field_node_conflict" && d.Tags.SequenceEqual(["2"]));
        Assert.Contains(r.Diagnostics, d => d.Code == "field_point_unmatched" && d.Tags.Count == 1);
        Assert.Contains(r.Diagnostics, d => d.Code == "field_node_unmatched" && d.Tags.SequenceEqual(["3"]));
        Assert.Equal(1, r.PointsUnused);
    }

    [Fact]
    public void Match_WithZ_UsesSpatialDistance()
    {
        var nodes = new List<FieldNode> { new("1", new(0, 0, 0)), new("2", new(0, 0, -10)) };
        var r = NodeFieldMatcher.Match([new(new(0, 0, -9.8), [5])], hasZ: true, nodes, 0.5);
        Assert.Equal(FieldHow.Matched, r.Targets.Single(t => t.Tag == "2").How);
    }
}
