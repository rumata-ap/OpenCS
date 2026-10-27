using CScore.Fem;
using CScore.Fem.Fragment;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Fem.Fragment;

public sealed class FragmentParentMapTests
{
    static PlanarVector3 V(double x, double y, double z) => new(x, y, z);

    static FemMeshNode N(string tag, double x, double y, double z) => new() { NodeTag = tag, X = x, Y = y, Z = z };

    static FemElement E(string tag, string type, string nodes) => new() { ElemTag = tag, ElemType = type, NodeIdsJson = nodes };

    [Fact]
    public void ShellsAndBars_MappedByCentroidAndMidpoint_OthersUnmapped()
    {
        FragmentRefElement[] refs =
        [
            new("101", "shell", [V(0, 0, 0), V(1, 0, 0), V(0, 1, 0), V(1, 1, 0)]),
            new("102", "shell", [V(1, 0, 0), V(2, 0, 0), V(1, 1, 0), V(2, 1, 0)]),
            new("201", "beam", [V(5, 0, 0), V(5, 0, 3)]),
        ];
        var nodes = new[]
        {
            N("1", 0, 0, 0), N("2", 0.5, 0, 0), N("3", 0, 1, 0), N("4", 0.5, 1, 0),
            N("5", 1.5, 0, 0), N("6", 2, 0, 0), N("7", 1.5, 1, 0), N("8", 2, 1, 0),
            N("10", 5, 0, 0), N("11", 5, 0, 1), N("12", 5, 0, 2),
            N("20", 0, 0, 1), N("21", 1, 0, 1), N("22", 0, 1, 1),
        }.ToDictionary(n => n.NodeTag, StringComparer.Ordinal);
        FemElement[] elements =
        [
            E("1", "shell", "[1,2,3,4]"), E("2", "shell", "[5,6,7,8]"),
            E("3", "beam", "[10,11]"), E("4", "beam", "[11,12]"),
            E("5", "shell", "[20,21,22]"),
        ];

        var map = FragmentParentMap.Build(elements, nodes, new ParentMeshMapper(refs));

        Assert.Equal("101", map.ByTag["1"].Tag);
        Assert.Equal("102", map.ByTag["2"].Tag);
        Assert.Equal("201", map.ByTag["3"].Tag);
        Assert.Equal("201", map.ByTag["4"].Tag);
        Assert.False(map.ByTag.ContainsKey("5"));
        Assert.Equal(["5"], map.Unmapped);
    }
}
