using System.Globalization;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Results;

/// <summary>Строго разбирает выходные файлы линейного расчёта FEM-схемы.</summary>
public sealed class FemLinearResultParser
{
    /// <summary>Читает node_disp.out, node_reactions.out, element_forces.out. Требует completed.marker.</summary>
    public (IReadOnlyList<FemNodeDisplacement>, IReadOnlyList<FemNodeReaction>, IReadOnlyList<FemElementEndForces>) Parse(string directory)
    {
        string marker = Path.Combine(directory, "completed.marker");
        if (!File.Exists(marker) || new FileInfo(marker).Length == 0)
            throw new OpenSeesResultException("MissingMarker", $"Файл завершения не найден или пуст: {marker}");

        var disp = ParseRows(Path.Combine(directory, "node_disp.out"), 7, "node_disp")
            .Select(c => new FemNodeDisplacement((int)c[0], c[1], c[2], c[3], c[4], c[5], c[6])).ToList();
        var react = ParseRows(Path.Combine(directory, "node_reactions.out"), 7, "node_reactions", allowMissing: true)
            .Select(c => new FemNodeReaction((int)c[0], c[1], c[2], c[3], c[4], c[5], c[6])).ToList();
        var forces = ParseRows(Path.Combine(directory, "element_forces.out"), 13, "element_forces")
            .Select(c => new FemElementEndForces((int)c[0],
                c[1], c[2], c[3], c[4], c[5], c[6],
                c[7], c[8], c[9], c[10], c[11], c[12])).ToList();

        return (disp, react, forces);
    }

    static List<double[]> ParseRows(string path, int cols, string name, bool allowMissing = false)
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
            if (parts.Length != cols)
                throw new OpenSeesResultException("WrongColumnCount",
                    $"{name} строка {lineNo}: ожидалось {cols} колонок, получено {parts.Length}.");
            var values = new double[cols];
            for (int i = 0; i < cols; i++)
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) ||
                    !double.IsFinite(values[i]))
                    throw new OpenSeesResultException("InvalidNumber", $"{name} строка {lineNo}: поле {i} = «{parts[i]}».");
            rows.Add(values);
        }
        return rows;
    }
}
