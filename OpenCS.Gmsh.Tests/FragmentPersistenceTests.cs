using System.Collections.Generic;
using System.IO;
using CScore;
using CScore.Planar;
using CScore.Planar.Fragments;
using CSmath;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Gmsh.Tests
{
    public class FragmentPersistenceTests
    {
        [Fact]
        public void SQLite_SchemaV50_SaveAndRetrieveFragment_RoundTripValid()
        {
            string dbPath = Path.GetTempFileName() + ".db";
            DatabaseService? db = null;
            try
            {
                db = new DatabaseService(dbPath);

                var contour = new Contour
                {
                    Id = 1,
                    Tag = "Wall Contour",
                    X = new List<double> { 0, 3, 3, 0 },
                    Y = new List<double> { 0, 0, 3, 3 }
                };

                var region = PlanarRegion.CreateFromContour(contour, frame: Frame3D.Identity, tag: "Persistent Wall 1");
                region.Id = 1;

                var fragment = new VerticalPlanarFragment
                {
                    FragmentId = 1,
                    Name = "Persistent Wall 1",
                    Region = region
                };

                db.SaveVerticalPlanarFragment(fragment);

                var retrieved = db.GetVerticalPlanarFragment(1);

                Assert.NotNull(retrieved);
                Assert.Equal(1, retrieved.FragmentId);
                Assert.Equal("Persistent Wall 1", retrieved.Name);
                Assert.NotNull(retrieved.Region);
            }
            finally
            {
                db?.Dispose();
                if (File.Exists(dbPath))
                {
                    try { File.Delete(dbPath); } catch { }
                }
            }
        }
    }
}
