using Microsoft.Data.Sqlite;
using OpenCS.Models;
using CScore;

namespace OpenCS.Utilites;

public partial class DatabaseService
{
    /// <summary>
    /// Атомарно сохраняет материализованное параметрическое сечение, junction и source.
    /// Все области сечения сохраняются до создания ссылок; Id=0 после этого является ошибкой.
    /// </summary>
    public void SaveParametricCrossSection(CrossSection section,
        ParametricRcSectionRecord record,
        IEnumerable<MaterialArea>? generatedAreas = null)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(record);

        var generated = (generatedAreas ?? section.Areas).Distinct().ToList();
        var oldGeneratedIds = ReadGeneratedAreaIds(section.Id);
        var originalSectionId = section.Id;
        var originalAreaIds = section.Areas.ToDictionary(a => a, a => a.Id);
        var originalGroupIds = section.Areas.SelectMany(a => a.Stirrups)
            .ToDictionary(g => g, g => g.Id);
        var originalElementIds = section.Areas.SelectMany(a => a.Stirrups)
            .SelectMany(g => g.Elements)
            .ToDictionary(e => e, e => e.Id);
        using var tx = _connection.BeginTransaction();
        try
        {
            // Сначала бетон и области без HostArea, затем дочерние области арматуры.
            foreach (var area in section.Areas
                .OrderBy(a => a.HostArea is null ? 0 : 1)
                .ThenBy(a => a.Category))
            {
                SaveMaterialAreaCore(area, _connection);
                if (area.Id == 0)
                    throw new InvalidOperationException($"Область «{area.Tag}» не получила Id.");
            }

            SaveCrossSectionCore(section);
            if (section.Id == 0)
                throw new InvalidOperationException("Параметрическое сечение не получило Id.");

            using (var deleteMap = _connection.CreateCommand())
            {
                deleteMap.CommandText = "DELETE FROM parametric_rc_generated_areas WHERE section_id=@sid";
                deleteMap.Parameters.AddWithValue("@sid", section.Id);
                deleteMap.ExecuteNonQuery();
            }
            foreach (var area in generated)
            {
                if (area.Id == 0)
                    throw new InvalidOperationException($"Сгенерированная область «{area.Tag}» не получила Id.");
                using var insertMap = _connection.CreateCommand();
                insertMap.CommandText = "INSERT INTO parametric_rc_generated_areas(section_id,area_id) VALUES(@sid,@aid)";
                insertMap.Parameters.AddWithValue("@sid", section.Id);
                insertMap.Parameters.AddWithValue("@aid", area.Id);
                insertMap.ExecuteNonQuery();
            }

            using (var source = _connection.CreateCommand())
            {
                source.CommandText = """
                    INSERT INTO parametric_rc_sections(section_id,definition_version,generator_version,definition_json,generated_fingerprint)
                    VALUES(@sectionId,@definitionVersion,@generatorVersion,@definitionJson,@fingerprint)
                    ON CONFLICT(section_id) DO UPDATE SET definition_version=excluded.definition_version,
                      generator_version=excluded.generator_version, definition_json=excluded.definition_json,
                      generated_fingerprint=excluded.generated_fingerprint
                    """;
                source.Parameters.AddWithValue("@sectionId", section.Id);
                source.Parameters.AddWithValue("@definitionVersion", record.DefinitionVersion);
                source.Parameters.AddWithValue("@generatorVersion", record.GeneratorVersion);
                source.Parameters.AddWithValue("@definitionJson", record.DefinitionJson);
                source.Parameters.AddWithValue("@fingerprint", record.GeneratedFingerprint);
                source.ExecuteNonQuery();
            }

            foreach (int areaId in oldGeneratedIds.Except(generated.Select(a => a.Id)))
            {
                using var used = _connection.CreateCommand();
                used.CommandText = "SELECT COUNT(*) FROM cross_section_areas WHERE area_id=@aid";
                used.Parameters.AddWithValue("@aid", areaId);
                if (Convert.ToInt64(used.ExecuteScalar()) != 0) continue;
                DeleteMaterialAreaRows(areaId);
            }

            tx.Commit();
            foreach (var area in section.Areas)
                if (!MaterialAreas.Contains(area)) MaterialAreas.Add(area);
            if (!CrossSections.Contains(section)) CrossSections.Add(section);
        }
        catch
        {
            tx.Rollback();
            if (originalSectionId == 0) section.Id = 0;
            foreach (var area in section.Areas)
            {
                if (originalAreaIds.TryGetValue(area, out var originalId))
                    area.Id = originalId;
                else if (originalAreaIds.Count == 0 || area.Id != 0)
                    area.Id = 0;
                foreach (var group in area.Stirrups)
                {
                    group.Id = originalGroupIds.TryGetValue(group, out var groupId) ? groupId : 0;
                    foreach (var element in group.Elements)
                        element.Id = originalElementIds.TryGetValue(element, out var elementId) ? elementId : 0;
                }
            }
            throw;
        }
    }

    /// <summary>Возвращает Id областей, ранее созданных для source-записи.</summary>
    public IReadOnlyList<int> GetParametricGeneratedAreaIds(int sectionId) =>
        ReadGeneratedAreaIds(sectionId);

    /// <summary>Удаляет только параметрический source и junction.</summary>
    public void DetachParametricRcSection(int sectionId)
    {
        using var tx = _connection.BeginTransaction();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM parametric_rc_generated_areas WHERE section_id=@id; DELETE FROM parametric_rc_sections WHERE section_id=@id;";
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

    List<int> ReadGeneratedAreaIds(int sectionId)
    {
        if (sectionId == 0) return [];
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT area_id FROM parametric_rc_generated_areas WHERE section_id=@sid";
        cmd.Parameters.AddWithValue("@sid", sectionId);
        using var reader = cmd.ExecuteReader();
        var ids = new List<int>();
        while (reader.Read()) ids.Add(reader.GetInt32(0));
        return ids;
    }

    void DeleteMaterialAreaRows(int areaId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM point_fibers WHERE area_id=@id;
            DELETE FROM mesh_fibers WHERE area_id=@id;
            DELETE FROM material_area_closed_stirrup_loops
              WHERE group_id IN (SELECT id FROM material_area_closed_stirrup_groups WHERE area_id=@id);
            DELETE FROM material_area_closed_stirrup_groups WHERE area_id=@id;
            DELETE FROM material_areas WHERE id=@id;
            """;
        cmd.Parameters.AddWithValue("@id", areaId);
        cmd.ExecuteNonQuery();
    }

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
