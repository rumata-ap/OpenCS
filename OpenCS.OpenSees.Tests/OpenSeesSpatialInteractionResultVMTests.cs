using System.Text.Json;
using CScore;
using OpenCS.OpenSees.Analysis;
using OpenCS.ViewModels;

namespace OpenCS.OpenSees.Tests;

public sealed class OpenSeesSpatialInteractionResultVMTests
{
    [Fact]
    public void VM_groups_axial_forces_preserves_angles_and_switches_selection()
    {
        CalcResult result = new()
        {
            Status = "ok",
            DataJson = JsonSerializer.Serialize(CreateResult())
        };

        OpenSeesSpatialInteractionResultVM vm = new(result);

        Assert.Equal(new[] { 100d, -200d }, vm.AvailableAxialForces);
        Assert.Equal(new[] { 0d, 90d }, vm.AvailableAngles);
        Assert.Equal(100, vm.SelectedAxialForce);
        Assert.Equal(0, vm.SelectedAngle);

        vm.SelectedAngle = 90;
        Assert.Equal(90, vm.SelectedPoint!.AngleDegrees);
        Assert.Equal(new[] { 1d, 2d }, vm.HistoryMomentMxKnM);

        vm.SelectedAxialForce = -200;
        Assert.Equal(new[] { 0d, 180d }, vm.AvailableAngles);
        Assert.Equal(0, vm.SelectedAngle);
        Assert.Equal(3000, vm.SelectedPoint!.MomentMxNm);
    }

    [Fact]
    public void VM_exposes_polar_and_history_series_in_display_units()
    {
        OpenSeesSpatialInteractionResultVM vm = new(new CalcResult
        {
            Status = "ok",
            DataJson = JsonSerializer.Serialize(CreateResult())
        });

        Assert.Equal(new double?[] { 1, 2 }, vm.PolarMxKnM);
        Assert.Equal(new[] { 0.5, 1d }, vm.HistoryMomentMyKnM);
        Assert.Equal(new[] { 0.0005, 0.001 }, vm.HistoryCurvatureMx);
        Assert.Equal(2, vm.PointRows.Count);
    }

    [Fact]
    public void VM_handles_error_and_empty_results_without_throwing()
    {
        OpenSeesSpatialInteractionResultVM error = new(new CalcResult
        {
            Status = "error",
            DataJson = "{\"error\":\"failed\"}"
        });
        OpenSeesSpatialInteractionResultVM empty = new(new CalcResult
        {
            Status = "not_converged",
            DataJson = JsonSerializer.Serialize(new SectionSpatialInteractionResult())
        });

        Assert.True(error.HasError);
        Assert.Equal("failed", error.ErrorText);
        Assert.True(empty.HasError);
        Assert.Empty(empty.AvailableAxialForces);
        Assert.Empty(empty.HistoryCurvatureMx);
    }

    [Fact]
    public void VM_exposes_report_summary_in_display_units()
    {
        OpenSeesSpatialInteractionResultVM vm = new(new CalcResult
        {
            Status = "ok",
            DataJson = JsonSerializer.Serialize(CreateResult())
        });

        Assert.Equal(1, vm.SelectedMomentMxKnM);
        Assert.Equal(1, vm.SelectedMomentMyKnM);
        Assert.Equal(0.001, vm.SelectedCurvatureMx);
        Assert.Equal(0.002, vm.SelectedCurvatureMy);
        Assert.Equal(2, vm.SelectedHistoryCount);
        Assert.True(vm.HasSelectedPoint);
        Assert.Equal("OpenSeesSpatialStatusOk", vm.SelectedStatusText);
    }

    [Fact]
    public void VM_exposes_geometry_kind_and_force_set_checks()
    {
        SectionSpatialInteractionResult result = new()
        {
            Status = "ok",
            GeometryKind = "plane",
            Points = CreateResult().Points,
            DemandChecks =
            [
                new SectionSpatialInteractionDemandCheck
                {
                    Num = 7,
                    Label = "LC-7",
                    AxialForceN = 100_000,
                    MomentMxNm = 12_000,
                    MomentMyNm = -8_000,
                    IsInside = true,
                    Utilization = 0.75,
                    Status = "inside"
                }
            ]
        };

        OpenSeesSpatialInteractionResultVM vm = new(new CalcResult
        {
            Status = "ok",
            DataJson = JsonSerializer.Serialize(result)
        });

        Assert.Equal("OpenSeesSpatialGeometryPlane", vm.GeometryKindText);
        Assert.Single(vm.DemandCheckRows);
        Assert.Equal(12, vm.DemandCheckRows[0].MomentMxKnM);
        Assert.Equal(0.75, vm.DemandCheckRows[0].Utilization);
        Assert.Equal("OpenSeesSpatialCheckInside", vm.DemandCheckRows[0].StatusText);
    }

    private static SectionSpatialInteractionResult CreateResult() => new()
    {
        Status = "ok",
        Points =
        [
            Point(100_000, 0, 1_000, 1_000),
            Point(100_000, 90, 2_000, 2_000),
            Point(-200_000, 0, 3_000, 3_000),
            Point(-200_000, 180, 4_000, 4_000)
        ]
    };

    private static SectionSpatialInteractionPoint Point(double n, double angle, double mx, double my) => new()
    {
        AxialForceN = n,
        AngleDegrees = angle,
        MomentMxNm = mx,
        MomentMyNm = my,
        CurvatureMx = 0.001,
        CurvatureMy = 0.002,
        Status = "ok",
        ArtifactDirectory = "artifact",
        HistoryRows =
        [
            new SpatialSectionHistoryRow { Step = 1, MomentMxNm = mx / 2, MomentMyNm = my / 2, CurvatureMx = 0.0005, CurvatureMy = 0.001, Converged = true },
            new SpatialSectionHistoryRow { Step = 2, MomentMxNm = mx, MomentMyNm = my, CurvatureMx = 0.001, CurvatureMy = 0.002, Converged = true }
        ],
        TerminalRow = new SpatialSectionHistoryRow { Step = 2, MomentMxNm = mx, MomentMyNm = my, CurvatureMx = 0.001, CurvatureMy = 0.002, Converged = true }
    };
}
