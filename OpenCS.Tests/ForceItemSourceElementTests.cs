using CScore;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Номер КЭ и сечения внешней схемы у строк наборов усилий: сохранение и миграция v67.</summary>
public sealed class ForceItemSourceElementTests
{
    [Fact]
    public void SaveForceSet_RoundTripsSourceElementAndSection()
    {
        string path = TempDb();
        try
        {
            using (var db = new DatabaseService(path))
            {
                db.SaveForceSet(new ForceSet
                {
                    Num = 1, Tag = "РСУ (C)", Kind = "bar", SourceType = "fea",
                    Items =
                    [
                        new LoadItem { Label = "э.12 с2 к1", N = 1, SourceElementNum = 12, SourceSectionNum = 2 },
                        new LoadItem { Label = "ручная", N = 2 },
                    ],
                });
                db.SaveForceSet(new ForceSet
                {
                    Num = 2, Tag = "DEAD", Kind = "shell", SourceType = "scad",
                    ShellItems =
                    [
                        new ShellLoadItem { Label = "15_Центр", Mx = 1, SourceElementNum = 15 },
                        new ShellLoadItem { Label = "э.7 с1", Mx = 2, SourceElementNum = 7, SourceSectionNum = 1 },
                    ],
                });
            }

            using var reopened = new DatabaseService(path);
            reopened.LoadAll();
            var bar = Assert.Single(reopened.ForceSets, f => f.Kind == "bar");
            Assert.Equal(12, bar.Items[0].SourceElementNum);
            Assert.Equal(2, bar.Items[0].SourceSectionNum);
            Assert.Null(bar.Items[1].SourceElementNum);
            Assert.Null(bar.Items[1].SourceSectionNum);

            var shell = Assert.Single(reopened.ForceSets, f => f.Kind == "shell");
            Assert.Equal(15, shell.ShellItems[0].SourceElementNum);
            Assert.Null(shell.ShellItems[0].SourceSectionNum);
            Assert.Equal(7, shell.ShellItems[1].SourceElementNum);
            Assert.Equal(1, shell.ShellItems[1].SourceSectionNum);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Migration66To67_BackfillsFromLabelsOfImportedSets()
    {
        string path = TempDb();
        try
        {
            using (new DatabaseService(path)) { }
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var seed = connection.CreateCommand();
                // Состояние v66: строки без номеров КЭ; четыре набора — API ЛИРЫ, SCAD, HTML ЛИРЫ, ручной.
                seed.CommandText = """
                    INSERT INTO force_sets (id, num, tag, kind, source_type) VALUES
                        (1, 1, 'РСУ (C)', 'bar',   'fea'),
                        (2, 2, 'DEAD',    'shell', 'scad'),
                        (3, 3, 'HTML',    'bar',   'lira'),
                        (4, 4, 'Ручной',  'bar',   NULL);
                    INSERT INTO force_items (id, set_id, num, label) VALUES
                        (1, 1, 1, 'э.12 с2 к1 A2'),
                        (2, 1, 2, 'node 10'),
                        (3, 3, 1, '127-1'),
                        (4, 4, 1, '10_С1');
                    INSERT INTO force_shell_items (id, set_id, num, label) VALUES
                        (1, 2, 1, '15_Центр_К2'),
                        (2, 2, 2, '3-4');
                    UPDATE force_items       SET source_elem_num = NULL, source_section_num = NULL;
                    UPDATE force_shell_items SET source_elem_num = NULL, source_section_num = NULL;
                    UPDATE settings SET value_json='66' WHERE key='schema_version';
                    """;
                seed.ExecuteNonQuery();
            }

            using (new DatabaseService(path)) { }

            using var verify = new SqliteConnection($"Data Source={path};Pooling=False");
            verify.Open();
            AssertSource(verify, "force_items", 1, 12, 2);
            AssertSource(verify, "force_items", 2, null, null);       // метка OpenSees
            AssertSource(verify, "force_items", 3, 127, 1);           // HTML ЛИРЫ
            AssertSource(verify, "force_items", 4, null, null);       // ручной набор не трогаем
            AssertSource(verify, "force_shell_items", 1, 15, null);
            AssertSource(verify, "force_shell_items", 2, null, null); // «3-4» у SCAD — не КЭ
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Migration66To67_MovesOpenSeesMemberSetsToSourceElementId()
    {
        string path = TempDb();
        try
        {
            int schemaId, memberId, groupId;
            using (var db = new DatabaseService(path))
            {
                var schema = new CScore.Fem.FemSchema { Tag = "Схема" };
                db.SaveFemSchema(schema);
                var member = new CScore.Fem.FemMember { SchemaId = schema.Id, ElemTag = "M1" };
                db.SaveFemMember(member);
                var group = new CScore.Fem.FemMemberGroup { SchemaId = schema.Id, Tag = "Группа", MemberTagsJson = "[1]" };
                db.SaveFemMemberGroup(group);
                (schemaId, memberId, groupId) = (schema.Id, member.Id, group.Id);
            }
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var seed = connection.CreateCommand();
                // v66: набор стержня OpenSees хранил id элемента в source_member_id; набор группы — id группы.
                seed.CommandText = $"""
                    INSERT INTO force_sets (id, num, tag, kind, source_type, source_schema_id, source_element_tag, source_member_id) VALUES
                        (1, 1, 'OS M1',  'bar', 'fea', {schemaId}, 'M1', {memberId}),
                        (2, 2, 'РСУ (C)', 'bar', 'fea', {schemaId}, NULL, {groupId});
                    UPDATE settings SET value_json='66' WHERE key='schema_version';
                    """;
                seed.ExecuteNonQuery();
            }

            using var reopened = new DatabaseService(path);
            reopened.LoadAll();
            var openSees = Assert.Single(reopened.ForceSets, f => f.Tag == "OS M1");
            Assert.Equal(memberId, openSees.SourceElementId);
            Assert.Null(openSees.SourceMemberId);
            var imported = Assert.Single(reopened.ForceSets, f => f.Tag == "РСУ (C)");
            Assert.Equal(groupId, imported.SourceMemberId);
            Assert.Null(imported.SourceElementId);
        }
        finally { Cleanup(path); }
    }

    static void AssertSource(SqliteConnection connection, string table, int id, int? elem, int? section)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT source_elem_num, source_section_num FROM {table} WHERE id={id}";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(elem, reader.IsDBNull(0) ? null : reader.GetInt32(0));
        Assert.Equal(section, reader.IsDBNull(1) ? null : reader.GetInt32(1));
    }

    static string TempDb() => Path.Combine(Path.GetTempPath(), $"opencs-force-item-source-{Guid.NewGuid():N}.db");

    static void Cleanup(string path)
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
