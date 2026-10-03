using CScore.Fem;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

/// <summary>Массовое назначение сечений КЭ сетки (сечения стержней импортированных схем).</summary>
public class FemElementCrossSectionsTests
{
    [Fact]
    public void SetFemElementCrossSections_PersistsAndUpdatesObjects()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), "opencs_elem_sections_" + Guid.NewGuid().ToString("N") + ".db");
        var db = new DatabaseService(dbPath);
        try
        {
            var schema = new FemSchema { Tag = "Схема" };
            db.SaveFemSchema(schema);
            db.SaveFemMeshSnapshot(schema.Id,
                [
                    new FemMeshNode { NodeTag = "1", X = 0 },
                    new FemMeshNode { NodeTag = "2", X = 1 },
                    new FemMeshNode { NodeTag = "3", X = 2 },
                ],
                [
                    new FemElement { ElemTag = "1", ElemType = "beam", NodeIdsJson = "[1,2]" },
                    new FemElement { ElemTag = "2", ElemType = "beam", NodeIdsJson = "[2,3]" },
                    new FemElement { ElemTag = "3", ElemType = "beam", NodeIdsJson = "[1,3]", CrossSectionId = 5 },
                ]);
            var mesh = db.GetFemMeshElements(schema.Id);
            var first = mesh.Single(e => e.ElemTag == "1");
            var second = mesh.Single(e => e.ElemTag == "2");

            db.SetFemElementCrossSections([(first, 11), (second, 12)]);

            Assert.Equal(11, first.CrossSectionId);
            var reloaded = db.GetFemMeshElements(schema.Id).ToDictionary(e => e.ElemTag, e => e.CrossSectionId);
            Assert.Equal(11, reloaded["1"]);
            Assert.Equal(12, reloaded["2"]);
            Assert.Equal(5, reloaded["3"]);
        }
        finally
        {
            db.Dispose();
            try { File.Delete(dbPath); } catch (IOException) { }
        }
    }
}
