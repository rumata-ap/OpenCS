using OpenCS.OpenSees.Analysis;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Tests.Fixtures;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Services;
using OpenCS.OpenSees.Tcl;

namespace OpenCS.OpenSees.Tests;

public sealed class SectionNonlinearConvergenceTests
{
    [Fact]
    public async Task Softening_section_converges_with_line_search()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        OpenSeesSectionModel model = new()
        {
            Materials =
            [
                new OpenSeesMaterialDefinition
                {
                    Tag = 1,
                    PositiveEnvelope =
                    [
                        new EnvelopePoint(0, 0),
                        new EnvelopePoint(0.0001, 1_050_000),
                        new EnvelopePoint(0.00015, 0),
                        new EnvelopePoint(1, 0)
                    ],
                    NegativeEnvelope =
                    [
                        new EnvelopePoint(-0.003, -14_500_000),
                        new EnvelopePoint(0, 0)
                    ]
                },
                new OpenSeesMaterialDefinition
                {
                    Tag = 2,
                    PositiveEnvelope =
                    [
                        new EnvelopePoint(0, 0),
                        new EnvelopePoint(0.002175, 435_000_000),
                        new EnvelopePoint(0.025, 435_000_000)
                    ],
                    NegativeEnvelope =
                    [
                        new EnvelopePoint(-0.0035, -435_000_000),
                        new EnvelopePoint(0, 0)
                    ]
                }
            ],
            Fibers =
            [
                new OpenSeesFiber(0.3, 0, 0.01, 1),
                new OpenSeesFiber(-0.3, 0, 0.02, 1),
                new OpenSeesFiber(-0.35, 0, 0.0002, 2)
            ]
        };
        string root = Path.Combine(Path.GetTempPath(), "opencs-opensees-repro", Guid.NewGuid().ToString("N"));

        try
        {
            SectionAnalysisResult result = await new SectionAnalysisService(
                new SectionMomentCurvatureTclGenerator(),
                new OpenSeesProcessRunner(),
                new OpenSeesArtifactStore(root)).RunAsync(
                model,
                new SectionAnalysisRequest { AxialForceN = -100_000, CurvatureStep = 0.0005, MaxSteps = 20 },
                new OpenSeesRunRequest
                {
                    ExecutablePath = executable,
                    WorkingDirectory = Path.GetTempPath(),
                    Timeout = TimeSpan.FromSeconds(30)
                },
                CancellationToken.None);

            Assert.True(
                result.Status == "ok",
                $"status={result.Status}; exit={result.RunResult?.ExitCode}; diagnostics={string.Join(" | ", result.Diagnostics)}; artifacts={result.ArtifactDirectory}");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
