using CScore.Import;
using Xunit;

namespace CScore.Tests.Import;

/// <summary>
/// Разбор текстового файла ЛИРЫ: «Тест_стена» (ЛИРА-САПФИР 2025) и синтетика по фрагментам «Мирной», ЗАГС, dynam,
/// dorfman_lin и примерам описания «ЛИРА-Windows», раздел 3.
/// </summary>
public class LiraTextReaderTests
{
    static LiraTextSchema Parse(string text) => LiraTextReader.Read(new StringReader(text));

    /// <summary>Массив для сравнения в кортеже (массивы в кортежах сравниваются по ссылке).</summary>
    static string J(int[] v) => string.Join(", ", v);

    [Fact]
    public void TestWall_HeaderElementsSupportsLoads()
    {
        var s = LiraTextReader.Read(Path.Combine(AppContext.BaseDirectory, "Import", "Fixtures", "lira-text-wall.txt"));

        Assert.Empty(s.Warnings);
        Assert.Equal("ТЕСТ_СТЕНА", s.Title);
        Assert.Equal(5, s.SchemaKind);
        Assert.Equal(LiraTextUnits.Default, s.Units);
        Assert.Equal(new Dictionary<int, string> { [1] = "ПОСТОЯННОЕ", [2] = "ВРЕМЕННЫЕ" }, s.LoadCaseNames);
        Assert.Equal("9.81054", s.HeaderRows[57]);

        Assert.Equal(384, s.Elements.Count);
        Assert.All(s.Elements, e => Assert.Equal((44, 1, 4), (e.Type, e.Stiffness, e.Nodes.Length)));
        Assert.Equal([5, 81, 1, 80], s.Elements[0].Nodes);
        Assert.Equal([425, 42, 43, 3], s.Elements[^1].Nodes);
        Assert.Equal(384, s.Elements[^1].Number);

        var stiff = Assert.Single(s.Stiffnesses);
        Assert.Equal(1, stiff.Number);
        Assert.Equal(["GEI 3.05915e+06 0.2 0.2"], stiff.Lines);
        Assert.Null(stiff.NumericValues);

        Assert.Equal(425, s.Nodes.Count);
        Assert.Equal(new LiraTextPoint(6, 0, 4), s.Nodes[2]);
        Assert.Equal(new LiraTextPoint(5.75, 0, 3.75), s.Nodes[^1]);

        // Документ 5 — только связи (строк из одного номера узла нет).
        Assert.Equal(80, s.Supports.Count);
        Assert.Empty(s.Couplings);
        Assert.Equal((1, "1, 2, 3"), (s.Supports[0].Node, J(s.Supports[0].Dofs)));
        Assert.Equal((3, "1, 2"), (s.Supports[2].Node, J(s.Supports[2].Dofs)));
        Assert.Equal((80, "2"), (s.Supports[^1].Node, J(s.Supports[^1].Dofs)));

        Assert.Equal(818, s.Loads.Count);
        Assert.Equal(new Dictionary<int, int> { [0] = 50, [16] = 384, [17] = 384 },
            s.Loads.GroupBy(l => l.Kind).ToDictionary(g => g.Key, g => g.Count()));
        Assert.Equal(new LiraTextLoad(3, 0, 3, 1, 1), s.Loads[0]);
        Assert.Equal(new LiraTextLoad(384, 16, 2, 4, 2), s.Loads[^1]);
        Assert.All(s.Loads, l => Assert.True(s.LoadValues.ContainsKey(l.ValueRow)));

        Assert.Equal(49, s.LoadValues.Count);
        Assert.Equal([20.3943, 0], s.LoadValues[1]);
        Assert.Equal([7.13801, 6.69189, 7.13801, 6.69189], s.LoadValues[3]);
        Assert.Equal([2.03943, 0, 0, 0], s.LoadValues[4]);

        Assert.Empty(s.Hinges);
        Assert.Empty(s.RigidBodies);
        Assert.Equal(53, s.PlateAxisAngles.Count);
        Assert.Equal([new LiraTextSkippedDocument(8, false, 2)], s.SkippedDocuments);
    }

    /// <summary>Фрагменты «Мирной» (шарниры, КЭ 57, объединения, АЖТ), ЗАГС (шарнир с полем 30), dynam (продолжение
    /// док. 7), dorfman_lin (АЖТ без кода) и описание сечения в фигурных скобках.</summary>
    const string Fragments = """
        ( 0/ 1; mirnaya_edit001_pack/ 2; 5/
        4;
        3:3 - 59 ;/
        33;M 1 CM 100 T 1 C 1 /
        39;
        1: СОБСТВЕННЫЙ ВЕС ;
        6: ВЕТЕР 0 (ПО X) ;
         /
        57; 9.81054 /

        )
        ( 1/
        44 1 56 71 57 72 /10 2 1 2 /57 3 819 /
        31 4 34 35 40 41/
        0 0 24 25 48 49/
        )
        ( 2/
        72 2 4 0/72 2 5 0/
        5 1 6 30/
         )
        ( 3/
        1 GEI 3.06e+06 0.2 0.25 RO 2.5 /
         0 WLKE 1 WLKG 1 PLKE 0.6 PLKG 0.6 /
        3 459000 8262 1434.37 3005.99 0 0 /
         0 RO 0.375/
         0 S0 3.06e+06 25 60/
         )
        {13/
        [1]
        Section = DoubleT
        Shape = |30М|
        }
        ( 4/
        0.00000 0.00000 0.00000 /6.00000 0.00000 0.00000 /0 1.5 /
         )
        ( 5/

        1116 1 2 3 /
        4775 /4776 /4795 /

        4812 1 2 3 /
        9866 /
        1 1 2 3 6 /
        2 1 2 6 /
        3111 1 2 3 /
        3130 /3145 /15076 /
         )
        ( 6/
         3 16 3 1 1 / 4 16 3 1 / 1116 0 1 2 6 /
         )
        ( 7/
        1 0 2 1.66214 /
         0 25.13 0 0 7.5 /
        2 1.16 0 /
         )
        ( 9/
        207 -0.683243 /209 -0.770314 /
         )
        ( 25/
         4944 6380 6381 6382 6441 6442 6443 -1/
         416 362 363 364 415 417 468 469 470/
         )
        """;

    [Fact]
    public void Fragments_AllDocuments()
    {
        var s = Parse(Fragments);

        Assert.Empty(s.Warnings);
        Assert.Equal("mirnaya_edit001_pack", s.Title);
        // Скобки в имени загружения не закрывают документ 0.
        Assert.Equal(new Dictionary<int, string> { [1] = "СОБСТВЕННЫЙ ВЕС", [6] = "ВЕТЕР 0 (ПО X)" }, s.LoadCaseNames);
        Assert.Equal("3:3 - 59 ;", s.HeaderRows[4]);

        // КЭ 31 — продолжение «0 0 узлы…»: 8 узлов.
        Assert.Equal(4, s.Elements.Count);
        Assert.Equal((57, 3, "819"), (s.Elements[2].Type, s.Elements[2].Stiffness, J(s.Elements[2].Nodes)));
        Assert.Equal((4, 31, "34, 35, 40, 41, 24, 25, 48, 49"),
            (s.Elements[3].Number, s.Elements[3].Type, J(s.Elements[3].Nodes)));

        Assert.Equal(3, s.Hinges.Count);
        Assert.Equal((72, 2, "4", 0.0), (s.Hinges[0].Element, s.Hinges[0].End, J(s.Hinges[0].Dofs), s.Hinges[0].Value));
        Assert.Equal((5, 1, "6", 30.0), (s.Hinges[2].Element, s.Hinges[2].End, J(s.Hinges[2].Dofs), s.Hinges[2].Value));

        Assert.Equal(2, s.Stiffnesses.Count);
        Assert.Equal(["GEI 3.06e+06 0.2 0.25 RO 2.5", "WLKE 1 WLKG 1 PLKE 0.6 PLKG 0.6"], s.Stiffnesses[0].Lines);
        Assert.Equal(3, s.Stiffnesses[1].Number);
        Assert.Equal(["459000 8262 1434.37 3005.99 0 0", "RO 0.375", "S0 3.06e+06 25 60"], s.Stiffnesses[1].Lines);
        Assert.Equal([459000, 8262, 1434.37, 3005.99, 0, 0], s.Stiffnesses[1].NumericValues);

        // Неполная запись координат — недостающие нули.
        Assert.Equal([new(0, 0, 0), new(6, 0, 0), new(0, 1.5, 0)], s.Nodes);

        // Объединение — «узел DOF…» и следом строки только с номером узла (пустые строки не важны).
        Assert.Equal(3, s.Couplings.Count);
        Assert.Equal((1116, "1, 2, 3", "4775, 4776, 4795"),
            (s.Couplings[0].Head, J(s.Couplings[0].Dofs), J(s.Couplings[0].Nodes)));
        Assert.Equal((4812, "9866"), (s.Couplings[1].Head, J(s.Couplings[1].Nodes)));
        Assert.Equal((3111, "3130, 3145, 15076"), (s.Couplings[2].Head, J(s.Couplings[2].Nodes)));
        Assert.Equal(2, s.Supports.Count);
        Assert.Equal((1, "1, 2, 3, 6"), (s.Supports[0].Node, J(s.Supports[0].Dofs)));
        Assert.Equal((2, "1, 2, 6"), (s.Supports[1].Node, J(s.Supports[1].Dofs)));

        // Запись без загружения — как у предыдущей.
        Assert.Equal([new(3, 16, 3, 1, 1), new(4, 16, 3, 1, 1), new(1116, 0, 1, 2, 6)], s.Loads);

        Assert.Equal([0, 2, 1.66214, 25.13, 0, 0, 7.5], s.LoadValues[1]);
        Assert.Equal([1.16, 0], s.LoadValues[2]);

        Assert.Equal(2, s.RigidBodies.Count);
        Assert.Equal((4944, "6380, 6381, 6382, 6441, 6442, 6443", (int?)-1),
            (s.RigidBodies[0].Master, J(s.RigidBodies[0].Slaves), s.RigidBodies[0].Code));
        Assert.Equal((416, 8, (int?)null), (s.RigidBodies[1].Master, s.RigidBodies[1].Slaves.Length, s.RigidBodies[1].Code));

        Assert.Equal("Section = DoubleT  Shape = |30М|", s.SectionDescriptions[1]);
        Assert.Equal([(207, -0.683243), (209, -0.770314)], s.PlateAxisAngles.Select(kv => (kv.Key, kv.Value)));
        Assert.Empty(s.SkippedDocuments);
    }

    [Fact]
    public void Units_FactorsFromRow33()
    {
        var s = Parse("( 0/ 33;MM 1000 CM 100 KN 9.81 C 1 / )");

        Assert.Equal(new LiraTextUnits("MM", 1000, "CM", 100, "KN", 9.81, "C", 1), s.Units);
        Assert.Equal(0.001, s.Units.LengthUnitM, 12);
        Assert.Equal(1 / 9.81, s.Units.ForceUnitT, 12);
    }

    /// <summary>Повторитель R — примеры п. 3.15 описания «ЛИРА-Windows».</summary>
    [Fact]
    public void Repeater_DescriptionExamples()
    {
        var s = Parse("""
            (1/11  2  5  6  19  20/
            R  1  3/
            0  0  1  2  -1  -1/)
            """);
        Assert.Empty(s.Warnings);
        Assert.Equal(
            [[5, 6, 19, 20], [6, 8, 18, 19], [7, 10, 17, 18], [8, 12, 16, 17]],
            s.Elements.Select(e => e.Nodes));

        // Плита: 9 КЭ в ряду, затем 12 повторов ряда.
        s = Parse("(1/11 1 1 2 15 16 /R 1 8/0 0 14 14 14 14/R 9 12/0 0 1 1 1 1/)");
        Assert.Equal(117, s.Elements.Count);
        Assert.Equal([15, 16, 29, 30], s.Elements[1].Nodes);
        Assert.Equal([125, 126, 139, 140], s.Elements[^1].Nodes);

        // Координаты: «0» — узел в начале, недостающие графы при ненулевом шаге — нули.
        s = Parse("(4/0/R 1 13/1/R 14 9/0 0.8/)");
        Assert.Equal(140, s.Nodes.Count);
        Assert.Equal(new LiraTextPoint(13, 0, 0), s.Nodes[13]);
        Assert.Equal(new LiraTextPoint(0, 0.8, 0), s.Nodes[14]);
        Assert.Equal(13, s.Nodes[^1].X);
        Assert.Equal(7.2, s.Nodes[^1].Y, 12);

        s = Parse("(6/1 6 3 1 1/R 1 116/1/)");
        Assert.Equal(117, s.Loads.Count);
        Assert.Equal(new LiraTextLoad(117, 6, 3, 1, 1), s.Loads[^1]);

        // Объединение по X узлов 51…92.
        s = Parse("(5/51 1/52/R 1 40/1/)");
        var c = Assert.Single(s.Couplings);
        Assert.Equal((51, "1"), (c.Head, J(c.Dofs)));
        Assert.Equal(Enumerable.Range(52, 41), c.Nodes);

        // Двухстрочные КЭ: повторяются обе строки, графы продолжения тоже со своим шагом.
        s = Parse("(1/31  5  1  2  18  19/0 0  44  45  61  62/\nR  2  8/0  1  1  1  1  1/)");
        Assert.Equal(9, s.Elements.Count);
        Assert.Equal((31, 13, "9, 10, 26, 27, 52, 53, 69, 70"),
            (s.Elements[^1].Type, s.Elements[^1].Stiffness, J(s.Elements[^1].Nodes)));
    }

    [Fact]
    public void Repeater_FromLineN3_AndBadRecordsKeepNumbering()
    {
        var s = Parse("(4/0 0 0/5 0 0/9 9 9/R 2 1 1/0 1/)");
        Assert.Equal([new(0, 0, 0), new(5, 0, 0), new(9, 9, 9), new(0, 1, 0), new(5, 1, 0)], s.Nodes);

        // Испорченный КЭ всё равно занимает номер; ошибка — в замечаниях.
        s = Parse("(1/10 1 1 2/10 X 2 3/10 1 3 4/)");
        Assert.Equal([1, 2, 3], s.Elements.Select(e => e.Number));
        Assert.Equal([3, 4], s.Elements[2].Nodes);
        Assert.Contains(s.Warnings, w => w.Contains("КЭ 2"));
    }
}
