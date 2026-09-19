using CScore;
using CScore.ParametricRc;
using CScore.Sp63Shear;
using OpenCS.Services;
using OpenCS.Utilites;
using Xunit;

namespace OpenCS.Tests;

public sealed class ParametricRcSectionIntegrationTests
{
    [Fact]
    public void AllFiveShapesGenerateSaveReloadAndKeepFingerprintAndGeometryPolicy()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-parametric-five-{Guid.NewGuid():N}.db");
        try
        {
            var definitions = new[]
            {
                ParametricRcSectionDefinition.Rectangle(0.30, 0.50) with
                {
                    LowerRebar = ParametricLongitudinalLayer.Physical(2, 0.016, -0.21)
                },
                ParametricRcSectionDefinition.Tee(0.60, 0.80, 0.20, 0.16),
                ParametricRcSectionDefinition.IBeam(0.60, 0.80, 0.20, 0.16),
                ParametricRcSectionDefinition.Circle(0.60) with
                {
                    PolarRebar = new ParametricPolarRebar(8, 0.016, 0.24)
                },
                ParametricRcSectionDefinition.Annulus(0.60, 0.32) with
                {
                    PolarRebar = new ParametricPolarRebar(8, 0.016, 0.24)
                }
            };

            using (var db = new DatabaseService(path))
            {
                var service = new ParametricRcSectionProjectService(db);
                foreach (var definition in definitions)
                {
                    var section = new CrossSection();
                    var result = service.GenerateAndSave(section, definition);
                    Assert.Empty(result.Diagnostics);
                    Assert.NotEqual(0, section.Id);
                    Assert.NotNull(section.Areas[0].WKT);
                    Assert.NotEmpty(section.Areas[0].WKT!);
                    if (definition.Shape == ParametricRcShape.Annulus)
                        Assert.Single(section.Areas[0].Holes);
                }
            }

            using var reopened = new DatabaseService(path);
            reopened.LoadAll();
            Assert.Equal(5, reopened.CrossSections.Count);
            var reopenedService = new ParametricRcSectionProjectService(reopened);
            foreach (var section in reopened.CrossSections)
            {
                var state = reopenedService.GetState(section);
                Assert.Equal(ParametricRcDefinitionLoadStatus.Supported, state.LoadStatus);
                Assert.False(state.IsStale);
                Assert.NotNull(section.Areas[0].WKT);
                Assert.NotEmpty(section.Areas[0].WKT!);
            }
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void IdealizedLayerAndGeneratedCutsRoundTripWithAsw()
    {
        string path = Path.Combine(Path.GetTempPath(), $"opencs-parametric-rebar-{Guid.NewGuid():N}.db");
        try
        {
            var definition = ParametricRcSectionDefinition.Rectangle(0.30, 0.50) with
            {
                LowerRebar = ParametricLongitudinalLayer.Idealized(
                    0.0012, 0.020, -0.21, IdealizedRebarAxis.Mx),
                StirrupCuts = [new(ParametricStirrupZone.Body,
                    ParametricStirrupDirection.Vertical, 2, 0.008, 0.20, 0.03, 17)]
            };
            int sectionId;
            double asw;
            using (var db = new DatabaseService(path))
            {
                var section = new CrossSection();
                new ParametricRcSectionProjectService(db).GenerateAndSave(section, definition);
                sectionId = section.Id;
                asw = section.Areas.Where(a => a.Category == AreaCategory.Stirrups)
                    .SelectMany(a => a.Stirrups).SelectMany(g => g.Elements)
                    .Sum(e => StirrupResolver.BranchArea(e, ShearPlane.Vy));
            }

            using var reopened = new DatabaseService(path);
            reopened.LoadAll();
            var sectionAfterReload = Assert.Single(reopened.CrossSections, s => s.Id == sectionId);
            var idealized = Assert.Single(sectionAfterReload.Areas,
                a => a.RebarRepresentation == RebarRepresentation.IdealizedLayer);
            Assert.Equal(IdealizedRebarAxis.Mx, idealized.IdealizedAxis);
            var stirrupArea = Assert.Single(sectionAfterReload.Areas,
                a => a.Category == AreaCategory.Stirrups);
            Assert.Equal(asw, stirrupArea.Stirrups.SelectMany(g => g.Elements)
                .Sum(e => StirrupResolver.BranchArea(e, ShearPlane.Vy)), 12);
            Assert.All(stirrupArea.Stirrups.SelectMany(g => g.Elements),
                e => Assert.False(string.IsNullOrWhiteSpace(e.CenterlineContour.WKT)));
            Assert.False(new ParametricRcSectionProjectService(reopened)
                .GetState(sectionAfterReload).IsStale);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
