using System;
using System.Collections.Generic;
using System.Linq;
using CScore;
using CScore.PlateRebar;
using Xunit;

namespace CScore.Tests.PlateRebar;

public class PlateRebarFieldResolverMeshTests
{
    static List<RebarZonePoint> Square(double u0, double v0, double size) =>
    [
        new() { U = u0, V = v0 },
        new() { U = u0 + size, V = v0 },
        new() { U = u0 + size, V = v0 + size },
        new() { U = u0, V = v0 + size },
    ];

    [Fact]
    public void ResolveMesh_TwoElementsSameLayout_ProduceSameFingerprint()
    {
        var field = new PlateRebarField([new PlateRebarLayer { Name = "Base", Asx = 0.001 }], []);
        var centroids = new (int ElementId, double U, double V)[] { (1, 0, 0), (2, 5, 5) };

        var result = PlateRebarFieldResolver.ResolveMesh(field, centroids);

        Assert.Equal(2, result.Count);
        Assert.Equal(result[0].LayoutFingerprint, result[1].LayoutFingerprint);
    }

    [Fact]
    public void ResolveMesh_ElementInsideZone_ProducesDifferentFingerprintThanOutside()
    {
        var zone = new RebarZone
        {
            Face = RebarFace.PlusN, Operation = RebarZoneOperation.Replace,
            Polygon = Square(0, 0, 2), Layout = new PlateRebarLayer { Asx = 0.002 },
        };
        var field = new PlateRebarField([new PlateRebarLayer { Asx = 0.001 }], [zone]);
        var centroids = new (int ElementId, double U, double V)[] { (1, 1, 1), (2, 10, 10) };

        var result = PlateRebarFieldResolver.ResolveMesh(field, centroids);

        Assert.NotEqual(
            result.Single(r => r.ElementId == 1).LayoutFingerprint,
            result.Single(r => r.ElementId == 2).LayoutFingerprint);
    }

    [Fact]
    public void ResolveMesh_DuplicateElementId_Throws()
    {
        var field = new PlateRebarField([], []);
        var centroids = new (int ElementId, double U, double V)[] { (1, 0, 0), (1, 1, 1) };

        Assert.Throws<ArgumentException>(() => PlateRebarFieldResolver.ResolveMesh(field, centroids));
    }

    [Fact]
    public void ResolveMesh_EmptyCentroids_ReturnsEmpty()
    {
        var field = new PlateRebarField([], []);

        var result = PlateRebarFieldResolver.ResolveMesh(field, Array.Empty<(int, double, double)>());

        Assert.Empty(result);
    }

    [Fact]
    public void ResolveMesh_PropagatesPriorityConflictDiagnosticPerElement()
    {
        var square = Square(0, 0, 2);
        var zoneA = new RebarZone { Face = RebarFace.PlusN, Priority = 1, Operation = RebarZoneOperation.Replace, Polygon = square, Layout = new() { Name = "A" } };
        var zoneB = new RebarZone { Face = RebarFace.PlusN, Priority = 1, Operation = RebarZoneOperation.Replace, Polygon = square, Layout = new() { Name = "B" } };
        var field = new PlateRebarField([], [zoneA, zoneB]);
        var centroids = new (int ElementId, double U, double V)[] { (42, 1, 1) };

        var result = PlateRebarFieldResolver.ResolveMesh(field, centroids);

        Assert.Single(result);
        Assert.Equal(42, result[0].ElementId);
        Assert.Contains(result[0].Layout.Diagnostics, d => d.Code == "plate_rebar_zone_priority_conflict");
    }
}
