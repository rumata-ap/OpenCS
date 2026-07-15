using OpenCS.OpenSees.Analysis;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Services;

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

    [Fact]
    public async Task Service_preserves_force_order_selects_last_converged_row_and_aggregates_error()
    {
        FakeSectionAnalysisExecutor executor = new();
        executor.Results.Enqueue(new SectionAnalysisResult
        {
            Status = "ok",
            Rows =
            [
                new SectionHistoryRow { Step = 1, Converged = true, BendingMomentNm = 10, Curvature = 1 },
                new SectionHistoryRow { Step = 2, Converged = true, BendingMomentNm = 20, Curvature = 2 }
            ],
            ArtifactDirectory = "artifact-1"
        });
        executor.Results.Enqueue(new SectionAnalysisResult
        {
            Status = "not_converged",
            Rows =
            [
                new SectionHistoryRow { Step = 1, Converged = true, BendingMomentNm = 30, Curvature = 3 },
                new SectionHistoryRow { Step = 2, Converged = false, BendingMomentNm = 40, Curvature = 4 }
            ],
            ArtifactDirectory = "artifact-2",
            Diagnostics = ["not converged"]
        });
        executor.Results.Enqueue(new SectionAnalysisResult
        {
            Status = "error",
            ArtifactDirectory = "artifact-3",
            Diagnostics = ["failed"]
        });

        SectionInteractionResult result = await new SectionInteractionService(executor).RunAsync(
            ValidModel(),
            new SectionInteractionRequest { AxialForcesN = [100, -200, 300], MaxCurvature = 0.01, Increments = 2 },
            new OpenSeesRunRequest { ExecutablePath = "OpenSees.exe" },
            CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.Equal(new[] { 100d, -200d, 300d }, executor.Requests.Select(request => request.AxialForceN));
        Assert.Equal(20, result.Points[0].BendingMomentNm);
        Assert.Equal(2, result.Points[0].TerminalRow!.Step);
        Assert.Equal(30, result.Points[1].BendingMomentNm);
        Assert.Null(result.Points[2].BendingMomentNm);
        Assert.Equal(new[] { "artifact-1", "artifact-2", "artifact-3" },
            result.Points.Select(point => point.ArtifactDirectory));
    }

    [Fact]
    public async Task Service_aggregates_partial_nonconvergence_without_error()
    {
        FakeSectionAnalysisExecutor executor = new();
        executor.Results.Enqueue(new SectionAnalysisResult { Status = "ok" });
        executor.Results.Enqueue(new SectionAnalysisResult { Status = "not_converged" });

        SectionInteractionResult result = await new SectionInteractionService(executor).RunAsync(
            ValidModel(),
            new SectionInteractionRequest { AxialForcesN = [0, 1] },
            new OpenSeesRunRequest { ExecutablePath = "OpenSees.exe" },
            CancellationToken.None);

        Assert.Equal("not_converged", result.Status);
        Assert.Equal(2, result.Points.Count);
    }

    [Fact]
    public async Task Service_stops_before_next_point_when_cancelled_between_runs()
    {
        FakeSectionAnalysisExecutor executor = new();
        executor.Results.Enqueue(new SectionAnalysisResult { Status = "ok" });
        executor.Results.Enqueue(new SectionAnalysisResult { Status = "ok" });
        using CancellationTokenSource cancellation = new();
        executor.AfterRun = () => cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => new SectionInteractionService(executor).RunAsync(
            ValidModel(),
            new SectionInteractionRequest { AxialForcesN = [0, 1] },
            new OpenSeesRunRequest { ExecutablePath = "OpenSees.exe" },
            cancellation.Token));

        Assert.Single(executor.Requests);
    }

    private static OpenSeesSectionModel ValidModel() => new()
    {
        Materials =
        [
            new OpenSeesMaterialDefinition
            {
                Tag = 1,
                PositiveEnvelope = [new EnvelopePoint(0, 0), new EnvelopePoint(0.01, 1)],
                NegativeEnvelope = [new EnvelopePoint(-0.01, -1), new EnvelopePoint(0, 0)]
            }
        ],
        Fibers = [new OpenSeesFiber(0, 0, 1, 1)]
    };

    private sealed class FakeSectionAnalysisExecutor : ISectionAnalysisExecutor
    {
        public List<SectionAnalysisRequest> Requests { get; } = [];
        public Queue<SectionAnalysisResult> Results { get; } = [];
        public Action? AfterRun { get; set; }

        public Task<SectionAnalysisResult> RunAsync(
            OpenSeesSectionModel model,
            SectionAnalysisRequest request,
            OpenSeesRunRequest processRequest,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            SectionAnalysisResult result = Results.Dequeue();
            AfterRun?.Invoke();
            return Task.FromResult(result);
        }
    }
}
