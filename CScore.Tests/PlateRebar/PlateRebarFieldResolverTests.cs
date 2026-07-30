using System.Collections.Generic;
using System.Linq;
using CScore;
using CScore.PlateRebar;
using Xunit;

namespace CScore.Tests.PlateRebar;

public class PlateRebarFieldResolverTests
{
    static List<RebarZonePoint> Square(double u0, double v0, double size) =>
    [
        new() { U = u0, V = v0 },
        new() { U = u0 + size, V = v0 },
        new() { U = u0 + size, V = v0 + size },
        new() { U = u0, V = v0 + size },
    ];

    [Fact]
    public void Resolve_NoZones_ReturnsBaseLayoutUnchanged()
    {
        var baseLayout = new List<PlateRebarLayer>
        {
            new() { Name = "Top", Face = RebarFace.PlusN, Asx = 0.001 },
            new() { Name = "Bottom", Face = RebarFace.MinusN, Asx = 0.002 },
        };
        var field = new PlateRebarField(baseLayout, []);

        var result = PlateRebarFieldResolver.Resolve(field, 0.5, 0.5);

        Assert.Equal(2, result.Layers.Count);
        Assert.Contains(result.Layers, l => l.Name == "Top");
        Assert.Contains(result.Layers, l => l.Name == "Bottom");
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Resolve_CentroidOutsideZone_ReturnsBaseLayoutOnly()
    {
        var baseLayout = new List<PlateRebarLayer> { new() { Name = "Base", Face = RebarFace.PlusN } };
        var zone = new RebarZone
        {
            Face = RebarFace.PlusN,
            Operation = RebarZoneOperation.Replace,
            Polygon = Square(0, 0, 1),
            Layout = new PlateRebarLayer { Name = "ZoneLayout" },
        };
        var field = new PlateRebarField(baseLayout, [zone]);

        var result = PlateRebarFieldResolver.Resolve(field, 5, 5);

        Assert.Single(result.Layers);
        Assert.Equal("Base", result.Layers[0].Name);
    }

    [Fact]
    public void Resolve_CentroidInsideZone_Replace_ReplacesBaseLayerOnMatchingFace()
    {
        var baseLayout = new List<PlateRebarLayer>
        {
            new() { Name = "Top", Face = RebarFace.PlusN },
            new() { Name = "Bottom", Face = RebarFace.MinusN },
        };
        var zone = new RebarZone
        {
            Face = RebarFace.PlusN,
            Operation = RebarZoneOperation.Replace,
            Polygon = Square(0, 0, 1),
            Layout = new PlateRebarLayer { Name = "ZoneTop" },
        };
        var field = new PlateRebarField(baseLayout, [zone]);

        var result = PlateRebarFieldResolver.Resolve(field, 0.5, 0.5);

        Assert.Equal(2, result.Layers.Count);
        Assert.Contains(result.Layers, l => l.Name == "ZoneTop");
        Assert.DoesNotContain(result.Layers, l => l.Name == "Top");
        Assert.Contains(result.Layers, l => l.Name == "Bottom");
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Resolve_CentroidInsideZone_Add_AppendsToBaseLayerOnMatchingFace()
    {
        var baseLayout = new List<PlateRebarLayer> { new() { Name = "Top", Face = RebarFace.PlusN } };
        var zone = new RebarZone
        {
            Face = RebarFace.PlusN,
            Operation = RebarZoneOperation.Add,
            Polygon = Square(0, 0, 1),
            Layout = new PlateRebarLayer { Name = "ExtraTop" },
        };
        var field = new PlateRebarField(baseLayout, [zone]);

        var result = PlateRebarFieldResolver.Resolve(field, 0.5, 0.5);

        Assert.Equal(2, result.Layers.Count);
        Assert.Contains(result.Layers, l => l.Name == "Top");
        Assert.Contains(result.Layers, l => l.Name == "ExtraTop");
    }

    [Fact]
    public void Resolve_ZoneOnOtherFace_DoesNotAffectRequestedFace()
    {
        var baseLayout = new List<PlateRebarLayer>
        {
            new() { Name = "Top", Face = RebarFace.PlusN },
            new() { Name = "Bottom", Face = RebarFace.MinusN },
        };
        var zone = new RebarZone
        {
            Face = RebarFace.MinusN,
            Operation = RebarZoneOperation.Replace,
            Polygon = Square(0, 0, 1),
            Layout = new PlateRebarLayer { Name = "ZoneBottom" },
        };
        var field = new PlateRebarField(baseLayout, [zone]);

        var result = PlateRebarFieldResolver.Resolve(field, 0.5, 0.5);

        Assert.Contains(result.Layers, l => l.Name == "Top");
        Assert.Contains(result.Layers, l => l.Name == "ZoneBottom");
        Assert.DoesNotContain(result.Layers, l => l.Name == "Bottom");
    }

    [Fact]
    public void Resolve_SameZone_MultipleCentroidsInside_ReturnsSameResult()
    {
        var baseLayout = new List<PlateRebarLayer> { new() { Name = "Top", Face = RebarFace.PlusN } };
        var zone = new RebarZone
        {
            Face = RebarFace.PlusN,
            Operation = RebarZoneOperation.Replace,
            Polygon = Square(0, 0, 2),
            Layout = new PlateRebarLayer { Name = "ZoneTop" },
        };
        var field = new PlateRebarField(baseLayout, [zone]);

        var r1 = PlateRebarFieldResolver.Resolve(field, 0.3, 0.3);
        var r2 = PlateRebarFieldResolver.Resolve(field, 1.5, 1.5);

        Assert.Equal(r1.Layers.Select(l => l.Name), r2.Layers.Select(l => l.Name));
    }
}
