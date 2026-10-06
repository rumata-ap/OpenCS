using CScore.Fem;
using CScore.Fem.Import;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Группы КЭ / группы КонЭ в БД: миграция v76, сохранение импорта, сервис групп.</summary>
public sealed class FemGroupKindsPersistenceTests
{
    static FemMeshNode N(string tag, double x) => new() { NodeTag = tag, X = x, Origin = FemMember.MeshSourceImported };

    static FemElement E(string tag, string nodes, string origin = FemMember.MeshSourceImported, string? member = null) =>
        new() { ElemTag = tag, ElemType = "beam", NodeIdsJson = nodes, Origin = origin, SourceMemberTag = member };

    [Fact]
    public void V75Database_GroupsGetKindOriginStringTagsAndTypeCodes()
    {
        string path = TempDbPath();
        try
        {
            int lira, editor, scad;
            using (var db = new DatabaseService(path))
            {
                var liraSchema = new FemSchema { Tag = "ЛИРА", SourceType = "lira" };
                var editorSchema = new FemSchema { Tag = "Редактор" };
                var scadSchema = new FemSchema { Tag = "SCAD", SourceType = "scad" };
                db.SaveFemSchema(liraSchema);
                db.SaveFemSchema(editorSchema);
                db.SaveFemSchema(scadSchema);
                (lira, editor, scad) = (liraSchema.Id, editorSchema.Id, scadSchema.Id);
                // У схемы ЛИРЫ есть свой КонЭ «1» — номер КЭ 1 с ним совпадает.
                db.SaveFemMember(new FemMember { SchemaId = lira, ElemTag = "1" });
                db.SaveFemMember(new FemMember { SchemaId = editor, ElemTag = "7" });
                db.SaveFemMember(new FemMember { SchemaId = editor, ElemTag = "8" });
                db.SaveFemMeshSnapshot(scad, [N("1", 0), N("2", 1)],
                    [E("1", "[1,2]", FemMember.MeshSourceGenerated), E("2", "[1,2]", FemMember.MeshSourceGenerated, "Б1")]);
            }
            Exec(path, $"""
                ALTER TABLE fem_member_groups DROP COLUMN kind;
                ALTER TABLE fem_member_groups DROP COLUMN origin;
                INSERT INTO fem_member_groups (schema_id, tag, member_type, member_tags_json) VALUES
                   ({lira}, 'Жёсткость 1', 'beam', '[1,2]'),
                   ({lira}, 'Своя', NULL, '[1]'),
                   ({lira}, 'Пустая ЛИРА', NULL, '[]'),
                   ({editor}, 'Балки', 'Балка', '[7,8]'),
                   ({editor}, 'Пустая', 'Ригель', '[]');
                UPDATE settings SET value_json = '75' WHERE key = 'schema_version';
                """);

            using (var db = new DatabaseService(path))
            {
                db.LoadAll();
                var groups = db.FemSchemas.SelectMany(s => s.MemberGroups).ToDictionary(g => g.Tag);

                Assert.Equal((FemMemberGroup.KindMesh, "import:lira"), (groups["Жёсткость 1"].Kind, groups["Жёсткость 1"].Origin));
                Assert.Equal(["1", "2"], groups["Жёсткость 1"].Tags);
                // Все теги совпали с КонЭ — по прежнему правилу это группа КонЭ.
                Assert.Equal((FemMemberGroup.KindMembers, FemMemberGroup.OriginManual), (groups["Своя"].Kind, groups["Своя"].Origin));
                Assert.Equal(FemMemberGroup.KindMesh, groups["Пустая ЛИРА"].Kind);
                Assert.Equal(FemMemberGroup.KindMembers, groups["Балки"].Kind);
                Assert.Equal(FemMemberTypes.Beam, groups["Балки"].MemberType);
                Assert.Equal(FemMemberGroup.KindMembers, groups["Пустая"].Kind);
                Assert.Equal("Ригель", groups["Пустая"].MemberType);

                var scadMesh = db.GetFemMeshElements(scad).ToDictionary(e => e.ElemTag);
                Assert.Equal(FemMember.MeshSourceImported, scadMesh["1"].Origin);
                Assert.Equal(FemMember.MeshSourceGenerated, scadMesh["2"].Origin);
            }
            Assert.Equal("[\"1\",\"2\"]", Scalar(path, "SELECT member_tags_json FROM fem_member_groups WHERE tag='Жёсткость 1'"));
        }
        finally
        {
            Delete(path);
        }
    }

    /// <summary>Импорт программы с моделью из КонЭ (как Robot): элементы со своей сеткой и группа КонЭ.</summary>
    static FemImportResult MemberProgramImport()
    {
        var b1 = new FemMember { ElemTag = "Бар 1", ElemType = "beam", NodeIdsJson = "[1,3]", MeshSource = FemMember.MeshSourceImported };
        var b2 = new FemMember { ElemTag = "Бар 2", ElemType = "beam", NodeIdsJson = "[3,4]", MeshSource = FemMember.MeshSourceImported };
        var group = FemGroupComposition.NewMembersGroup(0, ["Бар 1", "Бар 2"], "Ригели", FemMemberTypes.Beam,
            FemMemberGroup.ImportOrigin("robot"));
        var meshGroup = FemGroupComposition.NewMeshGroup(0, ["3"], "Конец", null, FemMemberGroup.ImportOrigin("robot"));
        return new FemImportResult(
            [N("1", 0), N("2", 1), N("3", 2), N("4", 3)],
            [E("1", "[1,2]"), E("2", "[2,3]"), E("3", "[3,4]")],
            [new FemNode { NodeTag = "1" }, new FemNode { NodeTag = "3", X = 2 }, new FemNode { NodeTag = "4", X = 3 }],
            [new FemImportMember(b1, null, ["1", "2"]), new FemImportMember(b2, null, ["3"])],
            [group, meshGroup]);
    }

    [Fact]
    public void SaveFemImport_MemberProgram_GroupScopeFindsMeshAfterReload()
    {
        string path = TempDbPath();
        try
        {
            int schemaId;
            using (var db = new DatabaseService(path))
            {
                var schema = new FemSchema { Tag = "Robot", SourceType = "robot" };
                db.SaveFemSchema(schema);
                schemaId = schema.Id;
                db.SaveFemImport(schemaId, MemberProgramImport());

                Assert.Throws<InvalidOperationException>(() => db.SaveFemImport(schemaId, MemberProgramImport()));
            }

            using (var db = new DatabaseService(path))
            {
                db.LoadAll();
                var schema = db.FemSchemas.Single(s => s.Id == schemaId);
                Assert.All(db.GetFemMembers(schemaId), m => Assert.True(m.IsMeshLocked));
                Assert.Equal(["Бар 1", "Бар 1", "Бар 2"],
                    db.GetFemMeshElements(schemaId).OrderBy(e => e.ElemTag).Select(e => e.SourceMemberTag));
                Assert.Equal(["1", "3", "4"], db.GetFemNodes(schemaId).Select(n => n.NodeTag).Order());
                Assert.Equal("3", db.GetFemMeshNodes(schemaId).Single(n => n.NodeTag == "3").SourceNodeTag);

                var girders = schema.MemberGroups.Single(g => g.Tag == "Ригели");
                Assert.Equal((FemMemberGroup.KindMembers, "import:robot"), (girders.Kind, girders.Origin));
                var scope = db.GetFemCheckScope(girders);
                Assert.Equal(2, scope.Members.Count);
                Assert.Equal([1, 2, 3], scope.ElementNumbers);

                var end = schema.MemberGroups.Single(g => g.Tag == "Конец");
                Assert.Equal([3], db.GetFemCheckScope(end).ElementNumbers);
            }
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void SaveFemImport_InconsistentResult_Throws_AndWritesNothing()
    {
        string path = TempDbPath();
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Robot", SourceType = "robot" };
            db.SaveFemSchema(schema);
            var import = MemberProgramImport();
            var broken = import with { Groups = [FemGroupComposition.NewMembersGroup(0, ["Нет такого"], "Г", null)] };

            var ex = Assert.Throws<InvalidOperationException>(() => db.SaveFemImport(schema.Id, broken));

            Assert.Contains("Нет такого", ex.Message);
            Assert.Empty(db.GetFemMeshElements(schema.Id));
            Assert.Empty(db.GetFemMembers(schema.Id));

            // КЭ с отсутствующим узлом — только предупреждение: импорты ЛИРЫ/SCAD сохраняли такие КЭ и раньше.
            var dangling = FemImportResult.MeshOnly([N("1", 0), N("2", 1)], [E("1", "[1,2]"), E("2", "[2,9]")], []);
            var warnings = db.SaveFemImport(schema.Id, dangling);
            Assert.Equal("import_mesh_node_missing", Assert.Single(warnings).Code);
            Assert.Equal(2, db.GetFemMeshElements(schema.Id).Count);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void GroupService_MeshGroupTakesOnlyImported_MembersGroupKeepsNonNumericTags()
    {
        string path = TempDbPath();
        try
        {
            using var db = new DatabaseService(path);
            var log = new RecordingLog();
            var service = new FemGroupService(db, log);
            var schema = new FemSchema { Tag = "Схема", SourceType = "lira" };
            db.SaveFemSchema(schema);
            db.SaveFemMember(new FemMember { SchemaId = schema.Id, ElemTag = "Колонна №5 · 1" });
            db.SaveFemMeshSnapshot(schema.Id, [N("1", 0), N("2", 1)],
            [
                E("1", "[1,2]"), E("2", "[1,2]"),
                E("101", "[1,2]", FemMember.MeshSourceGenerated, "Колонна №5 · 1"),
            ]);

            var mesh = service.CreateMeshGroup(schema, ["1", "101", "999"], null, "Плита")!;
            Assert.Equal(["1"], mesh.Tags);
            Assert.Equal(FemMemberTypes.Plate, mesh.MemberType);
            Assert.Equal(2, log.LogEntries.Count(e => e.Level == LogLevel.Warning));
            Assert.Null(service.CreateMeshGroup(schema, ["101"], "Ничего", null));

            var members = service.CreateMembersGroup(schema, ["Колонна №5 · 1", "лишний"], "Колонны", null)!;
            Assert.Equal(["Колонна №5 · 1"], members.Tags);
            Assert.Contains(members, schema.MemberGroups);

            Assert.Equal(1, service.AddTags(schema, mesh, ["2", "101"]));
            Assert.Equal(1, service.RemoveTags(mesh, ["1"]));

            using var reloaded = new DatabaseService(path);
            reloaded.LoadAll();
            var saved = reloaded.FemSchemas.Single(s => s.Id == schema.Id).MemberGroups.ToDictionary(g => g.Tag);
            Assert.Equal(["2"], saved[mesh.Tag].Tags);
            Assert.Equal(["Колонна №5 · 1"], saved["Колонны"].Tags);
            Assert.Equal([101], reloaded.GetFemCheckScope(saved["Колонны"]).ElementNumbers);
        }
        finally
        {
            Delete(path);
        }
    }

    /// <summary>Журнал без диспетчера: LogService в прогоне с WPF-приложением пишет асинхронно.</summary>
    sealed class RecordingLog : ILogService
    {
        public System.Collections.ObjectModel.ObservableCollection<LogEntry> LogEntries { get; } = [];
        public void Info(string message) => LogEntries.Add(new(message, LogLevel.Info, DateTime.Now));
        public void Warning(string message) => LogEntries.Add(new(message, LogLevel.Warning, DateTime.Now));
        public void Error(string message) => LogEntries.Add(new(message, LogLevel.Error, DateTime.Now));
    }

    static object? Scalar(string path, string sql)
    {
        using var c = new SqliteConnection($"Data Source={path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    static void Exec(string path, string sql)
    {
        using var c = new SqliteConnection($"Data Source={path};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    static string TempDbPath()
        => Path.Combine(Path.GetTempPath(), $"opencs-groups-{Guid.NewGuid():N}.db");

    static void Delete(string path)
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }
}
