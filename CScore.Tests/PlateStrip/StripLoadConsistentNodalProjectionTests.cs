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
        Assert.Equal(9.0, e.My1, 9);    // -Qz*L^2/12 (θy = −w′, правый момент вокруг y)
        Assert.Equal(-9.0, e.Vz2, 9);
        Assert.Equal(-9.0, e.My2, 9);   // +Qz*L^2/12
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
        Assert.Equal(6.0, e.My1, 9);   // -P*L/8 (θy = −w′, правый момент вокруг y)
        Assert.Equal(-6.0, e.My2, 9);
    }

    [Fact]
    public void Project_PointTransverseLoad_InsideElement_TotalMomentMatchesRightHandedResultant()
    {
        // Единственный случай, где знак My-компонент виден в итоговой проверке: точечная Pz
        // строго внутри элемента (при a=b и у равномерной нагрузки ΣMy = 0 и знак не проявляется).
        var load = new StripLoad
        {
            Kind = StripLoadKind.Point,
            SourceTag = "pz-quarter",
            StationFraction = 0.25,
            PzKn = -8.0
        };

        double lengthM = 6.0;
        var result = StripLoadConsistentNodalProjection.Project(
            new StripLoadSet([load]), lengthM, [0.0, 1.0]);

        Assert.True(result.IsCalculable);
        Assert.Equal(load.PzKn, result.TotalForceCheck[2], 9);
        // M = r×F: My = -x·Fz = -(0.25*6)*(-8) = 12
        Assert.Equal(-0.25 * lengthM * load.PzKn, result.TotalMomentCheck[1], 9);
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
    public void Project_InvalidLength_ReturnsDiagnostic()
    {
        foreach (double bad in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity })
        {
            var result = StripLoadConsistentNodalProjection.Project(
                new StripLoadSet([]), bad, [0.0, 1.0]);

            Assert.False(result.IsCalculable);
            Assert.Contains(result.Diagnostics, d => d.Code == "plate_strip_load_invalid_length");
        }
    }

    [Fact]
    public void Project_LinearLoadOverWholeElement_MatchesClosedFormNodalLoads()
    {
        // Классические consistent nodal loads для линейной нагрузки (q1 в начале, q2 в конце):
        //   f_w1 = L(7q1+3q2)/20,  f_θ1 = L²(3q1+2q2)/60,
        //   f_w2 = L(3q1+7q2)/20,  f_θ2 = -L²(2q1+3q2)/60,
        // где θ = +w′; в конвенции My (θy = −w′) моментные компоненты берутся с обратным знаком.
        var load = new StripLoad
        {
            Kind = StripLoadKind.DistributedLinear,
            SourceTag = "linear",
            StationStartFraction = 0.0,
            StationEndFraction = 1.0,
            QzKnM = 2.0,
            QzEndKnM = 8.0
        };

        var result = StripLoadConsistentNodalProjection.Project(
            new StripLoadSet([load]), 3.0, [0.0, 1.0]);

        Assert.True(result.IsCalculable);
        var e = result.Elements[0];
        Assert.Equal(5.7, e.Vz1, 9);
        Assert.Equal(-3.3, e.My1, 9);
        Assert.Equal(9.3, e.Vz2, 9);
        Assert.Equal(4.2, e.My2, 9);
    }

    [Fact]
    public void Project_PartialUniformLoadInsideElement_MatchesExactIntegrals()
    {
        // q на [0, L/2] одного элемента. Точные интегралы функций формы по половине элемента:
        //   ∫H1 = 13L/32, ∫H2 = 11L²/192, ∫H3 = 3L/32, ∫H4 = -5L²/192,
        //   ∫(1-ξ) = 3L/8,  ∫ξ = L/8.
        const double q = 6.0;
        const double lengthM = 4.0;
        var load = new StripLoad
        {
            Kind = StripLoadKind.DistributedUniform,
            SourceTag = "half",
            StationStartFraction = 0.0,
            StationEndFraction = 0.5,
            QxKnM = q,
            QyKnM = q,
            QzKnM = q
        };

        var result = StripLoadConsistentNodalProjection.Project(
            new StripLoadSet([load]), lengthM, [0.0, 1.0]);

        Assert.True(result.IsCalculable);
        var e = result.Elements[0];
        double l2 = lengthM * lengthM;

        Assert.Equal(q * 3.0 * lengthM / 8.0, e.N1, 9);
        Assert.Equal(q * lengthM / 8.0, e.N2, 9);

        Assert.Equal(q * 13.0 * lengthM / 32.0, e.Vy1, 9);
        Assert.Equal(q * 11.0 * l2 / 192.0, e.Mz1, 9);
        Assert.Equal(q * 3.0 * lengthM / 32.0, e.Vy2, 9);
        Assert.Equal(-q * 5.0 * l2 / 192.0, e.Mz2, 9);

        Assert.Equal(q * 13.0 * lengthM / 32.0, e.Vz1, 9);
        Assert.Equal(-q * 11.0 * l2 / 192.0, e.My1, 9);
        Assert.Equal(q * 3.0 * lengthM / 32.0, e.Vz2, 9);
        Assert.Equal(q * 5.0 * l2 / 192.0, e.My2, 9);
    }

    [Fact]
    public void Project_LoadCrossingElementBoundary_SplitsSymmetrically()
    {
        var load = new StripLoad
        {
            Kind = StripLoadKind.DistributedUniform,
            SourceTag = "middle-half",
            StationStartFraction = 0.25,
            StationEndFraction = 0.75,
            QzKnM = -4.0
        };

        double lengthM = 8.0;
        var result = StripLoadConsistentNodalProjection.Project(
            new StripLoadSet([load]), lengthM, [0.0, 0.5, 1.0]);

        Assert.True(result.IsCalculable);
        var left = result.Elements[0];
        var right = result.Elements[1];

        // Нагрузка симметрична относительно середины пролёта: элементы зеркальны.
        Assert.Equal(left.Vz1, right.Vz2, 9);
        Assert.Equal(left.Vz2, right.Vz1, 9);
        Assert.Equal(left.My1, -right.My2, 9);
        Assert.Equal(left.My2, -right.My1, 9);

        Assert.Equal(load.QzKnM * 0.5 * lengthM, result.TotalForceCheck[2], 9);
        Assert.Equal(-0.5 * lengthM * load.QzKnM * 0.5 * lengthM, result.TotalMomentCheck[1], 9);
    }

    [Fact]
    public void Project_LoadEntirelyInsideOneElement_LeavesOtherElementsUntouched()
    {
        var load = new StripLoad
        {
            Kind = StripLoadKind.DistributedUniform,
            SourceTag = "inner",
            StationStartFraction = 0.55,
            StationEndFraction = 0.7,
            QzKnM = -2.0
        };

        double lengthM = 10.0;
        var result = StripLoadConsistentNodalProjection.Project(
            new StripLoadSet([load]), lengthM, [0.0, 0.5, 0.75, 1.0]);

        Assert.True(result.IsCalculable);
        Assert.Equal(0.0, result.Elements[0].Vz1, 12);
        Assert.Equal(0.0, result.Elements[0].Vz2, 12);
        Assert.Equal(0.0, result.Elements[2].Vz1, 12);
        Assert.Equal(0.0, result.Elements[2].Vz2, 12);
        Assert.Equal(load.QzKnM * 0.15 * lengthM, result.TotalForceCheck[2], 9);
    }

    [Fact]
    public void Project_LinearLoad_IrregularStations_TotalsMatchAnalyticIntegral()
    {
        var load = new StripLoad
        {
            Kind = StripLoadKind.DistributedLinear,
            SourceTag = "linear-irregular",
            StationStartFraction = 0.0,
            StationEndFraction = 1.0,
            QzKnM = -1.0,
            QzEndKnM = -5.0
        };

        double lengthM = 5.0;
        var result = StripLoadConsistentNodalProjection.Project(
            new StripLoadSet([load]), lengthM, [0.0, 0.1, 0.35, 0.36, 0.8, 1.0]);

        Assert.True(result.IsCalculable);
        // ∫q dx = L(q1+q2)/2
        Assert.Equal(lengthM * (load.QzKnM + load.QzEndKnM) / 2.0, result.TotalForceCheck[2], 9);
        // My = -∫x·q(x) dx = -L²(q1/6 + q2/3)
        double expectedMy = -lengthM * lengthM * (load.QzKnM / 6.0 + load.QzEndKnM / 3.0);
        Assert.Equal(expectedMy, result.TotalMomentCheck[1], 9);
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
