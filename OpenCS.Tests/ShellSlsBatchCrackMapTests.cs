using OpenCS.ViewModels;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Карта ширины трещин по КЭ из пакета ПС2 слоистой пластины.</summary>
public sealed class ShellSlsBatchCrackMapTests
{
    const string Json = """
        {"acrc_lim_mm":0.3,"rows":[
          {"num":1,"elem":5,"status":"ok","acrc_mm":0.1},
          {"num":2,"elem":5,"status":"not_passed","acrc_mm":0.36},
          {"num":3,"elem":7,"status":"not_converged"},
          {"num":4,"status":"ok","acrc_mm":0.5},
          {"num":5,"elem":8,"status":"not_converged"},
          {"num":6,"elem":8,"status":"ok","acrc_mm":0.2}]}
        """;

    [Fact]
    public void MaxAcrcPerElement_SkipsRowsWithoutElem()
    {
        var map = ShellSlsBatchCrackMap.Build(Json);
        Assert.Equal(0.36, map.AcrcByElement[5]);
        Assert.Equal(1.2, map.UtilByElement[5], 6);
        Assert.Equal(0.2, map.AcrcByElement[8]);
        Assert.False(map.AcrcByElement.ContainsKey(4));
        Assert.Equal(0.3, map.AcrcLimMm);
    }

    [Fact]
    public void NotConvergedOnlyWhenNoRowConverged()
    {
        var map = ShellSlsBatchCrackMap.Build(Json);
        Assert.Equal([7], map.NotConvergedElements);
        Assert.Equal([8], map.PartlyNotConvergedElements);
    }

    [Fact]
    public void HasElements()
    {
        Assert.True(ShellSlsBatchCrackMap.Build(Json).HasElements);
        Assert.False(ShellSlsBatchCrackMap.Build("""{"rows":[{"num":1,"status":"ok","acrc_mm":0.1}]}""").HasElements);
    }
}
