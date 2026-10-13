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

    static PlateStripBeamAnalogy Angled(double endU, double endV) => new()
    {
        Id = "strip", SourceRegionId = 1, ExplicitWidthM = 1.0, Fingerprint = "fp",
        Geometry = new PlateStripGeometry
        {
            CenterLine = [new PlanarPoint2D(0, 0), new PlanarPoint2D(endU, endV)],
            LengthM = Math.Sqrt(endU * endU + endV * endV)
        }
    };

    sealed class FixedAxisSource(double axisDeg, double asx) : IStripRebarSource
    {
        public string Key => "test";
        public bool IsSpatial => false;
        public StripRebarSample Resolve(double u, double v) => new([Layer(asx)], [], axisDeg);
    }

    [Fact]
    public void StripAlongRegionX_IsAccepted_AlsoWhenDirectionReversed()
    {
        var field = new PlateRebarField([Layer(0.002)], []);

        Assert.True(PlateStripFiberLayouts.Resolve(field, Angled(8, 0), Stations, 2).IsCalculable);
        Assert.True(PlateStripFiberLayouts.Resolve(field, Angled(-8, 0), Stations, 2).IsCalculable);
    }

    [Fact]
    public void StripAtAngleToRebarAxis_IsBlockedWithDiagnostic()
    {
        var field = new PlateRebarField([Layer(0.002)], []);

        var layouts = PlateStripFiberLayouts.Resolve(field, Angled(6, 6), Stations, 2);

        Assert.False(layouts.IsCalculable);
        var d = Assert.Single(layouts.Diagnostics, d => d.Code == "plate_strip_rebar_axis_mismatch");
        Assert.True(d.IsError);
        Assert.Contains("45,0°", d.Message.Replace('.', ','));
    }

    [Fact]
    public void RebarAxisFromSource_IsComparedWithStripAxis()
    {
        // Ось армирования КЭ повёрнута на 30°; полоса вдоль неё допустима, вдоль оси U региона — нет.
        var along = Angled(Math.Cos(Math.PI / 6) * 8, Math.Sin(Math.PI / 6) * 8);

        Assert.True(PlateStripFiberLayouts.Resolve(new FixedAxisSource(30, 0.002), along, Stations, 2).IsCalculable);
        Assert.False(PlateStripFiberLayouts.Resolve(new FixedAxisSource(30, 0.002), Angled(8, 0), Stations, 2)
            .IsCalculable);
    }

    [Fact]
    public void LayerWithAngle_IsBlocked()
    {
        var angled = Layer(0.002);
        angled.Angle = 15;

        var layouts = PlateStripFiberLayouts.Resolve(new PlateRebarField([angled], []), Angled(8, 0), Stations, 2);

        Assert.False(layouts.IsCalculable);
        Assert.Contains(layouts.Diagnostics, d => d.Code == "plate_strip_rebar_layer_angle");
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
