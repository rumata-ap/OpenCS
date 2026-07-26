using System.Globalization;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Results;

/// <summary>Строго разбирает типизированные output-файлы shell-расчёта.</summary>
public sealed class ShellResultParser
{
    /// <summary>Читает displacement, reaction, element-force и section-resultant files.</summary>
    public ShellResult Parse(
        string directory,
        IReadOnlyDictionary<int, NormalizedShellElement> elements)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(elements);

        string markerPath = Path.Combine(directory, "completed.marker");
        if (!File.Exists(markerPath) || new FileInfo(markerPath).Length == 0)
            throw new OpenSeesResultException("MissingMarker", $"Файл завершения не найден или пуст: {markerPath}");

        string marker = File.ReadAllText(markerPath).Trim();
        // В Tcl/OpenSees analyze возвращает 0 при успехе и ненулевой код при отказе.
        string status = marker == "0" ? "completed" : "not_converged";

        List<double[]> displacementRows = ParseRows(Path.Combine(directory, "node_disp.out"), 7, "node_disp");
        List<double[]> reactionRows = ParseRows(Path.Combine(directory, "node_reactions.out"), 7, "node_reactions", allowMissing: true);
        List<double[]> elementRows = ParseVariableElementRows(
            Path.Combine(directory, "element_forces.out"), elements);
        List<double[]> sectionRows = ParseRows(Path.Combine(directory, "section_forces.out"), 10, "section_forces");

        var sections = new List<ShellSectionResultants>(sectionRows.Count);
        var sectionKeys = new HashSet<(int ElementTag, int IntegrationPoint)>();
        foreach (double[] row in sectionRows)
        {
            int elementTag = ToTag(row[0], "section_forces");
            if (!elements.TryGetValue(elementTag, out NormalizedShellElement? element))
                throw new OpenSeesResultException("UnknownElement", $"section_forces: неизвестный элемент {elementTag}.");
            int point = ToTag(row[1], "section_forces");
            if (point < 1 || point > element.IntegrationPointCount)
                throw new OpenSeesResultException("InvalidIntegrationPoint",
                    $"section_forces: элемент {elementTag}, integration point {point} вне диапазона 1..{element.IntegrationPointCount}.");
            if (!sectionKeys.Add((elementTag, point)))
                throw new OpenSeesResultException("DuplicateRow",
                    $"section_forces: повторная строка элемента {elementTag}, integration point {point}.");

            sections.Add(new ShellSectionResultants(
                elementTag, point,
                row[2], row[3], row[4], row[5], row[6], row[7], row[8], row[9]));
        }

        return new ShellResult
        {
            Status = status,
            ArtifactDirectory = directory,
            Displacements = displacementRows.Select(row => new ShellNodeDisplacement(
                ToTag(row[0], "node_disp"), row[1], row[2], row[3], row[4], row[5], row[6])).ToArray(),
            Reactions = reactionRows.Select(row => new ShellNodeReaction(
                ToTag(row[0], "node_reactions"), row[1], row[2], row[3], row[4], row[5], row[6])).ToArray(),
            ElementForces = elementRows.Select(row =>
            {
                int tag = ToTag(row[0], "element_forces");
                return new ShellElementNodalForces(tag, elements[tag].Kind, row[1..]);
            }).ToArray(),
            SectionResultants = sections,
            Diagnostics = []
        };
    }

    private static List<double[]> ParseVariableElementRows(
        string path,
        IReadOnlyDictionary<int, NormalizedShellElement> elements)
    {
        if (!File.Exists(path))
            throw new OpenSeesResultException("MissingFile", $"Файл element_forces не найден: {path}");

        var rows = new List<double[]>();
        int lineNo = 0;
        foreach (string raw in File.ReadAllLines(path))
        {
            lineNo++;
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            int tag = ParseTag(parts[0], "element_forces", lineNo);
            if (!elements.TryGetValue(tag, out NormalizedShellElement? element))
                throw new OpenSeesResultException("UnknownElement", $"element_forces строка {lineNo}: неизвестный элемент {tag}.");
            int expected = 1 + 6 * element.NodeTags.Count;
            if (parts.Length != expected)
                throw new OpenSeesResultException("WrongColumnCount",
                    $"element_forces строка {lineNo}: ожидалось {expected} колонок, получено {parts.Length}.");
            rows.Add(ParseParts(parts, "element_forces", lineNo));
        }

        return rows;
    }

    private static List<double[]> ParseRows(string path, int columns, string name, bool allowMissing = false)
    {
        if (!File.Exists(path))
        {
            if (allowMissing) return [];
            throw new OpenSeesResultException("MissingFile", $"Файл {name} не найден: {path}");
        }

        var rows = new List<double[]>();
        int lineNo = 0;
        foreach (string raw in File.ReadAllLines(path))
        {
            lineNo++;
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != columns)
                throw new OpenSeesResultException("WrongColumnCount",
                    $"{name} строка {lineNo}: ожидалось {columns} колонок, получено {parts.Length}.");
            rows.Add(ParseParts(parts, name, lineNo));
        }

        return rows;
    }

    private static double[] ParseParts(string[] parts, string name, int lineNo)
    {
        var values = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) ||
                !double.IsFinite(values[i]))
                throw new OpenSeesResultException("InvalidNumber",
                    $"{name} строка {lineNo}: поле {i} = «{parts[i]}».");
        return values;
    }

    private static int ParseTag(string text, string name, int lineNo)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tag) || tag <= 0)
            throw new OpenSeesResultException("InvalidTag", $"{name} строка {lineNo}: некорректный tag «{text}».");
        return tag;
    }

    private static int ToTag(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0 || value != Math.Truncate(value) || value > int.MaxValue)
            throw new OpenSeesResultException("InvalidTag", $"{name}: некорректный tag «{value.ToString(CultureInfo.InvariantCulture)}».");
        return (int)value;
    }
}
