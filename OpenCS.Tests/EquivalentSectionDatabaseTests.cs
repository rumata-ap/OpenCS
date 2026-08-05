using CScore.PlateStrip;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

public sealed class EquivalentSectionDatabaseTests
{
    [Fact]
    public void EquivalentSection_RoundTripsThroughSqlite()
    {
        string path = TempPath();
        try
        {
            var source = new DatabaseService(path);
            var original = Section();
            source.SaveEquivalentSection(original);

            var loaded = new DatabaseService(path);
            loaded.LoadAll();

            var actual = Assert.Single(loaded.EquivalentSections);
            Assert.NotEqual(0, actual.Id);
            Assert.Equal(original.Tag, actual.Tag);
            Assert.Equal(original.SourceRegionId, actual.SourceRegionId);
            Assert.Equal(original.SourceSchemaId, actual.SourceSchemaId);
            Assert.Equal(original.InputFingerprint, actual.InputFingerprint);
            Assert.Equal(original.ResultFingerprint, actual.ResultFingerprint);
            Assert.Equal(original.BeamTangent[0, 1], actual.BeamTangent[0, 1], 12);
            Assert.Contains(actual.Diagnostics, d => d.Code == "test_warning");

            loaded.Dispose();
            source.Dispose();
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void EquivalentSection_SaveUpdatesExistingRowAndDeleteRemovesIt()
    {
        string path = TempPath();
        try
        {
            using var db = new DatabaseService(path);
            var section = Section();
            db.SaveEquivalentSection(section);
            int id = section.Id;

            section.Tag = "updated";
            db.SaveEquivalentSection(section);
            Assert.Single(db.EquivalentSections);
            Assert.Equal(id, db.EquivalentSections[0].Id);
            Assert.Equal("updated", db.EquivalentSections[0].Tag);

            db.DeleteEquivalentSection(section);
            Assert.Empty(db.EquivalentSections);
        }
        finally
        {
            TryDelete(path);
        }
    }

    static EquivalentSection Section() => new()
    {
        Num = 2,
        Tag = "equiv-1",
        Description = "test",
        SourceSchemaId = 7,
        SourceRegionId = 11,
        SourcePlateSectionId = 13,
        Strip = new PlateStripBeamAnalogy
        {
            Id = "strip-1",
            SourceRegionId = 11,
            ExplicitWidthM = 2.0,
            Fingerprint = "strip-fp",
            Geometry = new PlateStripGeometry { LengthM = 6.0 }
        },
        ReductionPolicy = ReductionPolicy.ConstitutiveIntegration,
        SourceKind = EquivalentSectionSourceKind.PlateSectionTangentSnapshot,
        WidthIntegrationPoints = 2,
        BeamTangent = new[,] { { 2000.0, 40.0, 0.0 }, { 40.0, 600.0, 0.0 }, { 0.0, 0.0, 666.0 } },
        EA = 2000.0,
        EIy = 600.0,
        EIz = 666.0,
        IsCalculable = true,
        Diagnostics = [new("test_warning", "test", false)],
        InputFingerprint = "input-fp",
        ResultFingerprint = "result-fp"
    };

    static string TempPath() => Path.Combine(Path.GetTempPath(), $"opencs-equivalent-{Guid.NewGuid():N}.db");

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
