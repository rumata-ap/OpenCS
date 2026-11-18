using System.Text.Json;
using CScore.Fem;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Миграция v102: оси импортных стержней ЛИРЫ/SCAD — местная Y = Z1 (угол +90°), шарниры и местные нагрузки в новых
/// осях, результаты CSfea импортных схем сброшены; схема своего редактора не меняется.
/// </summary>
public sealed class ImportedBarAxesMigrationTests
{
    [Fact]
    public void V101Database_ImportedBarsRotatedReleasesLoadsAndResultsConverted()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-v102-{Guid.NewGuid():N}.db");
        try
        {
            int lira, fragment, native;
            using (var db = new DatabaseService(path))
            {
                var a = new FemSchema { Tag = "ЛИРА", SourceType = "lira" };
                var b = new FemSchema { Tag = "Фрагмент", SourceType = "fragment" };
                var c = new FemSchema { Tag = "Своя" };
                db.SaveFemSchema(a);
                db.SaveFemSchema(b);
                db.SaveFemSchema(c);
                (lira, fragment, native) = (a.Id, b.Id, c.Id);
            }
            Exec(path, $"""
                UPDATE fem_schemas SET parent_source_type = 'lira' WHERE id = {fragment};
                INSERT INTO fem_elements (schema_id, elem_tag, elem_type, origin, beam_rotation_deg, release_i, release_j, source_member_tag) VALUES
                   ({lira}, '1', 'beam', 'imported', -90, 18, NULL, NULL),
                   ({lira}, '2', 'beam', 'imported', 120, NULL, 48, NULL),
                   ({lira}, '3', 'shell', 'imported', NULL, NULL, NULL, NULL),
                   ({lira}, '4', 'beam', 'imported', NULL, NULL, NULL, NULL),
                   ({fragment}, '1', 'beam', 'imported', 0, NULL, NULL, 'К1'),
                   ({native}, '1', 'beam', 'generated', 30, 2, NULL, NULL);
                INSERT INTO fem_element_loads (schema_id, load_case_id, origin, target_kind, target_tags_json, load_kind, coordinate_system, axis, values_json) VALUES
                   ({lira}, 1, 'import:lira', 'elements', '["1","3"]', 'uniform', 'local', 'z', '[-5]'),
                   ({lira}, 1, 'import:lira', 'elements', '["2"]', 'trapezoid', 'local', 'y', '[1,0.5,2,3]'),
                   ({lira}, 1, 'manual', 'elements', '["1"]', 'point', 'local', 'x', '[7,1]'),
                   ({lira}, 1, 'manual', 'elements', '["1"]', 'uniform', 'global', 'y', '[4]'),
                   ({fragment}, 1, 'manual', 'members', '["К1"]', 'point', 'local', 'y', '[3,1]'),
                   ({native}, 1, 'manual', 'elements', '["1"]', 'uniform', 'local', 'y', '[9]');
                INSERT INTO fem_analyses (schema_id, tag, kind, status, result_id) VALUES
                   ({lira}, 'Лин', 'linear', 'ok', 77), ({native}, 'Лин', 'linear', 'ok', 78);
                
                UPDATE settings SET value_json = '101' WHERE key = 'schema_version';
                """);

            using (var db = new DatabaseService(path))
            {
                Assert.Contains(db.MigrationNotices, n => n.Contains("«ЛИРА»") && n.Contains("«Фрагмент»") && n.Contains(" 3,"));
                Assert.Single(db.MigrationNotices, n => n.Contains("CSfea") && n.Contains(": 1."));
            }

            var el = Rows(path, "SELECT schema_id, elem_tag, beam_rotation_deg, release_i, release_j FROM fem_elements")
                .ToDictionary(r => ($"{r[0]}", $"{r[1]}"), r => (r[2], r[3], r[4]));
            Assert.Equal((0.0, 36L, (object?)null), ((double)el[($"{lira}", "1")].Item1!, (long)el[($"{lira}", "1")].Item2!, el[($"{lira}", "1")].Item3)); // Qy + My → Qz + Mz
            Assert.Equal(-150.0, (double)el[($"{lira}", "2")].Item1!, 9);
            Assert.Equal(48L, (long)el[($"{lira}", "2")].Item3!); // My + Mz — без изменений
            Assert.Null(el[($"{lira}", "4")].Item1);
            Assert.Equal(90.0, (double)el[($"{fragment}", "1")].Item1!, 9);
            Assert.Equal((30.0, 2L), ((double)el[($"{native}", "1")].Item1!, (long)el[($"{native}", "1")].Item2!));

            var loads = Rows(path, "SELECT schema_id, target_kind, target_tags_json, load_kind, coordinate_system, axis, values_json FROM fem_element_loads ORDER BY id")
                .Select(r => (S: (long)r[0]!, Kind: (string)r[1]!, Tags: (string)r[2]!, Load: (string)r[3]!, Cs: (string)r[4]!, Axis: (string)r[5]!,
                    V: JsonSerializer.Deserialize<double[]>((string)r[6]!)!)).ToList();
            // Запись на стержень 1 и пластину 3 разделена: пластине — прежнее z, стержню — y (Y сетки = Z1).
            Assert.Contains(loads, l => l.S == lira && l.Tags == "[\"3\"]" && l.Axis == "z" && l.V.SequenceEqual([-5.0]));
            Assert.Contains(loads, l => l.S == lira && l.Tags == "[\"1\"]" && l.Load == "uniform" && l.Cs == "local" && l.Axis == "y" && l.V.SequenceEqual([-5.0]));
            // Y1 → −Z: q1, q2 трапеции с обратным знаком, привязки прежние.
            Assert.Contains(loads, l => l.S == lira && l.Load == "trapezoid" && l.Axis == "z" && l.V.SequenceEqual([-1.0, 0.5, -2.0, 3.0]));
            Assert.Contains(loads, l => l.S == lira && l.Load == "point" && l.Axis == "x" && l.V.SequenceEqual([7.0, 1.0]));
            Assert.Contains(loads, l => l.S == lira && l.Cs == "global" && l.Axis == "y" && l.V.SequenceEqual([4.0]));
            // Нагрузка на КонЭ фрагмента целиком на переведённом стержне — переводится на месте.
            Assert.Contains(loads, l => l.S == fragment && l.Kind == "members" && l.Axis == "z" && l.V.SequenceEqual([-3.0, 1.0]));
            Assert.Contains(loads, l => l.S == native && l.Axis == "y" && l.V.SequenceEqual([9.0]));
            Assert.Equal(7, loads.Count);

            var an = Rows(path, "SELECT schema_id, status, result_id FROM fem_analyses ORDER BY id");
            Assert.Equal(("created", (object?)null), ((string)an[0][1]!, an[0][2]));
            Assert.Equal(("ok", 78L), ((string)an[1][1]!, (long)an[1][2]!));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    static void Exec(string path, string sql)
    {
        using var c = new SqliteConnection($"Data Source={path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=OFF;" + sql;
        cmd.ExecuteNonQuery();
    }

    static List<object?[]> Rows(string path, string sql)
    {
        using var c = new SqliteConnection($"Data Source={path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var rows = new List<object?[]>();
        while (r.Read())
        {
            var row = new object?[r.FieldCount];
            for (int i = 0; i < row.Length; i++) row[i] = r.IsDBNull(i) ? null : r.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }
}
