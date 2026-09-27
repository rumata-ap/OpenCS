using OpenCS.Models;
using CScore;

namespace OpenCS.Utilites;

public partial class DatabaseService
{
    /// <summary>
    /// Атомарно сохраняет материализованное параметрическое стальное сечение, junction и source;
    /// прежние сгенерированные области без ссылок удаляются.
    /// </summary>
    public void SaveParametricSteelCrossSection(CrossSection section,
        ParametricSteelSectionRecord record,
        IEnumerable<MaterialArea>? generatedAreas = null)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(record);
        SaveParametricSectionCore(section, "parametric_steel_sections", "parametric_steel_generated_areas",
            record.DefinitionVersion, record.GeneratorVersion, record.DefinitionJson,
            record.GeneratedFingerprint, generatedAreas);
    }

    /// <summary>Возвращает Id областей, ранее созданных для стального source.</summary>
    public IReadOnlyList<int> GetParametricSteelGeneratedAreaIds(int sectionId) =>
        ReadGeneratedAreaIds("parametric_steel_generated_areas", sectionId);

    /// <summary>Удаляет только стальной параметрический source и junction, оставляя сечение и области.</summary>
    public void DetachParametricSteelSection(int sectionId)
    {
        using var tx = _connection.BeginTransaction();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM parametric_steel_generated_areas WHERE section_id=@id; DELETE FROM parametric_steel_sections WHERE section_id=@id;";
            cmd.Parameters.AddWithValue("@id", sectionId);
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>Сохраняет или заменяет стальной source (без перегенерации геометрии).</summary>
    public void SaveParametricSteelSectionRecord(ParametricSteelSectionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO parametric_steel_sections(section_id,definition_version,generator_version,definition_json,generated_fingerprint)
            VALUES(@sectionId,@definitionVersion,@generatorVersion,@definitionJson,@fingerprint)
            ON CONFLICT(section_id) DO UPDATE SET definition_version=excluded.definition_version,
              generator_version=excluded.generator_version, definition_json=excluded.definition_json,
              generated_fingerprint=excluded.generated_fingerprint;
            """;
        cmd.Parameters.AddWithValue("@sectionId", record.SectionId);
        cmd.Parameters.AddWithValue("@definitionVersion", record.DefinitionVersion);
        cmd.Parameters.AddWithValue("@generatorVersion", record.GeneratorVersion);
        cmd.Parameters.AddWithValue("@definitionJson", record.DefinitionJson);
        cmd.Parameters.AddWithValue("@fingerprint", record.GeneratedFingerprint);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Читает исходное описание стального параметрического сечения без нормализации JSON.</summary>
    public ParametricSteelSectionRecord? TryGetParametricSteelSectionRecord(int sectionId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT section_id,definition_version,generator_version,definition_json,generated_fingerprint FROM parametric_steel_sections WHERE section_id=@id";
        cmd.Parameters.AddWithValue("@id", sectionId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? new ParametricSteelSectionRecord(reader.GetInt32(0), reader.GetInt32(1),
            reader.GetInt32(2), reader.GetString(3), reader.GetString(4)) : null;
    }
}
