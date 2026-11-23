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
    // 2 — физические стержни ряда раскладываются по фактической ширине контура с отступом a
    // (раньше — на ±0,35·b без учёта защитного слоя, не совпадая с предпросмотром).
    public const int GeneratorVersion = 2;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Материалы определения: бетон, продольная арматура, материал каждого набора срезов (пути — как в документе
    /// <c>parametric</c>: <c>concrete</c>, <c>rebar</c>, <c>stirrups[i].material</c>).
    /// </summary>
    public static List<FieldError> ValidateMaterials(ParametricRcSectionDefinition d, IEnumerable<Material> materials)
    {
        var list = materials as IReadOnlyCollection<Material> ?? materials.ToList();
        return ValidateMaterials(d, id => list.FirstOrDefault(m => m.Id == id)?.Type);
    }

    /// <summary>То же по функции «Id → тип материала» (CLI: материалы документа ещё не записаны).</summary>
    public static List<FieldError> ValidateMaterials(ParametricRcSectionDefinition d, Func<int, MatType?> typeOf)
    {
        MatType? Type(int id) => id > 0 ? typeOf(id) : null;
        static bool IsRebar(MatType? t) => t is MatType.ReSteelF or MatType.ReSteelU;
        var errors = new List<FieldError>();
        if (Type(d.ConcreteMaterialId) != MatType.Concrete)
            errors.Add(new("concrete", Loc.S("ParametricRcMissingConcreteMaterial")));
        if (!IsRebar(Type(d.LongitudinalMaterialId)))
            errors.Add(new("rebar", Loc.S("ParametricRcMissingLongitudinalMaterial")));
        for (int i = 0; i < d.StirrupCuts.Count; i++)
            if (d.StirrupCuts[i].Count > 0 && !IsRebar(Type(d.StirrupCuts[i].MaterialId)))
                errors.Add(new($"stirrups[{i}].material", Loc.S("ParametricRcMissingStirrupMaterial")));
        return errors;
    }

    /// <summary>
    /// Проверка определения до генерации: обозначение, материалы нужного типа, затем геометрия генератора (путь
    /// пустой — относится к определению в целом). Окна и CLI вызывают её до <see cref="GenerateAndSave"/> (та проверяет только геометрию).
    /// </summary>
    public static List<FieldError> Validate(ParametricRcSectionDefinition d, IEnumerable<Material> materials)
    {
        var list = materials as IReadOnlyCollection<Material> ?? materials.ToList();
        return Validate(d, id => list.FirstOrDefault(m => m.Id == id)?.Type);
    }

    /// <inheritdoc cref="Validate(ParametricRcSectionDefinition, IEnumerable{Material})"/>
    public static List<FieldError> Validate(ParametricRcSectionDefinition d, Func<int, MatType?> typeOf)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(d.Tag)) errors.Add(new("tag", Loc.S("ParametricRcMissingTag")));
        errors.AddRange(ValidateMaterials(d, typeOf));
        errors.AddRange(ParametricRcSectionGenerator.Generate(d).Diagnostics.Select(m => new FieldError("", m)));
        return errors;
    }

    /// <summary>Формирует и атомарно сохраняет параметрическое сечение.</summary>
    public ParametricRcGenerationResult GenerateAndSave(
        CrossSection section, ParametricRcSectionDefinition definition)
    {
        var result = ParametricRcSectionGenerator.Generate(definition);
        if (result.Diagnostics.Count != 0)
            return result;

        section.Tag = definition.Tag;
        section.Areas = result.Section.Areas;
        foreach (var area in section.Areas)
            area.Material = database.Materials.FirstOrDefault(material => material.Id == area.MaterialId);
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
