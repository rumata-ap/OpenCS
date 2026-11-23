using System.Text.Json;
using System.Text.Json.Serialization;
using CScore;
using CScore.ParametricSteel;
using OpenCS.Models;
using OpenCS.Utilites;

namespace OpenCS.Services;

/// <summary>Статус чтения стального параметрического источника без подмены неизвестного JSON.</summary>
public enum ParametricSteelDefinitionLoadStatus { Supported, UnsupportedFutureVersion, InvalidJson, Missing }

/// <summary>Состояние стального параметрического источника относительно фактической геометрии.</summary>
public sealed record ParametricSteelSectionState(ParametricSteelDefinitionLoadStatus LoadStatus,
    bool IsStale, ParametricSteelSectionRecord? Record);

/// <summary>
/// Сопоставляет source-запись параметрического МК-сечения с его сформированной моделью и
/// выставляет runtime-привязку <see cref="CrossSection.ParametricSteel"/> (явный профиль СП 16).
/// </summary>
public sealed class ParametricSteelSectionProjectService(DatabaseService database)
{
    public const int DefinitionVersion = ParametricSteelSectionDefinition.CurrentVersion;
    public const int GeneratorVersion = ParametricSteelSectionGenerator.GeneratorVersion;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>JSON-контракт определения (camelCase, перечисления строками).</summary>
    public static string Serialize(ParametricSteelSectionDefinition definition) =>
        JsonSerializer.Serialize(definition, JsonOptions);

    /// <summary>Материал определения — сталь или произвольный (путь документа <c>steel</c>).</summary>
    public static List<FieldError> ValidateMaterials(ParametricSteelSectionDefinition d, IEnumerable<Material> materials) =>
        ValidateMaterials(d, id => materials.FirstOrDefault(x => x.Id == id)?.Type);

    /// <summary>То же по функции «Id → тип материала» (CLI: материалы документа ещё не записаны).</summary>
    public static List<FieldError> ValidateMaterials(ParametricSteelSectionDefinition d, Func<int, MatType?> typeOf) =>
        (d.MaterialId > 0 ? typeOf(d.MaterialId) : null) is MatType.Steel or MatType.Custom
            ? [] : [new("steel", Loc.S("ParametricSteelMissingMaterial"))];

    /// <summary>
    /// Проверка определения до генерации: обозначение, материал, затем геометрия генератора (путь пустой —
    /// определение в целом). Окна и CLI вызывают её до <see cref="GenerateAndSave"/> (та проверяет только геометрию).
    /// </summary>
    public static List<FieldError> Validate(ParametricSteelSectionDefinition d, IEnumerable<Material> materials) =>
        Validate(d, id => materials.FirstOrDefault(x => x.Id == id)?.Type);

    /// <inheritdoc cref="Validate(ParametricSteelSectionDefinition, IEnumerable{Material})"/>
    public static List<FieldError> Validate(ParametricSteelSectionDefinition d, Func<int, MatType?> typeOf)
    {
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(d.Tag)) errors.Add(new("tag", Loc.S("ParametricSteelMissingTag")));
        errors.AddRange(ValidateMaterials(d, typeOf));
        errors.AddRange(ParametricSteelSectionGenerator.Generate(d).Diagnostics.Select(m => new FieldError("", m)));
        return errors;
    }

    /// <summary>Формирует и атомарно сохраняет параметрическое сечение; при ошибках ввода ничего не пишет.</summary>
    public ParametricSteelGenerationResult GenerateAndSave(
        CrossSection section, ParametricSteelSectionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(definition);
        var result = ParametricSteelSectionGenerator.Generate(definition);
        if (result.Diagnostics.Count != 0)
            return result;

        section.Tag = definition.Tag;
        section.Areas = result.Section.Areas;
        foreach (var area in section.Areas)
            area.Material = database.Materials.FirstOrDefault(material => material.Id == area.MaterialId);
        string fingerprint = ParametricSteelSectionFingerprint.Compute(section, GeneratorVersion);
        var record = new ParametricSteelSectionRecord(
            section.Id, DefinitionVersion, GeneratorVersion, Serialize(definition), fingerprint);
        database.SaveParametricSteelCrossSection(section, record, result.GeneratedAreas);
        section.ParametricSteel = new ParametricSteelBinding(result.Profile!, fingerprint, GeneratorVersion, definition);
        return result with { Section = section };
    }

    /// <summary>Удаляет только source и mapping, оставляя сечение и области; снимает привязку.</summary>
    public void Detach(CrossSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        database.DetachParametricSteelSection(section.Id);
        section.ParametricSteel = null;
    }

    /// <summary>Перестраивает сечение из поддержанного JSON source.</summary>
    public ParametricSteelSectionState Restore(CrossSection section)
    {
        var state = GetState(section);
        if (!TryGetDefinition(section, out var definition)) return state;
        GenerateAndSave(section, definition);
        return GetState(section);
    }

    /// <summary>Безопасно читает поддержанное определение для команды редактирования.</summary>
    public bool TryGetDefinition(CrossSection section, out ParametricSteelSectionDefinition definition)
    {
        definition = null!;
        var state = GetState(section);
        if (state.LoadStatus != ParametricSteelDefinitionLoadStatus.Supported || state.Record is null)
            return false;
        var parsed = TryDeserialize(state.Record.DefinitionJson);
        if (parsed is null) return false;
        definition = parsed;
        return true;
    }

    /// <summary>Возвращает статус source-записи и не перезаписывает неизвестные версии.</summary>
    public ParametricSteelSectionState GetState(CrossSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var record = database.TryGetParametricSteelSectionRecord(section.Id);
        if (record is null) return new(ParametricSteelDefinitionLoadStatus.Missing, false, null);
        if (record.DefinitionVersion != DefinitionVersion)
            return new(ParametricSteelDefinitionLoadStatus.UnsupportedFutureVersion, false, record);
        if (TryDeserialize(record.DefinitionJson) is null)
            return new(ParametricSteelDefinitionLoadStatus.InvalidJson, false, record);
        string actual = ParametricSteelSectionFingerprint.Compute(section, record.GeneratorVersion);
        return new(ParametricSteelDefinitionLoadStatus.Supported,
            !string.Equals(actual, record.GeneratedFingerprint, StringComparison.Ordinal), record);
    }

    /// <summary>
    /// Выставляет привязку явного профиля сечениям с поддержанным и неустаревшим источником;
    /// остальным (нет источника, будущая версия, битый JSON, ручная правка) — снимает.
    /// </summary>
    public void ApplyBindings(IEnumerable<CrossSection> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        foreach (var section in sections)
        {
            section.ParametricSteel = null;
            var state = GetState(section);
            if (state.LoadStatus != ParametricSteelDefinitionLoadStatus.Supported || state.IsStale
                || state.Record is null)
                continue;
            var definition = TryDeserialize(state.Record.DefinitionJson);
            if (definition is null) continue;
            section.ParametricSteel = new ParametricSteelBinding(
                ParametricSteelSectionGenerator.ToSteelProfile(definition),
                state.Record.GeneratedFingerprint, state.Record.GeneratorVersion, definition);
        }
    }

    static ParametricSteelSectionDefinition? TryDeserialize(string json)
    {
        try { return JsonSerializer.Deserialize<ParametricSteelSectionDefinition>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }
}
