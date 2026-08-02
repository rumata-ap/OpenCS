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

        [Fact]
        public void SQLite_SchemaV50_SaveAndRetrieveFragmentResult_RoundTripValid()
        {
            string dbPath = Path.GetTempFileName() + ".db";
            DatabaseService? db = null;
            try
            {
                db = new DatabaseService(dbPath);

                var contour = new Contour { Id = 1, Tag = "Wall Contour",
                    X = new List<double> { 0, 3, 3, 0 }, Y = new List<double> { 0, 0, 3, 3 } };
                var region = PlanarRegion.CreateFromContour(contour, frame: Frame3D.Identity, tag: "Persistent Wall Result");
                region.Id = 2;
                var fragment = new VerticalPlanarFragment { FragmentId = 2, Name = "Persistent Wall Result", Region = region };
                db.SaveVerticalPlanarFragment(fragment);

                var result = new VerticalPlanarFragmentResult
                {
                    FragmentId = 2,
                    IsConverged = true,
                    MaxConcreteCompressionStrain = -0.0021,
                    MaxRebarTensileStrain = 0.0033,
                    ForceUnbalanceRatio = 0.002,
                    EnergyConfidence = "ExternalWorkOnly"
                };
                result.AuditReport = new FragmentAuditReport().Audit(fragment, result);

                db.SaveVerticalPlanarFragmentResult(2, "snapshot-fp-1", result);
                var retrieved = db.GetLatestVerticalPlanarFragmentResult(2);

                Assert.NotNull(retrieved);
                Assert.Equal(2, retrieved!.FragmentId);
                Assert.Equal(-0.0021, retrieved.MaxConcreteCompressionStrain, 10);
                Assert.Equal(FragmentAuditVerdict.Valid, retrieved.AuditReport.Verdict);
            }
            finally
            {
                db?.Dispose();
                if (File.Exists(dbPath)) { try { File.Delete(dbPath); } catch { } }
            }
        }
    }
}
