using System.Text.Json;
using CScore;
using CScore.ParametricSteel;
using CScore.Sp16;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

public sealed class ParametricSteelSectionProjectServiceTests
{
    static string TempDb(string name) =>
        Path.Combine(Path.GetTempPath(), $"opencs-parametric-steel-service-{name}-{Guid.NewGuid():N}.db");

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    static ParametricSteelSectionDefinition BentChannel() =>
        ParametricSteelSectionDefinition.BentChannel(0.20, 0.08, 0.006, 0.009) with
        {
            Catalog = new ParametricSteelCatalogRef("Швеллеры", "ГОСТ 8278-83", "200×80×6"),
            Flipped = true,
            Tag = "Гн. швеллер"
        };

    [Fact]
    public void GenerateAndSaveAssignsMaterialBindingAndSupportedState()
    {
        string path = TempDb("generate");
        try
        {
            using var db = new DatabaseService(path);
            var steel = new Material { Tag = "С245", Type = MatType.Steel };
            db.AddMaterial(steel);
            var section = new CrossSection();
            var service = new ParametricSteelSectionProjectService(db);

            var result = service.GenerateAndSave(section, BentChannel() with { MaterialId = steel.Id });

            Assert.Empty(result.Diagnostics);
            Assert.Same(section, result.Section);
            Assert.NotEqual(0, section.Id);
            Assert.Equal("Гн. швеллер", section.Tag);
            Assert.Same(steel, Assert.Single(section.Areas).Material);
            var profile = section.TryGetParametricSteelProfile();
            Assert.NotNull(profile);
            Assert.Equal(SteelProfileKind.Channel, profile.Kind);
            Assert.Equal(SteelFabrication.Bent, profile.Fabrication);
            Assert.True(profile.Flipped);

            var state = service.GetState(section);
            Assert.Equal(ParametricSteelDefinitionLoadStatus.Supported, state.LoadStatus);
            Assert.False(state.IsStale);
            Assert.Equal(ParametricSteelSectionProjectService.GeneratorVersion, state.Record!.GeneratorVersion);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void InvalidDefinitionWritesNothing()
    {
        string path = TempDb("invalid");
        try
        {
            using var db = new DatabaseService(path);
            var section = new CrossSection();

            var result = new ParametricSteelSectionProjectService(db).GenerateAndSave(
                section, ParametricSteelSectionDefinition.Pipe(0.10, 0.06));

            Assert.NotEmpty(result.Diagnostics);
            Assert.Equal(0, section.Id);
            Assert.Null(section.ParametricSteel);
            Assert.Empty(db.CrossSections);
            Assert.Empty(db.MaterialAreas);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void DefinitionJsonIsCamelCaseWithStringEnumsAndRoundTrips()
    {
        string path = TempDb("json");
        try
        {
            using var db = new DatabaseService(path);
            var section = new CrossSection();
            var service = new ParametricSteelSectionProjectService(db);
            var definition = BentChannel();
            service.GenerateAndSave(section, definition);

            string json = db.TryGetParametricSteelSectionRecord(section.Id)!.DefinitionJson;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.Equal("Channel", root.GetProperty("kind").GetString());
            Assert.Equal("Bent", root.GetProperty("fabrication").GetString());
            Assert.Equal(0.20, root.GetProperty("h").GetDouble());
            Assert.Equal("ГОСТ 8278-83", root.GetProperty("catalog").GetProperty("standard").GetString());
            Assert.False(root.TryGetProperty("Kind", out _));

            Assert.True(service.TryGetDefinition(section, out var loaded));
            Assert.Equal(definition, loaded);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void ManualGeometryChangeMakesSourceStaleAndDropsProfile()
    {
        string path = TempDb("stale");
        try
        {
            using var db = new DatabaseService(path);
            var section = new CrossSection();
            var service = new ParametricSteelSectionProjectService(db);
            service.GenerateAndSave(section, ParametricSteelSectionDefinition.WeldedIBeam(0.40, 0.20, 0.012, 0, 0, 0.008));

            section.Areas[0].Hull!.X[0] += 0.001;
            section.Areas[0].SetWKT();

            var state = service.GetState(section);
            Assert.Equal(ParametricSteelDefinitionLoadStatus.Supported, state.LoadStatus);
            Assert.True(state.IsStale);
            Assert.Null(section.TryGetParametricSteelProfile());

            service.ApplyBindings([section]);
            Assert.Null(section.ParametricSteel);

            var restored = service.Restore(section);
            Assert.False(restored.IsStale);
            Assert.NotNull(section.TryGetParametricSteelProfile());
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void FutureVersionAndInvalidJsonStayUntouchedAndUnbound()
    {
        string path = TempDb("future");
        try
        {
            using var db = new DatabaseService(path);
            var service = new ParametricSteelSectionProjectService(db);
            var future = new CrossSection();
            var broken = new CrossSection();
            var nullJson = new CrossSection();
            service.GenerateAndSave(future, ParametricSteelSectionDefinition.Plate(0.2, 0.01));
            service.GenerateAndSave(broken, ParametricSteelSectionDefinition.Plate(0.3, 0.01));
            service.GenerateAndSave(nullJson, ParametricSteelSectionDefinition.Plate(0.4, 0.01));
            db.SaveParametricSteelSectionRecord(db.TryGetParametricSteelSectionRecord(future.Id)! with
                { DefinitionVersion = 999, DefinitionJson = "future-payload" });
            db.SaveParametricSteelSectionRecord(db.TryGetParametricSteelSectionRecord(broken.Id)! with
                { DefinitionJson = "{not-json" });
            db.SaveParametricSteelSectionRecord(db.TryGetParametricSteelSectionRecord(nullJson.Id)! with
                { DefinitionJson = "null" });

            Assert.Equal(ParametricSteelDefinitionLoadStatus.UnsupportedFutureVersion, service.GetState(future).LoadStatus);
            Assert.False(service.GetState(future).IsStale);
            Assert.Equal(ParametricSteelDefinitionLoadStatus.InvalidJson, service.GetState(broken).LoadStatus);
            Assert.Equal(ParametricSteelDefinitionLoadStatus.InvalidJson, service.GetState(nullJson).LoadStatus);
            Assert.False(service.TryGetDefinition(future, out _));
            Assert.False(service.TryGetDefinition(broken, out _));

            service.ApplyBindings([future, broken, nullJson]);
            Assert.Null(future.ParametricSteel);
            Assert.Null(broken.ParametricSteel);
            Assert.Null(nullJson.ParametricSteel);

            service.Restore(future);
            Assert.Equal("future-payload", db.TryGetParametricSteelSectionRecord(future.Id)!.DefinitionJson);
            Assert.Equal("{not-json", db.TryGetParametricSteelSectionRecord(broken.Id)!.DefinitionJson);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void ApplyBindingsRestoresProfileAfterReloadAndIgnoresOrdinarySections()
    {
        string path = TempDb("reload");
        try
        {
            using (var db = new DatabaseService(path))
            {
                var service = new ParametricSteelSectionProjectService(db);
                service.GenerateAndSave(new CrossSection(), ParametricSteelSectionDefinition.BentBox(0.20, 0.10, 0.006, 0.006));
                service.GenerateAndSave(new CrossSection(),
                    ParametricSteelSectionDefinition.RolledIBeam(0.298, 0.149, 0.0055, 0.008, 0.013));
            }

            using var loaded = new DatabaseService(path);
            loaded.LoadAll();
            var ordinary = new CrossSection { Id = 12345 };
            var sections = loaded.CrossSections.Append(ordinary).ToList();
            Assert.All(loaded.CrossSections, s => Assert.Null(s.ParametricSteel));

            new ParametricSteelSectionProjectService(loaded).ApplyBindings(sections);

            var kinds = loaded.CrossSections.Select(s => s.TryGetParametricSteelProfile()!.Kind).ToList();
            Assert.Equal([SteelProfileKind.Box, SteelProfileKind.IBeam], kinds);
            var box = loaded.CrossSections[0].TryGetParametricSteelProfile()!;
            Assert.Equal(0.012, box.R, 12);
            Assert.Null(ordinary.ParametricSteel);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void DetachRemovesSourceAndBinding()
    {
        string path = TempDb("detach");
        try
        {
            using var db = new DatabaseService(path);
            var section = new CrossSection();
            var service = new ParametricSteelSectionProjectService(db);
            service.GenerateAndSave(section, ParametricSteelSectionDefinition.RoundBar(0.03));

            service.Detach(section);

            Assert.Null(section.ParametricSteel);
            Assert.Equal(ParametricSteelDefinitionLoadStatus.Missing, service.GetState(section).LoadStatus);
            Assert.Single(section.Areas);
        }
        finally { TryDelete(path); }
    }
}
