using OpenCS.OpenSees.Analysis;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Tests.Fixtures;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Services;
using OpenCS.OpenSees.Tcl;

namespace OpenCS.OpenSees.Tests;

public sealed class OpenSeesIntegrationTests
{
    [Fact]
    public async Task Elastic_rectangular_section_matches_EI_and_curvature_sign()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        string root = Path.Combine(Path.GetTempPath(), "opencs-opensees-integration", Guid.NewGuid().ToString("N"));
        try
        {
            OpenSeesSectionModel model = CrossSectionFixtures.SymmetricElasticSection();
            SectionAnalysisResult result = await new SectionAnalysisService(
                new SectionMomentCurvatureTclGenerator(),
                new OpenSeesProcessRunner(),
                new OpenSeesArtifactStore(root)).RunAsync(
                    model,
                    new SectionAnalysisRequest { MaxCurvature = 1e-5, Increments = 2 },
                    new OpenSeesRunRequest
                    {
                        ExecutablePath = executable,
                        WorkingDirectory = Path.GetTempPath(),
                        Timeout = TimeSpan.FromSeconds(30)
                    },
                    CancellationToken.None);

            Assert.True(result.Status == "ok", $"status={result.Status}; exit={result.RunResult?.ExitCode}; diagnostics={string.Join(" | ", result.Diagnostics)}");
            Assert.Equal(0, result.RunResult!.ExitCode);
            Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "completed.marker")));
            Assert.NotEmpty(result.Rows);

            SectionHistoryRow first = result.Rows[0];
            Assert.True(first.Curvature > 0);
            double expectedEi = 200_000_000 * (2 * 0.5 * 0.5 * 0.5);
            double actualEi = first.BendingMomentNm / first.Curvature;
            Assert.InRange(actualEi, expectedEi * 0.98, expectedEi * 1.02);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Custom_monotonic_envelope_is_reached_by_recorded_fiber()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        string root = Path.Combine(Path.GetTempPath(), "opencs-opensees-integration", Guid.NewGuid().ToString("N"));
        try
        {
            SectionAnalysisResult result = await new SectionAnalysisService(
                new SectionMomentCurvatureTclGenerator(),
                new OpenSeesProcessRunner(),
                new OpenSeesArtifactStore(root)).RunAsync(
                    new OpenSeesSectionModel
                    {
                        Materials =
                        [
                            new OpenSeesMaterialDefinition
                            {
                                Tag = 1,
                                PositiveEnvelope = [new EnvelopePoint(0, 0), new EnvelopePoint(0.001, 1_500_000)],
                                NegativeEnvelope = [new EnvelopePoint(-0.001, -1_500_000), new EnvelopePoint(0, 0)]
                            }
                        ],
                        Fibers =
                        [
                            new OpenSeesFiber(-0.5, 0, 0.5, 1),
                            new OpenSeesFiber(0.5, 0, 0.5, 1)
                        ]
                    },
                    new SectionAnalysisRequest { MaxCurvature = 0.002, Increments = 1 },
                    new OpenSeesRunRequest
                    {
                        ExecutablePath = executable,
                        WorkingDirectory = Path.GetTempPath(),
                        Timeout = TimeSpan.FromSeconds(30)
                    },
                    CancellationToken.None);

            Assert.True(result.Status == "ok", $"status={result.Status}; exit={result.RunResult?.ExitCode}; diagnostics={string.Join(" | ", result.Diagnostics)}");
            string fiberHistory = File.ReadAllText(Path.Combine(result.ArtifactDirectory, "fiber_history.out"));
            Assert.Contains("1.5e+06", fiberHistory);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task N_M_interaction_runs_three_axial_force_points_in_order()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        string root = Path.Combine(Path.GetTempPath(), "opencs-opensees-interaction", Guid.NewGuid().ToString("N"));
        try
        {
            SectionInteractionResult result = await new SectionInteractionService(
                new SectionAnalysisService(
                    new SectionMomentCurvatureTclGenerator(),
                    new OpenSeesProcessRunner(),
                    new OpenSeesArtifactStore(root))).RunAsync(
                CrossSectionFixtures.SymmetricElasticSection(),
                new SectionInteractionRequest
                {
                    AxialForcesN = [-100_000, 0, 100_000],
                    MaxCurvature = 1e-5,
                    Increments = 2
                },
                new OpenSeesRunRequest
                {
                    ExecutablePath = executable,
                    WorkingDirectory = Path.GetTempPath(),
                    Timeout = TimeSpan.FromSeconds(30)
                },
                CancellationToken.None);

            Assert.Equal("ok", result.Status);
            Assert.Equal(3, result.Points.Count);
            Assert.Equal(new[] { -100_000d, 0d, 100_000d }, result.Points.Select(point => point.AxialForceN));
            Assert.All(result.Points, point =>
            {
                Assert.True(point.BendingMomentNm.HasValue);
                Assert.True(point.Curvature > 0);
                Assert.True(Directory.Exists(point.ArtifactDirectory));
                Assert.True(File.Exists(Path.Combine(point.ArtifactDirectory, "completed.marker")));
                Assert.True(File.Exists(Path.Combine(point.ArtifactDirectory, "section_history.out")));
                Assert.True(File.Exists(Path.Combine(point.ArtifactDirectory, "manifest.json")));
            });
            Assert.Equal(3, result.Points.Select(point => point.ArtifactDirectory).Distinct().Count());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

}
