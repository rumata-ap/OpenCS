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
    // Физически нелинейные КЭ — как линейный аналог (тип + 400).
    [InlineData(405, 2, ScadElementKind.Beam)]
    [InlineData(410, 2, ScadElementKind.Beam)]
    [InlineData(401, 2, ScadElementKind.Beam)]
    [InlineData(444, 4, ScadElementKind.Shell)]
    [InlineData(442, 3, ScadElementKind.Shell)]
    [InlineData(411, 4, ScadElementKind.Shell)]
    [InlineData(405, 3, ScadElementKind.Skip)]
    [InlineData(421, 4, ScadElementKind.Skip)]
    [InlineData(451, 2, ScadElementKind.Skip)]
    [InlineData(500, 2, ScadElementKind.Skip)]
    [InlineData(400, 2, ScadElementKind.Skip)]
    public void Classify_ModelTypes(int type, int nodeCount, ScadElementKind expected) =>
        Assert.Equal(expected, ScadElementKinds.Classify(type, nodeCount));

    [Theory]
    [InlineData(405, 5)]
    [InlineData(444, 44)]
    [InlineData(499, 99)]
    [InlineData(44, 44)]
    [InlineData(400, 400)]
    [InlineData(500, 500)]
    public void LinearAnalog_SubtractsNonlinearPrefix(int type, int expected) =>
        Assert.Equal(expected, ScadElementKinds.LinearAnalog(type));
}
