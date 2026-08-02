using CScore.Fem;
using CScore.Planar;
using Xunit;

namespace CScore.Tests.Planar;

public sealed class PlanarConnectionTests
{
    [Fact]
    public void Validate_RejectsUnsavedSameAndUnknownRegions()
    {
        var connection = new PlanarConnection
        {
            Id = 0,
            SideA = new ConnectionLocus(10, [new(1, 1), new(1, 3)]),
            SideB = new ConnectionLocus(10, [new(1, 1), new(1, 3)])
        };

        var diagnostics = PlanarConnectionValidator.Validate(
            connection,
            new Dictionary<int, PlanarRegion> { [10] = Region(10) });

        Assert.Contains(diagnostics, item => item.Code == "planar_connection_id_invalid");
        Assert.Contains(diagnostics, item => item.Code == "planar_connection_same_region");
    }

    [Fact]
    public void Validate_RejectsMalformedLocusAndHostViolations()
    {
        var regionA = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] });
        regionA.Id = 10;
        regionA.Contours.Add(new Contour
        {
            Type = ContourType.Hole,
            X = [1, 3, 3, 1, 1],
            Y = [1, 1, 3, 3, 1]
        });
        var regionB = Region(20);
        var connection = new PlanarConnection
        {
            Id = 7,
            SideA = new ConnectionLocus(10, [new(0, 0), new(2, 2), new(0, 2), new(2, 0)]),
            SideB = new ConnectionLocus(20, [new(0, 0), new(4, 0)])
        };

        var diagnostics = PlanarConnectionValidator.Validate(
            connection,
            new Dictionary<int, PlanarRegion> { [10] = regionA, [20] = regionB });

        Assert.Contains(diagnostics, item => item.Code == "planar_connection_locus_invalid");
    }

    [Fact]
    public void Validate_RejectsDifferentSpatialLocus()
    {
        var connection = new PlanarConnection
        {
            Id = 7,
            SideA = new ConnectionLocus(10, [new(1, 1), new(1, 3)]),
            SideB = new ConnectionLocus(20, [new(2, 1), new(2, 3)])
        };

        var diagnostics = PlanarConnectionValidator.Validate(
            connection,
            new Dictionary<int, PlanarRegion> { [10] = Region(10), [20] = Region(20) });

        Assert.Contains(diagnostics, item => item.Code == "planar_connection_locus_space_mismatch");
    }

    [Fact]
    public void Validate_AcceptsReversedLocusOnInclinedFrame()
    {
        var regionA = Region(10);
        var regionB = PlanarRegion.CreateFromContour(
                new Contour { X = [0, 4, 4, 0], Y = [-1, -1, 1, 1] },
            frame: new Frame3D(
                new(2, 0, 0),
                new(0, 1, 0),
                new(0, 0, 1),
                new(1, 0, 0)));
        regionB.Id = 20;

        var connection = new PlanarConnection
        {
            Id = 7,
            MeshMode = PlanarConnectionMeshMode.EmbeddedLocus,
            SideA = new ConnectionLocus(10, [new(2, 1), new(2, 3)]),
            SideB = new ConnectionLocus(20, [new(3, 0), new(1, 0)])
        };

        var diagnostics = PlanarConnectionValidator.Validate(
            connection,
            new Dictionary<int, PlanarRegion> { [10] = regionA, [20] = regionB });

        Assert.DoesNotContain(diagnostics, item => item.IsError);
    }

    [Fact]
    public void Fingerprint_IsDeterministicAndChangesForContractInputs()
    {
        var first = Connection(PlanarConnectionMeshMode.EmbeddedLocus);
        var same = Connection(PlanarConnectionMeshMode.EmbeddedLocus);

        Assert.Equal(
            PlanarConnectionFingerprint.Compute(first),
            PlanarConnectionFingerprint.Compute(same));
        Assert.NotEqual(
            PlanarConnectionFingerprint.Compute(first),
            PlanarConnectionFingerprint.Compute(Connection(PlanarConnectionMeshMode.IndependentMpc)));
        Assert.NotEqual(
            PlanarConnectionFingerprint.Compute(first),
            PlanarConnectionFingerprint.Compute(Connection(PlanarConnectionMeshMode.EmbeddedLocus, 1e-7)));
    }

    [Fact]
    public void Graph_RejectsDuplicateConnectionIds()
    {
        var graph = new PlanarConnectionGraph
        {
            Connections = [Connection(PlanarConnectionMeshMode.EmbeddedLocus), Connection(PlanarConnectionMeshMode.IndependentMpc)]
        };

        var diagnostics = graph.Validate(new Dictionary<int, PlanarRegion>
        {
            [10] = Region(10),
            [20] = RegionB(20)
        });

        Assert.Contains(diagnostics, item => item.Code == "planar_connection_id_duplicate");
    }

    [Fact]
    public void Graph_RejectsDuplicateSpatialLocusWithReversedPoints()
    {
        var first = Connection(PlanarConnectionMeshMode.EmbeddedLocus);
        var second = Connection(PlanarConnectionMeshMode.EmbeddedLocus);
        second.Id = 8;
        second.SideA = new ConnectionLocus(10, second.SideA.Points.Reverse().ToArray());
        second.SideB = new ConnectionLocus(20, second.SideB.Points.Reverse().ToArray());
        var graph = new PlanarConnectionGraph { Connections = [first, second] };

        var diagnostics = graph.Validate(new Dictionary<int, PlanarRegion>
        {
            [10] = Region(10),
            [20] = RegionB(20)
        });

        Assert.Contains(diagnostics, item => item.Code == "planar_connection_spatial_duplicate");
    }

    static PlanarConnection Connection(
        PlanarConnectionMeshMode mode,
        double tolerance = 1e-8) => new()
    {
        Id = 7,
        Tag = "plate-wall",
        MeshMode = mode,
        MatchingToleranceM = tolerance,
        SideA = new ConnectionLocus(10, [new(2, 1), new(2, 3)]),
        SideB = new ConnectionLocus(20, [new(3, 0), new(1, 0)])
    };

    static PlanarRegion Region(int id)
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 4, 4, 0], Y = [0, 0, 4, 4] });
        region.Id = id;
        return region;
    }

    static PlanarRegion RegionB(int id)
    {
        var region = PlanarRegion.CreateFromContour(
            new Contour { X = [0, 4, 4, 0], Y = [-1, -1, 1, 1] },
            frame: new Frame3D(
                new(2, 0, 0),
                new(0, 1, 0),
                new(0, 0, 1),
                new(1, 0, 0)));
        region.Id = id;
        return region;
    }
}
