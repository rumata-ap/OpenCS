using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;

namespace OpenCS.OpenSees.Results;

/// <summary>Разбирает catalog и выборочно материализует material states shell/beam.</summary>
public sealed class ShellStateParser
{
    private sealed record StepStatus(int StepIndex, int StageIndex, double LoadFactor, bool Converged);

    private sealed record CatalogDto(
        int Version,
        List<LayerGroupDto>? ShellLayerGroups,
        List<BeamLocationDto>? BeamFiberLocations,
        List<string>? OptionalResponses);

    private sealed record LayerGroupDto(
        int IntegrationPoint,
        int LayerIndex,
        string? ResponseKind,
        List<int>? ElementTags,
        string? FileName,
        int ComponentCount,
        int? SectionTag,
        int? MaterialTag,
        ShellLayerKind? LayerKind,
        string? SourceId,
        double? CenterZ,
        double? Thickness,
        string? SectionFingerprint,
        string? Unit);

    private sealed record BeamLocationDto(
        int ElementTag,
        int IntegrationPoint,
        int FiberIndex,
        int SectionTag,
        double Y,
        double Z,
        int MaterialTag);

    /// <summary>Читает и проверяет state_order.json из каталога артефакта.</summary>
    public ShellStateCatalog ParseCatalog(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string path = Path.Combine(directory, "state_order.json");
        if (!File.Exists(path))
            throw new OpenSeesResultException("MissingFile", $"Файл material-state catalog не найден: {path}");

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new JsonStringEnumConverter());
            CatalogDto dto = JsonSerializer.Deserialize<CatalogDto>(File.ReadAllText(path), options)
                ?? throw new OpenSeesResultException("InvalidStateOrder", "material-state catalog пуст.");

            bool isV2 = dto.Version >= 2;
            if (dto.Version is not (1 or 2))
                throw new OpenSeesResultException("InvalidStateOrder", $"Неподдерживаемая версия material-state catalog: {dto.Version}.");

            var groups = (dto.ShellLayerGroups ?? []).Select(group =>
            {
                if (group.IntegrationPoint <= 0 || group.LayerIndex <= 0 ||
                    string.IsNullOrWhiteSpace(group.ResponseKind) ||
                    string.IsNullOrWhiteSpace(group.FileName) || group.ElementTags is null ||
                    group.ElementTags.Count == 0 || group.ElementTags.Any(tag => tag <= 0) ||
                    group.ElementTags.Distinct().Count() != group.ElementTags.Count ||
                    (!isV2 && (group.ComponentCount != 5 || group.ResponseKind is not ("stress" or "strain"))))
                    throw new OpenSeesResultException("InvalidStateOrder", "Некорректная shell material-state recorder group.");
                if (isV2)
                {
                    if (group.SectionTag is not (> 0))
                        throw new OpenSeesResultException("InvalidStateOrder", "v2 group: отсутствует sectionTag.");
                    if (group.MaterialTag is not (> 0))
                        throw new OpenSeesResultException("InvalidStateOrder", "v2 group: отсутствует materialTag.");
                    if (group.LayerKind is null)
                        throw new OpenSeesResultException("InvalidStateOrder", "v2 group: отсутствует layerKind.");
                    if (string.IsNullOrWhiteSpace(group.SourceId))
                        throw new OpenSeesResultException("InvalidStateOrder", "v2 group: отсутствует sourceId.");
                    if (group.CenterZ is not double centerZ || !double.IsFinite(centerZ))
                        throw new OpenSeesResultException("InvalidStateOrder", "v2 group: некорректный centerZ.");
                    if (group.Thickness is not double thickness || !double.IsFinite(thickness) || thickness <= 0)
                        throw new OpenSeesResultException("InvalidStateOrder", "v2 group: некорректная толщина.");
                    if (string.IsNullOrWhiteSpace(group.SectionFingerprint))
                        throw new OpenSeesResultException("InvalidStateOrder", "v2 group: отсутствует sectionFingerprint.");
                    if (string.IsNullOrWhiteSpace(group.Unit))
                        throw new OpenSeesResultException("InvalidStateOrder", "v2 group: отсутствует unit.");
                    if (group.ComponentCount <= 0)
                        throw new OpenSeesResultException("InvalidStateOrder", "v2 group: некорректный componentCount.");
                }
                EnsureSafePath(directory, group.FileName);
                return new ShellLayerStateGroup(
                    group.IntegrationPoint, group.LayerIndex, group.ResponseKind!,
                    group.ElementTags, group.FileName, group.ComponentCount)
                {
                    SectionTag = group.SectionTag,
                    MaterialTag = group.MaterialTag,
                    LayerKind = group.LayerKind,
                    SourceId = group.SourceId,
                    CenterZ = group.CenterZ,
                    Thickness = group.Thickness,
                    SectionFingerprint = group.SectionFingerprint,
                    Unit = group.Unit
                };
            }).ToArray();

            var duplicateGroups = groups
                .GroupBy(group => (group.SectionTag ?? 0, group.IntegrationPoint, group.LayerIndex, group.ResponseKind))
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateGroups is not null)
                throw new OpenSeesResultException("DuplicateStateGroup", "В material-state catalog повторяется shell recorder group.");

            var locations = (dto.BeamFiberLocations ?? []).Select(location =>
            {
                if (location.ElementTag <= 0 || location.IntegrationPoint <= 0 || location.FiberIndex < 0 ||
                    location.SectionTag <= 0 || location.MaterialTag <= 0 ||
                    !double.IsFinite(location.Y) || !double.IsFinite(location.Z))
                    throw new OpenSeesResultException("InvalidStateOrder", "Некорректное положение beam fiber в catalog.");
                return new ShellBeamFiberLocation(
                    location.ElementTag, location.IntegrationPoint, location.FiberIndex,
                    location.SectionTag, location.Y, location.Z, location.MaterialTag);
            }).ToArray();

            if (locations.GroupBy(location =>
                    (location.ElementTag, location.IntegrationPoint, location.FiberIndex)).Any(group => group.Count() > 1))
                throw new OpenSeesResultException("DuplicateFiberLocation", "В material-state catalog повторяется beam fiber location.");

            return new ShellStateCatalog(
                dto.Version,
                groups,
                locations,
                (dto.OptionalResponses ?? []).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToArray());
        }
        catch (OpenSeesResultException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new OpenSeesResultException("InvalidStateOrder", $"material-state catalog повреждён: {ex.Message}");
        }
    }

    /// <summary>Читает shell stress/strain выбранного элемента, IP, слоя и шага.</summary>
    public IReadOnlyList<RCShellLayerState> ParseShellLayers(
        string directory,
        ShellStateCatalog catalog,
        int elementTag,
        int integrationPoint,
        int layerIndex,
        int stepIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(catalog);
        if (elementTag <= 0) throw new ArgumentOutOfRangeException(nameof(elementTag));
        if (integrationPoint <= 0) throw new ArgumentOutOfRangeException(nameof(integrationPoint));
        if (layerIndex <= 0) throw new ArgumentOutOfRangeException(nameof(layerIndex));
        if (stepIndex <= 0) throw new ArgumentOutOfRangeException(nameof(stepIndex));

        StepStatus? targetStep = FindSuccessfulStep(directory, stepIndex, out int rowIndex);
        if (targetStep is null) return [];

        ShellLayerStateGroup stressGroup = FindLayerGroup(catalog, integrationPoint, layerIndex, "stress");
        ShellLayerStateGroup strainGroup = FindLayerGroup(catalog, integrationPoint, layerIndex, "strain");
        int stressElementIndex = FindElementIndex(stressGroup, elementTag);
        int strainElementIndex = FindElementIndex(strainGroup, elementTag);
        double[] stressRow = ParseMatrixRow(directory, stressGroup, rowIndex);
        double[] strainRow = ParseMatrixRow(directory, strainGroup, rowIndex);

        if (stressGroup.SectionTag is null || stressGroup.MaterialTag is not (> 0) ||
            stressGroup.LayerKind is null || string.IsNullOrWhiteSpace(stressGroup.SourceId))
            throw new OpenSeesResultException("state_catalog_provenance_missing",
                "Material-state catalog не содержит provenance (v1 legacy); строгий разбор состояния невозможен.");

        return
        [
            new RCShellLayerState(
                new RCShellMaterialStateKey(
                    targetStep.StepIndex, targetStep.StageIndex, targetStep.LoadFactor,
                    elementTag, integrationPoint, layerIndex,
                    ShellMaterialStateLocationKind.ShellLayer),
                stressGroup.MaterialTag!.Value,
                stressGroup.LayerKind!.Value,
                stressRow[(1 + stressElementIndex * 5)..(1 + (stressElementIndex + 1) * 5)],
                strainRow[(1 + strainElementIndex * 5)..(1 + (strainElementIndex + 1) * 5)],
                CatalogGroup: stressGroup)
        ];
    }

    /// <summary>Читает beam fiber states выбранного элемента/IP/fiber и шага.</summary>
    public IReadOnlyList<RCShellBeamFiberState> ParseBeamFibers(
        string directory,
        ShellStateCatalog catalog,
        int elementTag,
        int integrationPoint,
        int fiberIndex,
        int stepIndex)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (elementTag <= 0) throw new ArgumentOutOfRangeException(nameof(elementTag));
        if (integrationPoint <= 0) throw new ArgumentOutOfRangeException(nameof(integrationPoint));
        if (fiberIndex < 0) throw new ArgumentOutOfRangeException(nameof(fiberIndex));
        if (stepIndex <= 0) throw new ArgumentOutOfRangeException(nameof(stepIndex));

        StepStatus? targetStep = FindSuccessfulStep(directory, stepIndex, out _);
        if (targetStep is null) return [];

        var location = catalog.BeamFiberLocations.SingleOrDefault(item =>
            item.ElementTag == elementTag && item.IntegrationPoint == integrationPoint && item.FiberIndex == fiberIndex);
        if (location is null)
            throw new OpenSeesResultException("UnknownFiberLocation", "Запрошенное beam fiber location отсутствует в catalog.");

        string path = Path.Combine(directory, "shell_beam_fiber_states.out");
        if (!File.Exists(path)) return [];
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 8)
                throw new OpenSeesResultException("WrongColumnCount", "shell beam fiber state: ожидалось 8 колонок.");
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int step) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int stage) ||
                !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double loadFactor) ||
                !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int element) ||
                !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ip) ||
                !int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int fiber) ||
                !double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out double stress) ||
                !double.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out double strain) ||
                !double.IsFinite(loadFactor) || !double.IsFinite(stress) || !double.IsFinite(strain))
                throw new OpenSeesResultException("InvalidNumber", "shell beam fiber state содержит некорректное число.");
            if (step == stepIndex && element == elementTag && ip == integrationPoint && fiber == fiberIndex)
            {
                var key = new RCShellMaterialStateKey(
                    step, stage, loadFactor, element, ip, fiber,
                    ShellMaterialStateLocationKind.BeamFiber);
                return [new RCShellBeamFiberState(key, stress, strain)];
            }
        }

        return [];
    }

    private static ShellLayerStateGroup FindLayerGroup(
        ShellStateCatalog catalog, int integrationPoint, int layerIndex, string response)
    {
        return catalog.ShellLayerGroups.SingleOrDefault(group =>
                   group.IntegrationPoint == integrationPoint &&
                   group.LayerIndex == layerIndex &&
                   string.Equals(group.ResponseKind, response, StringComparison.OrdinalIgnoreCase))
               ?? throw new OpenSeesResultException("MissingStateGroup", $"Не найдена shell recorder group {integrationPoint}/{layerIndex}/{response}.");
    }

    private static int FindElementIndex(ShellLayerStateGroup group, int elementTag)
    {
        int index = Array.IndexOf(group.ElementTags.ToArray(), elementTag);
        if (index < 0)
            throw new OpenSeesResultException("UnknownElement", $"Элемент {elementTag} отсутствует в shell recorder group.");
        return index;
    }

    private static double[] ParseMatrixRow(string directory, ShellLayerStateGroup group, int rowIndex)
    {
        string path = Path.Combine(directory, group.FileName);
        if (!File.Exists(path))
            throw new OpenSeesResultException("MissingFile", $"Файл material-state не найден: {path}");

        int expectedColumns = 1 + group.ElementTags.Count * group.ComponentCount;
        var rows = new List<double[]>();
        string[] lines = File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string[] parts = lines[lineIndex].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != expectedColumns)
            {
                if (lineIndex == lines.Length - 1) break;
                throw new OpenSeesResultException("WrongColumnCount", $"{group.FileName}: ожидалось {expectedColumns} колонок.");
            }

            var values = new double[expectedColumns];
            for (int i = 0; i < values.Length; i++)
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) ||
                    !double.IsFinite(values[i]))
                {
                    if (lineIndex == lines.Length - 1) { values = []; break; }
                    throw new OpenSeesResultException("InvalidNumber", $"{group.FileName}: не удалось разобрать число.");
                }
            if (values.Length > 0) rows.Add(values);
        }

        if (rowIndex >= rows.Count)
            throw new OpenSeesResultException("MissingStateRow", $"В файле {group.FileName} отсутствует строка шага {rowIndex + 1}.");
        return rows[rowIndex];
    }

    private static StepStatus? FindSuccessfulStep(string directory, int stepIndex, out int rowIndex)
    {
        rowIndex = 0;
        string path = Path.Combine(directory, "step_status.out");
        if (!File.Exists(path))
            throw new OpenSeesResultException("MissingFile", $"Файл step_status не найден: {path}");

        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 5 ||
                !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int step) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int stage) ||
                !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double loadFactor) ||
                !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int converged) ||
                !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
                converged is not (0 or 1) || !double.IsFinite(loadFactor))
                throw new OpenSeesResultException("InvalidNumber", "step_status содержит некорректную строку.");

            if (converged == 1)
            {
                if (step == stepIndex)
                    return new StepStatus(step, stage, loadFactor, true);
                rowIndex++;
            }
        }

        rowIndex = 0;
        return null;
    }

    private static void EnsureSafePath(string directory, string fileName)
    {
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(directory, fileName));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new OpenSeesResultException("InvalidStateOrder", $"Путь material-state выходит за пределы каталога: {fileName}");
    }
}
