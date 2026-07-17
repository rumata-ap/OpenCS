using CScore.Import;
using Xunit;

namespace CScore.Tests;

public class LiraFileParserTests
{
    const string TestLirPath = @"C:\Users\palex\Documents\prj\obshezitie_belgorod\calc\hostel.lir";

    [Fact]
    public void Parse_HeaderParsed_OffsetAfterHeader()
    {
        if (!File.Exists(TestLirPath)) return; // пропуск если файл недоступен

        var bytes = File.ReadAllBytes(TestLirPath);
        int offset = 0;

        // Пропуск 3 строк
        for (int i = 0; i < 3; i++)
        {
            while (offset < bytes.Length && bytes[offset] != 0) offset++;
            offset++;
        }

        Assert.Equal(85, offset); // заголовок = 85 байт (3 null-terminated строки)
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
        // 3 короткие null-terminated строки: "AB\0CD\0EF\0"
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
    public void Parse_Nodes3D_HasExpectedCount()
    {
        if (!File.Exists(TestLirPath)) return;

        var data = LiraFileParser.Parse(TestLirPath);

        // Block 2 содержит 173 692 узла (валидных записей с int32=1)
        Assert.Equal(173692, data.Nodes.Count);
    }

    [Fact]
    public void Parse_Nodes3D_CoordinatesInRange()
    {
        if (!File.Exists(TestLirPath)) return;

        var data = LiraFileParser.Parse(TestLirPath);

        Assert.All(data.Nodes, n =>
        {
            Assert.InRange(n.X, -10.0, 65.0);
            Assert.InRange(n.Y, -35.0, 10.0);
            Assert.InRange(n.Z, -5.0, 35.0);
        });
    }

    [Fact]
    public void Parse_Elements_HasExpectedCount()
    {
        if (!File.Exists(TestLirPath)) return;

        var data = LiraFileParser.Parse(TestLirPath);

        // Всего 200 769 элементов (783 стержня + 10 944 треугольника + 174 219 четырёхугольника + 14 823 смешанных)
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
    public void Parse_Elements_ShellElementsHaveValidNodes()
    {
        if (!File.Exists(TestLirPath)) return;

        var data = LiraFileParser.Parse(TestLirPath);

        var shellElems = data.Elements.Where(e => e.NodeIds.Length >= 3).ToList();
        Assert.NotEmpty(shellElems);

        // Все ID узлов должны быть в диапазоне [0, nodeCount)
        int nodeCount = data.Nodes.Count;
        Assert.All(shellElems, e =>
        {
            Assert.All(e.NodeIds, nid => Assert.InRange(nid, 0, nodeCount - 1));
        });
    }
}
