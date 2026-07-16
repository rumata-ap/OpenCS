using OpenCS.OpenSees.Analysis;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Services;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;

namespace OpenCS.OpenSees.Tests;

public sealed class OpenSeesSpatialIntegrationTests
{
    [Fact]
    public async Task Symmetric_elastic_section_completes_full_four_direction_turn()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        string root = Path.Combine(Path.GetTempPath(), "opencs-opensees-spatial", Guid.NewGuid().ToString("N"));
        try
        {
            SectionSpatialInteractionResult result = await new SectionSpatialInteractionService(
                new SpatialSectionAnalysisService(
                    new SpatialSectionTclGenerator(),
                    new OpenSeesProcessRunner(),
                    new OpenSeesArtifactStore(root))).RunAsync(
                CrossSectionFixtures.SymmetricElasticSection(),
                new SectionSpatialInteractionRequest
                {
                    AxialForcesN = [0],
                    AngleStepDegrees = 90,
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

            Assert.True(result.Status == "ok", $"status={result.Status}; diagnostics={string.Join(" | ", result.Diagnostics)}");
            Assert.Equal(new[] { 0d, 90d, 180d, 270d }, result.Points.Select(point => point.AngleDegrees));
            Assert.Equal(4, result.Points.Select(point => point.ArtifactDirectory).Distinct().Count());
            Assert.All(result.Points, point =>
            {
                Assert.Equal("ok", point.Status);
                Assert.NotEmpty(point.HistoryRows);
                Assert.True(point.TerminalRow?.Converged);
                Assert.True(Directory.Exists(point.ArtifactDirectory));
                Assert.True(File.Exists(Path.Combine(point.ArtifactDirectory, "completed.marker")));
                Assert.True(File.Exists(Path.Combine(point.ArtifactDirectory, "section_history.out")));
            });

            SectionSpatialInteractionPoint atZero = result.Points[0];
            SectionSpatialInteractionPoint at90 = result.Points[1];
            SectionSpatialInteractionPoint at180 = result.Points[2];
            SectionSpatialInteractionPoint at270 = result.Points[3];
            Assert.True(atZero.MomentMxNm > 0);
            Assert.True(at90.MomentMyNm > 0);
            Assert.True(at180.MomentMxNm < 0);
            Assert.True(at270.MomentMyNm < 0);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
