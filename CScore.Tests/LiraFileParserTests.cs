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

        // Block 1 (11748) + Block 2 (173692) = 185440 узлов
        Assert.Equal(185440, data.Nodes.Count);
    }

    [Fact]
    public void Parse_Nodes_Block1NodesHaveZZero()
    {
        if (!File.Exists(TestLirPath)) return;
        var data = LiraFileParser.Parse(TestLirPath);

        // Первые 11748 узлов (Block 1) должны иметь Z = 0
        var block1Nodes = data.Nodes.Take(11748).ToList();
        Assert.All(block1Nodes, n => Assert.Equal(0.0, n.Z, 10));
    }

    [Fact]
    public void Parse_Nodes_Block2NodesHaveValidZ()
    {
        if (!File.Exists(TestLirPath)) return;
        var data = LiraFileParser.Parse(TestLirPath);

        // Узлы Block 2 (начиная с ID 11749) должны иметь Z в диапазоне [-5, 35]
        var block2Nodes = data.Nodes.Skip(11748).ToList();
        Assert.All(block2Nodes, n => Assert.InRange(n.Z, -5.0, 35.0));
    }

    [Fact]
    public void Parse_Elements_HasExpectedCount()
    {
        if (!File.Exists(TestLirPath)) return;
        var data = LiraFileParser.Parse(TestLirPath);
        Assert.Equal(200769, data.Elements.Count);
    }

    [Fact]
    public void Parse_Elements_BarElementsCount()
    {
        if (!File.Exists(TestLirPath)) return;
        var data = LiraFileParser.Parse(TestLirPath);

        int barCount = data.Elements.Count(e => e.NodeIds.Length == 2);
        Assert.Equal(783, barCount);
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

        // Стержни (type=10) должны быть либо вертикальными (колонны), либо горизонтальными (балки)
        // Проверяем что длина > 0.5м
        var bars = data.Elements.Where(e => e.NodeIds.Length == 2).Take(20).ToList();
        var nodeDict = data.Nodes.ToDictionary(n => n.Id);

        foreach (var bar in bars)
        {
            var n1 = nodeDict[bar.NodeIds[0]];
            var n2 = nodeDict[bar.NodeIds[1]];
            double len = Math.Sqrt(Math.Pow(n1.X - n2.X, 2) + Math.Pow(n1.Y - n2.Y, 2) + Math.Pow(n1.Z - n2.Z, 2));
            Assert.True(len > 0.5, $"Bar length should be > 0.5m: N{bar.NodeIds[0]}-{bar.NodeIds[1]} len={len:F3}m");
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

        // Узлы: Block 1 + Block 2
        Assert.Equal(185440, data.Nodes.Count);

        // Элементы
        Assert.Equal(200769, data.Elements.Count);

        // Стержни (2 узла)
        int bars = data.Elements.Count(e => e.NodeIds.Length == 2);
        Assert.Equal(783, bars);

        // Оболочки (3-4 узла)
        int shells = data.Elements.Count(e => e.NodeIds.Length == 3 || e.NodeIds.Length == 4);
        Assert.Equal(200769 - 783, shells);

        // Все ID узлов валидны (1-based)
        int nodeCount = data.Nodes.Count;
        Assert.All(data.Elements, e =>
        {
            Assert.All(e.NodeIds, nid => Assert.InRange(nid, 1, nodeCount));
        });
    }
}
