using CScore.Planar;
using CScore.PlateRebar;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

/// <summary>Срез 8c: армирование полосы по элементам и точкам ширины из PlateRebarField.</summary>
public sealed class PlateStripFiberLayoutsTests
{
    static readonly double[] Stations = [0.0, 0.25, 0.5, 0.75, 1.0];

    static PlateStripBeamAnalogy Analogy() => new()
    {
        Id = "strip", SourceRegionId = 1, ExplicitWidthM = 2.0, Fingerprint = "fp",
        Geometry = new PlateStripGeometry
        {
            CenterLine = [new PlanarPoint2D(0, 0), new PlanarPoint2D(8, 0)],
            LeftBoundary = [new PlanarPoint2D(0, 1), new PlanarPoint2D(8, 1)],
            RightBoundary = [new PlanarPoint2D(0, -1), new PlanarPoint2D(8, -1)],
            Polygon = [new(0, -1), new(8, -1), new(8, 1), new(0, 1)],
            LengthM = 8
        }
    };

    static PlateRebarLayer Layer(double asx) =>
        new() { InputMode = "direct", Asx = asx, Zsx = -0.1, Face = RebarFace.MinusN };

    [Fact]
    public void WithoutZones_AllElementsAndWidthPointsShareBaseLayout()
    {
        var field = new PlateRebarField([Layer(0.002)], []);
        // Геометрия границ не нужна: без зон пространственный резолв не вызывается.
        var analogy = new PlateStripBeamAnalogy
        {
            Id = "s", SourceRegionId = 1, ExplicitWidthM = 1.0, Fingerprint = "x",
            Geometry = new PlateStripGeometry { LengthM = 8 }
        };

        var layouts = PlateStripFiberLayouts.Resolve(field, analogy, Stations, widthPointCount: 2);

        Assert.True(layouts.IsCalculable);
        Assert.Equal(4, layouts.PerElement.Count);
        Assert.All(layouts.PerElement, e =>
        {
            Assert.Equal(2, e.Count);
            Assert.All(e, p => Assert.Equal(0.002, Assert.Single(p).Asx));
        });
    }

    [Fact]
    public void Zone_ReplacesLayoutOnlyWhereElementMidpointFallsInside()
    {
        // Зона покрывает u ∈ [4, 8]: элементы 3 и 4 (середины 5 и 7), по ширине — вся полоса.
        var zone = new RebarZone
        {
            Name = "support", Face = RebarFace.MinusN, Operation = RebarZoneOperation.Replace, Priority = 1,
            Polygon = [new() { U = 4, V = -2 }, new() { U = 9, V = -2 }, new() { U = 9, V = 2 }, new() { U = 4, V = 2 }],
            Layout = Layer(0.006)
        };
        var field = new PlateRebarField([Layer(0.002)], [zone]);

        var layouts = PlateStripFiberLayouts.Resolve(field, Analogy(), Stations, widthPointCount: 2);

        Assert.True(layouts.IsCalculable, string.Join("; ", layouts.Diagnostics.Select(d => d.Message)));
        double[] asx = layouts.PerElement.Select(e => e[0].Sum(l => l.Asx)).ToArray();
        Assert.Equal([0.002, 0.002, 0.006, 0.006], asx);
    }

    [Fact]
    public void BrokenGeometry_IsReportedAsErrorNotThrown()
    {
        var zone = new RebarZone
        {
            Face = RebarFace.MinusN, Operation = RebarZoneOperation.Replace,
            Polygon = [new() { U = 0, V = -1 }, new() { U = 1, V = -1 }, new() { U = 1, V = 1 }],
            Layout = Layer(0.006)
        };
        var analogy = new PlateStripBeamAnalogy
        {
            Id = "s", SourceRegionId = 1, ExplicitWidthM = 1.0, Fingerprint = "x",
            Geometry = new PlateStripGeometry { LengthM = 8 }
        };

        var layouts = PlateStripFiberLayouts.Resolve(
            new PlateRebarField([], [zone]), analogy, Stations, widthPointCount: 1);

        Assert.False(layouts.IsCalculable);
        Assert.Contains(layouts.Diagnostics, d => d.Code == "plate_strip_fiber_layout_sampling_failed");
    }
}
