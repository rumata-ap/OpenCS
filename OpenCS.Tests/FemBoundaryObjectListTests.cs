using CScore.Fem;
using CScore.Planar;
using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Состав типа ГУ текстом («Узлы №» RFEM): диапазоны узлов, грани «КонЭ/сторона» и «КонЭ/о1/сторона».</summary>
public sealed class FemBoundaryObjectListTests
{
    [Fact]
    public void FormatTags_CollapsesRuns_KeepsPairs_AppendsNonNumeric()
    {
        Assert.Equal("1-4 7 8 12 A", FemBoundaryObjectList.FormatTags(["8", "A", "3", "1", "2", "4", "12", "7", "3"]));
        Assert.Equal("", FemBoundaryObjectList.FormatTags([]));
    }

    [Fact]
    public void ParseTags_ExpandsRanges_AnySeparators_RoundTrip()
    {
        var tags = FemBoundaryObjectList.ParseTags(" 1-3, 7;A\n 2 ", out var error);
        Assert.Null(error);
        Assert.Equal(["1", "2", "3", "7", "A"], tags!);
        Assert.Equal("1-3 7 A", FemBoundaryObjectList.FormatTags(tags!));
    }

    [Theory]
    [InlineData("5-2")]
    [InlineData("1-x")]
    public void ParseTags_BadRange_IsError(string text)
    {
        Assert.Null(FemBoundaryObjectList.ParseTags(text, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Edges_OuterAndHole_RoundTrip_SortedByMember()
    {
        FemPlateEdgeKey outer = new("12", BoundaryLoop.Outer, 0, 2), hole = new("12", BoundaryLoop.Hole, 0, 1), other = new("3", BoundaryLoop.Outer, 0, 0);
        string text = FemBoundaryObjectList.FormatEdges([hole, outer, other]);
        Assert.Equal("3/1 12/3 12/о1/2", text);
        Assert.Equal([other, outer, hole], FemBoundaryObjectList.ParseEdges(text, out _)!);
        // Латинская «o» в номере отверстия — то же.
        Assert.Equal([hole], FemBoundaryObjectList.ParseEdges("12/o1/2", out _)!);
    }

    [Theory]
    [InlineData("12")]
    [InlineData("12/0")]
    [InlineData("12/x1/2")]
    [InlineData("/2")]
    public void ParseEdges_Bad_IsError(string text)
    {
        Assert.Null(FemBoundaryObjectList.ParseEdges(text, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}
