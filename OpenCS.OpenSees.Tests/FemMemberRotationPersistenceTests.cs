using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.OpenSees.Tests;

public sealed class FemMemberRotationPersistenceTests
{
    [Fact]
    public void SaveFemMember_RoundTripsRotationDeg()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fem-member-rotation-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Схема", SourceType = "internal" };
            db.SaveFemSchema(schema);

            var member = new FemMember
            {
                SchemaId = schema.Id,
                ElemTag = "1",
                ElemType = "beam",
                NodeIdsJson = "[1,2]",
                RotationDeg = 37.5,
            };
            db.SaveFemMember(member);

            var reloaded = Assert.Single(db.GetFemMembers(schema.Id));
            Assert.Equal(37.5, reloaded.RotationDeg);

            reloaded.RotationDeg = -90;
            db.SaveFemMember(reloaded);
            var reloadedAgain = Assert.Single(db.GetFemMembers(schema.Id));
            Assert.Equal(-90, reloadedAgain.RotationDeg);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>Свежая БД (EnsureCreated, без прогона Migrate()) должна сразу иметь колонку —
    /// см. класс багов, найденный и исправленный в этой же сессии (EnsureCreated и Migrate()
    /// — два независимых пути, оба обязаны знать о каждой новой колонке).</summary>
    [Fact]
    public void FreshDatabase_FemMembersTableHasRotationDegColumn()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fem-member-rotation-fresh-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Схема", SourceType = "internal" };
            db.SaveFemSchema(schema);
            var member = new FemMember { SchemaId = schema.Id, ElemTag = "1", ElemType = "beam", NodeIdsJson = "[1,2]", RotationDeg = 12 };

            db.SaveFemMember(member); // throws SqliteException if the column is missing

            Assert.Equal(12, Assert.Single(db.GetFemMembers(schema.Id)).RotationDeg);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
