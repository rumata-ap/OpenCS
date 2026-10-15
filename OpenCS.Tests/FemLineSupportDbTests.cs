using CScore.Fem;
using CScore.Planar;
using Microsoft.Data.Sqlite;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Линейные связи граней пластин в БД: запись и чтение, полная замена, копия и удаление схемы.</summary>
public sealed class FemLineSupportDbTests
{
    [Fact]
    public void RoundTrip_Replace_Duplicate_Delete()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-line-supports-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var schema = new FemSchema { Tag = "Плита" };
            db.SaveFemSchema(schema);

            var edge = new FemPlateEdge(new FemPlateEdgeKey("P", BoundaryLoop.Hole, 1, 2), new(1, 2, 3), new(4, 5, 6), new(0, 0, 1));
            var hinge = new FemLineSupport { Mask = 0b111, FixEdgeRotation = true };
            hinge.AssignEdge(edge);
            var elastic = new FemLineSupport { Id = 77, MemberTag = "Q" };
            elastic.SetStiffnesses([0, 0, 5e7, 0, 0, 1e5]);
            db.ReplaceFemLineSupports(schema.Id, [hinge, elastic, new FemLineSupport { MemberTag = "пустая" }]);

            var read = db.GetFemLineSupports(schema.Id);
            Assert.Equal(2, read.Count); // связь без закреплений и пружин не пишется
            var h = read[0];
            Assert.Equal(new FemPlateEdgeKey("P", BoundaryLoop.Hole, 1, 2), h.Key);
            Assert.Equal((edge.A, edge.B), h.Ends);
            Assert.True(h.FixEdgeRotation);
            Assert.Equal(0b111, h.Mask);
            Assert.Equal(new[] { 0, 0, 5e7, 0, 0, 1e5 }, read[1].Stiffnesses);
            Assert.NotEqual(77, elastic.Id); // новые Id после полной замены
            Assert.Equal(read[1].Id, elastic.Id);

            var copy = db.DuplicateFemSchema(schema.Id, "Копия");
            Assert.Equal(2, db.GetFemLineSupports(copy.Id).Count);
            Assert.Equal(2, db.GetFemLineSupports(schema.Id).Count);

            db.ReplaceFemLineSupports(schema.Id, [read[1]]);
            Assert.Equal(["Q"], db.GetFemLineSupports(schema.Id).Select(s => s.MemberTag));

            db.DeleteFemSchema(copy);
            Assert.Empty(db.GetFemLineSupports(copy.Id));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }
    }
}
