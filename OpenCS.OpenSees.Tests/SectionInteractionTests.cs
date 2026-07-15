using OpenCS.OpenSees.Analysis;

namespace OpenCS.OpenSees.Tests;

public sealed class SectionInteractionTests
{
    [Fact]
    public void Request_requires_nonempty_finite_unique_axial_forces()
    {
        SectionInteractionRequest valid = new()
        {
            AxialForcesN = [-100_000, 0, 100_000],
            MaxCurvature = 0.01,
            Increments = 20
        };

        valid.Validate();

        Assert.Throws<ArgumentException>(() => new SectionInteractionRequest
        {
            AxialForcesN = [], MaxCurvature = 0.01, Increments = 20
        }.Validate());
        Assert.Throws<ArgumentException>(() => new SectionInteractionRequest
        {
            AxialForcesN = [0, double.NaN], MaxCurvature = 0.01, Increments = 20
        }.Validate());
        Assert.Throws<ArgumentException>(() => new SectionInteractionRequest
        {
            AxialForcesN = [0, 0], MaxCurvature = 0.01, Increments = 20
        }.Validate());
        Assert.Throws<ArgumentException>(() => new SectionInteractionRequest
        {
            AxialForcesN = [0], MaxCurvature = 0, Increments = 20
        }.Validate());
    }

    [Fact]
    public void Request_preserves_input_order()
    {
        SectionInteractionRequest request = new() { AxialForcesN = [100, -200, 0] };

        Assert.Equal(new[] { 100d, -200d, 0d }, request.AxialForcesN);
    }

    [Fact]
    public void Point_can_keep_last_converged_row_for_not_converged_analysis()
    {
        SectionHistoryRow row = new() { Step = 2, Converged = true, BendingMomentNm = 123 };
        SectionInteractionPoint point = new()
        {
            AxialForceN = 10,
            BendingMomentNm = row.BendingMomentNm,
            TerminalRow = row,
            Status = "not_converged"
        };

        Assert.Equal(123, point.BendingMomentNm);
        Assert.Equal(2, point.TerminalRow!.Step);
        Assert.Equal("not_converged", point.Status);
    }
}
