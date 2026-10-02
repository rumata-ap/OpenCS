using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

public class ScadElementKindsTests
{
    [Theory]
    [InlineData(10, 2, ScadElementKind.Beam)]
    [InlineData(5, 2, ScadElementKind.Beam)]
    [InlineData(42, 3, ScadElementKind.Shell)]
    [InlineData(44, 4, ScadElementKind.Shell)]
    [InlineData(11, 4, ScadElementKind.Shell)]
    [InlineData(51, 1, ScadElementKind.Skip)]
    [InlineData(51, 2, ScadElementKind.Skip)]
    [InlineData(100, 3, ScadElementKind.Skip)]
    [InlineData(200, 2, ScadElementKind.Skip)]
    [InlineData(21, 4, ScadElementKind.Skip)]
    [InlineData(30, 3, ScadElementKind.Skip)]
    [InlineData(10, 3, ScadElementKind.Skip)]
    [InlineData(44, 8, ScadElementKind.Skip)]
    public void Classify_ModelTypes(int type, int nodeCount, ScadElementKind expected) =>
        Assert.Equal(expected, ScadElementKinds.Classify(type, nodeCount));
}
