using System.Text.Json;
using CScore.Fire;
using CSfea.Thermal;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Миграция v62: один тепловой расчёт на огневое сечение, момент проверки в задачах —
/// время, а не номер снимка.
/// </summary>
public sealed class FireSchemaV62Tests
{
    [Fact]
    public void V61History_KeepsLatestPerSection_AndConvertsSnapshotIndexToTime()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fire-v62-{Guid.NewGuid():N}.db");
        try
        {
            CreateV61Database(path, out int oldId, out int latestId, out int otherId);

            using (var db = new DatabaseService(path))
            {
                Assert.Equal(latestId, db.GetFireThermalResultInfo(1)!.Id);
                Assert.False(db.FireThermalResultExists(oldId));
                Assert.Equal(otherId, db.GetFireThermalResultInfo(2)!.Id);
            }

            // Задача 1 ссылалась на старый расчёт (0/30/60/90 мин), снимок 2 → 60 мин.
            var p1 = ReadParams(path, 1);
            Assert.Equal(-1, p1.GetProperty("snapshot_index").GetInt32());
            Assert.Equal(60.0, p1.GetProperty("snapshot_time_min").GetDouble(), 6);

            // Задача 2 — на несуществующий расчёт: момент сброшен на конец.
            var p2 = ReadParams(path, 2);
            Assert.Equal(-1, p2.GetProperty("snapshot_index").GetInt32());
            Assert.False(p2.TryGetProperty("snapshot_time_min", out _));

            // Задача 3 — конец расчёта: не тронута.
            var p3 = ReadParams(path, 3);
            Assert.False(p3.TryGetProperty("snapshot_time_min", out _));

            // Уникальный индекс: вторая строка того же сечения невозможна.
            using var connection = new SqliteConnection($"Data Source={path}");
            connection.Open();
            using var dup = connection.CreateCommand();
            dup.CommandText = "INSERT INTO fire_thermal_results (fire_section_id, blob) VALUES (1, x'00')";
            Assert.Throws<SqliteException>(() => dup.ExecuteNonQuery());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    static void CreateV61Database(string path, out int oldId, out int latestId, out int otherId)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE settings (key TEXT PRIMARY KEY, value_json TEXT NOT NULL);
                INSERT INTO settings (key, value_json) VALUES ('schema_version', '61');
                CREATE TABLE fire_thermal_results (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    fire_section_id INTEGER NOT NULL,
                    created TEXT NOT NULL DEFAULT '',
                    blob BLOB NOT NULL,
                    input_json TEXT,
                    input_hash TEXT,
                    snapshot_count INTEGER,
                    duration_min REAL
                );
                CREATE TABLE calc_tasks (
                    id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    num             INTEGER NOT NULL DEFAULT 0,
                    tag             TEXT NOT NULL DEFAULT '',
                    kind            TEXT NOT NULL DEFAULT 'strain_state',
                    section_id      INTEGER NOT NULL DEFAULT 0,
                    force_set_id    INTEGER NOT NULL DEFAULT 0,
                    force_item_id   INTEGER NOT NULL DEFAULT 0,
                    calc_type       TEXT NOT NULL DEFAULT 'C',
                    params_json     TEXT NOT NULL DEFAULT '{}'
                );
                """;
            cmd.ExecuteNonQuery();
        }

        oldId = Insert(connection, 1, Result([0.0, 30.0, 60.0, 90.0]));
        latestId = Insert(connection, 1, Result([0.0, 60.0, 120.0]));
        otherId = Insert(connection, 2, Result([0.0, 60.0]));

        AddTask(connection, "fire_r_check", $$"""{"fire_section_id":1,"thermal_result_id":{{oldId}},"method":"fiber","snapshot_index":2}""");
        AddTask(connection, "fire_r_check_batch", """{"fire_section_id":1,"thermal_result_id":999,"method":"fiber","snapshot_index":1}""");
        AddTask(connection, "fire_thermal_curvature", $$"""{"fire_section_id":1,"thermal_result_id":{{latestId}},"snapshot_index":-1}""");
    }

    static int Insert(SqliteConnection connection, int fireSectionId, FireThermalResult result)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO fire_thermal_results (fire_section_id, blob, input_hash, snapshot_count, duration_min)
            VALUES (@s, @b, 'aaaaaaaaaaaaaaaa', @c, @d);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@s", fireSectionId);
        cmd.Parameters.AddWithValue("@b", FireThermalBlobCodec.Pack(result));
        cmd.Parameters.AddWithValue("@c", result.Snapshots.Length);
        cmd.Parameters.AddWithValue("@d", result.FireDurationMin);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    static void AddTask(SqliteConnection connection, string kind, string paramsJson)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO calc_tasks (kind, params_json) VALUES (@k, @p)";
        cmd.Parameters.AddWithValue("@k", kind);
        cmd.Parameters.AddWithValue("@p", paramsJson);
        cmd.ExecuteNonQuery();
    }

    static JsonElement ReadParams(string path, int taskId)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT params_json FROM calc_tasks WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", taskId);
        return JsonDocument.Parse((string)cmd.ExecuteScalar()!).RootElement.Clone();
    }

    static FireThermalResult Result(double[] times)
    {
        var mesh = new HeatMesh(x: [0.0, 1.0, 0.0], y: [0.0, 0.0, 1.0], elements: [[0, 1, 2]]);
        return new FireThermalResult
        {
            MeshInfo = new FireMeshBuildResult { Mesh = mesh, BoundaryEdges = [], Rebars = [] },
            TimesMin = times,
            Snapshots = times.Select(_ => new[] { 20.0, 20.0, 20.0 }).ToArray(),
            AggregateType = "silicate",
            FireDurationMin = times[^1]
        };
    }
}
