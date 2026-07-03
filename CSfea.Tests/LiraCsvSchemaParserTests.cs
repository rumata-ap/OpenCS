using CScore.Import;

namespace CSfea.Tests;

/// <summary>Тесты парсера и конвертера CSV-схем ЛираСАПР.</summary>
static class LiraCsvSchemaParserTests
{
    const string Downloads = @"C:\Users\palex\Downloads";
    static string Nodes      => Path.Combine(Downloads, "лира_узлы_пример.csv");
    static string Elements   => Path.Combine(Downloads, "лира_элементы_пример.csv");
    static string BarStiff   => Path.Combine(Downloads, "лира_жесткости_стержни.csv");
    static string PlateStiff => Path.Combine(Downloads, "лира_жесткости_пластины.csv");

    public static void RunAll()
    {
        TestHarness.Section("LiraCsvSchemaParser");

        if (!File.Exists(Nodes) || !File.Exists(Elements))
        {
            TestHarness.Check("LiraCsv — пример файлы присутствуют", false,
                "файлы не найдены в Downloads, тест пропущен");
            return;
        }

        TestNodes();
        TestElements();
        TestBarStiffnesses();
        TestPlateStiffnesses();
        TestFullParse();
        TestConverter();
    }

    static void TestNodes()
    {
        var data = new LiraSchemaData();
        // парсим только узлы — остальные файлы не нужны
        var result = LiraCsvSchemaParser.Parse(Nodes, Elements);

        TestHarness.Check("Nodes: загружено > 5000 узлов", result.Nodes.Count > 5000,
            $"count={result.Nodes.Count}");

        TestHarness.Check("Nodes: узел с Id > 0 присутствует",
            result.Nodes.Any(n => n.Id > 0));

        // первый узел должен иметь Id > 0 и конечные координаты
        var first = result.Nodes[0];
        TestHarness.Check("Nodes: первый Id положительный", first.Id > 0,
            $"Id={first.Id}");
        TestHarness.Check("Nodes: первый DofMask >= 0", first.DofMask >= 0,
            $"DofMask={first.DofMask}");

        // закреплённые узлы: маска может быть 0 для всех, если опоры заданы отдельно
        int fixedCount = result.Nodes.Count(n => n.DofMask != 0);
        Console.WriteLine($"    Закреплённых узлов (DofMask≠0): {fixedCount}");
    }

    static void TestElements()
    {
        var result = LiraCsvSchemaParser.Parse(Nodes, Elements);

        TestHarness.Check("Elements: загружено > 0 элементов", result.Elements.Count > 0,
            $"count={result.Elements.Count}");

        // первый элемент — стержень КЭ-10
        var first = result.Elements[0];
        TestHarness.Check("Elements[0]: Id = 101", first.Id == 101,
            $"Id={first.Id}");
        TestHarness.Check("Elements[0]: FeType = 10", first.FeType == 10,
            $"FeType={first.FeType}");
        TestHarness.Check("Elements[0]: SectionCount = 3", first.SectionCount == 3,
            $"SectionCount={first.SectionCount}");
        TestHarness.Check("Elements[0]: StiffnessId = 4", first.StiffnessId == 4,
            $"StiffnessId={first.StiffnessId}");
        TestHarness.Check("Elements[0]: 2 узла", first.NodeIds.Length == 2,
            $"NodeIds={string.Join(",", first.NodeIds)}");
        TestHarness.Check("Elements[0]: NodeIds[0] = 36526", first.NodeIds[0] == 36526,
            $"NodeIds[0]={first.NodeIds[0]}");
        TestHarness.Check("Elements[0]: NodeIds[1] = 36527", first.NodeIds[1] == 36527,
            $"NodeIds[1]={first.NodeIds[1]}");

        // все стержни должны иметь ровно 2 узла
        var bars = result.Elements.Where(e => e.NodeIds.Length == 2).ToList();
        TestHarness.Check("Elements: стержни (2 узла) есть", bars.Count > 0,
            $"barCount={bars.Count}");
    }

    static void TestBarStiffnesses()
    {
        if (!File.Exists(BarStiff))
        {
            TestHarness.Check("BarStiff: файл найден", false, "пропущено");
            return;
        }
        var result = LiraCsvSchemaParser.Parse(Nodes, Elements, BarStiff);

        TestHarness.Check("BarStiff: загружена >= 1 жёсткость", result.BarStiffnesses.Count >= 1,
            $"count={result.BarStiffnesses.Count}");

        // по примеру: единственная жёсткость с Id=4 ("Брус 40 X 40")
        var s = result.BarStiffnesses.FirstOrDefault(x => x.Id == 4);
        TestHarness.Check("BarStiff: жёсткость Id=4 найдена", s != null);
        if (s == null) return;

        TestHarness.Check("BarStiff: Name содержит '40'", s.Name.Contains("40"),
            $"Name='{s.Name}'");
        TestHarness.Check("BarStiff: EF = 489600",
            Math.Abs(s.EF - 489600) < 1, $"EF={s.EF}");
        TestHarness.Check("BarStiff: EIy = 3916.8",
            Math.Abs(s.EIy - 3916.8) < 0.1, $"EIy={s.EIy}");
        TestHarness.Check("BarStiff: EIz = 3916.8",
            Math.Abs(s.EIz - 3916.8) < 0.1, $"EIz={s.EIz}");
        TestHarness.Check("BarStiff: GIk = 4569.6",
            Math.Abs(s.GIk - 4569.6) < 0.1, $"GIk={s.GIk}");
    }

    static void TestPlateStiffnesses()
    {
        if (!File.Exists(PlateStiff))
        {
            TestHarness.Check("PlateStiff: файл найден", false, "пропущено");
            return;
        }
        var result = LiraCsvSchemaParser.Parse(Nodes, Elements, null, PlateStiff);

        TestHarness.Check("PlateStiff: загружено >= 4 записи", result.PlateStiffnesses.Count >= 4,
            $"count={result.PlateStiffnesses.Count}");

        // по примеру: жёсткость Id=1 (Плита H 20, E=2750000, V12=0.2, H=20 мм)
        var p = result.PlateStiffnesses.FirstOrDefault(x => x.Id == 1);
        TestHarness.Check("PlateStiff: Id=1 найдена", p != null);
        if (p == null) return;

        TestHarness.Check("PlateStiff: Name содержит '20'", p.Name.Contains("20"),
            $"Name='{p.Name}'");
        TestHarness.Check("PlateStiff: E = 2750000",
            Math.Abs(p.E - 2750000) < 1, $"E={p.E}");
        TestHarness.Check("PlateStiff: V12 = 0.2",
            Math.Abs(p.V12 - 0.2) < 0.001, $"V12={p.V12}");
        TestHarness.Check("PlateStiff: H = 20 мм",
            Math.Abs(p.H_mm - 20) < 0.1, $"H_mm={p.H_mm}");
    }

    static void TestFullParse()
    {
        var barStiff   = File.Exists(BarStiff)   ? BarStiff   : null;
        var plateStiff = File.Exists(PlateStiff) ? PlateStiff : null;
        var result = LiraCsvSchemaParser.Parse(Nodes, Elements, barStiff, plateStiff);

        TestHarness.Check("Full: нет исключений при полном парсинге", true);
        TestHarness.Check("Full: узлы > 0",    result.Nodes.Count > 0,    $"{result.Nodes.Count}");
        TestHarness.Check("Full: элементы > 0", result.Elements.Count > 0, $"{result.Elements.Count}");

        int barCount = result.Elements.Count(e => e.NodeIds.Length == 2);
        TestHarness.Check("Full: стержней > 0", barCount > 0, $"bars={barCount}");

        Console.WriteLine($"    Узлов: {result.Nodes.Count}, " +
                          $"элементов: {result.Elements.Count} (стержней: {barCount}), " +
                          $"жёсткостей: bar={result.BarStiffnesses.Count} plate={result.PlateStiffnesses.Count}");
    }

    static void TestConverter()
    {
        if (!File.Exists(BarStiff)) return;
        var result  = LiraCsvSchemaParser.Parse(Nodes, Elements, BarStiff);
        var members = LiraSchemaConverter.ToFemMembersByStiffness(result, schemaId: 999);

        TestHarness.Check("Converter: создан >= 1 FemMember", members.Length >= 1,
            $"members={members.Length}");

        // все стержни одной жёсткости (Id=4) — должен быть ровно 1 член
        var m = members.FirstOrDefault(x => x.Tag.Contains("40"));
        TestHarness.Check("Converter: FemMember с именем жёсткости найден", m != null,
            $"Tag='{m?.Tag}'");
        TestHarness.Check("Converter: SchemaId = 999", members.All(x => x.SchemaId == 999));

        // ElemIdsJson должен быть непустым JSON-массивом
        bool hasIds = members.All(x =>
            x.ElemIdsJson.StartsWith("[") && x.ElemIdsJson.Length > 3);
        TestHarness.Check("Converter: ElemIdsJson непустой", hasIds,
            $"sample='{members[0].ElemIdsJson[..Math.Min(40, members[0].ElemIdsJson.Length)]}'");

        // узлы и элементы
        var nodes    = LiraSchemaConverter.ToFemNodes(result, 999);
        var elements = LiraSchemaConverter.ToFemBarElements(result, 999);
        TestHarness.Check("Converter: ToFemNodes.Count = Nodes.Count",
            nodes.Length == result.Nodes.Count,
            $"{nodes.Length} vs {result.Nodes.Count}");
        TestHarness.Check("Converter: ToFemBarElements все beam",
            elements.All(e => e.ElemType == "beam"));
        TestHarness.Check("Converter: FemElement.NodeIdsJson валиден",
            elements.All(e => e.NodeIdsJson.StartsWith("[") && e.NodeIdsJson.Contains(",")));
    }
}
