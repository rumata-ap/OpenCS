using System.Globalization;
using System.Text.Json;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Results;

/// <summary>Строго разбирает состояния fiber-секций и положения точек интегрирования.</summary>
public sealed class FemNonlinearFiberStateParser
{
    /// <summary>Читает строки step/load-factor/element/IP/fiber/stress/strain.</summary>
    public IReadOnlyList<FemNonlinearFiberState> Parse(string path) => ParseCore(path, null);

    /// <summary>Читает только состояния заданной точки интегрирования, не загружая весь файл в память.</summary>
    public IReadOnlyList<FemNonlinearFiberState> ParseSection(string path, int elementTag, int integrationPoint)
    {
        if (elementTag <= 0) throw new ArgumentOutOfRangeException(nameof(elementTag));
        if (integrationPoint <= 0) throw new ArgumentOutOfRangeException(nameof(integrationPoint));
        return ParseCore(path, (element, ip) => element == elementTag && ip == integrationPoint);
    }

    IReadOnlyList<FemNonlinearFiberState> ParseCore(string path, Func<int, int, bool>? filter)
    {
        if (!File.Exists(path))
            throw new OpenSeesResultException("MissingFile", $"Файл состояний фибр не найден: {path}");

        var result = new List<FemNonlinearFiberState>();
        var keys = new HashSet<(int Step, int Element, int Ip, int Fiber)>();
        int lineNo = 0;
        foreach (var raw in File.ReadLines(path))
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.Contains('\0'))
                throw new OpenSeesResultException("CorruptedOutput",
                    $"fiber states строка {lineNo}: найден нулевой байт; файл результата повреждён OpenSees.");
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 7)
                throw new OpenSeesResultException("WrongColumnCount", $"fiber states строка {lineNo}: ожидалось 7 колонок, получено {parts.Length}.");
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int step) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double loadFactor) ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int element) ||
                !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ip) ||
                !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int fiber) ||
                !double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double stress) ||
                !double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out double strain) ||
                !double.IsFinite(loadFactor) || !double.IsFinite(stress) || !double.IsFinite(strain) ||
                step <= 0 || element <= 0 || ip <= 0 || fiber < 0)
                throw new OpenSeesResultException("InvalidNumber", $"fiber states строка {lineNo}: некорректное число.");
            if (filter is not null && !filter(element, ip)) continue;
            if (!keys.Add((step, element, ip, fiber)))
                throw new OpenSeesResultException("DuplicateFiberState", $"fiber states строка {lineNo}: повтор состояния.");
            result.Add(new FemNonlinearFiberState(step, loadFactor, element, ip, fiber, stress, strain));
        }
        return result;
    }

    /// <summary>Читает JSON с фактическими положениями точек интегрирования.</summary>
    public IReadOnlyList<FemNonlinearSectionLocation> ParseLocations(string path)
    {
        if (!File.Exists(path))
            throw new OpenSeesResultException("MissingFile", $"Файл порядка сечений не найден: {path}");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("locations", out var locations) ||
                locations.ValueKind != JsonValueKind.Array)
                throw new OpenSeesResultException("InvalidSectionOrder", "В файле порядка отсутствует массив locations.");

            var result = new List<FemNonlinearSectionLocation>();
            var keys = new HashSet<(int Element, int Ip)>();
            foreach (var item in locations.EnumerateArray())
            {
                int element = item.GetProperty("elementTag").GetInt32();
                int ip = item.GetProperty("integrationPoint").GetInt32();
                int section = item.GetProperty("sectionTag").GetInt32();
                int? fiberCount = item.TryGetProperty("fiberCount", out var fc) ? fc.GetInt32() : null;
                double distance = item.GetProperty("distanceFromElementStartM").GetDouble();
                double length = item.GetProperty("elementLengthM").GetDouble();
                double relative = item.GetProperty("relativePosition").GetDouble();
                if (element <= 0 || ip <= 0 || section <= 0 || fiberCount is <= 0 ||
                    !double.IsFinite(distance) || !double.IsFinite(length) || !double.IsFinite(relative) ||
                    length <= 0 || relative < -1e-9 || relative > 1 + 1e-9 || !keys.Add((element, ip)))
                    throw new OpenSeesResultException("InvalidSectionOrder", "Некорректная или повторная точка интегрирования.");
                result.Add(new FemNonlinearSectionLocation(element, ip, section, null, distance, length, relative));
            }
            return result;
        }
        catch (OpenSeesResultException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new OpenSeesResultException("InvalidSectionOrder", $"Файл порядка сечений повреждён: {ex.Message}");
        }
    }
}
