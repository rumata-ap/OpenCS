using OpenCS.OpenSees.Analysis;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Services;

namespace OpenCS.OpenSees.Tests;

public sealed class SpatialInteractionTests
{
    [Fact]
    public void Request_generates_full_turn_without_duplicate_360()
    {
        SectionSpatialInteractionRequest request = new()
        {
            AxialForcesN = [-100_000, 0, 100_000],
            AngleStepDegrees = 45,
            MaxCurvature = 0.01,
            Increments = 20
        };

        request.Validate();

        Assert.Equal(8, request.GenerateAnglesDegrees().Count);
        Assert.Equal(0, request.GenerateAnglesDegrees()[0]);
        Assert.Equal(315, request.GenerateAnglesDegrees()[^1]);
    }

    [Fact]
    public void Request_rejects_non_dividing_angle_step()
    {
        SectionSpatialInteractionRequest request = new()
        {
            AxialForcesN = [0],
            AngleStepDegrees = 7,
            MaxCurvature = 0.01,
            Increments = 20
        };

        Assert.Throws<ArgumentException>(() => request.Validate());
    }

    [Fact]
    public void Spatial_point_maps_zero_and_ninety_degrees_to_Mx_and_My()
    {
        SpatialSectionAnalysisRequest zero = SpatialSectionAnalysisRequest.At(0, 0, 0.01, 20);
        SpatialSectionAnalysisRequest ninety = SpatialSectionAnalysisRequest.At(0, 90, 0.01, 20);

        Assert.Equal(0.01, zero.CurvatureMxAtMax, 12);
        Assert.Equal(0, zero.CurvatureMyAtMax, 12);
        Assert.Equal(0, ninety.CurvatureMxAtMax, 12);
        Assert.Equal(0.01, ninety.CurvatureMyAtMax, 12);
    }

    [Fact]
    public void Request_rejects_empty_nonfinite_and_duplicate_axial_forces()
    {
        Assert.Throws<ArgumentException>(() => new SectionSpatialInteractionRequest
        {
            AxialForcesN = []
        }.Validate());

        Assert.Throws<ArgumentException>(() => new SectionSpatialInteractionRequest
        {
            AxialForcesN = [double.NaN]
        }.Validate());

        Assert.Throws<ArgumentException>(() => new SectionSpatialInteractionRequest
        {
            AxialForcesN = [0, 0]
        }.Validate());
    }

    [Fact]
    public async Task Service_preserves_force_angle_order_history_terminal_and_artifacts()
    {
        FakeSpatialExecutor executor = new();

        SectionSpatialInteractionResult result = await new SectionSpatialInteractionService(executor).RunAsync(
            ValidModel(),
            new SectionSpatialInteractionRequest
            {
                AxialForcesN = [100_000, -200_000],
                AngleStepDegrees = 90,
                MaxCurvature = 0.01,
                Increments = 2
            },
            new OpenSeesRunRequest { ExecutablePath = "OpenSees.exe" },
            CancellationToken.None);

        Assert.Equal("ok", result.Status);
        Assert.Equal(
            new[] { "100000/0", "100000/90", "100000/180", "100000/270", "-200000/0", "-200000/90", "-200000/180", "-200000/270" },
            executor.Requests.Select(request => $"{request.AxialForceN:0}/{request.AngleDegrees:0}"));
        Assert.Equal(8, result.Points.Count);
        Assert.All(result.Points, point =>
        {
            Assert.Equal(2, point.HistoryRows.Count);
            Assert.Equal(1, point.TerminalRow!.Step);
            Assert.Equal(point.TerminalRow.MomentMxNm, point.MomentMxNm);
            Assert.Equal(point.TerminalRow.MomentMyNm, point.MomentMyNm);
            Assert.StartsWith("artifact-", point.ArtifactDirectory, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Service_aggregates_errors_and_continues_after_one_run_exception()
    {
        FakeSpatialExecutor executor = new() { ThrowOnCall = 2 };

        SectionSpatialInteractionResult result = await new SectionSpatialInteractionService(executor).RunAsync(
            ValidModel(),
            new SectionSpatialInteractionRequest { AxialForcesN = [0], AngleStepDegrees = 180 },
            new OpenSeesRunRequest { ExecutablePath = "OpenSees.exe" },
            CancellationToken.None);

        Assert.Equal("error", result.Status);
        Assert.Equal(2, result.Points.Count);
        Assert.Equal("ok", result.Points[0].Status);
        Assert.Equal("error", result.Points[1].Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("fake spatial failure", StringComparison.Ordinal));
        Assert.Equal(2, executor.Requests.Count);
    }

    [Fact]
    public async Task Service_aggregates_not_converged_when_no_point_has_error()
    {
        FakeSpatialExecutor executor = new() { Status = "not_converged" };

        SectionSpatialInteractionResult result = await new SectionSpatialInteractionService(executor).RunAsync(
            ValidModel(),
            new SectionSpatialInteractionRequest { AxialForcesN = [0], AngleStepDegrees = 180 },
            new OpenSeesRunRequest { ExecutablePath = "OpenSees.exe" },
            CancellationToken.None);

        Assert.Equal("not_converged", result.Status);
        Assert.All(result.Points, point => Assert.Equal("not_converged", point.Status));
    }

    [Fact]
    public async Task Service_stops_before_next_point_when_cancelled_between_runs()
    {
        FakeSpatialExecutor executor = new();
        using CancellationTokenSource cancellation = new();
        executor.AfterRun = () => cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => new SectionSpatialInteractionService(executor).RunAsync(
            ValidModel(),
            new SectionSpatialInteractionRequest { AxialForcesN = [0], AngleStepDegrees = 180 },
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

    private sealed class FakeSpatialExecutor : ISpatialSectionAnalysisExecutor
    {
        public List<SpatialSectionAnalysisRequest> Requests { get; } = [];
        public string Status { get; init; } = "ok";
        public int? ThrowOnCall { get; init; }
        public Action? AfterRun { get; set; }

        public Task<SpatialSectionAnalysisResult> RunAsync(
            OpenSeesSectionModel model,
            SpatialSectionAnalysisRequest request,
            OpenSeesRunRequest processRequest,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (ThrowOnCall == Requests.Count)
                throw new InvalidOperationException("fake spatial failure");

            SpatialSectionHistoryRow converged = new()
            {
                Step = 1,
                Converged = true,
                MomentMxNm = request.AxialForceN + request.AngleDegrees,
                MomentMyNm = request.AxialForceN - request.AngleDegrees,
                CurvatureMx = 0.001,
                CurvatureMy = 0.002
            };
            SpatialSectionHistoryRow notConverged = new()
            {
                Step = 2,
                Converged = false,
                MomentMxNm = converged.MomentMxNm + 1,
                MomentMyNm = converged.MomentMyNm + 1
            };

            AfterRun?.Invoke();
            return Task.FromResult(new SpatialSectionAnalysisResult
            {
                Status = Status,
                Rows = [converged, notConverged],
                ArtifactDirectory = $"artifact-{Requests.Count}",
                Diagnostics = Status == "ok" ? [] : ["not converged"]
            });
        }
    }
}
