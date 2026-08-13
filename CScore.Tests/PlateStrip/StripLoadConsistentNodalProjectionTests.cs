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

    [Fact]
    public void Project_PointAxialLoad_MidElement_LinearSplit()
    {
        var load = new StripLoad
        {
            Kind = StripLoadKind.Point,
            SourceTag = "px",
            StationFraction = 0.5,
            PxKn = 12.0
        };

        var result = StripLoadConsistentNodalProjection.Project(
            new StripLoadSet([load]), 6.0, [0.0, 1.0]);

        Assert.True(result.IsCalculable);
        Assert.Equal(6.0, result.Elements[0].N1, 9);
        Assert.Equal(6.0, result.Elements[0].N2, 9);
    }

    [Fact]
    public void Project_PointTransverseLoad_Midspan_MatchesClassicFixedEndFormula()
    {
        var load = new StripLoad
        {
            Kind = StripLoadKind.Point,
            SourceTag = "pz",
            StationFraction = 0.5,
            PzKn = -8.0
        };

        var result = StripLoadConsistentNodalProjection.Project(
            new StripLoadSet([load]), 6.0, [0.0, 1.0]);

        var e = result.Elements[0];
        Assert.Equal(-4.0, e.Vz1, 9);  // P/2
        Assert.Equal(-4.0, e.Vz2, 9);
        Assert.Equal(-6.0, e.My1, 9);  // P*L/8 = -8*6/8
        Assert.Equal(6.0, e.My2, 9);
    }

    [Fact]
    public void Project_AppliedMzAtNode_TransfersFullyToThatNode()
    {
        var load = new StripLoad
        {
            Kind = StripLoadKind.Point,
            SourceTag = "mz-at-node",
            StationFraction = 0.0,
            MzKnM = 5.0
        };

        var result = StripLoadConsistentNodalProjection.Project(
            new StripLoadSet([load]), 6.0, [0.0, 1.0]);

        var e = result.Elements[0];
        Assert.Equal(5.0, e.Mz1, 9);
        Assert.Equal(0.0, e.Mz2, 9);
        Assert.Equal(0.0, e.Vy1, 9);
        Assert.Equal(0.0, e.Vy2, 9);
    }

    [Fact]
    public void Project_PointExactlyOnInteriorStation_BelongsToRightElement()
    {
        var load = new StripLoad
        {
            Kind = StripLoadKind.Point,
            SourceTag = "px-boundary",
            StationFraction = 0.5,
            PxKn = 10.0
        };

        var result = StripLoadConsistentNodalProjection.Project(
            new StripLoadSet([load]), 6.0, [0.0, 0.5, 1.0]);

        // a=0 в элементе [0.5,1.0] -> вся сила в N1 этого элемента, элемент [0,0.5] не затронут.
        Assert.Equal(0.0, result.Elements[0].N1, 9);
        Assert.Equal(0.0, result.Elements[0].N2, 9);
        Assert.Equal(10.0, result.Elements[1].N1, 9);
        Assert.Equal(0.0, result.Elements[1].N2, 9);
    }

    [Fact]
    public void Project_EmptyLoadSet_ReturnsZeroedElements()
    {
        var result = StripLoadConsistentNodalProjection.Project(
            new StripLoadSet([]), 6.0, [0.0, 0.5, 1.0]);

        Assert.True(result.IsCalculable);
        Assert.Equal(2, result.Elements.Count);
        Assert.All(result.Elements, e => Assert.Equal(StripElementNodalLoad.Zero, e));
        Assert.Equal(new[] { 0.0, 0.0, 0.0 }, result.TotalForceCheck);
        Assert.Equal(new[] { 0.0, 0.0, 0.0 }, result.TotalMomentCheck);
    }

    [Fact]
    public void Project_InvalidStripLoadComponent_ReturnsInvalidInputDiagnosticAndSkipsIt()
    {
        var bad = new StripLoad { Kind = StripLoadKind.DistributedUniform, QzKnM = double.NaN };
        var good = new StripLoad
        {
            Kind = StripLoadKind.DistributedUniform,
            StationStartFraction = 0.0,
            StationEndFraction = 1.0,
            QxKnM = 1.0
        };

        var result = StripLoadConsistentNodalProjection.Project(
            new StripLoadSet([bad, good]), 6.0, [0.0, 1.0]);

        // Диагностика по одной невалидной нагрузке делает ВЕСЬ результат IsCalculable=false
        // (как PlanarLoadMapper.MapCore: IsCalculable = diagnostics.All(d => !d.IsError), а
        // IsError по умолчанию true) — но валидная нагрузка всё равно лумпится в Elements,
        // чтобы вызывающий код видел частичный результат при отладке.
        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_load_invalid_input");
        Assert.Equal(6.0, result.Elements[0].N1 + result.Elements[0].N2, 9); // good всё равно применён
    }

    [Fact]
    public void Project_StripLoadWithExcessiveMx_ReturnsTorqueDiagnostic()
    {
        var badTorque = new StripLoad
        {
            Kind = StripLoadKind.Point,
            SourceTag = "manually-constructed",
            StationFraction = 0.5,
            PzKn = -10.0,
            MxKnM = 5.0 // сконструировано в обход Map, не проверено там
        };

        var result = StripLoadConsistentNodalProjection.Project(
            new StripLoadSet([badTorque]), 6.0, [0.0, 1.0]);

        Assert.False(result.IsCalculable);
        Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_load_produces_torque");
        Assert.Equal(0.0, result.Elements[0].Vz1, 9); // нагрузка с недопустимым Mx не лумпится
    }

    [Fact]
    public void Project_UniformAndPointCombined_TotalsMatchDirectIntegration()
    {
        var udl = new StripLoad
        {
            Kind = StripLoadKind.DistributedUniform,
            SourceTag = "udl",
            StationStartFraction = 0.0,
            StationEndFraction = 1.0,
            QzKnM = -2.0
        };
        var point = new StripLoad
        {
            Kind = StripLoadKind.Point,
            SourceTag = "point",
            StationFraction = 0.25,
            PzKn = -5.0
        };

        double lengthM = 8.0;
        var result = StripLoadConsistentNodalProjection.Project(
            new StripLoadSet([udl, point]), lengthM, [0.0, 0.25, 0.6, 1.0]);

        double totalVz = result.TotalForceCheck[2];
        Assert.Equal(udl.QzKnM * lengthM + point.PzKn, totalVz, 6);

        // My относительно station=0: вклад силы Fz на позиции s — "-s*Fz" (см. M=r×F в спеке).
        // Для равномерной Qz на всём [0,L]: -∫₀ᴸ s·Qz ds = -Qz·L²/2.
        double expectedMy = -udl.QzKnM * lengthM * lengthM / 2.0
            - point.StationFraction * lengthM * point.PzKn;
        Assert.Equal(expectedMy, result.TotalMomentCheck[1], 6);
    }
}
