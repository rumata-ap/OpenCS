using CScore.Import;
using OpenCS.Services;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Координаты узлов из таблицы API ЛИРЫ — в единицах геометрии документа; схема OpenCS — в метрах.</summary>
public sealed class LiraApiNodeUnitsTests
{
    [Fact]
    public void ParseNodes_MillimetreDocument_GivesMetres()
    {
        var data = new LiraSchemaData();
        LiraApiSchemaReader.ParseNodes(new object[,]
        {
            { 1, 10875.0, 16787.5, -3400.0, "1", "1", "1", "0", "0", "0" },
            { "x", 0, 0, 0, "", "", "", "", "", "" },
        }, 0.001, data);

        var n = Assert.Single(data.Nodes);
        Assert.Equal(10.875, n.X, 9);
        Assert.Equal(16.7875, n.Y, 9);
        Assert.Equal(-3.4, n.Z, 9);
        Assert.Equal(0b111, n.DofMask);
    }
}
