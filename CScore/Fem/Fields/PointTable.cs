using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CScore.Fem.Fields;

/// <summary>
/// Таблица точек из CSV или буфера обмена (Excel). Разделитель — «;», таб или «,» (по первой строке, в этом порядке);
/// при «;» и табе десятичная запятая допустима. Первая строка — заголовок, если в ней есть нечисловая ячейка.
/// </summary>
public sealed class PointTable
{
    public IReadOnlyList<string> Headers { get; }
    /// <summary>Ячейки строк по столбцам заголовка; null — не число или пусто.</summary>
    public IReadOnlyList<double?[]> Rows { get; }
    public char Separator { get; }

    PointTable(IReadOnlyList<string> headers, IReadOnlyList<double?[]> rows, char separator) =>
        (Headers, Rows, Separator) = (headers, rows, separator);

    public static PointTable Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select(l => l.TrimStart('﻿')).Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count == 0) return new PointTable([], [], ';');
        char sep = lines[0].Contains(';') ? ';' : lines[0].Contains('\t') ? '\t' : ',';
        bool decimalComma = sep != ',';
        var first = lines[0].Split(sep);
        // Заголовок — если в первой строке есть непустая нечисловая ячейка; пустая ячейка (нет Z) — ещё данные.
        bool hasHeader = first.Any(c => c.Trim().Trim('"').Length > 0 && Number(c, decimalComma) == null);
        var headers = hasHeader
            ? first.Select(c => c.Trim().Trim('"')).ToList()
            : Enumerable.Range(1, first.Length).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList();
        var rows = new List<double?[]>();
        foreach (var line in lines.Skip(hasHeader ? 1 : 0))
        {
            var cells = line.Split(sep);
            var row = new double?[headers.Count];
            for (int i = 0; i < row.Length && i < cells.Length; i++) row[i] = Number(cells[i], decimalComma);
            rows.Add(row);
        }
        return new PointTable(headers, rows, sep);
    }

    /// <summary>
    /// Текст файла таблицы: с BOM — по нему; без BOM — UTF-8, если байты им корректны, иначе Windows-1251 (CSV из
    /// Excel в русской Windows).
    /// </summary>
    public static string ReadFile(string path) => Decode(File.ReadAllBytes(path));

    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF))
            return new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true).ReadToEnd();
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1251).GetString(bytes);
        }
    }

    static double? Number(string cell, bool decimalComma)
    {
        string s = cell.Trim().Trim('"').Replace(" ", "").Replace(" ", "");
        if (decimalComma) s = s.Replace(',', '.');
        // NaN и бесконечность — не числа таблицы: сломали бы слияние дублей и k-d дерево.
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v : null;
    }

    /// <summary>Есть ли в столбце хоть одно число (пустой столбец Z не предлагается как координата).</summary>
    public bool ColumnHasNumbers(int column) => Rows.Any(r => column < r.Length && r[column] != null);

    /// <summary>Масштаб длины по единицам в заголовке: «X [см]» → 0,01 м, «(мм)» → 0,001; без единиц — 1 (метры).</summary>
    public static double LengthScaleFromHeader(string header)
    {
        var m = Regex.Match(header, @"[\[\(]\s*(мм|см|м|mm|cm|m)\s*[\]\)]", RegexOptions.IgnoreCase);
        if (!m.Success) return 1;
        return m.Groups[1].Value.ToLowerInvariant() switch { "мм" or "mm" => 0.001, "см" or "cm" => 0.01, _ => 1 };
    }

    /// <summary>Точки по выбранным столбцам; строка, где хоть одна выбранная ячейка не число, пропускается.</summary>
    public (List<FieldPoint> Points, int Skipped) Select(int x, int y, int? z, IReadOnlyList<int> values)
    {
        var points = new List<FieldPoint>(Rows.Count);
        int skipped = 0;
        foreach (var r in Rows)
        {
            if (r[x] is not double px || r[y] is not double py || (z is int zi && r[zi] is null) || values.Any(v => r[v] is null))
            {
                skipped++;
                continue;
            }
            double pz = z is int zc ? r[zc]!.Value : 0;
            points.Add(new FieldPoint(new FieldVec3(px, py, pz), values.Select(v => r[v]!.Value).ToArray()));
        }
        return (points, skipped);
    }
}
