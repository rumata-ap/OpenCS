using CScore.Fem;
using CScore.Import;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Жёсткости ЛИРЫ при схеме (таблицы API 9 и 10) и миграция v69 (оси усилий стержней).</summary>
public sealed class LiraStiffnessesTests
{
    [Fact]
    public void ParseTables_StiffnessesAndElementNumbers()
    {
        var data = new LiraSchemaData();
        data.Elements.Add(new LiraElementRecord(5, 10, 2, 0, [1, 2]));
        data.Elements.Add(new LiraElementRecord(6, 44, 1, 0, [1, 2, 3, 4]));

        LiraApiSchemaReader.ParseStiffnesses(new object[,]
        {
            { 1, 0, 0, "Брус 30 X 50", "Ro:2.5 E:3e+06 B:30 H:50 EF:4.5e+05 EIy:9375 EIz:3375 GIk:5000 BAR_END" },
            { 2, 36, 0, "Пластина H 20", "Ro:2.5 E:3e+06 V:0.2 H:20 PLATE_END" },
            { "x", 0, 0, "мусор", "" },
        }, 0.01, data);
        var byElement = new Dictionary<int, int>();
        LiraApiSchemaReader.ParseElementStiffnesses(new object[,] { { 5, 1 }, { 6, 2 }, { 7, 0 } }, byElement);
        data.ApplyStiffnesses(byElement);

        Assert.Equal(2, data.Stiffnesses.Count);
        Assert.Equal(new LiraBarRect(0.3, 0.5), LiraStiffnessParams.BarRect(data.Stiffnesses[0]));
        Assert.Equal(new Dictionary<int, int> { [5] = 1, [6] = 2 }, byElement);
        Assert.Equal(1, data.Elements[0].StiffnessId);
        Assert.Equal(2, data.Elements[1].StiffnessId);
        // Без CSV-импорта списки жёсткостей стержней и пластин заполняются из таблицы.
        Assert.Equal(4.5e5, Assert.Single(data.BarStiffnesses).EF);
        Assert.Equal(200, Assert.Single(data.PlateStiffnesses).H_mm, 9);
    }

    [Fact]
    public void ReplaceStiffnesses_RoundTripsAndSetsElementNumbers()
    {
        string path = TempDb();
        try
        {
            int schemaId;
            using (var db = new DatabaseService(path))
            {
                var schema = new FemSchema { Tag = "Схема" };
                db.SaveFemSchema(schema);
                schemaId = schema.Id;
                db.SaveFemMeshSnapshot(schemaId, [],
                [
                    new FemElement { ElemTag = "5", ElemType = "beam", NodeIdsJson = "[1,2]", Origin = FemMember.MeshSourceImported },
                    new FemElement { ElemTag = "6", ElemType = "beam", NodeIdsJson = "[2,3]", Origin = FemMember.MeshSourceImported, StiffnessNum = 9 },
                ]);
                int updated = db.ReplaceFemSchemaStiffnesses(schemaId,
                    [new LiraStiffnessRecord(1, 0, "Брус 30 X 50", "B:30 H:50 BAR_END", 0.01)],
                    new Dictionary<string, int> { ["5"] = 1 });
                Assert.Equal(1, updated);
            }

            using var reopened = new DatabaseService(path);
            var stiffness = Assert.Single(reopened.GetFemSchemaStiffnesses(schemaId)).Value;
            Assert.Equal("Брус 30 X 50", stiffness.Name);
            Assert.Equal(0.01, stiffness.SectionUnitM);
            var elements = reopened.GetFemMeshElements(schemaId).ToDictionary(e => e.ElemTag);
            Assert.Equal(1, elements["5"].StiffnessNum);
            Assert.Null(elements["6"].StiffnessNum);   // в таблице ЛИРЫ нет — номер сброшен
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Migration68To69_SwapsBarMomentAxesOfImportedSets()
    {
        string path = TempDb();
        try
        {
            using (new DatabaseService(path)) { }
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var seed = connection.CreateCommand();
                // v68: наборы файлового импорта ЛИРЫ и SCAD, API ЛИРЫ (fea с номером КЭ), OpenSees (fea без номера), ручной.
                seed.CommandText = """
                    INSERT INTO force_sets (id, num, tag, kind, source_type) VALUES
                        (1, 1, 'HTML',    'bar', 'lira'),
                        (2, 2, 'DEAD',    'bar', 'scad'),
                        (3, 3, 'РСУ (C)', 'bar', 'fea'),
                        (4, 4, 'OS',      'bar', 'fea'),
                        (5, 5, 'Ручной',  'bar', NULL);
                    INSERT INTO force_items (id, set_id, num, label, mx, my, vx, vy, source_elem_num) VALUES
                        (1, 1, 1, '127-1',      1, 2, 3, 4, 127),
                        (2, 2, 1, '10-1',       1, 2, 3, 4, NULL),
                        (3, 3, 1, 'э.12 с2 к1', 1, 2, 3, 4, 12),
                        (4, 4, 1, 'node 10',    1, 2, 3, 4, NULL),
                        (5, 5, 1, '10_С1',      1, 2, 3, 4, NULL);
                    UPDATE settings SET value_json='68' WHERE key='schema_version';
                    """;
                seed.ExecuteNonQuery();
            }

            using (new DatabaseService(path)) { }

            using var verify = new SqliteConnection($"Data Source={path};Pooling=False");
            verify.Open();
            AssertForces(verify, 1, 2, 1, 4, 3);
            AssertForces(verify, 2, 2, 1, 4, 3);
            AssertForces(verify, 3, 2, 1, 4, 3);
            AssertForces(verify, 4, 1, 2, 3, 4);   // OpenSees — оси OpenCS, не трогаем
            AssertForces(verify, 5, 1, 2, 3, 4);   // ручной
        }
        finally { Cleanup(path); }
    }

    static void AssertForces(SqliteConnection connection, int id, double mx, double my, double vx, double vy)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT mx, my, vx, vy FROM force_items WHERE id={id}";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal((mx, my, vx, vy), (reader.GetDouble(0), reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3)));
    }

    static string TempDb() => Path.Combine(Path.GetTempPath(), $"opencs-lira-stiffness-{Guid.NewGuid():N}.db");

    static void Cleanup(string path)
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
