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

    [Fact]
    public void ApplySupports_Table4_AddsMasksToNodes_AndConvertsToMeshSupports()
    {
        var data = new LiraSchemaData();
        LiraApiSchemaReader.ParseNodes(new object[,]
        {
            { 1, 0.0, 0.0, 0.0 }, { 2, 1.0, 0.0, 0.0 }, { 3, 2.0, 0.0, 0.0 },
        }, 1, data);
        LiraApiSchemaReader.ApplySupports(new object[,]
        {
            { 1, "1", "1", "1", "", "", "", "" },
            { 2, "", "", "#", "1", "1", "1", "1" },
            { 3, "", "", "", "", "", "", "" },
            { "", "", "", "", "", "", "", "" },
        }, data);

        Assert.Equal([0b111, 0b1111100, 0], data.Nodes.Select(n => n.DofMask));
        var supports = LiraSchemaConverter.ToFemMeshNodeSupports(data);
        Assert.Equal([("1", 0b111), ("2", 0b111100)], supports.Select(s => (s.NodeTag, s.Mask)));
        Assert.All(supports, s => Assert.Equal("import:lira", s.Origin));
    }
}
