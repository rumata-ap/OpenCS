using CScore.Import;
using Xunit;

namespace CScore.Tests;

public sealed class LiraSchemaConverterTests
{
    static LiraSchemaData BuildData() => new()
    {
        Nodes = { new LiraNodeRecord(1, 0, 0, 0, 0), new LiraNodeRecord(2, 1, 0, 0, 0), new LiraNodeRecord(3, 1, 1, 0, 0) },
        Elements =
        {
            new LiraElementRecord(10, 10, 1, 5, [1, 2]),
            new LiraElementRecord(20, 42, 1, 7, [1, 2, 3]),
        },
        BarStiffnesses = { new LiraBarStiffnessRecord(5, "Балка 20x30", 1, 1, 1, 1) },
        PlateStiffnesses = { new LiraPlateStiffnessRecord(7, "Плита 200", 1, 0.2, 200) },
    };

    [Fact]
    public void ToFemMeshNodesMapsAllNodesWithSchemaId()
    {
        var nodes = LiraSchemaConverter.ToFemMeshNodes(BuildData(), schemaId: 9);

        Assert.Equal(3, nodes.Length);
        Assert.All(nodes, n => Assert.Equal(9, n.SchemaId));
        Assert.Equal("2", nodes[1].NodeTag);
        Assert.Equal(1, nodes[1].X);
    }

    [Fact]
    public void ToFemMeshBarElementsMapsOnlyTwoNodeElementsWithBeamType()
    {
        var elements = LiraSchemaConverter.ToFemMeshBarElements(BuildData(), schemaId: 9);

        Assert.Single(elements);
        Assert.Equal("10", elements[0].ElemTag);
        Assert.Equal("beam", elements[0].ElemType);
        Assert.Equal("Балка 20x30", elements[0].SectionTag);
        Assert.Equal(1, elements[0].Node1);
        Assert.Equal(2, elements[0].Node2);
    }

    [Fact]
    public void ToFemMeshShellElementsMapsOnlyThreeOrFourNodeElementsWithShellType()
    {
        var elements = LiraSchemaConverter.ToFemMeshShellElements(BuildData(), schemaId: 9);

        Assert.Single(elements);
        Assert.Equal("20", elements[0].ElemTag);
        Assert.Equal("shell", elements[0].ElemType);
        Assert.Equal("Плита 200", elements[0].SectionTag);
        Assert.Equal(3, elements[0].Node3);
    }
}
