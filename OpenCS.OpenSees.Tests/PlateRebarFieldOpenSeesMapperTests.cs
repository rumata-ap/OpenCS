using System.Collections.Generic;
using CScore;
using CScore.PlateRebar;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Structural;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public sealed class PlateRebarFieldOpenSeesMapperTests
{
    [Fact]
    public void MapMesh_TwoElementsSameResolvedLayout_ShareOneSection()
    {
        var section = new PlateSection { H = 0.2, NLayers = 2 };
        var field = new PlateRebarField([], []);
        var centroids = new (int ElementId, double U, double V)[] { (1, 0, 0), (2, 5, 5) };

        var result = PlateRebarFieldOpenSeesMapper.MapMesh(
            section, field, ShellFrame.Identity, Resolver(), centroids);

        Assert.Single(result.Sections);
        Assert.Equal(result.ElementSectionTag[1], result.ElementSectionTag[2]);
    }

    [Fact]
    public void MapMesh_TwoElementsDifferentResolvedLayout_GetDistinctSections()
    {
        var section = new PlateSection { H = 0.2, NLayers = 2 };
        var zone = new RebarZone
        {
            Face = RebarFace.PlusN,
            Operation = RebarZoneOperation.Replace,
            Polygon = [new() { U = 0, V = 0 }, new() { U = 2, V = 0 }, new() { U = 2, V = 2 }, new() { U = 0, V = 2 }],
            Layout = new PlateRebarLayer { Asx = 0.001, Zsx = -0.09 },
        };
        var field = new PlateRebarField([], [zone]);
        var centroids = new (int ElementId, double U, double V)[] { (1, 1, 1), (2, 10, 10) };

        var result = PlateRebarFieldOpenSeesMapper.MapMesh(
            section, field, ShellFrame.Identity, Resolver(), centroids);

        Assert.Equal(2, result.Sections.Count);
        Assert.NotEqual(result.ElementSectionTag[1], result.ElementSectionTag[2]);
    }

    [Fact]
    public void MapMesh_PropagatesResolverDiagnosticsWithElementId()
    {
        var section = new PlateSection { H = 0.2, NLayers = 2 };
        var square = new List<RebarZonePoint> { new() { U = 0, V = 0 }, new() { U = 2, V = 0 }, new() { U = 2, V = 2 }, new() { U = 0, V = 2 } };
        var zoneA = new RebarZone { Face = RebarFace.PlusN, Priority = 1, Operation = RebarZoneOperation.Replace, Polygon = square, Layout = new() };
        var zoneB = new RebarZone { Face = RebarFace.PlusN, Priority = 1, Operation = RebarZoneOperation.Replace, Polygon = square, Layout = new() };
        var field = new PlateRebarField([], [zoneA, zoneB]);
        var centroids = new (int ElementId, double U, double V)[] { (7, 1, 1) };

        var result = PlateRebarFieldOpenSeesMapper.MapMesh(
            section, field, ShellFrame.Identity, Resolver(), centroids);

        Assert.Contains(result.RebarDiagnostics,
            d => d.ElementId == 7 && d.Diagnostic.Code == "plate_rebar_zone_priority_conflict");
    }

    private static IPlateSectionShellMaterialResolver Resolver() => new TestResolver();

    private sealed class TestResolver : IPlateSectionShellMaterialResolver
    {
        public NativeShellMaterialDefinition ResolveConcrete(int sourceMaterialId) =>
            new(1, $"concrete:{sourceMaterialId}", new ElasticIsotropicShellMaterialSpec(30e9, 0.2));

        public NativeShellMaterialDefinition ResolveRebar(int sourceMaterialId) =>
            new(2, $"rebar:{sourceMaterialId}", new PlateRebarShellMaterialSpec(500, 0));
    }
}
