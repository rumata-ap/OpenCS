using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Разбор номеров узлов КЭ в 3D-виде — тот же результат, что у JsonSerializer.</summary>
public class Fem3DNodeIdsTests
{
    [Theory]
    [InlineData("[1,2,3,4]", new[] { 1, 2, 3, 4 })]
    [InlineData(" [ 10 , -2 ] ", new[] { 10, -2 })]
    [InlineData("[]", new int[0])]
    [InlineData("", new int[0])]
    [InlineData(null, new int[0])]
    [InlineData("[1.0,2]", null)]   // нестандартная запись — через JsonSerializer (и его исключение)
    public void NodeIds_ParsesLikeJson(string? json, int[]? expected)
    {
        if (expected == null)
            Assert.ThrowsAny<System.Text.Json.JsonException>(() => Fem3DVM.NodeIds(json));
        else
            Assert.Equal(expected, Fem3DVM.NodeIds(json));
    }
}
