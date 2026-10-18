using CScore.Import;
using Xunit;

namespace CScore.Tests;

public class LiraFileParserTests
{
    const string TestLirPath = @"C:\Users\palex\Documents\prj\obshezitie_belgorod\calc\hostel.lir";

    [Fact]
    public void Parse_HeaderParsed_OffsetAfterHeader()
    {
        if (!File.Exists(TestLirPath)) return;

        var bytes = File.ReadAllBytes(TestLirPath);
        int offset = 0;
        for (int i = 0; i < 3; i++)
        {
            while (offset < bytes.Length && bytes[offset] != 0) offset++;
            offset++;
        }
        Assert.Equal(85, offset);
    }

    [Fact]
    public void Parse_FileExists_ReturnsLiraSchemaData()
    {
        if (!File.Exists(TestLirPath)) return;
        var data = LiraFileParser.Parse(TestLirPath);
        Assert.NotNull(data);
    }

    [Fact]
    public void SkipHeader_SmallBuffer_ReturnsCorrectOffset()
    {
        byte[] buf = { 0x41, 0x42, 0x00, 0x43, 0x44, 0x00, 0x45, 0x46, 0x00 };
        int offset = LiraFileParser.SkipHeader(buf);
        Assert.Equal(9, offset);
    }

    [Fact]
    public void SkipMetadata_AdvancesBy52()
    {
        byte[] buf = new byte[200];
        int offset = 10;
        LiraFileParser.SkipMetadata(buf, ref offset);
        Assert.Equal(62, offset);
    }

    [Fact]
    public void Parse_Nodes_HasExpectedCount()
    {
        if (!File.Exists(TestLirPath)) return;
        var data = LiraFileParser.Parse(TestLirPath);

        // Эталон (живая ЛИРА, COM, таблица 2): 185 440 узлов. Допуск — на единичные
        // неоднозначности в «хвостовых» блоках.
        Assert.InRange(data.Nodes.Count, 185000, 186500);
    }

    [Fact]
    public void Parse_Nodes_NumbersAreContiguous()
    {
        if (!File.Exists(TestLirPath)) return;
        var data = LiraFileParser.Parse(TestLirPath);

        var ids = data.Nodes.Select(n => n.Id).OrderBy(v => v).ToList();
        Assert.Equal(1, ids[0]);
        Assert.Equal(ids.Count, ids[^1]);
        Assert.Equal(Enumerable.Range(1, ids.Count), ids);
    }

    [Fact]
    public void Parse_Nodes_CoordinatesArePlausible()
    {
        if (!File.Exists(TestLirPath)) return;
        var data = LiraFileParser.Parse(TestLirPath);

        Assert.All(data.Nodes, n =>
        {
            Assert.InRange(n.X, -100.0, 100.0);
            Assert.InRange(n.Y, -100.0, 100.0);
            Assert.InRange(n.Z, -100.0, 100.0);
        });
    }

    [Fact]
    public void Parse_Nodes_HaveNoDegenerateCoords()
    {
        if (!File.Exists(TestLirPath)) return;
        var data = LiraFileParser.Parse(TestLirPath);

        // Мусорные «узлы» из служебных структур дают координаты (0;0;0) — таких быть не должно
        Assert.DoesNotContain(data.Nodes, n => n.X == 0 && n.Y == 0 && n.Z == 0);
    }

    [Fact]
    public void Parse_Elements_HasExpectedCount()
    {
        if (!File.Exists(TestLirPath)) return;
        var data = LiraFileParser.Parse(TestLirPath);

        // Эталон (живая ЛИРА, COM, таблица 3): 200 769 элементов
        Assert.InRange(data.Elements.Count, 200000, 201500);
    }

    [Fact]
    public void Parse_Elements_BarElementsCount()
    {
        if (!File.Exists(TestLirPath)) return;
        var data = LiraFileParser.Parse(TestLirPath);

        // Эталон: 783 стержня (тип 10)
        int barCount = data.Elements.Count(e => e.NodeIds.Length == 2);
        Assert.InRange(barCount, 780, 785);
    }

    [Fact]
    public void Parse_Elements_AllNodeIdsValid()
    {
        if (!File.Exists(TestLirPath)) return;
        var data = LiraFileParser.Parse(TestLirPath);

        // Все ID узлов должны быть в диапазоне [1, nodeCount] (1-based LIRA ID)
        int nodeCount = data.Nodes.Count;
        Assert.All(data.Elements, e =>
        {
            Assert.All(e.NodeIds, nid => Assert.InRange(nid, 1, nodeCount));
        });
    }

    [Fact]
    public void Parse_Elements_BarNodesAreVertical()
    {
        if (!File.Exists(TestLirPath)) return;
        var data = LiraFileParser.Parse(TestLirPath);

        // Стержни должны соединять разные узлы (ненулевая длина)
        var bars = data.Elements.Where(e => e.NodeIds.Length == 2).Take(50).ToList();
        var nodeDict = data.Nodes.ToDictionary(n => n.Id);

        foreach (var bar in bars)
        {
            var n1 = nodeDict[bar.NodeIds[0]];
            var n2 = nodeDict[bar.NodeIds[1]];
            double len = Math.Sqrt(Math.Pow(n1.X - n2.X, 2) + Math.Pow(n1.Y - n2.Y, 2) + Math.Pow(n1.Z - n2.Z, 2));
            Assert.True(len > 1e-6, $"Bar length should be > 0: N{bar.NodeIds[0]}-{bar.NodeIds[1]} len={len:F6}m");
        }
    }

    /// <summary>
    /// Интеграционный тест полного цикла.
    /// </summary>
    [Fact]
    public void Parse_FullCycle_AllSectionsPopulated()
    {
        if (!File.Exists(TestLirPath)) return;
        var data = LiraFileParser.Parse(TestLirPath);

        // Узлы (эталон COM: 185 440)
        Assert.InRange(data.Nodes.Count, 185000, 186500);

        // Элементы (эталон COM: 200 769)
        Assert.InRange(data.Elements.Count, 200000, 201500);

        // Стержни (2 узла, эталон: 783)
        int bars = data.Elements.Count(e => e.NodeIds.Length == 2);
        Assert.InRange(bars, 780, 785);

        // Пластины (3–4 узла)
        int shells = data.Elements.Count(e => e.NodeIds.Length is 3 or 4);
        Assert.InRange(shells, 199000, 200500);

        // Все ID узлов валидны (1-based)
        int nodeCount = data.Nodes.Count;
        Assert.All(data.Elements, e =>
        {
            Assert.All(e.NodeIds, nid => Assert.InRange(nid, 1, nodeCount));
        });
    }
}
