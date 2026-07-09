using CScore.Import;

namespace CSfea.Tests;

static class ScadTextParserTests
{
    // Синтетическая фикстура: 6 узлов, 6 записей в блоке элементов (2 из них — не топология
    // и должны быть пропущены, но обязаны "съесть" номер элемента), 2 жёсткости, 1 группа
    // с диапазоном "3-4", покрывающая часть элементов.
    const string Fixture =
        "(0;Version=21;SubVersion=1/1;\"Test\";/)" +
        "(1/10 1 1 2 /51 9 3 /44 2 1 2 3 4 /42 2 4 5 6 /100 99 1 2 3 /10 1 5 6 /)" +
        "(3/1  S0 900000 40 90 NU 0.2 RO 0.9 TMP 1.2e-05 Shift 5043.87 110000 113015 " +
        "    Material=\"{11111111-0000-0000-0000-000000000001}\"  Name \"Стойка\"/" +
        "2 GE 1.8e+06 0.2 0.22 RO 2.5 TMP 1.2e-05 1.2e-05 " +
        "    Material=\"{11111111-0000-0000-0000-000000000002}\"  Name \"Плита\"/)" +
        "(4/0 0 0/1 0 0/1 1 0/0 1 0/0 0 1/1 0 1/)" +
        "(47/Name=\"ТестГруппа\" 2  : 1 3-4/)";

    public static void RunAll()
    {
        TestHarness.Section("ScadTextParser");
        TestParseSuccess();
        TestRealFile();
        TestConverter();
    }

    static void TestParseSuccess()
    {
        var r = ScadTextParser.ParseText(Fixture);
        TestHarness.Check("Success", r.Success, r.Error ?? "");
        if (!r.Success || r.Data == null) return;

        var d = r.Data;
        TestHarness.Check("Nodes.Count == 6", d.Nodes.Count == 6, $"{d.Nodes.Count}");
        TestHarness.Check("Node[0] Id=1 (0,0,0)",
            d.Nodes[0].Id == 1 && d.Nodes[0].X == 0 && d.Nodes[0].Y == 0 && d.Nodes[0].Z == 0);
        TestHarness.Check("Node[2] Id=3 (1,1,0)",
            d.Nodes[2].Id == 3 && d.Nodes[2].X == 1 && d.Nodes[2].Y == 1 && d.Nodes[2].Z == 0);

        TestHarness.Check("Elements.Count == 4 (2 пропущены: пружина, жёсткая вставка)",
            d.Elements.Count == 4, $"{d.Elements.Count}");
        var ids = d.Elements.Select(e => e.Id).OrderBy(x => x).ToArray();
        TestHarness.Check("Element Ids = {1,3,4,6} (номера не переиспользуются)",
            ids.SequenceEqual([1, 3, 4, 6]), string.Join(",", ids));

        var bar = d.Elements.First(e => e.Id == 1);
        TestHarness.Check("Элемент 1 — стержень (2 узла)", bar.NodeIds.SequenceEqual([1, 2]));
        var quad = d.Elements.First(e => e.Id == 3);
        TestHarness.Check("Элемент 3 — четырёхугольник (4 узла)", quad.NodeIds.SequenceEqual([1, 2, 3, 4]));
        var tri = d.Elements.First(e => e.Id == 4);
        TestHarness.Check("Элемент 4 — треугольник (3 узла)", tri.NodeIds.SequenceEqual([4, 5, 6]));

        TestHarness.Check("Warnings: 2 (тип 51 и тип 100 пропущены)",
            r.Warnings.Count == 2, string.Join(" | ", r.Warnings));

        TestHarness.Check("Stiffnesses.Count == 2", d.Stiffnesses.Count == 2, $"{d.Stiffnesses.Count}");
        var s1 = d.Stiffnesses.First(s => s.Id == 1);
        TestHarness.Check("Stiffness 1: Name=Стойка, Kind=Bar",
            s1.Name == "Стойка" && s1.Kind == ScadStiffnessKind.Bar);
        var s2 = d.Stiffnesses.First(s => s.Id == 2);
        TestHarness.Check("Stiffness 2: Name=Плита, Kind=Shell",
            s2.Name == "Плита" && s2.Kind == ScadStiffnessKind.Shell);

        TestHarness.Check("Groups.Count == 1", d.Groups.Count == 1, $"{d.Groups.Count}");
        var g = d.Groups[0];
        TestHarness.Check("Group Name = ТестГруппа", g.Name == "ТестГруппа", g.Name);
        TestHarness.Check("Group ElementIds = {1,3,4} (диапазон 3-4 развёрнут)",
            g.ElementIds.OrderBy(x => x).SequenceEqual([1, 3, 4]),
            string.Join(",", g.ElementIds));
    }

    const string RealFile = @"O:\docs\SCAD\museum.txt";

    static void TestRealFile()
    {
        if (!File.Exists(RealFile))
        {
            TestHarness.Check("Реальный файл museum.txt найден", false, "пропущено — файл недоступен");
            return;
        }

        var r = ScadTextParser.Parse(RealFile);
        TestHarness.Check("RealFile: Success", r.Success, r.Error ?? "");
        if (!r.Success || r.Data == null) return;

        var d = r.Data;
        TestHarness.Check("RealFile: Nodes.Count == 112525", d.Nodes.Count == 112525, $"{d.Nodes.Count}");

        int barCount   = d.Elements.Count(e => e.NodeIds.Length == 2);
        int tri3Count  = d.Elements.Count(e => e.NodeIds.Length == 3);
        int quad4Count = d.Elements.Count(e => e.NodeIds.Length == 4);
        TestHarness.Check("RealFile: стержней == 3651", barCount == 3651, $"{barCount}");
        TestHarness.Check("RealFile: треугольников == 22414", tri3Count == 22414, $"{tri3Count}");
        TestHarness.Check("RealFile: четырёхугольников == 97071", quad4Count == 97071, $"{quad4Count}");
        TestHarness.Check("RealFile: Elements.Count == 123136",
            d.Elements.Count == 123136, $"{d.Elements.Count}");

        // В .sli — 27 слотов жёсткости (Num=1..27), но id=18 в текстовом формате не имеет записи
        // вовсе (пустой/неиспользуемый слот) — это особенность данных, а не ошибка парсера.
        TestHarness.Check("RealFile: Stiffnesses.Count == 26", d.Stiffnesses.Count == 26, $"{d.Stiffnesses.Count}");

        TestHarness.Check("RealFile: Groups.Count == 2", d.Groups.Count == 2, $"{d.Groups.Count}");
        TestHarness.Check("RealFile: группа 'Покрытие' присутствует",
            d.Groups.Any(g => g.Name == "Покрытие"));
        TestHarness.Check("RealFile: группа 'Ферма' присутствует",
            d.Groups.Any(g => g.Name == "Ферма"));

        Console.WriteLine($"    Предупреждений: {r.Warnings.Count}");
        foreach (var w in r.Warnings) Console.WriteLine($"      - {w}");
    }

    static void TestConverter()
    {
        var r = ScadTextParser.ParseText(Fixture);
        if (!r.Success || r.Data == null) { TestHarness.Check("Converter: fixture parses", false); return; }
        var data = r.Data;

        var nodes = ScadSchemaConverter.ToFemNodes(data, schemaId: 42);
        TestHarness.Check("Converter: ToFemNodes.Length == 6", nodes.Length == 6, $"{nodes.Length}");
        TestHarness.Check("Converter: node[0] SchemaId=42, NodeTag=1",
            nodes[0].SchemaId == 42 && nodes[0].NodeTag == "1");
        TestHarness.Check("Converter: node[2] X=1,Y=1,Z=0",
            nodes[2].X == 1 && nodes[2].Y == 1 && nodes[2].Z == 0);

        var elements = ScadSchemaConverter.ToFemElements(data, schemaId: 42);
        TestHarness.Check("Converter: ToFemElements.Length == 4", elements.Length == 4, $"{elements.Length}");
        var barEl = elements.First(e => e.ElemTag == "1");
        TestHarness.Check("Converter: элемент 1 — beam, SectionTag=Стойка",
            barEl.ElemType == "beam" && barEl.SectionTag == "Стойка");
        var quadEl = elements.First(e => e.ElemTag == "3");
        TestHarness.Check("Converter: элемент 3 — shell, SectionTag=Плита, NodeIdsJson=[1,2,3,4]",
            quadEl.ElemType == "shell" && quadEl.SectionTag == "Плита" &&
            quadEl.NodeIdsJson == "[1,2,3,4]");

        var members = ScadSchemaConverter.ToFemMembers(data, schemaId: 42);
        TestHarness.Check("Converter: ToFemMembers.Length == 2", members.Length == 2, $"{members.Length}");
        var groupMember = members.FirstOrDefault(m => m.Tag == "ТестГруппа");
        TestHarness.Check("Converter: member 'ТестГруппа' найден", groupMember != null);
        TestHarness.Check("Converter: 'ТестГруппа'.ElemIdsJson содержит 1,3,4",
            groupMember != null &&
            new[] { 1, 3, 4 }.All(id => groupMember.ElemIdsJson.Contains(id.ToString())));
        var fallbackMember = members.FirstOrDefault(m => m.Tag == "Стойка");
        TestHarness.Check("Converter: member 'Стойка' (по жёсткости, элемент 6 вне группы) найден",
            fallbackMember != null);
        TestHarness.Check("Converter: 'Стойка'.MemberType == beam",
            fallbackMember != null && fallbackMember.MemberType == "beam");
    }
}
