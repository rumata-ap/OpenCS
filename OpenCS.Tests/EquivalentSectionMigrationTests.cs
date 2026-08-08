using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

public sealed class EquivalentSectionMigrationTests
{
    [Fact]
    public void OldDatabaseWithoutNewColumns_MigratesWithExpectedDefaults()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-equivalent-migration-{Guid.NewGuid():N}.db");
        try
        {
            CreateV52Database(path);

            using var db = new DatabaseService(path);
            db.LoadAll();

            var section = Assert.Single(db.EquivalentSections);
            Assert.Equal(0.5, section.SpanStationFraction, 12);
            Assert.Equal("", section.SourceRegionFingerprint);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    static void CreateV52Database(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE settings (key TEXT PRIMARY KEY, value_json TEXT NOT NULL);
            INSERT INTO settings (key, value_json) VALUES ('schema_version', '52');

            CREATE TABLE equivalent_sections (
                id                      INTEGER PRIMARY KEY AUTOINCREMENT,
                num                     INTEGER NOT NULL DEFAULT 0,
                tag                     TEXT NOT NULL DEFAULT '',
                description             TEXT,
                source_schema_id        INTEGER NOT NULL DEFAULT 0,
                source_region_id        INTEGER NOT NULL DEFAULT 0,
                source_plate_section_id INTEGER NOT NULL DEFAULT 0,
                source_kind             TEXT NOT NULL DEFAULT 'PlateSectionTangentSnapshot',
                reduction_policy        TEXT NOT NULL DEFAULT 'ConstitutiveIntegration',
                width_integration_points INTEGER NOT NULL DEFAULT 2,
                strip_json              TEXT NOT NULL DEFAULT '{}',
                embedding_json          TEXT NOT NULL DEFAULT '{}',
                linearization_json      TEXT NOT NULL DEFAULT '{}',
                tangent_json            TEXT NOT NULL DEFAULT '[]',
                diagnostics_json        TEXT NOT NULL DEFAULT '[]',
                input_fingerprint       TEXT NOT NULL DEFAULT '',
                result_fingerprint      TEXT NOT NULL DEFAULT '',
                is_calculable           INTEGER NOT NULL DEFAULT 0,
                is_stale                INTEGER NOT NULL DEFAULT 0
            );
            INSERT INTO equivalent_sections
                (num, tag, source_schema_id, source_region_id, source_plate_section_id,
                 strip_json, tangent_json, diagnostics_json, input_fingerprint, result_fingerprint,
                 is_calculable, is_stale)
            VALUES (1, 'old-row', 1, 2, 3, '{}', '[]', '[]', 'fp-in', 'fp-out', 1, 0);
        """;
        cmd.ExecuteNonQuery();
    }
}
