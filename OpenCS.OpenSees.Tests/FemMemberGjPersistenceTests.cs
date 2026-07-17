using CScore.Fem;
using OpenCS.Utilites;

namespace OpenCS.OpenSees.Tests;

public sealed class FemMemberGjPersistenceTests
{
    [Fact]
    public void FemMember_GjFieldsRoundTripThroughDatabase()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-fem-gj-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "GJ", SourceType = "internal" };
            db.SaveFemSchema(schema);
            var member = new FemMember
            {
                SchemaId = schema.Id, ElemTag = "1",
                GjStrategy = "saint_venant", GjTorsionTaskId = 7
            };
            db.SaveFemMember(member);

            db.LoadAll();
            var loaded = db.GetFemMembers(schema.Id).Single();

            Assert.Equal("saint_venant", loaded.GjStrategy);
            Assert.Equal(7, loaded.GjTorsionTaskId);
            Assert.Null(loaded.GjManualValue);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
