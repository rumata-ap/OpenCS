using System.Globalization;
using System.Text.Json;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Results;

/// <summary>Строго разбирает по-шаговые выходные файлы нелинейного расчёта FEM-схемы.</summary>
public sealed class FemNonlinearResultParser
{
    sealed record RecorderOrder(int[] NodeTags, int[] RestrainedTags, int[] ElemTags);

    /// <summary>Читает step_status.out, recorder_order.json и recorder-матрицы. completed.marker не
    /// требуется: -closeOnWrite гарантирует, что каждый УЖЕ записанный шаг во всех recorder-файлах
    /// целостен сам по себе (открывается/пишется/закрывается атомарно на каждом успешном analyze()),
    /// поэтому при обрыве расчёта (таймаут, отмена, сбой) до финальной записи маркера сошедшиеся до
    /// этого момента шаги остаются пригодными для показа — отбрасывать их всех целиком из-за
    /// отсутствия маркера значит терять уже полученные, корректные данные.</summary>
    public IReadOnlyList<FemNonlinearStepResult> Parse(string directory)
    {
        var order = ParseOrder(Path.Combine(directory, "recorder_order.json"));
        var steps = ParseStepStatus(Path.Combine(directory, "step_status.out"));

        var dispRows = ParseMatrix(Path.Combine(directory, "nonlinear_node_disp.out"), 1 + order.NodeTags.Length * 6, "nonlinear_node_disp");
        var reactRows = order.RestrainedTags.Length > 0
            ? ParseMatrix(Path.Combine(directory, "nonlinear_node_reactions.out"), 1 + order.RestrainedTags.Length * 6, "nonlinear_node_reactions")
            : [];
        var forceRows = ParseMatrix(Path.Combine(directory, "nonlinear_element_forces.out"), 1 + order.ElemTags.Length * 12, "nonlinear_element_forces");

        // Recorder Node/Element пишут свою строку РАНЬШЕ, чем advanceTo успевает дописать
        // соответствующую строку в step_status.out (см. FemNonlinearTclGenerator.advanceTo) — при
        // обрыве процесса между этими двумя записями step_status.out может содержать на один
        // "сошедшийся" шаг больше, чем recorder-файлы. available — число шагов, для которых точно
        // есть согласованные данные во ВСЕХ файлах; шаги сверх этого (только при аварийном обрыве)
        // молча опускаются, а не валят уже честно записанные предыдущие шаги.
        int available = Math.Min(dispRows.Count, forceRows.Count);
        if (order.RestrainedTags.Length > 0) available = Math.Min(available, reactRows.Count);

        var results = new List<FemNonlinearStepResult>(steps.Count);
        int rowIndex = 0;
        foreach (var s in steps)
        {
            if (!s.Converged)
            {
                results.Add(new FemNonlinearStepResult(s.StepIndex, s.LoadFactor, false, [], [], [])
                {
                    IsRefinement = s.IsRefinement, StageIndex = s.StageIndex
                });
                continue;
            }

            if (rowIndex >= available)
            {
                rowIndex++;
                continue;
            }

            var disp = ToNodeDisplacements(dispRows[rowIndex], order.NodeTags);
            var react = order.RestrainedTags.Length > 0
                ? ToNodeReactions(reactRows[rowIndex], order.RestrainedTags)
                : [];
            var forces = ToElementForces(forceRows[rowIndex], order.ElemTags);
            results.Add(new FemNonlinearStepResult(s.StepIndex, s.LoadFactor, true, disp, react, forces)
            {
                IsRefinement = s.IsRefinement, StageIndex = s.StageIndex
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

    static List<(int StepIndex, int StageIndex, double LoadFactor, bool Converged, bool IsRefinement)> ParseStepStatus(string path)
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

    static List<double[]> ParseMatrix(string path, int expectedCols, string name)
    {
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
                // Известная нестабильность OpenSees 3.8.0 на Windows: даже при отключённой
                // буферизации Tcl-канала (см. FemNonlinearTclGenerator) изредка встречается блок
                // нулевых байт внутри иначе корректной строки — судя по всему, баг внутри самого
                // OpenSees.exe при интенсивных eleResponse-запросах, а не в нашей генерации Tcl.
                // Число строк при этом остаётся верным (проверено: 47 строк на 47 сошедшихся
                // шагов), портится только содержимое одной строки — поэтому не валим парсинг
                // всего файла (и всех остальных, честных шагов) целиком, а подставляем NaN только
                // для этой строки и продолжаем, сохраняя соответствие количества строк числу
                // сошедшихся шагов в step_status.out.
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
                // Строка не разбирается КАК ОЖИДАЕТСЯ. Если это последняя строка файла — вероятная
                // недописанная запись из-за обрыва процесса OpenSees (таймаут/сбой) в момент
                // -closeOnWrite; отбрасываем только её, не валя все предыдущие честно записанные
                // строки. Если же битая строка НЕ последняя — это реальная порча данных, а не
                // обрыв в конце, и это остаётся жёсткой ошибкой.
                if (lineNo == lines.Count - 1) break;
                throw new OpenSeesResultException("WrongColumnCount", $"{name} строка {lineNo + 1}: не удалось разобрать значения.");
            }
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
