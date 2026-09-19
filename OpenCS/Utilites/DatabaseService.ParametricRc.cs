using Microsoft.Data.Sqlite;
using OpenCS.Models;

namespace OpenCS.Utilites;

public partial class DatabaseService
{
    /// <summary>Сохраняет или заменяет источник параметрического сечения.</summary>
    public void SaveParametricRcSectionRecord(ParametricRcSectionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO parametric_rc_sections(section_id,definition_version,generator_version,definition_json,generated_fingerprint)
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

    /// <summary>Читает исходное описание параметрического сечения без нормализации JSON.</summary>
    public ParametricRcSectionRecord? TryGetParametricRcSectionRecord(int sectionId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT section_id,definition_version,generator_version,definition_json,generated_fingerprint FROM parametric_rc_sections WHERE section_id=@id";
        cmd.Parameters.AddWithValue("@id", sectionId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? new ParametricRcSectionRecord(reader.GetInt32(0), reader.GetInt32(1),
            reader.GetInt32(2), reader.GetString(3), reader.GetString(4)) : null;
    }
}
