using CScore;
using CScore.PlateRebar;
using Xunit;

namespace CScore.Tests.PlateRebar;

public class PlateRebarFieldGridResolverTests
{
    static Contour Square(double x0, double y0, double x1, double y1) =>
        new() { X = [x0, x1, x1, x0], Y = [y0, y0, y1, y1] };

    [Fact]
    public void Resolve_NoZones_ReturnsOnlyBackground()
    {
        var baseLayout = new List<PlateRebarLayer> { new() { Name = "Base", Asx = 0.001, Face = RebarFace.MinusN } };
        var field = new PlateRebarField(baseLayout, []);
        var hull = Square(0, 0, 10, 10);

        var combos = PlateRebarFieldGridResolver.Resolve(field, hull, [], 1.0);

        Assert.Single(combos);
        Assert.Equal(PlateRebarLayoutFingerprint.Compute(baseLayout), combos[0].Fingerprint);
    }

    [Fact]
    public void Resolve_ZoneCoveringHalf_ReturnsBackgroundAndZoneCombination()
    {
        var baseLayer = new PlateRebarLayer { Name = "Base", Asx = 0.001, Face = RebarFace.MinusN };
        var zoneLayer = new PlateRebarLayer { Name = "Zone", Asx = 0.002, Face = RebarFace.MinusN };
        var zone = new RebarZone
        {
            Name = "Z1", Face = RebarFace.MinusN, Operation = RebarZoneOperation.Replace,
            Layout = zoneLayer,
            Polygon = [new() { U = 0, V = 0 }, new() { U = 5, V = 0 }, new() { U = 5, V = 10 }, new() { U = 0, V = 10 }],
        };
        var field = new PlateRebarField([baseLayer], [zone]);
        var hull = Square(0, 0, 10, 10);

        var combos = PlateRebarFieldGridResolver.Resolve(field, hull, [], 1.0);

        Assert.Equal(2, combos.Count);
        var zoneCombo = Assert.Single(combos, c => c.Layers.Count == 1 && c.Layers[0].Name == "Zone");
        Assert.Equal(0.002, zoneCombo.Layers[0].Asx);
    }

    [Fact]
    public void Resolve_ZoneFullyInsideHole_IsNotDiscovered()
    {
        var baseLayer = new PlateRebarLayer { Name = "Base", Face = RebarFace.MinusN };
        var zone = new RebarZone
        {
            Name = "Z1", Face = RebarFace.MinusN, Operation = RebarZoneOperation.Replace,
            Layout = new PlateRebarLayer { Name = "Zone", Face = RebarFace.MinusN },
            Polygon = [new() { U = 0, V = 0 }, new() { U = 5, V = 0 }, new() { U = 5, V = 10 }, new() { U = 0, V = 10 }],
        };
        var field = new PlateRebarField([baseLayer], [zone]);
        var hull = Square(0, 0, 10, 10);
        var hole = Square(0, 0, 5, 10);

        var combos = PlateRebarFieldGridResolver.Resolve(field, hull, [hole], 1.0);

        Assert.Single(combos);
    }
}
