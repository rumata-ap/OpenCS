using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace CScore.Import;

/// <summary>Таблица ячеек из файла: имя (лист, «Таблица 1») и строки текста.</summary>
public sealed record ForceTable(string Name, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>
/// Чтение таблиц усилий как текста ячеек: CSV/TSV/TXT (UTF-8 или Windows-1251, разделитель — табуляция, «;» или «,»),
/// XLSX/XLS (каждый лист — таблица), DOCX (каждая таблица документа; объединённые ячейки раскрываются).
/// Смысл колонок разбирает <see cref="ForceTableParser"/>.
/// </summary>
public static class ForceTableReader
{
    /// <summary>Расширения файлов, которые читает <see cref="Read"/>.</summary>
    public static readonly IReadOnlyList<string> Extensions = [".csv", ".tsv", ".txt", ".xlsx", ".xls", ".docx"];

    /// <summary>Таблицы файла; <paramref name="sheet"/> — имя или номер (с 1) листа книги Excel.</summary>
    public static IReadOnlyList<ForceTable> Read(string path, string? sheet = null)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".csv" or ".tsv" or ".txt" => [ReadDelimited(DecodeText(File.ReadAllBytes(path)), Path.GetFileName(path))],
            ".xlsx" or ".xls" => ReadExcel(path, sheet),
            ".docx" => ReadDocx(path),
            _ => throw new NotSupportedException(ext),
        };
    }

    /// <summary>UTF-8 (с BOM или без), иначе Windows-1251.</summary>
    public static string DecodeText(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1251).GetString(bytes);
        }
    }

    /// <summary>Текст с разделителями: табуляция, если есть, иначе «;», иначе «,»; кавычки CSV снимаются.</summary>
    public static ForceTable ReadDelimited(string text, string name)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var probe = lines.Where(l => l.Trim().Length > 0).Take(20).ToList();
        char sep = probe.Any(l => l.Contains('\t')) ? '\t' : probe.Any(l => l.Contains(';')) ? ';' : ',';
        var rows = new List<IReadOnlyList<string>>();
        foreach (string line in lines)
            rows.Add(SplitLine(line, sep));
        return new ForceTable(name, rows);
    }

    static List<string> SplitLine(string line, char sep)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"' && cell.ToString().Trim().Length == 0) { cell.Clear(); quoted = true; }
            else if (c == sep) { cells.Add(cell.ToString().Trim()); cell.Clear(); }
            else cell.Append(c);
        }
        cells.Add(cell.ToString().Trim());
        return cells;
    }

    static IReadOnlyList<ForceTable> ReadExcel(string path, string? sheet)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(stream);
        var tables = new List<ForceTable>();
        int index = 0;
        do
        {
            index++;
            string name = reader.Name ?? $"Sheet{index}";
            bool wanted = sheet == null
                          || string.Equals(sheet.Trim(), name, StringComparison.OrdinalIgnoreCase)
                          || int.TryParse(sheet, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n == index;
            if (!wanted) continue;
            var rows = new List<IReadOnlyList<string>>();
            while (reader.Read())
            {
                var row = new List<string>(reader.FieldCount);
                for (int i = 0; i < reader.FieldCount; i++)
                    row.Add(reader.GetValue(i) switch
                    {
                        null => "",
                        double d => d.ToString("R", CultureInfo.InvariantCulture),
                        float f => f.ToString("R", CultureInfo.InvariantCulture),
                        decimal m => m.ToString(CultureInfo.InvariantCulture),
                        var v => (Convert.ToString(v, CultureInfo.InvariantCulture) ?? "").Trim(),
                    });
                rows.Add(row);
            }
            tables.Add(new ForceTable(name, rows));
        } while (reader.NextResult());
        if (sheet != null && tables.Count == 0)
            throw new InvalidOperationException($"Лист «{sheet}» не найден.");
        return tables;
    }

    static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    static IReadOnlyList<ForceTable> ReadDocx(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return ReadDocx(stream);
    }

    /// <summary>Таблицы документа Word: <c>w:gridSpan</c> — пустые ячейки-добавки, продолжение <c>w:vMerge</c> — текст ячейки сверху.</summary>
    public static IReadOnlyList<ForceTable> ReadDocx(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("В файле нет word/document.xml.");
        XDocument doc;
        using (var xml = entry.Open()) doc = XDocument.Load(xml);
        var tables = new List<ForceTable>();
        foreach (var tbl in doc.Descendants(W + "tbl").Where(t => !t.Ancestors(W + "tbl").Any()))
        {
            var rows = new List<IReadOnlyList<string>>();
            List<string>? previous = null;
            foreach (var tr in tbl.Elements(W + "tr"))
            {
                var row = new List<string>();
                int before = GridValue(tr.Element(W + "trPr")?.Element(W + "gridBefore"));
                for (int i = 0; i < before; i++) row.Add("");
                foreach (var tc in tr.Elements(W + "tc"))
                {
                    var props = tc.Element(W + "tcPr");
                    int span = Math.Max(1, GridValue(props?.Element(W + "gridSpan")));
                    var vMerge = props?.Element(W + "vMerge");
                    bool continues = vMerge != null && (string?)vMerge.Attribute(W + "val") != "restart";
                    string text = continues
                        ? previous != null && row.Count < previous.Count ? previous[row.Count] : ""
                        : CellText(tc);
                    row.Add(text);
                    for (int i = 1; i < span; i++) row.Add("");
                }
                rows.Add(row);
                previous = row;
            }
            tables.Add(new ForceTable($"Таблица {tables.Count + 1}", rows));
        }
        return tables;
    }

    static int GridValue(XElement? e) =>
        e != null && int.TryParse((string?)e.Attribute(W + "val"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    static string CellText(XElement tc) =>
        string.Join(" ", tc.Elements(W + "p")
            .Select(p => string.Concat(p.Descendants().Select(e =>
                e.Name == W + "t" ? e.Value : e.Name == W + "tab" ? " " : "")).Trim())
            .Where(s => s.Length > 0));
}
