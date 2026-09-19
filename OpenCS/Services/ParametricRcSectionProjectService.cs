using System.Text.Json;
using CScore;
using CScore.ParametricRc;
using OpenCS.Models;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Статус чтения параметрического источника без подмены неизвестного JSON.</summary>
public enum ParametricRcDefinitionLoadStatus { Supported, UnsupportedFutureVersion, InvalidJson, Missing }

/// <summary>Состояние параметрического источника относительно фактической геометрии.</summary>
public sealed record ParametricRcSectionState(ParametricRcDefinitionLoadStatus LoadStatus,
    bool IsStale, ParametricRcSectionRecord? Record);

/// <summary>Сопоставляет source-запись параметрического сечения с его сформированной моделью.</summary>
public sealed class ParametricRcSectionProjectService(DatabaseService database)
{
    const int DefinitionVersion = 1;

    /// <summary>Возвращает статус source-записи и не перезаписывает неизвестные версии.</summary>
    public ParametricRcSectionState GetState(CrossSection section)
    {
        var record = database.TryGetParametricRcSectionRecord(section.Id);
        if (record is null) return new(ParametricRcDefinitionLoadStatus.Missing, false, null);
        if (record.DefinitionVersion != DefinitionVersion)
            return new(ParametricRcDefinitionLoadStatus.UnsupportedFutureVersion, false, record);
        try { _ = JsonSerializer.Deserialize<ParametricRcSectionDefinition>(record.DefinitionJson); }
        catch (JsonException) { return new(ParametricRcDefinitionLoadStatus.InvalidJson, false, record); }
        string actual = ParametricRcSectionFingerprint.Compute(section, record.GeneratorVersion);
        return new(ParametricRcDefinitionLoadStatus.Supported, !string.Equals(actual, record.GeneratedFingerprint, StringComparison.Ordinal), record);
    }
}
