using CScore.Fem;
using CScore.Fem.Fields;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Поля из таблиц в БД (v88): запись/чтение, кэш значений, порядок, ручные правки, копия и удаление схемы.</summary>
public sealed class FemFieldDbTests
{
    static FemField NewField(int schemaId, string name) => new()
    {
        SchemaId = schemaId, Name = name, Quantity = FemFieldQuantities.Spring, HasZ = false, SourceFile = "Rz.csv",
        Definition = new FemFieldDefinition
        {
            XHeader = "X [см]", YHeader = "Y [см]",
            Columns = [new FemFieldColumn("Rz [тс/см]", "kz", "тс/см", 980665)],
            Alignment = new FieldAlignment(0.01, Dx: 63.6, Dy: 29.7),
            Targets = new FemFieldTargets { ElemTags = ["5"] },
            Settings = new FieldSettings(FieldMethod.Match, MatchTolerance: 0.5),
        },
        Points = [new(new(-6013.3, 150, 0), [360.8]), new(new(-99.9, -99.9, 0), [337.9])],
    };

    [Fact]
    public void RoundTrip_Values_Order_Overrides_Duplicate_Delete()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fields-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "КСП" };
            db.SaveFemSchema(schema);

            var a = NewField(schema.Id, "Rz свай");
            var b = NewField(schema.Id, "C1");
            b.Quantity = FemFieldQuantities.FoundationC1;
            db.SaveFemField(a);
            db.SaveFemField(b);
            Assert.True(a.Id > 0 && b.Id > a.Id);

            var read = db.GetFemFields(schema.Id);
            Assert.Equal(["Rz свай", "C1"], read.Select(f => f.Name));
            var r = read[0];
            Assert.Equal(a.Definition.ToJson(), r.Definition.ToJson());
            Assert.Equal(2, r.Points.Count);
            Assert.Equal(new FieldVec3(-6013.3, 150, 0), r.Points[0].P);
            Assert.Equal([360.8], r.Points[0].Values);
            Assert.False(r.HasZ);
            Assert.True(r.Enabled);
            Assert.Equal("Rz.csv", r.SourceFile);
            Assert.Null(r.MeshSignature);

            db.ReplaceFemFieldValues(a.Id,
                [new(a.Id, FemFieldTargetKind.MeshNode, "7", "kz", 3.5e8, FieldHow.Matched, 0.3),
                 new(a.Id, FemFieldTargetKind.MeshNode, "8", "kz", null, FieldHow.None, double.PositiveInfinity)], "SIG");
            var values = db.GetFemFieldValues(a.Id);
            Assert.Equal(2, values.Count);
            Assert.Equal(3.5e8, values[0].Value);
            Assert.Null(values[1].Value);
            Assert.Equal(FieldHow.None, values[1].How);
            Assert.Equal("SIG", db.GetFemFields(schema.Id)[0].MeshSignature);

            db.SetFemFieldOrder([b.Id, a.Id]);
            db.SetFemFieldEnabled(a.Id, false);
            read = db.GetFemFields(schema.Id);
            Assert.Equal(["C1", "Rz свай"], read.Select(f => f.Name));
            Assert.False(read[1].Enabled);

            a.Name = "Rz (новая таблица)";
            a.MeshSignature = null;
            db.SaveFemField(a);
            Assert.Empty(db.GetFemFieldValues(a.Id));   // правка поля сбрасывает кэш

            db.ReplaceFemParamOverrides(schema.Id, [new(FemFieldTargetKind.Shell, "3", FemFieldQuantities.FoundationC1, "c1", 1.2e6)]);
            Assert.Equal(1.2e6, Assert.Single(db.GetFemParamOverrides(schema.Id)).Value);

            var copy = db.DuplicateFemSchema(schema.Id, "Копия");
            var copied = db.GetFemFields(copy.Id);
            Assert.Equal(2, copied.Count);
            Assert.All(copied, f => Assert.Null(f.MeshSignature));
            Assert.Single(db.GetFemParamOverrides(copy.Id));

            db.DeleteFemField(b.Id);
            Assert.Single(db.GetFemFields(schema.Id));
            db.DeleteFemSchema(copy);
            Assert.Empty(db.GetFemFields(copy.Id));
            Assert.Empty(db.GetFemParamOverrides(copy.Id));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public void DuplicateSchema_RemapsTargetGroupIds()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fields-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Плита" };
            db.SaveFemSchema(schema);
            var group = new FemMemberGroup { Tag = "Фундамент" };
            db.SaveFemMemberGroups(schema.Id, [group]);
            var field = NewField(schema.Id, "C1");
            field.Quantity = FemFieldQuantities.FoundationC1;
            field.Definition.Targets = new FemFieldTargets { GroupIds = [group.Id, 999] };
            db.SaveFemField(field);

            var copy = db.DuplicateFemSchema(schema.Id, "Копия");
            var copiedGroup = Assert.Single(copy.MemberGroups);
            Assert.NotEqual(group.Id, copiedGroup.Id);
            // Несуществующая группа остаётся (в копии — та же диагностика «цели нет»), а не отбрасывается в «вся схема».
            Assert.Equal([copiedGroup.Id, 999], Assert.Single(db.GetFemFields(copy.Id)).Definition.Targets.GroupIds);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }
    }
}
