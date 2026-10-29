using System.Buffers.Binary;
using System.Text;
using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>
/// Проверки чтения дополнительных блоков бинарного .lir (жёсткости, загружения, физические нагрузки)
/// на синтетических схемах и на контрольном файле, сверенном с текстовым экспортом ЛИРЫ.
/// </summary>
public class LiraBinaryExtrasReaderTests
{
    /// <summary>
    /// Путь к контрольному .lir (<c>vtr_stlb_…_2026-10-09.lir</c>, 114 309 узлов / 161 823 КЭ).
    /// Задаётся переменной окружения <c>OPENCES_LIR_SAMPLE</c>; без неё тест пропускается.
    /// </summary>
    const string SampleVariable = "OPENCES_LIR_SAMPLE";

    static LiraBinaryExtrasReaderTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    // ------------------------------------------------------------------ Синтетическая схема

    /// <summary>Строитель синтетического .lir: геометрия и три дополнительных блока.</summary>
    sealed class LirBuilder
    {
        const string HeaderText = "LIRA-SAPR 2013 software ver.13.0.0.a";

        readonly List<(double X, double Y, double Z, uint Flag)> _nodes = [];
        readonly List<(int[] Slots, ushort Definition, uint Flag)> _elements = [];
        readonly List<(int Node, int Code, int Case, float Value)> _nodalLoads = [];
        readonly List<(int Element, int Tag, int Code, int Case, int Group, float[] Payload)> _elementLoads = [];
        readonly List<int> _stiffness = [];
        readonly List<string> _loadCases = [];
        readonly List<(int Element, string Label)> _labels = [];
        bool _writeStiffness = true;
        bool _writeNodeObjects = true;
        bool _breakConnectivity;
        bool _wideEmptyLabel;

        public int AddNode(double x, double y, double z)
        {
            _nodes.Add((x, y, z, 1));
            return _nodes.Count - 1;
        }

        public int AddElement(int[] slots, ushort definition, int stiffness)
        {
            _elements.Add((slots, definition, 1));
            _stiffness.Add(stiffness);
            return _elements.Count - 1;
        }

        /// <summary>Удалённая запись КЭ: остаётся в файле (номер и слоты сохраняются), в схему не переносится.</summary>
        public int AddDeletedElement(int[] slots, ushort definition)
        {
            _elements.Add((slots, definition, 0));
            _stiffness.Add(0);
            return _elements.Count - 1;
        }

        public void AddNodalLoad(int node, int code, int loadCase, float value) =>
            _nodalLoads.Add((node, code, loadCase, value));

        public void AddElementLoad(int element, int tag, int code, int loadCase, int group, float[] payload) =>
            _elementLoads.Add((element, tag, code, loadCase, group, payload));

        public void SetLabel(int element, string label) => _labels.Add((element, label));

        public void AddLoadCase(string name) => _loadCases.Add(name);

        public void WithoutStiffness() => _writeStiffness = false;

        public void WithoutNodeObjects() => _writeNodeObjects = false;

        /// <summary>Портит хвостовую связность объектов элементов (сверка должна отказать).</summary>
        public void BreakConnectivity() => _breakConnectivity = true;

        /// <summary>Пустая метка объекта элемента в «широком» варианте (FF FE FF 00), как в части файлов ЛИРЫ.</summary>
        public void UseWideEmptyLabel() => _wideEmptyLabel = true;

        public byte[] Build()
        {
            var ms = new MemoryStream();
            var w  = new BinaryWriter(ms);

            w.Write((byte)HeaderText.Length);
            w.Write(Encoding.ASCII.GetBytes(HeaderText));

            var meta = new byte[52];
            BinaryPrimitives.WriteUInt32LittleEndian(meta.AsSpan(12), (uint)_nodes.Count);
            BinaryPrimitives.WriteUInt32LittleEndian(meta.AsSpan(20), (uint)_elements.Count);
            w.Write(meta);

            foreach (var n in _nodes) { w.Write(n.X); w.Write(n.Y); w.Write(n.Z); w.Write(n.Flag); }

            foreach (var e in _elements)
            {
                var record = new byte[38];
                for (int i = 0; i < e.Slots.Length; i++)
                    BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4 * i), e.Slots[i]);
                BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(32), e.Definition);
                BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(34), e.Flag);
                w.Write(record);
            }

            // Имя модели — строка с префиксом длины, как в файле ЛИРЫ.
            WriteString(w, "extras-test_2026-10-29");

            if (_writeNodeObjects) WriteNodeObjects(w);
            WriteElementObjects(w);
            if (_writeStiffness) WriteStiffness(w);
            if (_loadCases.Count > 0)
            {
                w.Write(new byte[8]);
                foreach (var name in _loadCases) WriteString(w, name);
            }

            w.Write(new byte[16]);
            w.Flush();
            return ms.ToArray();
        }

        void WriteNodeObjects(BinaryWriter w)
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                var loads = _nodalLoads.Where(l => l.Node == i).ToList();
                w.Write((uint)i);
                w.Write(1u);
                w.Write(_nodes[i].X);
                w.Write(_nodes[i].Y);
                w.Write(_nodes[i].Z);
                w.Write((ushort)0);
                w.Write((ushort)0);
                w.Write((ushort)0);
                w.Write((ushort)loads.Count);
                foreach (var l in loads)
                {
                    w.Write((ushort)l.Code);
                    w.Write((ushort)l.Case);
                    w.Write(l.Value);
                    w.Write((ushort)0);
                }
                w.Write(new byte[8]);
            }
        }

        void WriteElementObjects(BinaryWriter w)
        {
            for (int i = 0; i < _elements.Count; i++)
            {
                var (slots, definition, _) = _elements[i];
                int type = definition switch { 0 => 10, 1 => 42, 2 => 44, _ => 56 };
                string label = string.Empty;
                foreach (var pair in _labels) if (pair.Element == i) label = pair.Label;
                var loads = _elementLoads.Where(l => l.Element == i).ToList();

                w.Write((ushort)0);            // класс объекта
                w.Write((uint)i);              // последовательный номер с нуля
                w.Write(1u);                   // флаги (младший бит — живая запись)
                w.Write((ushort)type);         // тип КЭ
                w.Write((ushort)slots.Length); // число вершин
                w.Write(0u);                   // неинтерпретированное поле
                if (_wideEmptyLabel && label.Length == 0) w.Write([0xFF, 0xFE, 0xFF, 0x00]);
                else WriteString(w, label);
                w.Write(1.0f);                 // общий множитель
                w.Write((ushort)loads.Count);

                foreach (var l in loads)
                {
                    w.Write((ushort)l.Tag);
                    w.Write((ushort)l.Code);
                    w.Write((ushort)l.Case);
                    w.Write((ushort)l.Group);
                    foreach (var value in l.Payload) w.Write(value);
                }

                w.Write(new byte[16]);         // неразобранный хвост объекта
                foreach (var slot in slots)
                    w.Write((uint)(_breakConnectivity && i == 0 ? slot + 1000 : slot));
            }
        }

        void WriteStiffness(BinaryWriter w)
        {
            w.Write(new byte[8]);
            foreach (var s in _stiffness) w.Write((ushort)s);
            foreach (var s in _stiffness) w.Write((ushort)s);   // вторая, побитовая копия массива
        }

        static void WriteString(BinaryWriter w, string value)
        {
            var bytes = Encoding.GetEncoding(1251).GetBytes(value);
            w.Write((byte)bytes.Length);
            w.Write(bytes);
        }
    }

    static LiraSchemaData Parse(byte[] bytes, LiraBinaryExtrasMode mode = LiraBinaryExtrasMode.StiffnessAndLoads)
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-lir-extras-{Guid.NewGuid():N}.lir");
        File.WriteAllBytes(path, bytes);
        try { return LiraFileParser.Parse(path, mode); }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Схема 3×3 узла: четыре четырёхугольника и четыре стержня.
    /// Жёсткости задаются так, чтобы массив не был периодичным.
    /// </summary>
    static LirBuilder GridSchema()
    {
        var builder = new LirBuilder();
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                builder.AddNode(x, y, 0);

        builder.AddElement([0, 1, 4, 3], 2, 0);
        builder.AddElement([1, 2, 5, 4], 2, 1);
        builder.AddElement([3, 4, 7, 6], 2, 2);
        builder.AddElement([4, 5, 8, 7], 2, 0);
        builder.AddElement([0, 3], 0, 1);
        builder.AddElement([2, 5], 0, 2);
        builder.AddElement([6, 7], 0, 0);
        builder.AddElement([7, 8], 0, 1);
        return builder;
    }

    // ------------------------------------------------------------------ Жёсткости

    [Fact]
    public void Extra_StiffnessAssignment_IsAppliedToOneBasedNumbers()
    {
        var data = Parse(GridSchema().Build());

        Assert.Equal([1, 2, 3, 1, 2, 3, 1, 2], data.Elements.Select(e => e.StiffnessId).ToArray());
        Assert.Contains(data.BinaryNotes, n => n.Contains("жёсткости прочитаны"));
    }

    [Fact]
    public void Extra_NoStiffnessBlock_KeepsGeometryAndReportsNote()
    {
        var builder = GridSchema();
        builder.WithoutStiffness();
        var data = Parse(builder.Build());

        Assert.Equal(8, data.Elements.Count);
        Assert.All(data.Elements, e => Assert.Equal(0, e.StiffnessId));
        Assert.Contains(data.BinaryNotes, n => n.Contains("массив номеров жёсткостей не найден"));
    }

    // ------------------------------------------------------------------ Узловые нагрузки

    [Fact]
    public void Extra_NodalLoads_AreReadWithDirectionCodeMinus100()
    {
        var builder = GridSchema();
        builder.AddNodalLoad(2, 103, 1, 105.6f);
        builder.AddNodalLoad(2, 105, 2, -22.2f);
        var data = Parse(builder.Build());

        var loads = data.BinaryLoads.Where(l => l.TargetKind == LiraLoadTargetKind.Node).ToList();
        Assert.Equal(2, loads.Count);
        Assert.All(loads, l => Assert.Equal(3, l.TargetId));
        Assert.Equal([3, 5], loads.Select(l => l.DirectionCode).ToArray());
        Assert.Equal([1, 2], loads.Select(l => l.LoadCaseId).ToArray());
        Assert.Equal(105.6, loads[0].P0, 4);
        Assert.Equal(-22.2, loads[1].P0, 4);
        Assert.All(loads, l => Assert.Equal(0, l.LoadType));
        Assert.All(loads, l => Assert.Equal(0, l.SerializedTag));   // тег узловой записи — 0
    }

    [Fact]
    public void Extra_NoNodeObjects_ReportsNote()
    {
        var builder = GridSchema();
        builder.WithoutNodeObjects();
        var data = Parse(builder.Build());

        Assert.Empty(data.BinaryLoads);
        Assert.Contains(data.BinaryNotes, n => n.Contains("массив объектов узлов не найден"));
    }

    // ------------------------------------------------------------------ Элементные нагрузки

    [Fact]
    public void Extra_ElementLoads_AllSupportedTagsAreMappedToTextOrder()
    {
        var builder = GridSchema();
        builder.AddElementLoad(4, 2, 163, 1, 15001, [0.99f, 0f, 1.1f]);              // стержень, тип 16
        builder.AddElementLoad(1, 12, 163, 2, 0, [1.925f, 1.1f]);                    // пластина, тип 16
        builder.AddElementLoad(1, 10, 153, 3, 0, [0.5f, 0.25f, 0.75f]);              // пластина, тип 15
        builder.AddElementLoad(1, 14, 73, 4, 261, [5.9f, 5.9f, 5.67f, 5.67f]);       // пластина, тип 7
        builder.AddElementLoad(1, 28, 193, 5, 0, [2.44f, 2.44f, 1f, 3f]);            // пластина, тип 19
        var data = Parse(builder.Build());

        var loads = data.BinaryLoads.Where(l => l.TargetKind == LiraLoadTargetKind.Element).ToList();
        Assert.Equal(5, loads.Count);

        // записи идут в порядке файла — по элементам: сначала КЭ №2 (пластина), затем КЭ №5 (стержень)
        var plate = loads.Where(l => l.TargetId == 2).ToList();
        var bar   = loads.Single(l => l.TargetId == 5);
        Assert.Equal([16, 15, 7, 19], plate.Select(l => l.LoadType).ToArray());
        Assert.Equal(16, bar.LoadType);
        Assert.Equal(3, bar.DirectionCode);
        Assert.All(loads, l => Assert.Equal(3, l.DirectionCode));

        Assert.Equal(1.925, plate[0].P0, 5);
        Assert.Equal(0.5,  plate[1].P0, 6);
        Assert.Equal(0.25, plate[1].P1, 6);
        Assert.Equal(0.75, plate[1].P2, 6);
        Assert.Equal(0.0,  plate[1].P3, 6);
        Assert.Equal(5.9,  plate[2].P0, 5);
        Assert.Equal(5.67, plate[2].P2, 5);

        // тип 19: в файле сначала интенсивности, затем вершины — в порядке TXT наоборот
        Assert.Equal(1.0,  plate[3].P0, 6);
        Assert.Equal(3.0,  plate[3].P1, 6);
        Assert.Equal(2.44, plate[3].P2, 5);
        Assert.Equal(2.44, plate[3].P3, 5);
        Assert.Equal([2.44f, 2.44f, 1f, 3f], plate[3].NativeParameters.Select(v => (float)v).ToArray());

        Assert.Equal(0.99, bar.P0, 5);
        Assert.Equal(15001, bar.SourceGroupRaw);
        Assert.Equal(2, plate[0].LoadCaseId);
        Assert.All(loads, l => Assert.InRange(l.BinaryOffset, 1, long.MaxValue));
    }

    [Fact]
    public void Extra_ElementLoads_BrokenConnectivity_AreNotPublished()
    {
        var builder = GridSchema();
        builder.AddElementLoad(1, 12, 163, 1, 0, [1.5f, 1.0f]);
        builder.BreakConnectivity();
        var data = Parse(builder.Build());

        Assert.Empty(data.BinaryLoads);
        Assert.Equal(8, data.Elements.Count);                       // геометрия импортирована
        Assert.Contains(data.BinaryNotes, n => n.Contains("не прошла сверку связности"));
    }

    [Fact]
    public void Extra_UnknownLoadTag_AbortsElementLoadsWithNote()
    {
        var builder = GridSchema();
        builder.AddElementLoad(1, 99, 163, 1, 0, [1f, 2f]);
        var data = Parse(builder.Build());

        Assert.Empty(data.BinaryLoads);
        Assert.Contains(data.BinaryNotes, n => n.Contains("не прошла сверку связности"));
    }

    // ------------------------------------------------------------------ Загружения

    [Fact]
    public void Extra_ElementLoads_WideEmptyLabelAreRead()
    {
        var builder = GridSchema();
        builder.UseWideEmptyLabel();
        builder.AddElementLoad(1, 12, 163, 1, 0, [1.5f, 1.0f]);
        var data = Parse(builder.Build());

        var load = Assert.Single(data.BinaryLoads);
        Assert.Equal(LiraLoadTargetKind.Element, load.TargetKind);
        Assert.Equal(2, load.TargetId);
        Assert.Equal(1.5, load.P0, 6);
    }

    [Fact]
    public void Extra_ElementLoads_DeletedRecordsKeepNumbering()
    {
        var builder = GridSchema();
        builder.AddDeletedElement([0, 1], 0);          // запись №9: удалена, но объект в файле есть
        builder.AddElement([1, 4, 5, 8], 2, 2);        // запись №10
        builder.AddElementLoad(9, 12, 163, 1, 0, [2.5f, 1.0f]);
        var data = Parse(builder.Build());

        Assert.Equal(9, data.Elements.Count);          // десятая запись импортирована под своим номером
        var load = Assert.Single(data.BinaryLoads);
        Assert.Equal(10, load.TargetId);
        Assert.Equal(2.5, load.P0, 6);
    }

    [Fact]
    public void Extra_ElementLoads_TagsFromTextExportAreMapped()
    {
        var builder = GridSchema();
        builder.AddElementLoad(1, 0,  153, 1, 84,  [0.272769f, 3.8f]);                    // тип 15 в точке: q, u
        builder.AddElementLoad(1, 4,  172, 2, 1,   [-0.077f, 0.0f, -0.0655f, -101f, 1f]); // тип 17
        builder.AddElementLoad(4, 7,  85,  3, 0,   [45.5f]);                             // тип 8
        builder.AddElementLoad(4, 2,  61,  4, 0,   [0.075f, 0f, 1f]);                    // тип 6 через тег 2
        var data = Parse(builder.Build());

        var plate = data.BinaryLoads.Where(l => l.TargetId == 2).ToList();
        Assert.Equal([15, 17], plate.Select(l => l.LoadType).ToArray());
        Assert.Equal(0.272769, plate[0].P0, 5);
        Assert.Equal(3.8, plate[0].P1, 5);
        Assert.Equal(0.0, plate[0].P2, 6);      // v в записи не хранится
        Assert.Equal(-0.077, plate[1].P0, 5);
        Assert.Equal(-0.0655, plate[1].P2, 5);
        Assert.Equal(0.0, plate[1].P3, 6);      // четвёртый параметр TXT в файле не хранится

        var bar = data.BinaryLoads.Where(l => l.TargetId == 5).ToList();
        Assert.Equal([8, 6], bar.Select(l => l.LoadType).ToArray());
        Assert.Equal(45.5, bar[0].P0, 5);
        Assert.Equal(0.075, bar[1].P0, 6);
        Assert.Equal(0.0, bar[1].P1, 6);        // множитель не попадает в параметры
    }

    [Fact]
    public void Extra_LoadCaseNames_AreReadInFileOrder()
    {
        var builder = GridSchema();
        builder.AddLoadCase("Собственный вес");
        builder.AddLoadCase("Полезная нагрузка");
        builder.AddLoadCase("Кратковременная");
        builder.AddNodalLoad(0, 103, 3, 1f);
        var data = Parse(builder.Build());

        Assert.Equal(3, data.LoadCases.Count);
        Assert.Equal(["Собственный вес", "Полезная нагрузка", "Кратковременная"],
            data.LoadCases.Select(c => c.Name).ToArray());
        Assert.Equal([1, 2, 3], data.LoadCases.Select(c => c.Id).ToArray());
    }

    // ------------------------------------------------------------------ Режимы

    [Fact]
    public void Extra_ModeNone_ReadsGeometryOnly()
    {
        var builder = GridSchema();
        builder.AddNodalLoad(0, 103, 1, 1f);
        var data = Parse(builder.Build(), LiraBinaryExtrasMode.None);

        Assert.Equal(8, data.Elements.Count);
        Assert.All(data.Elements, e => Assert.Equal(0, e.StiffnessId));
        Assert.Empty(data.BinaryLoads);
        Assert.Empty(data.BinaryNotes);
    }

    [Fact]
    public void Extra_ModeStiffness_ReadsStiffnessWithoutLoads()
    {
        var builder = GridSchema();
        builder.AddNodalLoad(0, 103, 1, 1f);
        var data = Parse(builder.Build(), LiraBinaryExtrasMode.Stiffness);

        Assert.Equal([1, 2, 3, 1, 2, 3, 1, 2], data.Elements.Select(e => e.StiffnessId).ToArray());
        Assert.Empty(data.BinaryLoads);
    }

    // ------------------------------------------------------------------ Контрольный файл

    /// <summary>
    /// Сверка с контрольной моделью: 114 309 узлов / 161 823 КЭ / 1 007 757 записей нагрузок,
    /// эталон — текстовый экспорт ЛИРЫ (все цели, типы, направления и загружения совпали).
    /// </summary>
    [Fact]
    public void Extra_ControlFile_MatchesTextExportReferenceCounts()
    {
        string? path = Environment.GetEnvironmentVariable(SampleVariable);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        var data = LiraFileParser.Parse(path, LiraBinaryExtrasMode.StiffnessAndLoads);

        Assert.Equal(114309, data.Nodes.Count);
        Assert.Equal(125512, data.Elements.Count);       // без одноузловых связей (36 311)

        // жёсткости: 13 определений → номера 1..13, назначены всем импортируемым КЭ
        Assert.Equal(13, data.Elements.Max(e => e.StiffnessId));
        Assert.All(data.Elements, e => Assert.InRange(e.StiffnessId, 1, 13));

        Assert.Equal(18, data.LoadCases.Count);

        Assert.Equal(1007757, data.BinaryLoads.Count);
        Assert.Equal(6,      data.BinaryLoads.Count(l => l.SerializedTag == 0));
        Assert.Equal(202,    data.BinaryLoads.Count(l => l.SerializedTag == 2));
        Assert.Equal(838284, data.BinaryLoads.Count(l => l.SerializedTag == 10));
        Assert.Equal(166383, data.BinaryLoads.Count(l => l.SerializedTag == 12));
        Assert.Equal(2590,   data.BinaryLoads.Count(l => l.SerializedTag == 14));
        Assert.Equal(292,    data.BinaryLoads.Count(l => l.SerializedTag == 28));

        Assert.Equal(41073,  data.BinaryLoads.Count(l => l.LoadType == 6));
        Assert.Equal(2590,   data.BinaryLoads.Count(l => l.LoadType == 7));
        Assert.Equal(838284, data.BinaryLoads.Count(l => l.LoadType == 15));
        Assert.Equal(125512, data.BinaryLoads.Count(l => l.LoadType == 16));
        Assert.Equal(292,    data.BinaryLoads.Count(l => l.LoadType == 19));

        // узловые нагрузки узла 29698: загружения 17 и 18, величины из текстового экспорта
        var nodal = data.BinaryLoads
            .Where(l => l.TargetKind == LiraLoadTargetKind.Node && l.TargetId == 29698)
            .ToList();
        Assert.Equal(6, nodal.Count);
        Assert.Equal(105.6, nodal.Single(l => l.LoadCaseId == 18 && l.DirectionCode == 3).P0, 4);
        Assert.Equal(-22.2, nodal.Single(l => l.LoadCaseId == 18 && l.DirectionCode == 2).P0, 4);
        Assert.Equal(720.0, nodal.Single(l => l.LoadCaseId == 17 && l.DirectionCode == 5).P0, 4);
    }
}
