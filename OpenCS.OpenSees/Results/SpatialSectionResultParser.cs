using System.Globalization;
using OpenCS.OpenSees.Analysis;

namespace OpenCS.OpenSees.Results;

/// <summary>
/// Разбирает файлы результатов пространственного расчёта сечения.
/// </summary>
public sealed class SpatialSectionResultParser
{
    /// <summary>Ожидаемое число столбцов в строке истории.</summary>
    public const int ColumnCount = 10;

    /// <summary>
    /// Читает историю и проверяет наличие маркерного файла успешно завершённого расчёта.
    /// </summary>
    /// <param name="historyPath">Путь к файлу истории.</param>
    /// <param name="markerPath">Путь к маркерному файлу.</param>
    /// <returns>Строки истории в порядке их появления в файле.</returns>
    public IReadOnlyList<SpatialSectionHistoryRow> Parse(string historyPath, string markerPath)
    {
        if (!File.Exists(historyPath))
        {
            throw new OpenSeesResultException(
                "MissingHistory",
                $"Не найден файл истории пространственного расчёта: {historyPath}");
        }

        if (!File.Exists(markerPath) || new FileInfo(markerPath).Length == 0)
        {
            throw new OpenSeesResultException(
                "MissingMarker",
                $"Не найден маркер завершения пространственного расчёта: {markerPath}");
        }

        var rows = new List<SpatialSectionHistoryRow>();
        var lineNumber = 0;

        foreach (var rawLine in File.ReadLines(historyPath))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length != ColumnCount)
            {
                throw Error(
                    "WrongColumnCount",
                    lineNumber,
                    $"ожидалось {ColumnCount}, получено {columns.Length}");
            }

            var step = ParseInt(columns[0], "step", lineNumber);
            var loadFactor = ParseFiniteDouble(columns[1], "loadFactor", lineNumber);
            var axialForceN = ParseFiniteDouble(columns[2], "axialForceN", lineNumber);
            var openSeesMzNm = ParseFiniteDouble(columns[3], "openSeesMzNm", lineNumber);
            var openSeesMyNm = ParseFiniteDouble(columns[4], "openSeesMyNm", lineNumber);
            var rotationY = ParseFiniteDouble(columns[5], "rotationY", lineNumber);
            var rotationZ = ParseFiniteDouble(columns[6], "rotationZ", lineNumber);
            var curvatureMagnitude = ParseFiniteDouble(columns[7], "curvatureMagnitude", lineNumber);
            var converged = ParseBoolean(columns[8], "converged", lineNumber);
            var residual = ParseFiniteDouble(columns[9], "residual", lineNumber);

            rows.Add(new SpatialSectionHistoryRow
            {
                Step = step,
                LoadFactor = loadFactor,
                AxialForceN = axialForceN,
                MomentMxNm = openSeesMzNm,
                MomentMyNm = openSeesMyNm,
                CurvatureMx = rotationZ,
                CurvatureMy = rotationY,
                CurvatureMagnitude = curvatureMagnitude,
                Converged = converged,
                Residual = residual,
                OpenSeesMzNm = openSeesMzNm,
                OpenSeesMyNm = openSeesMyNm,
                OpenSeesRotationY = rotationY,
                OpenSeesRotationZ = rotationZ
            });
        }

        if (rows.Count == 0)
        {
            throw new OpenSeesResultException(
                "Empty",
                $"Файл истории пространственного расчёта не содержит строк результатов: {historyPath}");
        }

        return rows;
    }

    private static int ParseInt(string value, string column, int lineNumber)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            return result;

        throw Error("InvalidNumber", lineNumber, $"столбец {column} содержит '{value}' вместо целого числа");
    }

    private static double ParseFiniteDouble(string value, string column, int lineNumber)
    {
        if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out var result) && double.IsFinite(result))
            return result;

        throw Error("InvalidNumber", lineNumber, $"столбец {column} содержит '{value}' вместо конечного числа");
    }

    private static bool ParseBoolean(string value, string column, int lineNumber)
    {
        if (value is "1" or "true" or "True" or "TRUE")
            return true;
        if (value is "0" or "false" or "False" or "FALSE")
            return false;

        throw Error("InvalidBoolean", lineNumber, $"столбец {column} содержит '{value}'");
    }

    private static OpenSeesResultException Error(string code, int lineNumber, string details)
    {
        return new OpenSeesResultException(code, $"Ошибка в строке {lineNumber}: {details}.");
    }
}
