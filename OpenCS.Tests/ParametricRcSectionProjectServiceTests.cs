using CScore;
using CScore.ParametricRc;
using OpenCS.Models;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

public sealed class ParametricRcSectionProjectServiceTests
{
    [Fact]
    public void GeneratedSourceIsSupportedAndManualGeometryChangeMakesItStale()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-parametric-state-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var section = new CrossSection();
            var definition = ParametricRcSectionDefinition.Rectangle(0.30, 0.50);
            var service = new ParametricRcSectionProjectService(db);

            service.GenerateAndSave(section, definition);

            var supported = service.GetState(section);
            Assert.Equal(ParametricRcDefinitionLoadStatus.Supported, supported.LoadStatus);
            Assert.False(supported.IsStale);

            section.Areas[0].Hull!.X[0] += 0.001;
            section.Areas[0].SetWKT();

            var stale = service.GetState(section);
            Assert.Equal(ParametricRcDefinitionLoadStatus.Supported, stale.LoadStatus);
            Assert.True(stale.IsStale);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void GenerateAndSaveAssignsSelectedMaterialsToGeneratedAreas()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-parametric-materials-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var concrete = new Material { Tag = "B25", Type = MatType.Concrete };
            var reinforcement = new Material { Tag = "A500C", Type = MatType.ReSteelF };
            db.AddMaterial(concrete);
            db.AddMaterial(reinforcement);

            var definition = ParametricRcSectionDefinition.Rectangle(0.30, 0.50) with
            {
                ConcreteMaterialId = concrete.Id,
                LongitudinalMaterialId = reinforcement.Id,
                LowerRebar = ParametricLongitudinalLayer.Physical(2, 0.016, -0.21),
                StirrupCuts =
                [
                    new(ParametricStirrupZone.Body, ParametricStirrupDirection.Vertical,
                        2, 0.008, 0.20, 0.03, reinforcement.Id)
                ]
            };
            var section = new CrossSection();

            var result = new ParametricRcSectionProjectService(db).GenerateAndSave(section, definition);

            Assert.Empty(result.Diagnostics);
            Assert.Same(concrete, section.Areas.Single(a => a.Category == AreaCategory.Region).Material);
            Assert.Same(reinforcement, section.Areas.Single(a => a.Category == AreaCategory.RebarGroup).Material);
            Assert.Same(reinforcement, section.Areas.Single(a => a.Category == AreaCategory.Stirrups).Material);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void FutureDefinitionVersionRemainsVisibleWithoutDeserializationOrOverwrite()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-parametric-future-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var section = new CrossSection();
            var definition = ParametricRcSectionDefinition.Rectangle(0.30, 0.50);
            var service = new ParametricRcSectionProjectService(db);
            service.GenerateAndSave(section, definition);

            var original = db.TryGetParametricRcSectionRecord(section.Id)!;
            db.SaveParametricRcSectionRecord(original with { DefinitionVersion = 999, DefinitionJson = "future-payload" });

            var state = service.GetState(section);

            Assert.Equal(ParametricRcDefinitionLoadStatus.UnsupportedFutureVersion, state.LoadStatus);
            Assert.False(state.IsStale);
            Assert.Equal("future-payload", db.TryGetParametricRcSectionRecord(section.Id)!.DefinitionJson);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void InvalidDefinitionJsonGetsInvalidStatusWithoutOverwrite()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-parametric-invalid-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var section = new CrossSection();
            var service = new ParametricRcSectionProjectService(db);
            service.GenerateAndSave(section, ParametricRcSectionDefinition.Rectangle(0.30, 0.50));
            var original = db.TryGetParametricRcSectionRecord(section.Id)!;
            db.SaveParametricRcSectionRecord(original with { DefinitionJson = "{not-json" });

            var state = service.GetState(section);

            Assert.Equal(ParametricRcDefinitionLoadStatus.InvalidJson, state.LoadStatus);
            Assert.Equal("{not-json", db.TryGetParametricRcSectionRecord(section.Id)!.DefinitionJson);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void NullDefinitionJsonGetsInvalidStatusWithoutOverwrite()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-parametric-null-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var section = new CrossSection();
            var service = new ParametricRcSectionProjectService(db);
            service.GenerateAndSave(section, ParametricRcSectionDefinition.Rectangle(0.30, 0.50));
            var original = db.TryGetParametricRcSectionRecord(section.Id)!;
            db.SaveParametricRcSectionRecord(original with { DefinitionJson = "null" });

            var state = service.GetState(section);

            Assert.Equal(ParametricRcDefinitionLoadStatus.InvalidJson, state.LoadStatus);
            Assert.Equal("null", db.TryGetParametricRcSectionRecord(section.Id)!.DefinitionJson);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void InvalidStirrupCenterlineRollsBackGeneratedRowsAndIds()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-parametric-rollback-{Guid.NewGuid():N}.db");
        try
        {
            using var db = new DatabaseService(path);
            var concrete = new MaterialArea { Category = AreaCategory.Region };
            concrete.Hull = new Contour([-0.15, 0.15, 0.15, -0.15, -0.15],
                [-0.25, -0.25, 0.25, 0.25, -0.25], "бетон") { Type = ContourType.Hull };
            concrete.SetWKT();
            var stirrups = new MaterialArea { Category = AreaCategory.Stirrups, MaterialId = 1 };
            var group = new StirrupGroup { MaterialId = 1, SpacingM = 0.20 };
            group.Elements.Add(new StirrupElement
            {
                CenterlineContour = new Contour(),
                BarAreaM2 = 0.00005,
                BarDiameterM = 0.008
            });
            stirrups.Stirrups.Add(group);
            var section = new CrossSection { Areas = [concrete, stirrups] };
            var record = new ParametricRcSectionRecord(0, 1, 1, "{}", "fingerprint");

            Assert.Throws<InvalidOperationException>((Action)(() => db.SaveParametricCrossSection(section, record)));

            Assert.Equal(0, section.Id);
            Assert.All(section.Areas, area => Assert.Equal(0, area.Id));
            Assert.Empty(db.CrossSections);
            Assert.Empty(db.MaterialAreas);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
