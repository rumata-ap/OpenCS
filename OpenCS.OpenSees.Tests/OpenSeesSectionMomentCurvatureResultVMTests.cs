using System.Text.Json;
using CScore;
using OpenCS.OpenSees.Analysis;
using OpenCS.ViewModels;

namespace OpenCS.OpenSees.Tests;

public sealed class OpenSeesSectionMomentCurvatureResultVMTests
{
    [Fact]
    public void View_model_preserves_history_and_last_converged_row()
    {
        SectionAnalysisResult analysis = new()
        {
            Status = "not_converged",
            Rows =
            [
                new SectionHistoryRow
                {
                    Step = 1,
                    LoadFactor = 10,
                    AxialForceN = -100_000,
                    BendingMomentNm = 20_000,
                    Curvature = 0.001,
                    Converged = true
                },
                new SectionHistoryRow
                {
                    Step = 2,
                    LoadFactor = 11,
                    AxialForceN = -100_000,
                    BendingMomentNm = 21_000,
                    Curvature = 0.002,
                    Converged = false
                }
            ],
            Diagnostics = ["solver failed"],
            ArtifactDirectory = "artifacts"
        };
        CalcResult result = new()
        {
            Status = "not_converged",
            DataJson = JsonSerializer.Serialize(analysis)
        };

        OpenSeesSectionMomentCurvatureResultVM viewModel =
            new(result);

        Assert.Equal("not_converged", viewModel.Status);
        Assert.Equal(2, viewModel.Rows.Count);
        Assert.Equal(20, viewModel.Rows[0].MomentKnM);
        Assert.Equal(0.002, viewModel.Rows[1].Curvature);
        Assert.Equal(1, viewModel.ConvergedRowCount);
        Assert.Single(viewModel.ConvergedRows);
        Assert.Equal(0.001, viewModel.ConvergedRows[0].Curvature);
        Assert.Equal(20, viewModel.LastConvergedRow!.MomentKnM);
        Assert.Equal("artifacts", viewModel.ArtifactDirectory);
        Assert.Contains("solver failed", viewModel.DiagnosticsText);
    }
}
