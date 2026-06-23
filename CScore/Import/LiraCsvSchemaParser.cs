using System.Globalization;
using System.Text;

namespace CScore.Import;

/// <summary>Парсер CSV-экспортов расчётной схемы ЛираСАПР (кодировка Windows-1251, разделитель «;»).</summary>
public static class LiraCsvSchemaParser
{
    static readonly Encoding Cp1251 = Encoding.GetEncoding(1251);

    /// <summary>
    /// Читает CSV-файлы ЛираСАПР и возвращает объединённый контейнер данных схемы.
    /// </summary>
    /// <param name="nodesPath">Путь к файлу узлов.</param>
    /// <param name="elementsPath">Путь к файлу элементов.</param>
    /// <param name="barStiffPath">Путь к файлу жёсткостей стержней (опционально).</param>
    /// <param name="plateStiffPath">Путь к файлу жёсткостей пластин (опционально).</param>
    public static LiraSchemaData Parse(
        string  nodesPath,
        string  elementsPath,
        string? barStiffPath   = null,
        string? plateStiffPath = null)
    {
        var data = new LiraSchemaData();
        ParseNodes(nodesPath, data);
        ParseElements(elementsPath, data);
        if (barStiffPath   != null) ParseBarStiffnesses(barStiffPath, data);
        if (plateStiffPath != null) ParsePlateStiffnesses(plateStiffPath, data);
        return data;
    }

    static void ParseNodes(string path, LiraSchemaData data)
    {
        foreach (var line in ReadLines(path).Skip(1))
        {
            var cols = line.Split(';');
            if (cols.Length < 4) continue;
            if (!int.TryParse(cols[0].Trim(), out int id)) continue;
            data.Nodes.Add(new LiraNodeRecord(
                id,
                ParseDouble(cols[1]),
                ParseDouble(cols[2]),
                ParseDouble(cols[3]),
                ParseDofMask(cols, 4)
            ));
        }
    }

    static void ParseElements(string path, LiraSchemaData data)
    {
        foreach (var line in ReadLines(path).Skip(1))
        {
            var cols = line.Split(';');
            if (cols.Length < 12) continue;
            if (!int.TryParse(cols[0].Trim(), out int id)) continue;
            if (!int.TryParse(cols[1].Trim(), out int feType)) continue;
            _ = int.TryParse(cols[2].Trim(), out int secCount);
            _ = int.TryParse(cols[3].Trim(), out int stiffId);
            var nodeIds = cols[11]
                .Split(',')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Select(s => int.TryParse(s, out int n) ? n : -1)
                .Where(n => n > 0)
                .ToArray();
            if (nodeIds.Length == 0) continue;
            data.Elements.Add(new LiraElementRecord(id, feType, secCount, stiffId, nodeIds));
        }
    }

    static void ParseBarStiffnesses(string path, LiraSchemaData data)
    {
        foreach (var line in ReadLines(path).Skip(1))
        {
            var cols = line.Split(';');
            if (cols.Length < 10) continue;
            if (!int.TryParse(cols[0].Trim(), out int id)) continue;
            _ = int.TryParse(cols[1].Trim(), out int typeCode);
            data.BarStiffnesses.Add(new LiraBarStiffnessRecord(
                id,
                typeCode,
                cols[2].Trim(),
                ParseDouble(cols[6]),   // EF
                ParseDouble(cols[7]),   // EIy
                ParseDouble(cols[8]),   // EIz
                ParseDouble(cols[9])    // GIk
            ));
        }
    }

    static void ParsePlateStiffnesses(string path, LiraSchemaData data)
    {
        foreach (var line in ReadLines(path).Skip(1))
        {
            var cols = line.Split(';');
            if (cols.Length < 11) continue;
            if (!int.TryParse(cols[0].Trim(), out int id)) continue;
            _ = int.TryParse(cols[1].Trim(), out int typeCode);
            data.PlateStiffnesses.Add(new LiraPlateStiffnessRecord(
                id,
                typeCode,
                cols[2].Trim(),
                ParseDouble(cols[4]),   // E
                ParseDouble(cols[6]),   // V12
                ParseDouble(cols[10])   // H, мм
            ));
        }
    }

    static IEnumerable<string> ReadLines(string path)
        => File.ReadLines(path, Cp1251).Where(l => l.Trim().Length > 0);

    static double ParseDouble(string s)
    {
        s = s.Trim().Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
    }

    static int ParseDofMask(string[] cols, int startIndex)
    {
        int mask = 0;
        for (int i = 0; i < 7 && startIndex + i < cols.Length; i++)
            if (cols[startIndex + i].Trim() == "1")
                mask |= (1 << i);
        return mask;
    }
}
