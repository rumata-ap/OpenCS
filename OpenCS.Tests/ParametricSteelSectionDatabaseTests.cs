using CScore;
using CScore.ParametricSteel;
using OpenCS.Models;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

public sealed class ParametricSteelSectionDatabaseTests
{
    static string TempDb(string name) =>
        Path.Combine(Path.GetTempPath(), $"opencs-parametric-steel-{name}-{Guid.NewGuid():N}.db");

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    static (CrossSection Section, ParametricSteelSectionRecord Record) Generate(
        ParametricSteelSectionDefinition definition)
    {
        var generated = ParametricSteelSectionGenerator.Generate(definition);
        Assert.Empty(generated.Diagnostics);
        var record = new ParametricSteelSectionRecord(0, 1, 1, "{\"kind\":\"Box\"}",
            ParametricSteelSectionFingerprint.Compute(generated.Section, 1));
        return (generated.Section, record);
    }

    [Fact]
    public void SaveRoundTripsSourceJunctionAndHole()
    {
        string path = TempDb("roundtrip");
        try
        {
            var (section, record) = Generate(ParametricSteelSectionDefinition.WeldedBox(0.30, 0.20, 0.008, 0.010));
            int areaId;
            using (var db = new DatabaseService(path))
            {
                db.SaveParametricSteelCrossSection(section, record);
                areaId = section.Areas.Single().Id;
                Assert.NotEqual(0, areaId);
            }

            using var loaded = new DatabaseService(path);
            loaded.LoadAll();
            var restored = Assert.Single(loaded.CrossSections);
            var area = Assert.Single(restored.Areas);
            Assert.Contains(area.Contours, c => c.Type == ContourType.Hole);
            var stored = loaded.TryGetParametricSteelSectionRecord(restored.Id)!;
            Assert.Equal(restored.Id, stored.SectionId);
            Assert.Equal(record.DefinitionJson, stored.DefinitionJson);
            Assert.Equal(record.GeneratedFingerprint, stored.GeneratedFingerprint);
            Assert.Equal(stored.GeneratedFingerprint, ParametricSteelSectionFingerprint.Compute(restored, 1));
            Assert.Equal([areaId], loaded.GetParametricSteelGeneratedAreaIds(restored.Id));
            Assert.Null(loaded.TryGetParametricRcSectionRecord(restored.Id));
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void ResaveDeletesPreviousUnusedGeneratedArea()
    {
        string path = TempDb("resave");
        try
        {
            using var db = new DatabaseService(path);
            var (section, record) = Generate(ParametricSteelSectionDefinition.Plate(0.20, 0.01));
            db.SaveParametricSteelCrossSection(section, record);
            int oldAreaId = section.Areas.Single().Id;

            var (second, secondRecord) = Generate(ParametricSteelSectionDefinition.Plate(0.30, 0.01));
            section.Areas = second.Areas;
            db.SaveParametricSteelCrossSection(section, secondRecord with { SectionId = section.Id });

            int newAreaId = section.Areas.Single().Id;
            Assert.NotEqual(oldAreaId, newAreaId);
            Assert.Equal([newAreaId], db.GetParametricSteelGeneratedAreaIds(section.Id));

            using var reloaded = new DatabaseService(path);
            reloaded.LoadAll();
            Assert.DoesNotContain(reloaded.MaterialAreas, a => a.Id == oldAreaId);
            Assert.Contains(reloaded.MaterialAreas, a => a.Id == newAreaId);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void DetachKeepsSectionAndAreas()
    {
        string path = TempDb("detach");
        try
        {
            using var db = new DatabaseService(path);
            var (section, record) = Generate(ParametricSteelSectionDefinition.Pipe(0.159, 0.006));
            db.SaveParametricSteelCrossSection(section, record);

            db.DetachParametricSteelSection(section.Id);

            Assert.Null(db.TryGetParametricSteelSectionRecord(section.Id));
            Assert.Empty(db.GetParametricSteelGeneratedAreaIds(section.Id));
            using var reloaded = new DatabaseService(path);
            reloaded.LoadAll();
            Assert.Single(Assert.Single(reloaded.CrossSections).Areas);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void DeleteCrossSectionRemovesSteelSource()
    {
        string path = TempDb("delete");
        try
        {
            using var db = new DatabaseService(path);
            var (section, record) = Generate(ParametricSteelSectionDefinition.RoundBar(0.03));
            db.SaveParametricSteelCrossSection(section, record);
            int id = section.Id;

            db.DeleteCrossSection(section);

            Assert.Null(db.TryGetParametricSteelSectionRecord(id));
            Assert.Empty(db.GetParametricSteelGeneratedAreaIds(id));
        }
        finally { TryDelete(path); }
    }
}
