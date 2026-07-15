using OpenCS.OpenSees.Analysis;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Services;
using OpenCS.OpenSees.Tcl;

namespace OpenCS.OpenSees.Tests;

public sealed class SectionAnalysisServiceTests
{
    [Fact]
    public async Task Service_generates_runs_parses_and_finalizes_manifest_in_order()
    {
        string root = Path.Combine(Path.GetTempPath(), "opencs-opensees-service", Guid.NewGuid().ToString("N"));
        try
        {
            List<string> calls = [];
            FakeGenerator generator = new(calls);
            FakeRunner runner = new(calls, notConverged: false);
            SectionAnalysisService service = new(
                generator,
                runner,
                new OpenSeesArtifactStore(root));

            SectionAnalysisResult result = await service.RunAsync(
                CreateModel(),
                new SectionAnalysisRequest { MaxCurvature = 0.01, Increments = 1 },
                new OpenSeesRunRequest
                {
                    ExecutablePath = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                    WorkingDirectory = Path.GetTempPath(),
                    Timeout = TimeSpan.FromSeconds(5)
                },
                CancellationToken.None);

            Assert.Equal("ok", result.Status);
            Assert.Single(result.Rows);
            Assert.Equal(new[] { "generate", "run" }, calls);
            Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "manifest.json")));
            Assert.Equal("ok", result.Status);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Service_propagates_not_converged_and_preserves_diagnostics()
    {
        string root = Path.Combine(Path.GetTempPath(), "opencs-opensees-service", Guid.NewGuid().ToString("N"));
        try
        {
            SectionAnalysisService service = new(
                new FakeGenerator([]),
                new FakeRunner([], notConverged: true),
                new OpenSeesArtifactStore(root));

            SectionAnalysisResult result = await service.RunAsync(
                CreateModel(),
                new SectionAnalysisRequest { MaxCurvature = 0.01, Increments = 1 },
                new OpenSeesRunRequest
                {
                    ExecutablePath = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                    WorkingDirectory = Path.GetTempPath(),
                    Timeout = TimeSpan.FromSeconds(5)
                },
                CancellationToken.None);

            Assert.Equal("not_converged", result.Status);
            Assert.Contains(result.Diagnostics, message => message.Contains("conver", StringComparison.OrdinalIgnoreCase));
            Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "stdout.txt")));
            Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "stderr.txt")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static OpenSeesSectionModel CreateModel() => new()
    {
        Materials =
        [
            new OpenSeesMaterialDefinition
            {
                Tag = 1,
                PositiveEnvelope = [new EnvelopePoint(0, 0), new EnvelopePoint(0.001, 1_000_000)],
                NegativeEnvelope = [new EnvelopePoint(-0.001, -1_000_000), new EnvelopePoint(0, 0)]
            }
        ],
        Fibers = [new OpenSeesFiber(0, 0, 1, 1)]
    };

    private sealed class FakeGenerator(List<string> calls) : IOpenSeesTclGenerator
    {
        public string Generate(OpenSeesSectionModel model, SectionAnalysisRequest request)
        {
            calls.Add("generate");
            return "wipe\n";
        }
    }

    private sealed class FakeRunner(List<string> calls, bool notConverged) : IOpenSeesProcessRunner
    {
        public async Task<OpenSeesRunResult> RunAsync(OpenSeesRunRequest request, CancellationToken cancellationToken)
        {
            calls.Add("run");
            await File.WriteAllTextAsync(
                Path.Combine(request.WorkingDirectory, "section_history.out"),
                "1 1 0 10 0 0.001 " + (notConverged ? "0" : "1") + " 0\n",
                cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(request.WorkingDirectory, "completed.marker"), "done\n", cancellationToken);
            return new OpenSeesRunResult
            {
                ExitCode = notConverged ? 2 : 0,
                Stdout = "fake stdout",
                Stderr = notConverged ? "fake convergence error" : "",
                Duration = TimeSpan.FromMilliseconds(1)
            };
        }
    }
}
