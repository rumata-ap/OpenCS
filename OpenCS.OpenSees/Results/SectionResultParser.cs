using System.Globalization;
using OpenCS.OpenSees.Analysis;

namespace OpenCS.OpenSees.Results;

/// <summary>Строго разбирает recorder-файл истории section_history.out.</summary>
public sealed class SectionResultParser
{
    /// <summary>Схема файла: восемь колонок после необязательных комментариев.</summary>
    public const int ColumnCount = 8;

    /// <summary>Читает историю и требует непустой completion marker.</summary>
    public IReadOnlyList<SectionHistoryRow> Parse(
        string historyPath,
        string markerPath,
        SectionBendingAxis axis = SectionBendingAxis.Mx)
    {
        if (!File.Exists(historyPath))
            throw new OpenSeesResultException("MissingHistory", $"Файл истории не найден: {historyPath}");
        if (!File.Exists(markerPath) || new FileInfo(markerPath).Length == 0)
            throw new OpenSeesResultException("MissingMarker", $"Файл завершения не найден или пуст: {markerPath}");

        string[] lines = File.ReadAllLines(historyPath);
        List<SectionHistoryRow> rows = [];
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            string[] columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length != ColumnCount)
            {
                throw Error(
                    "WrongColumnCount",
                    lineIndex + 1,
                    $"Ожидалось {ColumnCount} колонок, получено {columns.Length}: {line}");
            }

            if (!int.TryParse(columns[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int step))
                throw Error("InvalidNumber", lineIndex + 1, "Некорректный step.");

            double loadFactor = ParseFinite(columns[1], "loadFactor", lineIndex + 1);
            double axialForce = ParseFinite(columns[2], "axialForceN", lineIndex + 1);
            double bendingMoment = ParseFinite(columns[3], "bendingMomentNm", lineIndex + 1);
            double axialStrain = ParseFinite(columns[4], "axialStrain", lineIndex + 1);
            double curvature = ParseFinite(columns[5], "curvature", lineIndex + 1);
            bool converged = ParseBoolean(columns[6], lineIndex + 1);
            double residual = ParseFinite(columns[7], "residual", lineIndex + 1);

            rows.Add(new SectionHistoryRow
            {
                Step = step,
                LoadFactor = loadFactor,
                AxialForceN = axialForce,
                BendingMomentNm = bendingMoment,
                AxialStrain = axialStrain,
                Curvature = curvature,
                Converged = converged,
                Residual = residual,
                Axis = axis,
                OpenSeesBendingMomentNm = bendingMoment,
                OpenSeesCurvature = curvature
            });
        }

        if (rows.Count == 0)
            throw new OpenSeesResultException("Empty", "Файл истории не содержит строк результата.");

        return rows;
    }

    private static double ParseFinite(string value, string name, int line)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
            !double.IsFinite(parsed))
            throw Error("InvalidNumber", line, $"Некорректное поле {name}: {value}.");

        return parsed;
    }

    private static bool ParseBoolean(string value, int line) => value switch
    {
        "1" or "true" or "True" or "TRUE" => true,
        "0" or "false" or "False" or "FALSE" => false,
        _ => throw Error("InvalidBoolean", line, $"Некорректный признак converged: {value}.")
    };

    private static OpenSeesResultException Error(string code, int line, string message) =>
        new(code, $"Строка {line}: {message}");
}
