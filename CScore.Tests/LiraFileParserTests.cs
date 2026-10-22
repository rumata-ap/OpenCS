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

    // ------------------------------------------------------------------ Структурное чтение (синтетика)

    /// <summary>Синтетический .lir: заголовок длиной 36, служебный блок, узлы по 28 байт, элементы по 38 байт.</summary>
    static byte[] BuildLir(
        (double X, double Y, double Z, uint Flag)[] nodes,
        (uint[] Slots, ushort Def, uint Flag)[] elems)
    {
        const string text = "LIRA-SAPR 2013 software ver.13.0.0.a";   // 36 символов
        var ms = new MemoryStream();
        var w  = new BinaryWriter(ms);
        w.Write((byte)text.Length);
        w.Write(System.Text.Encoding.ASCII.GetBytes(text));
        var meta = new byte[52];
        BitConverter.GetBytes((uint)nodes.Length).CopyTo(meta, 12);
        BitConverter.GetBytes((uint)elems.Length).CopyTo(meta, 20);
        w.Write(meta);
        foreach (var n in nodes) { w.Write(n.X); w.Write(n.Y); w.Write(n.Z); w.Write(n.Flag); }
        foreach (var e in elems)
        {
            var rec = new byte[38];
            for (int i = 0; i < e.Slots.Length; i++) BitConverter.GetBytes(e.Slots[i]).CopyTo(rec, 4 * i);
            BitConverter.GetBytes(e.Def).CopyTo(rec, 32);
            BitConverter.GetBytes(e.Flag).CopyTo(rec, 34);
            w.Write(rec);
        }
        w.Write(new byte[64]);   // хвост файла (не расшифрован)
        return ms.ToArray();
    }

    static LiraSchemaData ParseBytes(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-lir-{Guid.NewGuid():N}.lir");
        File.WriteAllBytes(path, bytes);
        try { return LiraFileParser.Parse(path); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_Structured_ReadsNodesAndElements_ZeroIndexIsNodeOne()
    {
        var data = ParseBytes(BuildLir(
            [(0, 0, 0, 1), (1, 0, 0, 1), (1, 1, 0, 1), (0, 1, 0, 1)],
            [([0, 1, 2, 3], 2, 1), ([0, 1], 0, 1)]));

        Assert.Equal(4, data.Nodes.Count);
        Assert.Equal([1, 2, 3, 4], data.Elements[0].NodeIds);   // первый индекс 0 — узел №1, не «отсутствует»
        Assert.Equal(44, data.Elements[0].FeType);
        Assert.Equal([1, 2], data.Elements[1].NodeIds);
        Assert.Equal(10, data.Elements[1].FeType);
    }

    [Fact]
    public void Parse_Structured_DeletedRecordsAreSkipped_NumberingKept()
    {
        var data = ParseBytes(BuildLir(
            [(0, 0, 0, 1), (5, 5, 5, 0), (1, 0, 0, 1), (2, 0, 0, 1)],
            [([0, 2], 0, 0), ([2, 3], 0, 1)]));

        Assert.Equal([1, 3, 4], data.Nodes.Select(n => n.Id).ToArray());   // узел 2 удалён, остальные не сдвинуты
        var e = Assert.Single(data.Elements);
        Assert.Equal(2, e.Id);                                              // КЭ 1 удалён, номер КЭ 2 сохранён
        Assert.Equal([3, 4], e.NodeIds);
    }

    [Fact]
    public void Parse_Structured_SingleNodeLinksAreSkipped()
    {
        var data = ParseBytes(BuildLir(
            [(0, 0, 0, 1), (1, 0, 0, 1), (2, 0, 0, 1)],
            [([2], 3, 1), ([0, 1, 2], 1, 1)]));

        var e = Assert.Single(data.Elements);
        Assert.Equal(2, e.Id);
        Assert.Equal(42, e.FeType);
    }

    [Fact]
    public void Parse_UnknownHeader_FallsBackToScanner_DoesNotThrow()
    {
        var bytes = new byte[2048];
        bytes[0] = 40;                     // длина заголовка не совпадает с сигнатурой
        var data = ParseBytes(bytes);
        Assert.Empty(data.Nodes);
        Assert.Empty(data.Elements);
    }

    [Fact]
    public void Parse_Structured_ElementReferencingDeletedNode_FallsBackNotCrashes()
    {
        // противоречивая структура: живой КЭ ссылается на удалённый узел — структурный путь отказывается
        var bytes = BuildLir(
            [(0, 0, 0, 1), (1, 0, 0, 0)],
            [([0, 1], 0, 1)]);
        var data = ParseBytes(bytes);
        Assert.NotNull(data);
    }

    [Fact]
    public void Parse_Structured_StaleSlotsBeyondDefinitionNodeCount_AreIgnored()
    {
        // стержень (определение 0): в слотах 3 и 4 остался мусор — узлов всё равно два
        var data = ParseBytes(BuildLir(
            [(0, 0, 0, 1), (1, 0, 0, 1), (2, 0, 0, 1), (3, 0, 0, 1)],
            [([0, 1, 2, 3], 0, 1)]));

        var e = Assert.Single(data.Elements);
        Assert.Equal([1, 2], e.NodeIds);
        Assert.Equal(10, e.FeType);
    }

    [Fact]
    public void Parse_Structured_QuadWithLastNodeOne_KeepsFourNodes()
    {
        // четырёхугольник (определение 2), последний узел — №1 (индекс 0): нулевой слот не означает «нет узла»
        var data = ParseBytes(BuildLir(
            [(0, 0, 0, 1), (1, 0, 0, 1), (1, 1, 0, 1), (0, 1, 0, 1)],
            [([1, 2, 3, 0], 2, 1)]));

        var e = Assert.Single(data.Elements);
        Assert.Equal([2, 3, 4, 1], e.NodeIds);
    }

    [Fact]
    public void Parse_Structured_SingleNodeLinkWithStaleSlots_IsNotQuad()
    {
        var data = ParseBytes(BuildLir(
            [(0, 0, 0, 1), (1, 0, 0, 1), (2, 0, 0, 1), (3, 0, 0, 1)],
            [([2, 1, 3, 0], 3, 1)]));

        Assert.Empty(data.Elements);
    }

    [Fact]
    public void Parse_Structured_VolumetricDefinitions_AreSkipped()
    {
        var data = ParseBytes(BuildLir(
            [(0, 0, 0, 1), (1, 0, 0, 1), (0, 1, 0, 1), (0, 0, 1, 1)],
            [([0, 1, 2, 3], 8, 1)]));   // определение 8 — тетраэдр: число узлов не 2–4 по определению

        Assert.Empty(data.Elements);
    }
}
