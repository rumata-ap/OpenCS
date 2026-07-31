using System.Globalization;
using System.Text.Json;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Results;

/// <summary>Разбирает recorder-based постадийный вывод нелинейного shell-расчёта. Портирует
/// устойчивый к обрывам паттерн FemNonlinearResultParser (-closeOnWrite гарантирует, что каждый
/// уже записанный шаг целостен сам по себе — completed.marker не обязателен для показа уже
/// сошедшихся шагов при обрыве процесса).</summary>
public sealed class ShellResultParser
{
    private sealed record RecorderOrder(
        int[] NodeTags, int[] RestrainedTags, int[] ShellElementTags, int[] NonlinearBeamElementTags,
        (int Point, int[] ElementTags, string File)[] SectionForceGroups);

    /// <summary>Читает recorder_order.json, step_status.out и recorder-матрицы каталога.</summary>
    public ShellResult Parse(
        string directory,
        IReadOnlyDictionary<int, NormalizedShellElement> elements)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(elements);

        RecorderOrder order = ParseOrder(Path.Combine(directory, "recorder_order.json"));
        var stepStatus = ParseStepStatus(Path.Combine(directory, "step_status.out"));

        List<double[]> dispRows = ParseMatrix(Path.Combine(directory, "shell_node_disp.out"), 1 + order.NodeTags.Length * 6, "shell_node_disp");
        List<double[]> reactRows = order.RestrainedTags.Length > 0
            ? ParseMatrix(Path.Combine(directory, "shell_node_reactions.out"), 1 + order.RestrainedTags.Length * 6, "shell_node_reactions")
            : [];
        List<double[]> elementForceRows = order.ShellElementTags.Length > 0
            ? ParseVariableElementRows(Path.Combine(directory, "shell_element_forces.out"), order.ShellElementTags, elements)
            : [];
        List<double[]> beamForceRows = order.NonlinearBeamElementTags.Length > 0
            ? ParseMatrix(Path.Combine(directory, "shell_beam_element_forces.out"), 1 + order.NonlinearBeamElementTags.Length * 12, "shell_beam_element_forces")
            : [];
        var sectionForceRowsByFile = order.SectionForceGroups.ToDictionary(
            g => g.File,
            g => ParseMatrix(Path.Combine(directory, g.File), 1 + g.ElementTags.Length * 8, g.File));
        ShellStateCatalog? stateCatalog = File.Exists(Path.Combine(directory, "state_order.json"))
            ? new ShellStateParser().ParseCatalog(directory)
            : null;

        int available = dispRows.Count;
        available = Math.Min(available, order.RestrainedTags.Length > 0 ? reactRows.Count : available);
        available = Math.Min(available, order.ShellElementTags.Length > 0 ? elementForceRows.Count : available);
        available = Math.Min(available, order.NonlinearBeamElementTags.Length > 0 ? beamForceRows.Count : available);
        foreach (var rows in sectionForceRowsByFile.Values)
            available = Math.Min(available, rows.Count);

        var steps = new List<RCShellStepResult>(stepStatus.Count);
        int rowIndex = 0;
        foreach (var s in stepStatus)
        {
            if (!s.Converged)
            {
                steps.Add(new RCShellStepResult(s.StepIndex, s.StageIndex, s.LoadFactor, false, [], [], [], [], [])
                {
                    IsRefinement = s.IsRefinement
                });
                continue;
            }

            if (rowIndex >= available) { rowIndex++; continue; }

            var displacements = ToNodeDisplacements(dispRows[rowIndex], order.NodeTags);
            var reactions = order.RestrainedTags.Length > 0
                ? ToNodeReactions(reactRows[rowIndex], order.RestrainedTags)
                : [];
            var elementForces = order.ShellElementTags.Length > 0
                ? ToElementForces(elementForceRows[rowIndex], order.ShellElementTags, elements)
                : [];
            var beamForces = order.NonlinearBeamElementTags.Length > 0
                ? ToBeamElementForces(beamForceRows[rowIndex], order.NonlinearBeamElementTags)
                : [];
            var sectionResultants = new List<ShellSectionResultants>();
            foreach (var group in order.SectionForceGroups)
            {
                double[] row = sectionForceRowsByFile[group.File][rowIndex];
                sectionResultants.AddRange(ToSectionResultants(row, group.ElementTags, group.Point));
            }

            steps.Add(new RCShellStepResult(
                s.StepIndex, s.StageIndex, s.LoadFactor, true,
                displacements, reactions, elementForces, sectionResultants, beamForces));
            rowIndex++;
        }

        RCShellStepResult? last = steps.LastOrDefault();
        bool completed = last is { Converged: true } &&
            steps.Count > 0 && stepStatus[^1].Converged;

        return new ShellResult
        {
            Status = completed ? "completed" : "not_converged",
            ArtifactDirectory = directory,
            Steps = steps,
            Displacements = last?.Displacements ?? [],
            Reactions = last?.Reactions ?? [],
            ElementForces = last?.ElementForces ?? [],
            SectionResultants = last?.SectionResultants ?? [],
            Diagnostics = [],
            StateCatalog = stateCatalog
        };
    }

    private static RecorderOrder ParseOrder(string path)
    {
        if (!File.Exists(path))
            throw new OpenSeesResultException("MissingFile", $"Файл порядка тегов не найден: {path}");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            int[] ReadInts(string prop) => doc.RootElement.GetProperty(prop).EnumerateArray().Select(e => e.GetInt32()).ToArray();
            var groups = doc.RootElement.GetProperty("sectionForceGroups").EnumerateArray()
                .Select(e => (
                    e.GetProperty("point").GetInt32(),
                    e.GetProperty("elementTags").EnumerateArray().Select(t => t.GetInt32()).ToArray(),
                    e.GetProperty("file").GetString() ?? ""))
                .ToArray();
            return new RecorderOrder(
                ReadInts("nodeTags"), ReadInts("restrainedTags"),
                ReadInts("shellElementTags"), ReadInts("nonlinearBeamElementTags"), groups);
        }
        catch (JsonException ex)
        {
            throw new OpenSeesResultException("InvalidOrderFile", $"recorder_order.json повреждён: {ex.Message}");
        }
    }

    private static List<(int StepIndex, int StageIndex, double LoadFactor, bool Converged, bool IsRefinement)> ParseStepStatus(string path)
    {
        if (!File.Exists(path))
            throw new OpenSeesResultException("MissingFile", $"Файл step_status не найден: {path}");
        var rows = new List<(int, int, double, bool, bool)>();
        int lineNo = 0;
        foreach (var raw in File.ReadAllLines(path))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 5)
                throw new OpenSeesResultException("WrongColumnCount", $"step_status строка {lineNo}: ожидалось 5 колонок, получено {parts.Length}.");
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var step) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var stage) ||
                !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var lf) ||
                !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var convergedFlag) ||
                !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var refinementFlag) ||
                (convergedFlag is not (0 or 1)) || (refinementFlag is not (0 or 1)))
                throw new OpenSeesResultException("InvalidNumber", $"step_status строка {lineNo}: не удалось разобрать значения.");
            rows.Add((step, stage, lf, convergedFlag != 0, refinementFlag != 0));
        }
        return rows;
    }

    private static List<double[]> ParseMatrix(string path, int expectedCols, string name)
    {
        // native recorder -closeOnWrite создаёт файл только на первой успешной записи —
        // если ни один шаг не сошёлся, файла может не быть вовсе (не ошибка, см.
        // FemNonlinearResultParser.ParseMatrix — тот же принцип).
        if (!File.Exists(path)) return [];
        var lines = File.ReadAllLines(path)
            .Select(raw => raw.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();
        var rows = new List<double[]>();
        for (int lineNo = 0; lineNo < lines.Count; lineNo++)
        {
            var line = lines[lineNo];
            if (line.Contains('\0'))
            {
                // Известная нестабильность OpenSees 3.8.0 на Windows при интенсивных
                // eleResponse-запросах (см. FemNonlinearResultParser) — подставляем NaN вместо
                // провала всего файла, сохраняя число строк.
                rows.Add(Enumerable.Repeat(double.NaN, expectedCols).ToArray());
                continue;
            }
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            bool malformed = parts.Length != expectedCols;
            var values = new double[expectedCols];
            if (!malformed)
                for (int i = 0; i < expectedCols; i++)
                    if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || !double.IsFinite(values[i]))
                    { malformed = true; break; }
            if (malformed)
            {
                if (lineNo == lines.Count - 1) break;
                throw new OpenSeesResultException("WrongColumnCount", $"{name} строка {lineNo + 1}: не удалось разобрать значения.");
            }
            rows.Add(values);
        }
        return rows;
    }

    private static List<double[]> ParseVariableElementRows(
        string path, int[] elementTags, IReadOnlyDictionary<int, NormalizedShellElement> elements)
    {
        int expected = 1 + elementTags.Sum(tag => elements.TryGetValue(tag, out var el) ? el.NodeTags.Count * 6 : 0);
        return ParseMatrix(path, expected, "shell_element_forces");
    }

    private static IReadOnlyList<ShellNodeDisplacement> ToNodeDisplacements(double[] row, int[] nodeTags)
    {
        var list = new List<ShellNodeDisplacement>(nodeTags.Length);
        for (int k = 0; k < nodeTags.Length; k++)
        {
            int off = 1 + k * 6;
            list.Add(new ShellNodeDisplacement(nodeTags[k], row[off], row[off + 1], row[off + 2], row[off + 3], row[off + 4], row[off + 5]));
        }
        return list;
    }

    private static IReadOnlyList<ShellNodeReaction> ToNodeReactions(double[] row, int[] nodeTags)
    {
        var list = new List<ShellNodeReaction>(nodeTags.Length);
        for (int k = 0; k < nodeTags.Length; k++)
        {
            int off = 1 + k * 6;
            list.Add(new ShellNodeReaction(nodeTags[k], row[off], row[off + 1], row[off + 2], row[off + 3], row[off + 4], row[off + 5]));
        }
        return list;
    }

    private static IReadOnlyList<ShellElementNodalForces> ToElementForces(
        double[] row, int[] elementTags, IReadOnlyDictionary<int, NormalizedShellElement> elements)
    {
        var list = new List<ShellElementNodalForces>(elementTags.Length);
        int offset = 1;
        foreach (int tag in elementTags)
        {
            NormalizedShellElement element = elements[tag];
            int width = element.NodeTags.Count * 6;
            list.Add(new ShellElementNodalForces(tag, element.Kind, row[offset..(offset + width)]));
            offset += width;
        }
        return list;
    }

    private static IReadOnlyList<FemElementEndForces> ToBeamElementForces(double[] row, int[] elementTags)
    {
        var list = new List<FemElementEndForces>(elementTags.Length);
        for (int k = 0; k < elementTags.Length; k++)
        {
            int off = 1 + k * 12;
            list.Add(new FemElementEndForces(elementTags[k],
                row[off], row[off + 1], row[off + 2], row[off + 3], row[off + 4], row[off + 5],
                row[off + 6], row[off + 7], row[off + 8], row[off + 9], row[off + 10], row[off + 11]));
        }
        return list;
    }

    private static IReadOnlyList<ShellSectionResultants> ToSectionResultants(double[] row, int[] elementTags, int point)
    {
        var list = new List<ShellSectionResultants>(elementTags.Length);
        for (int k = 0; k < elementTags.Length; k++)
        {
            int off = 1 + k * 8;
            list.Add(new ShellSectionResultants(elementTags[k], point,
                row[off], row[off + 1], row[off + 2], row[off + 3], row[off + 4], row[off + 5], row[off + 6], row[off + 7]));
        }
        return list;
    }
}
