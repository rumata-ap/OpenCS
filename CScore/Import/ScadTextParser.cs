using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CScore.Import;

/// <summary>
/// Разбор нативного текстового экспорта SCAD (реверс-инжинерено на файле SCAD 21.1,
/// официальной спецификации формата нет). Файл — последовательность блоков вида
/// "(N/запись1/запись2/.../)" (кодировка Windows-1251). В экспорте SCAD до v11
/// вокруг номера блока допускаются пробелы: "( 1/...)" вместо "(1/...)".
/// Разбираются только блоки, нужные для топологии: (1) элементы, (3) жёсткости,
/// (4) узлы, (47) именованные группы.
/// </summary>
public static class ScadTextParser
{
    static ScadTextParser()
    {
        // Windows-1251 не зарегистрирована в .NET Core/5+ по умолчанию.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    const int ExpectedVersion    = 21;
    const int ExpectedSubVersion = 1;

    /// <summary>
    /// Типы КЭ, распознаваемые как топология: (код_типа, число_узлов) → стержень/оболочка.
    /// Остальные типы (пружины — 51, жёсткие вставки/объединения узлов — 100, и любые прочие)
    /// пропускаются, но их позиция в блоке (1) всё равно учитывается при нумерации элементов —
    /// иначе номера разойдутся с диапазонами в именованных группах (блок 47).
    /// </summary>
    static readonly HashSet<(int Type, int NodeCount)> KnownStructuralTypes =
    [
        (1, 2), (5, 2), (10, 2),   // стержни
        (42, 3),                   // треугольная оболочка
        (44, 4),                   // четырёхугольная оболочка
    ];

    static readonly Regex BlockBoundary =
        new(@"\)\s*\(\s*(\d+)\s*[;/]", RegexOptions.Compiled);

    static readonly Regex VersionHeader =
        new(@"\(0;Version=(\d+);SubVersion=(\d+)", RegexOptions.Compiled);

    // GE/GEI — оболочки (GEI в экспорте SCAD до v11), STZ/S0 — стержни, SPRING — пружины.
    static readonly Regex StiffnessHeader =
        new(@"^(\d+)\s+(GEI|GE|STZ|S0|SPRING)\b", RegexOptions.Compiled);

    static readonly Regex NameRegex =
        new("Name\\s+\"([^\"]*)\"", RegexOptions.Compiled);

    static readonly Regex GroupRegex =
        new("Name=\"([^\"]*)\"\\s+(\\d+)\\s*:\\s*(.+)", RegexOptions.Compiled);

    public static ScadImportResult Parse(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path, Encoding.GetEncoding(1251));
        }
        catch (Exception ex)
        {
            return new ScadImportResult { Error = $"Не удалось прочитать файл: {ex.Message}" };
        }
        return ParseText(text);
    }

    public static ScadImportResult ParseText(string text)
    {
        var result = new ScadImportResult();

        var versionMatch = VersionHeader.Match(text);
        if (versionMatch.Success)
        {
            int version    = int.Parse(versionMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            int subVersion = int.Parse(versionMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            if (version != ExpectedVersion || subVersion != ExpectedSubVersion)
            {
                result.Warnings.Add(
                    $"Файл создан SCAD версии {version}.{subVersion}; разбор формата проверен " +
                    $"на версии {ExpectedVersion}.{ExpectedSubVersion} — результат может быть неточным.");
            }
        }

        string elementsBlock = ExtractBlockContent(text, 1);
        string stiffBlock    = ExtractBlockContent(text, 3);
        string nodesBlock    = ExtractBlockContent(text, 4);
        string groupsBlock   = ExtractBlockContent(text, 47);

        if (elementsBlock.Length == 0)
        {
            result.Error = "В файле не найден блок элементов \"(1/...)\" — похоже, это не " +
                            "текстовый экспорт SCAD или файл повреждён.";
            return result;
        }
        if (nodesBlock.Length == 0)
        {
            result.Error = "В файле не найден блок координат узлов \"(4/...)\" — похоже, это не " +
                            "текстовый экспорт SCAD или файл повреждён.";
            return result;
        }
        if (stiffBlock.Length == 0)
        {
            result.Error = "В файле не найден блок жёсткостей \"(3/...)\" — похоже, это не " +
                            "текстовый экспорт SCAD или файл повреждён.";
            return result;
        }

        var data = new ScadSchemaData();
        data.Nodes.AddRange(ParseNodes(nodesBlock));

        var (elements, skipped) = ParseElements(elementsBlock);
        data.Elements.AddRange(elements);
        foreach (var (typeCode, count) in skipped.OrderBy(kv => kv.Key))
        {
            result.Warnings.Add(
                $"Пропущено {count} записей неизвестного/вспомогательного типа КЭ {typeCode} " +
                "(не распознан как стержень или оболочка).");
        }

        data.Stiffnesses.AddRange(ParseStiffnesses(stiffBlock));

        if (groupsBlock.Length > 0)
            data.Groups.AddRange(ParseGroups(groupsBlock));

        result.Data = data;
        return result;
    }

    /// <summary>
    /// Извлекает содержимое блока "(blockNumber/...)" — от маркера "(N/" (или "( N /"
    /// в экспорте SCAD до v11) до начала следующего блока (")(" любого номера)
    /// или до конца файла.
    /// </summary>
    static string ExtractBlockContent(string text, int blockNumber)
    {
        // Пробелы вокруг номера допускаются: в SCAD до v11 маркеры пишутся как "( 1/".
        var start = Regex.Match(text, $@"\(\s*{blockNumber}\s*/");
        if (!start.Success) return "";

        int contentStart = start.Index + start.Length;
        var next = BlockBoundary.Match(text, contentStart);
        int contentEnd = next.Success ? next.Index : text.Length;

        string raw = text[contentStart..contentEnd].TrimEnd();
        if (raw.EndsWith(')')) raw = raw[..^1];
        return raw;
    }

    /// <summary>Схлопывает переносы строк/пробелы экспортного файла и делит блок на записи по "/".</summary>
    static List<string> SplitRecords(string blockContent)
    {
        string normalized = Regex.Replace(blockContent, @"\s+", " ").Trim();
        return normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .ToList();
    }

    static List<ScadNodeRecord> ParseNodes(string blockContent)
    {
        var nodes = new List<ScadNodeRecord>();
        int id = 1;
        foreach (var rec in SplitRecords(blockContent))
        {
            int currentId = id++;
            var parts = rec.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // SCAD опускает координату Z в записи, если она равна нулю — "X Y" без третьего числа.
            if (parts.Length != 2 && parts.Length != 3) continue;
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
            {
                continue;
            }
            double z = 0.0;
            if (parts.Length == 3 &&
                !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
            {
                continue;
            }
            nodes.Add(new ScadNodeRecord(currentId, x, y, z));
        }
        return nodes;
    }

    static (List<ScadElementRecord> Elements, Dictionary<int, int> SkippedByType) ParseElements(string blockContent)
    {
        var elements = new List<ScadElementRecord>();
        var skipped  = new Dictionary<int, int>();
        int id = 1;

        foreach (var rec in SplitRecords(blockContent))
        {
            int currentId = id++;
            var parts = rec.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;
            if (!int.TryParse(parts[0], out int typeCode)) continue;
            if (!int.TryParse(parts[1], out int stiffId)) continue;

            var nodeIds = new int[parts.Length - 2];
            bool ok = true;
            for (int i = 0; i < nodeIds.Length; i++)
            {
                if (!int.TryParse(parts[i + 2], out nodeIds[i])) { ok = false; break; }
            }
            if (!ok) continue;

            if (!KnownStructuralTypes.Contains((typeCode, nodeIds.Length)))
            {
                skipped[typeCode] = skipped.GetValueOrDefault(typeCode) + 1;
                continue;
            }

            elements.Add(new ScadElementRecord(currentId, typeCode, stiffId, nodeIds));
        }

        return (elements, skipped);
    }

    static List<ScadStiffnessRecord> ParseStiffnesses(string blockContent)
    {
        var result = new List<ScadStiffnessRecord>();
        foreach (var rec in SplitRecords(blockContent))
        {
            var m = StiffnessHeader.Match(rec);
            if (!m.Success) continue;

            int id = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var kind = m.Groups[2].Value switch
            {
                "GE" or "GEI"     => ScadStiffnessKind.Shell,
                "STZ" or "S0"     => ScadStiffnessKind.Bar,
                _                 => ScadStiffnessKind.Other,
            };
            var nameMatch = NameRegex.Match(rec);
            string? name = nameMatch.Success ? nameMatch.Groups[1].Value.Trim() : null;

            result.Add(new ScadStiffnessRecord(id, name, kind));
        }
        return result;
    }

    static List<ScadGroupRecord> ParseGroups(string blockContent)
    {
        var result = new List<ScadGroupRecord>();
        foreach (var rec in SplitRecords(blockContent))
        {
            var m = GroupRegex.Match(rec);
            if (!m.Success) continue;

            string name     = m.Groups[1].Value;
            string kindCode = m.Groups[2].Value;
            if (kindCode != "2") continue; // код "2" = выборка элементов; прочие коды (узлы и т.п.) не относятся к топологии элементов

            var ids = ExpandRanges(m.Groups[3].Value);
            result.Add(new ScadGroupRecord(name, ids));
        }
        return result;
    }

    /// <summary>Разворачивает список токенов вида "183-813 6812 145-182" в массив ID.</summary>
    static int[] ExpandRanges(string rangesText)
    {
        var result = new List<int>();
        foreach (var tok in rangesText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int dash = tok.IndexOf('-', 1);
            if (dash > 0 &&
                int.TryParse(tok[..dash], out int from) &&
                int.TryParse(tok[(dash + 1)..], out int to))
            {
                for (int i = from; i <= to; i++) result.Add(i);
            }
            else if (int.TryParse(tok, out int single))
            {
                result.Add(single);
            }
        }
        return result.ToArray();
    }
}
