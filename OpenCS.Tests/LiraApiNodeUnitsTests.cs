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

    /// <summary>
    /// Таблица 5 «C1C2 Пластины» в т/м³ (как на «Мирной»): C1 → Н/м³, нулевые строки пропускаются, ненулевой C2
    /// считается; C1 стержня (КЭ не пластина) в ГУ КЭ не попадает.
    /// </summary>
    [Fact]
    public void ParsePlateFoundation_Table5_TonPerCubicMetre()
    {
        var data = new LiraSchemaData();
        data.Elements.Add(new LiraElementRecord(10, 44, 0, 1, [1, 2, 3, 4]));
        data.Elements.Add(new LiraElementRecord(11, 42, 0, 1, [1, 2, 3]));
        data.Elements.Add(new LiraElementRecord(12, 10, 0, 2, [1, 2]));
        LiraApiSchemaReader.ParsePlateFoundation(new object[,]
        {
            { 10, 959.0, 0.0, 2.11, 0.0, 19.9, "0 - Линейная" },
            { 11, 1000.0, 0.0, 0.0, 0.0, 0.0, "0 - Линейная" },
            { 12, 500.0, 0.0, 0.0, 0.0, 0.0, "0 - Линейная" },
            { 13, 0.0, 0.0, 0.0, 0.0, 0.0, "0 - Линейная" },
        }, 9.80665 * 1000, data);

        Assert.Equal(959 * 9806.65, data.PlateFoundationC1[10], 6);
        Assert.False(data.PlateFoundationC1.ContainsKey(13));
        Assert.Equal(1, data.PlateFoundationBeyondC1);
        var props = LiraSchemaConverter.ToFemElementBoundaryProps(data);
        Assert.Equal(["10", "11"], props.Keys.Order());
        Assert.Equal(1000 * 9806.65, props["11"].FoundationC1!.Value, 6);
    }
}
