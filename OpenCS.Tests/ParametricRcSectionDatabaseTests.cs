using CScore.ParametricRc;
using OpenCS.Models;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

public sealed class ParametricRcSectionDatabaseTests
{
    [Fact]
    public void SaveParametricCrossSectionRoundTripsSourceJunctionAndFingerprint()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-parametric-{Guid.NewGuid():N}.db");
        try
        {
            var generated = ParametricRcSectionGenerator.Generate(
                ParametricRcSectionDefinition.Rectangle(0.30, 0.50) with
                {
                    LowerRebar = ParametricLongitudinalLayer.Physical(2, 0.016, -0.21)
                });
            var record = new ParametricRcSectionRecord(
                0, 1, 1, "{\"Shape\":\"Rectangle\"}",
                ParametricRcSectionFingerprint.Compute(generated.Section, 1));

            using (var db = new DatabaseService(path))
                db.SaveParametricCrossSection(generated.Section, record);

            using var loaded = new DatabaseService(path);
            loaded.LoadAll();
            var section = Assert.Single(loaded.CrossSections);
            Assert.NotEmpty(section.Areas);
            Assert.Equal(section.Id, loaded.TryGetParametricRcSectionRecord(section.Id)!.SectionId);
            Assert.Equal(record.GeneratedFingerprint,
                loaded.TryGetParametricRcSectionRecord(section.Id)!.GeneratedFingerprint);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
