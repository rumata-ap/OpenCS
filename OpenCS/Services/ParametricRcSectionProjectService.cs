using System.Text.Json;
using System.Text.Json.Serialization;
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
    public const int DefinitionVersion = ParametricRcSectionDefinition.CurrentVersion;
    public const int GeneratorVersion = 1;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Формирует и атомарно сохраняет параметрическое сечение.</summary>
    public ParametricRcGenerationResult GenerateAndSave(
        CrossSection section, ParametricRcSectionDefinition definition)
    {
        var result = ParametricRcSectionGenerator.Generate(definition);
        if (result.Diagnostics.Count != 0)
            return result;

        section.Tag = definition.Tag;
        section.Areas = result.Section.Areas;
        var json = JsonSerializer.Serialize(definition, JsonOptions);
        var record = new ParametricRcSectionRecord(
            section.Id, DefinitionVersion, GeneratorVersion, json,
            ParametricRcSectionFingerprint.Compute(section, GeneratorVersion));
        database.SaveParametricCrossSection(section, record, result.GeneratedAreas);
        return result with { Section = section };
    }

    /// <summary>Удаляет только source и mapping, оставляя сечение и области.</summary>
    public void Detach(CrossSection section)
    {
        database.DetachParametricRcSection(section.Id);
    }

    /// <summary>Восстанавливает materialized section из поддержанного JSON source.</summary>
    public ParametricRcSectionState Restore(CrossSection section)
    {
        var state = GetState(section);
        if (!TryGetDefinition(section, out var definition)) return state;
        GenerateAndSave(section, definition);
        return GetState(section);
    }

    /// <summary>Безопасно читает поддержанное определение для команды редактирования.</summary>
    public bool TryGetDefinition(CrossSection section, out ParametricRcSectionDefinition definition)
    {
        definition = null!;
        var state = GetState(section);
        if (state.LoadStatus != ParametricRcDefinitionLoadStatus.Supported || state.Record is null)
            return false;
        try
        {
            definition = JsonSerializer.Deserialize<ParametricRcSectionDefinition>(
                state.Record.DefinitionJson, JsonOptions)!;
            return definition is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Возвращает статус source-записи и не перезаписывает неизвестные версии.</summary>
    public ParametricRcSectionState GetState(CrossSection section)
    {
        var record = database.TryGetParametricRcSectionRecord(section.Id);
        if (record is null) return new(ParametricRcDefinitionLoadStatus.Missing, false, null);
        if (record.DefinitionVersion != DefinitionVersion)
            return new(ParametricRcDefinitionLoadStatus.UnsupportedFutureVersion, false, record);
        try
        {
            var definition = JsonSerializer.Deserialize<ParametricRcSectionDefinition>(
                record.DefinitionJson, JsonOptions);
            if (definition is null)
                return new(ParametricRcDefinitionLoadStatus.InvalidJson, false, record);
        }
        catch (JsonException) { return new(ParametricRcDefinitionLoadStatus.InvalidJson, false, record); }
        string actual = ParametricRcSectionFingerprint.Compute(section, record.GeneratorVersion);
        return new(ParametricRcDefinitionLoadStatus.Supported, !string.Equals(actual, record.GeneratedFingerprint, StringComparison.Ordinal), record);
    }
}
