using System.IO;
using OpenCS.Gmsh.Parsing;
using Xunit;

namespace OpenCS.Gmsh.Tests;

public sealed class GmshMsh41ReaderTests
{
    [Fact]
    public void Read_MapsElementBlockEntityToPhysicalGroup()
    {
        var document = GmshMsh41Reader.Read(ValidMixedFixture);

        var triangle = Assert.Single(document.Elements, element => element.ElementType == 2);
        Assert.Equal(2001, triangle.PhysicalGroup);
        Assert.Equal(2, triangle.EntityDimension);
        Assert.Equal("constraint:region-1:region", triangle.PhysicalName);

        var line = Assert.Single(document.Elements, element => element.ElementType == 1);
        Assert.Equal(3001, line.PhysicalGroup);
        Assert.Equal("constraint:curve-1:curve", line.PhysicalName);
    }

    [Fact]
    public void Read_HandlesMultipleNodeAndElementBlocks()
    {
        var document = GmshMsh41Reader.Read(ValidMixedFixture);

        Assert.Equal(4, document.Nodes.Count);
        Assert.Equal(4, document.Elements.Count);
        Assert.Equal([1, 2, 3, 4], document.Nodes.Select(node => node.RawId));
        Assert.All(document.Elements, element => Assert.NotEmpty(element.RawNodeIds));
    }

    [Fact]
    public void Read_ReportsUnsupportedElementType()
    {
        var document = GmshMsh41Reader.Read(ValidMixedFixture.Replace("2 1 2 1\n2 1 2 3", "2 1 4 1\n2 1 2 3 4"));

        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "gmsh_unsupported_element");
        Assert.False(document.IsCalculable);
    }

    [Fact]
    public void Read_RejectsBinaryMeshFormat()
    {
        var binary = ValidMixedFixture.Replace("4.1 0 8", "4.1 1 8");

        var exception = Assert.Throws<InvalidDataException>(() => GmshMsh41Reader.Read(binary));

        Assert.Contains("ASCII", exception.Message);
    }

    [Fact]
    public void Read_RejectsDuplicateNodeTags()
    {
        var duplicate = ValidMixedFixture.Replace("1\n2\n3\n4\n0 0 0", "1\n2\n2\n4\n0 0 0");

        Assert.Throws<InvalidDataException>(() => GmshMsh41Reader.Read(duplicate));
    }

    const string ValidMixedFixture = """
        $MeshFormat
        4.1 0 8
        $EndMeshFormat
        $PhysicalNames
        3
        1 3001 "constraint:curve-1:curve"
        1 3002 "constraint:point-1:point"
        2 2001 "constraint:region-1:region"
        $EndPhysicalNames
        $Entities
        4 1 1 0
        1 0 0 0 1 3002
        2 1 0 0 1 0 0 0 0
        3 0 1 0 1 0 0 0 0
        4 1 1 0 1 0 0 0 0
        5 0 0 0 1 1 0 1 3001 2 1 2
        1 0 0 0 1 1 0 1 2001 4 1 2 3 4
        $EndEntities
        $Nodes
        1 4 1 4
        2 1 0 4
        1
        2
        3
        4
        0 0 0
        1 0 0
        0 1 0
        1 1 0
        $EndNodes
        $Elements
        4 4 1 4
        0 1 15 1
        4 4
        1 5 1 1
        5 1 2
        2 1 2 1
        2 1 2 3
        2 1 3 1
        3 1 2 3 4
        $EndElements
        """;
}
