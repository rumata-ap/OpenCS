using System.Globalization;
using System.Text.Json;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Results;

/// <summary>Строго разбирает по-шаговые выходные файлы нелинейного расчёта FEM-схемы.</summary>
public sealed class FemNonlinearResultParser
{
    sealed record RecorderOrder(int[] NodeTags, int[] RestrainedTags, int[] ElemTags);

    /// <summary>Читает step_status.out, recorder_order.json и recorder-матрицы. Требует completed.marker.</summary>
    public IReadOnlyList<FemNonlinearStepResult> Parse(string directory)
    {
        string marker = Path.Combine(directory, "completed.marker");
        if (!File.Exists(marker) || new FileInfo(marker).Length == 0)
            throw new OpenSeesResultException("MissingMarker", $"Файл завершения не найден или пуст: {marker}");

        var order = ParseOrder(Path.Combine(directory, "recorder_order.json"));
        var steps = ParseStepStatus(Path.Combine(directory, "step_status.out"));

        int convergedCount = steps.Count(s => s.Converged);
        var dispRows = ParseMatrix(Path.Combine(directory, "nonlinear_node_disp.out"), 1 + order.NodeTags.Length * 6, "nonlinear_node_disp");
        var reactRows = order.RestrainedTags.Length > 0
            ? ParseMatrix(Path.Combine(directory, "nonlinear_node_reactions.out"), 1 + order.RestrainedTags.Length * 6, "nonlinear_node_reactions")
            : [];
        var forceRows = ParseMatrix(Path.Combine(directory, "nonlinear_element_forces.out"), 1 + order.ElemTags.Length * 12, "nonlinear_element_forces");

        if (dispRows.Count != convergedCount)
            throw new OpenSeesResultException("RowCountMismatch",
                $"nonlinear_node_disp: ожидалось {convergedCount} строк (по числу сошедшихся шагов), получено {dispRows.Count}.");
        if (forceRows.Count != convergedCount)
            throw new OpenSeesResultException("RowCountMismatch",
                $"nonlinear_element_forces: ожидалось {convergedCount} строк, получено {forceRows.Count}.");
        if (order.RestrainedTags.Length > 0 && reactRows.Count != convergedCount)
            throw new OpenSeesResultException("RowCountMismatch",
                $"nonlinear_node_reactions: ожидалось {convergedCount} строк, получено {reactRows.Count}.");

        var results = new List<FemNonlinearStepResult>(steps.Count);
        int rowIndex = 0;
        foreach (var s in steps)
        {
            if (!s.Converged)
            {
                results.Add(new FemNonlinearStepResult(s.StepIndex, s.LoadFactor, false, [], [], [])
                {
                    IsRefinement = s.IsRefinement
                });
                continue;
            }

            var disp = ToNodeDisplacements(dispRows[rowIndex], order.NodeTags);
            var react = order.RestrainedTags.Length > 0
                ? ToNodeReactions(reactRows[rowIndex], order.RestrainedTags)
                : [];
            var forces = ToElementForces(forceRows[rowIndex], order.ElemTags);
            results.Add(new FemNonlinearStepResult(s.StepIndex, s.LoadFactor, true, disp, react, forces)
            {
                IsRefinement = s.IsRefinement
            });
            rowIndex++;
        }
        return results;
    }

    static RecorderOrder ParseOrder(string path)
    {
        if (!File.Exists(path))
            throw new OpenSeesResultException("MissingFile", $"Файл порядка тегов не найден: {path}");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            int[] Read(string prop) => doc.RootElement.GetProperty(prop).EnumerateArray().Select(e => e.GetInt32()).ToArray();
            return new RecorderOrder(Read("nodeTags"), Read("restrainedTags"), Read("elemTags"));
        }
        catch (JsonException ex)
        {
            throw new OpenSeesResultException("InvalidOrderFile", $"recorder_order.json повреждён: {ex.Message}");
        }
    }

    static List<(int StepIndex, double LoadFactor, bool Converged, bool IsRefinement)> ParseStepStatus(string path)
    {
        if (!File.Exists(path))
            throw new OpenSeesResultException("MissingFile", $"Файл step_status не найден: {path}");
        var rows = new List<(int, double, bool, bool)>();
        int lineNo = 0;
        foreach (var raw in File.ReadAllLines(path))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4)
                throw new OpenSeesResultException("WrongColumnCount", $"step_status строка {lineNo}: ожидалось 4 колонки, получено {parts.Length}.");
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var step) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lf) ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var convergedFlag) ||
                !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var refinementFlag) ||
                (convergedFlag is not (0 or 1)) || (refinementFlag is not (0 or 1)))
                throw new OpenSeesResultException("InvalidNumber", $"step_status строка {lineNo}: не удалось разобрать значения.");
            rows.Add((step, lf, convergedFlag != 0, refinementFlag != 0));
        }
        return rows;
    }

    static List<double[]> ParseMatrix(string path, int expectedCols, string name)
    {
        if (!File.Exists(path)) return [];
        var rows = new List<double[]>();
        int lineNo = 0;
        foreach (var raw in File.ReadAllLines(path))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.Contains('\0'))
                throw new OpenSeesResultException("CorruptedOutput",
                    $"{name} строка {lineNo}: найден нулевой байт; файл результата повреждён OpenSees.");
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != expectedCols)
                throw new OpenSeesResultException("WrongColumnCount", $"{name} строка {lineNo}: ожидалось {expectedCols} колонок, получено {parts.Length}.");
            var values = new double[expectedCols];
            for (int i = 0; i < expectedCols; i++)
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !double.IsFinite(values[i]))
                    throw new OpenSeesResultException("InvalidNumber", $"{name} строка {lineNo}: поле {i} = «{parts[i]}».");
            rows.Add(values);
        }
        return rows;
    }

    static IReadOnlyList<FemNodeDisplacement> ToNodeDisplacements(double[] row, int[] nodeTags)
    {
        var list = new List<FemNodeDisplacement>(nodeTags.Length);
        for (int k = 0; k < nodeTags.Length; k++)
        {
            int off = 1 + k * 6;
            list.Add(new FemNodeDisplacement(nodeTags[k], row[off], row[off + 1], row[off + 2], row[off + 3], row[off + 4], row[off + 5]));
        }
        return list;
    }

    static IReadOnlyList<FemNodeReaction> ToNodeReactions(double[] row, int[] nodeTags)
    {
        var list = new List<FemNodeReaction>(nodeTags.Length);
        for (int k = 0; k < nodeTags.Length; k++)
        {
            int off = 1 + k * 6;
            list.Add(new FemNodeReaction(nodeTags[k], row[off], row[off + 1], row[off + 2], row[off + 3], row[off + 4], row[off + 5]));
        }
        return list;
    }

    static IReadOnlyList<FemElementEndForces> ToElementForces(double[] row, int[] elemTags)
    {
        var list = new List<FemElementEndForces>(elemTags.Length);
        for (int k = 0; k < elemTags.Length; k++)
        {
            int off = 1 + k * 12;
            list.Add(new FemElementEndForces(elemTags[k],
                row[off], row[off + 1], row[off + 2], row[off + 3], row[off + 4], row[off + 5],
                row[off + 6], row[off + 7], row[off + 8], row[off + 9], row[off + 10], row[off + 11]));
        }
        return list;
    }
}
