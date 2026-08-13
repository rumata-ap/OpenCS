using System.Linq;
using CScore.PlateStrip;
using Xunit;

namespace CScore.Tests.PlateStrip;

public sealed class StripLoadConsistentNodalProjectionTests
{
    [Fact]
    public void Project_InvalidStationList_ReturnsDiagnostic()
    {
        var result = StripLoadConsistentNodalProjection.Project(
            new StripLoadSet([]), 6.0, [0.0]);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_load_invalid_stations");
    }

    [Fact]
    public void Project_StationsNotStartingAtZeroOrEndingAtOne_ReturnsDiagnostic()
    {
        var result = StripLoadConsistentNodalProjection.Project(
            new StripLoadSet([]), 6.0, [0.1, 1.0]);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_load_invalid_stations");
    }

    [Fact]
    public void Project_UnsortedStations_ReturnsDiagnostic()
    {
        var result = StripLoadConsistentNodalProjection.Project(
            new StripLoadSet([]), 6.0, [0.0, 0.7, 0.3, 1.0]);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_load_invalid_stations");
    }

    [Fact]
    public void Project_UniformTransverseLoad_SingleElement_MatchesFixedEndFormula()
    {
        var load = new StripLoad
        {
            Kind = StripLoadKind.DistributedUniform,
            SourceTag = "udl",
            StationStartFraction = 0.0,
            StationEndFraction = 1.0,
            QzKnM = -3.0
        };

        var result = StripLoadConsistentNodalProjection.Project(
            new StripLoadSet([load]), 6.0, [0.0, 1.0]);

        Assert.True(result.IsCalculable);
        var e = result.Elements[0];
        Assert.Equal(-9.0, e.Vz1, 9);   // Qz*L/2 = -3*6/2
        Assert.Equal(-9.0, e.My1, 9);   // Qz*L^2/12 = -3*36/12
        Assert.Equal(-9.0, e.Vz2, 9);
        Assert.Equal(9.0, e.My2, 9);    // -Qz*L^2/12
        Assert.Equal(0.0, e.N1, 9);
        Assert.Equal(0.0, e.Vy1, 9);
        Assert.Equal(0.0, e.Mz1, 9);
    }

    [Fact]
    public void Project_UniformAxialAndInPlaneLoad_MultiElement_SumsMatchTotal()
    {
        var load = new StripLoad
        {
            Kind = StripLoadKind.DistributedUniform,
            SourceTag = "combo",
            StationStartFraction = 0.0,
            StationEndFraction = 1.0,
            QxKnM = 2.0,
            QyKnM = -1.0
        };

        var result = StripLoadConsistentNodalProjection.Project(
            new StripLoadSet([load]), 6.0, [0.0, 0.5, 1.0]);

        Assert.True(result.IsCalculable);
        Assert.Equal(2, result.Elements.Count);
        double totalN = result.Elements.Sum(e => e.N1 + e.N2);
        double totalVy = result.Elements.Sum(e => e.Vy1 + e.Vy2);
        Assert.Equal(2.0 * 6.0, totalN, 9);   // Qx * lengthM
        Assert.Equal(-1.0 * 6.0, totalVy, 9); // Qy * lengthM
    }
}
