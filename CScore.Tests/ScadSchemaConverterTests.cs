using CScore.Import;
using Xunit;

namespace CScore.Tests;

public sealed class ScadSchemaConverterTests
{
    static ScadSchemaData BuildData() => new()
    {
        Nodes = { new ScadNodeRecord(1, 0, 0, 0), new ScadNodeRecord(2, 1, 0, 0), new ScadNodeRecord(3, 1, 1, 0), new ScadNodeRecord(4, 0, 1, 0) },
        Elements =
        {
            new ScadElementRecord(1, 2, 5, [1, 2]),
            new ScadElementRecord(2, 44, 6, [1, 2, 3, 4]),
        },
        Stiffnesses =
        {
            new ScadStiffnessRecord(5, "Балка 20x30", ScadStiffnessKind.Bar),
            new ScadStiffnessRecord(6, "Плита 200", ScadStiffnessKind.Shell, ThicknessM: 0.2),
        },
    };

    [Fact]
    public void ToFemMeshNodesMapsAllNodesWithSchemaId()
    {
        var nodes = ScadSchemaConverter.ToFemMeshNodes(BuildData(), schemaId: 3);

        Assert.Equal(4, nodes.Length);
        Assert.All(nodes, n => Assert.Equal(3, n.SchemaId));
        Assert.Equal("1", nodes[0].NodeTag);
    }

    [Fact]
    public void ToFemMeshElementsMapsBeamAndShellByNodeCount()
    {
        var elements = ScadSchemaConverter.ToFemMeshElements(BuildData(), schemaId: 3);

        Assert.Equal(2, elements.Length);
        var bar = Assert.Single(elements, e => e.ElemType == "beam");
        Assert.Equal("1", bar.ElemTag);
        Assert.Equal("Балка 20x30", bar.SectionTag);
        var shell = Assert.Single(elements, e => e.ElemType == "shell");
        Assert.Equal("2", shell.ElemTag);
        Assert.Equal("Плита 200", shell.SectionTag);
        Assert.Equal(0.2, shell.ThicknessM);
        Assert.Equal(4, shell.Node4);
    }
}
