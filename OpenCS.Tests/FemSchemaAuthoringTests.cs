using CScore;
using CScore.Fem;
using CScore.Fem.Editing;
using CScore.Planar;
using Microsoft.Data.Sqlite;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>
/// Служба МКЭ-схем: имя по умолчанию и проверка имени, копия, состав удаляемого и отказ у родителя субмоделей,
/// удаление пластины (линейные связи, нагрузки, группы; область — при записи) и отмена его в сессии.
/// </summary>
[Collection("Loc language")]
public sealed class FemSchemaAuthoringTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"opencs_femschema_{Guid.NewGuid():N}");
    public FemSchemaAuthoringTests()
    {
        Directory.CreateDirectory(_dir);
        Loc.SetLanguage("ru-RU");
    }
    public void Dispose()
    {
        Loc.SetLanguage(null);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    DatabaseService NewDb() => new(Path.Combine(_dir, $"{Guid.NewGuid():N}.db"));

    /// <summary>Схема с балкой Б1 (узлы 1–2) и пластиной П1 6×4 м с линейной связью грани, нагрузкой на КЭ пластины и
    /// группой из Б1 и П1.</summary>
    static FemSchema SchemaWithPlate(DatabaseService db, string tag = "Рама")
    {
        var schema = FemSchemaAuthoring.CreateNew(db, tag);
        var region = new PlanarRegion { Contours = [new Contour([0, 6, 6, 0, 0], [0, 0, 4, 4, 0], "П1") { Type = ContourType.Hull }] };
        int regionId = db.AddPlanarRegion(region, schema.Id);
        region.Id = regionId;
        var plate = new FemMember { SchemaId = schema.Id, ElemTag = "П1", ElemType = "shell", PlanarRegionId = regionId };
        var beam = new FemMember { SchemaId = schema.Id, ElemTag = "Б1", ElemType = "beam", NodeIdsJson = "[1,2]" };
        var session = FemSchemaAuthoring.OpenSession(db, schema);
        session.Nodes.Add(new FemNode { SchemaId = schema.Id, NodeTag = "1", X = 0 });
        session.Nodes.Add(new FemNode { SchemaId = schema.Id, NodeTag = "2", X = 6 });
        session.Members.Add(beam);
        session.Members.Add(plate);
        var edge = FemLineSupportGeometry.Edges(plate, region)[0];
        var support = new FemLineSupport { Mask = 0b111 };
        support.AssignEdge(edge);
        session.LineSupports.Add(support);
        var lc = new FemLoadCase { Id = session.AllocateTemporaryLoadCaseId(), SchemaId = schema.Id, Tag = "Пост", Sp20Type = FemSp20Types.Permanent };
        session.LoadCases.Add(lc);
        var load = new FemElementLoad { SchemaId = schema.Id, LoadCaseId = lc.Id, TargetKind = FemLoadTargetKinds.Members };
        load.SetTargetTags(["П1", "Б1"]);
        session.ElementLoads.Add(load);
        var only = new FemElementLoad { SchemaId = schema.Id, LoadCaseId = lc.Id, TargetKind = FemLoadTargetKinds.Members };
        only.SetTargetTags(["П1"]);
        session.ElementLoads.Add(only);
        session.MemberGroups.Add(FemGroupComposition.NewMembersGroup(schema.Id, ["Б1", "П1"], "Все", null));
        Assert.True(FemSchemaAuthoring.Save(db, session, out var d), string.Join("; ", d.Select(x => x.Message)));
        // Загружение получило Id при записи — нагрузки ссылаются на него.
        db.LoadAll();
        return db.FemSchemas.Single(s => s.Id == schema.Id);
    }

    [Fact]
    public void DefaultTag_IsFirstFree_AndTagErrors()
    {
        using var db = NewDb();
        Assert.Equal("Схема 1", FemSchemaAuthoring.DefaultTag(db));
        var a = FemSchemaAuthoring.CreateNew(db);
        Assert.Equal("Схема 1", a.Tag);
        Assert.Equal("Схема 2", FemSchemaAuthoring.CreateNew(db).Tag);

        Assert.Equal(Loc.S("FemSchemaErrTagEmpty"), FemSchemaAuthoring.TagError(db, "  "));
        Assert.NotNull(FemSchemaAuthoring.TagError(db, "схема 2"));          // регистр не различается
        Assert.Null(FemSchemaAuthoring.TagError(db, "Схема 1", a.Id));       // своё имя — можно
        Assert.NotNull(FemSchemaAuthoring.Rename(db, a, "Схема 2"));
        Assert.Equal("Схема 1", a.Tag);
        Assert.Null(FemSchemaAuthoring.Rename(db, a, "  Рама  "));
        Assert.Equal("Рама", a.Tag);
        Assert.Throws<InvalidOperationException>(() => FemSchemaAuthoring.CreateNew(db, "Рама"));
    }

    [Fact]
    public void Duplicate_FreeCopyName_CopiesInput()
    {
        using var db = NewDb();
        var schema = SchemaWithPlate(db);
        var copy = FemSchemaAuthoring.Duplicate(db, schema);
        var copy2 = FemSchemaAuthoring.Duplicate(db, schema);
        Assert.Equal("Рама (копия)", copy.Tag);
        Assert.Equal("Рама (копия 2)", copy2.Tag);
        Assert.NotEqual(schema.Id, copy.Id);
        Assert.Equal(["Б1", "П1"], db.GetFemMembers(copy.Id).Select(m => m.ElemTag).OrderBy(t => t));
        Assert.Single(db.GetPlanarRegions(copy.Id));
    }

    [Fact]
    public void DeleteImpact_CountsAndQuestion_ThenDeletes()
    {
        using var db = NewDb();
        var schema = SchemaWithPlate(db);
        db.SaveFemAnalysis(new FemAnalysis { SchemaId = schema.Id, Tag = "Линейный" });
        var impact = FemSchemaAuthoring.DeleteImpact(db, schema);
        Assert.Equal(1, impact.Analyses);
        Assert.False(impact.Blocked);
        string q = FemSchemaAuthoring.DeleteQuestion(schema, impact);
        Assert.Contains("«Рама»", q);
        Assert.Contains(string.Format(Loc.S("FemSchemaDeleteAnalyses"), 1), q);
        Assert.DoesNotContain(Loc.S("FemSchemaDeleteChecks").Split(':')[0], q);

        Assert.True(FemSchemaAuthoring.TryDelete(db, schema, out var refusal));
        Assert.Null(refusal);
        db.LoadAll();
        Assert.DoesNotContain(db.FemSchemas, s => s.Id == schema.Id);
        Assert.Empty(db.GetPlanarRegions(schema.Id));
    }

    [Fact]
    public void Delete_ParentOfSubmodel_RefusedWithNames_NoException()
    {
        string path = Path.Combine(_dir, "submodel.db");
        using var db = new DatabaseService(path);
        var parent = FemSchemaAuthoring.CreateNew(db, "Родитель");
        var child = FemSchemaAuthoring.CreateNew(db, "Балка-субмодель");
        using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA foreign_keys=OFF; INSERT INTO submodel_extractions (parent_schema_id, submodel_schema_id, parent_analysis_id, parent_result_id) VALUES ({parent.Id}, {child.Id}, 0, 0)";
            cmd.ExecuteNonQuery();
        }
        var impact = FemSchemaAuthoring.DeleteImpact(db, parent);
        Assert.True(impact.Blocked);
        Assert.False(FemSchemaAuthoring.TryDelete(db, parent, out var refusal));
        Assert.Contains("«Балка-субмодель»", refusal);
        Assert.Contains(db.FemSchemas, s => s.Id == parent.Id);
    }

    [Fact]
    public void DeletePlanarMember_RemovesSupportsLoadsGroupTag_AndRegion()
    {
        using var db = NewDb();
        var schema = SchemaWithPlate(db);
        var session = FemSchemaAuthoring.OpenSession(db, schema);
        var (supports, loads, groups) = FemSchemaAuthoring.PlanarDeleteImpact(session, "П1");
        Assert.Equal((1, 2, 1), (supports, loads, groups));
        Assert.Contains("«П1»", FemSchemaAuthoring.PlanarDeleteQuestion(session, "П1"));

        FemSchemaAuthoring.DeletePlanarMember(db, schema, "П1");

        Assert.Equal(["Б1"], db.GetFemMembers(schema.Id).Select(m => m.ElemTag));
        Assert.Empty(db.GetFemLineSupports(schema.Id));
        var left = Assert.Single(db.GetFemElementLoads(schema.Id));
        Assert.Equal(["Б1"], left.TargetTags);
        db.LoadAll();
        Assert.Equal(["Б1"], db.FemSchemas.Single(s => s.Id == schema.Id).MemberGroups.Single().Tags);
        Assert.Empty(db.GetPlanarRegions(schema.Id));
    }

    [Fact]
    public void PlanarDeleteInSession_UndoRestores_RegionRemovedOnlyOnSave()
    {
        using var db = NewDb();
        var schema = SchemaWithPlate(db);
        var session = FemSchemaAuthoring.OpenSession(db, schema);
        var plate = session.Members.Single(m => m.ElemTag == "П1");
        var loadsBefore = session.ElementLoads.Select(l => l.TargetTagsJson).ToList();

        session.Execute(new DeleteMemberCommand(plate));
        Assert.Single(session.ElementLoads);
        Assert.Single(db.GetPlanarRegions(schema.Id));                 // до записи область в базе
        session.Undo();
        Assert.Contains(plate, session.Members);
        Assert.Single(session.LineSupports);
        Assert.Equal(loadsBefore, session.ElementLoads.Select(l => l.TargetTagsJson));

        session.Redo();
        Assert.True(FemSchemaAuthoring.Save(db, session, out _));
        Assert.Empty(db.GetPlanarRegions(schema.Id));
    }

    [Fact]
    public void Save_KeepsRegionsWithoutMembersThatWereNotRemoved()
    {
        using var db = NewDb();
        var schema = FemSchemaAuthoring.CreateNew(db, "Импорт");
        // Область без КонЭ (например, подготовленная импортом) — запись схемы её не трогает.
        db.AddPlanarRegion(new PlanarRegion { Contours = [new Contour([0, 1, 1, 0, 0], [0, 0, 1, 1, 0], "x") { Type = ContourType.Hull }] }, schema.Id);
        var session = FemSchemaAuthoring.OpenSession(db, schema);
        session.Nodes.Add(new FemNode { SchemaId = schema.Id, NodeTag = "1" });
        Assert.True(FemSchemaAuthoring.Save(db, session, out _));
        Assert.Single(db.GetPlanarRegions(schema.Id));
    }
}
