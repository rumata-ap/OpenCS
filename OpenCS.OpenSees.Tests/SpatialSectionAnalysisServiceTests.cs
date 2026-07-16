using OpenCS.OpenSees.Analysis;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Model;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Services;
using OpenCS.OpenSees.Tcl;

namespace OpenCS.OpenSees.Tests;

public sealed class SpatialSectionAnalysisServiceTests
{
    [Fact]
    public async Task Service_generates_runs_parses_and_finalizes_manifest_in_order()
    {
        string root = CreateRoot();
        try
        {
            List<string> calls = [];
            RecordingGenerator generator = new(calls);
            RecordingRunner runner = new(calls);
            SpatialSectionAnalysisService service = new(
                generator,
                runner,
                new OpenSeesArtifactStore(root));

            SpatialSectionAnalysisResult result = await service.RunAsync(
                CreateModel(),
                new SpatialSectionAnalysisRequest
                {
                    AxialForceN = 1_000,
                    AngleDegrees = 90,
                    MaxCurvature = 0.01,
                    Increments = 2
                },
                CreateProcessRequest(),
                CancellationToken.None);

            Assert.Equal("ok", result.Status);
            Assert.Equal(2, result.Rows.Count);
            Assert.Equal(90, generator.Request!.AngleDegrees);
            Assert.Equal(new[] { "generate", "run" }, calls);
            Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "script.tcl")));
            Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "manifest.json")));
            Assert.Equal("ok", ReadManifestStatus(result.ArtifactDirectory));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Service_returns_not_converged_for_nonzero_exit_with_valid_history()
    {
        string root = CreateRoot();
        try
        {
            SpatialSectionAnalysisService service = new(
                new RecordingGenerator([]),
                new RecordingRunner([], exitCode: 2, converged: false),
                new OpenSeesArtifactStore(root));

            SpatialSectionAnalysisResult result = await service.RunAsync(
                CreateModel(),
                new SpatialSectionAnalysisRequest { AxialForceN = 1_000 },
                CreateProcessRequest(),
                CancellationToken.None);

            Assert.Equal("not_converged", result.Status);
            Assert.NotEmpty(result.Rows);
            Assert.Contains(result.Diagnostics, message => message.Contains("кодом 2", StringComparison.Ordinal));
            Assert.Equal("not_converged", ReadManifestStatus(result.ArtifactDirectory));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Service_returns_error_for_missing_marker_and_preserves_artifacts()
    {
        string root = CreateRoot();
        try
        {
            SpatialSectionAnalysisService service = new(
                new RecordingGenerator([]),
                new RecordingRunner([], writeMarker: false),
                new OpenSeesArtifactStore(root));

            SpatialSectionAnalysisResult result = await service.RunAsync(
                CreateModel(),
                new SpatialSectionAnalysisRequest { AxialForceN = 1_000 },
                CreateProcessRequest(),
                CancellationToken.None);

            Assert.Equal("error", result.Status);
            Assert.Contains(result.Diagnostics, message => message.Contains("MissingMarker", StringComparison.Ordinal));
            Assert.True(File.Exists(Path.Combine(result.ArtifactDirectory, "script.tcl")));
            Assert.Equal("error", ReadManifestStatus(result.ArtifactDirectory));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Service_returns_error_for_generator_failure()
    {
        string root = CreateRoot();
        try
        {
            SpatialSectionAnalysisService service = new(
                new ThrowingGenerator(),
                new RecordingRunner([]),
                new OpenSeesArtifactStore(root));

            SpatialSectionAnalysisResult result = await service.RunAsync(
                CreateModel(),
                new SpatialSectionAnalysisRequest { AxialForceN = 1_000 },
                CreateProcessRequest(),
                CancellationToken.None);

            Assert.Equal("error", result.Status);
            Assert.Contains("generator failed", result.Diagnostics);
            Assert.Empty(result.ArtifactDirectory);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task Service_honors_cancellation_before_generation_and_during_runner()
    {
        string root = CreateRoot();
        try
        {
            using CancellationTokenSource before = new();
            before.Cancel();
            RecordingGenerator generator = new([]);
            SpatialSectionAnalysisService service = new(
                generator,
                new RecordingRunner([]),
                new OpenSeesArtifactStore(root));

            await Assert.ThrowsAsync<OperationCanceledException>(() => service.RunAsync(
                CreateModel(),
                new SpatialSectionAnalysisRequest { AxialForceN = 1_000 },
                CreateProcessRequest(),
                before.Token));
            Assert.Null(generator.Request);

            using CancellationTokenSource during = new();
            SpatialSectionAnalysisService cancellingService = new(
                new RecordingGenerator([]),
                new RecordingRunner([], cancellation: during),
                new OpenSeesArtifactStore(root));

            await Assert.ThrowsAsync<OperationCanceledException>(() => cancellingService.RunAsync(
                CreateModel(),
                new SpatialSectionAnalysisRequest { AxialForceN = 1_000 },
                CreateProcessRequest(),
                during.Token));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "opencs-opensees-spatial-service", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private static OpenSeesRunRequest CreateProcessRequest() => new()
    {
        ExecutablePath = "OpenSees.exe",
        WorkingDirectory = Path.GetTempPath(),
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static string ReadManifestStatus(string artifactDirectory)
    {
        string json = File.ReadAllText(Path.Combine(artifactDirectory, "manifest.json"));
        return System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("Status").GetString()!;
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

    private sealed class RecordingGenerator(List<string> calls) : ISpatialSectionTclGenerator
    {
        public SpatialSectionAnalysisRequest? Request { get; private set; }

        public string Generate(OpenSeesSectionModel model, SpatialSectionAnalysisRequest request)
        {
            calls.Add("generate");
            Request = request;
            return "wipe\n";
        }
    }

    private sealed class ThrowingGenerator : ISpatialSectionTclGenerator
    {
        public string Generate(OpenSeesSectionModel model, SpatialSectionAnalysisRequest request) =>
            throw new InvalidOperationException("generator failed");
    }

    private sealed class RecordingRunner : IOpenSeesProcessRunner
    {
        private readonly List<string> _calls;
        private readonly int _exitCode;
        private readonly bool _converged;
        private readonly bool _writeMarker;
        private readonly CancellationTokenSource? _cancellation;

        public RecordingRunner(
            List<string> calls,
            int exitCode = 0,
            bool converged = true,
            bool writeMarker = true,
            CancellationTokenSource? cancellation = null)
        {
            _calls = calls;
            _exitCode = exitCode;
            _converged = converged;
            _writeMarker = writeMarker;
            _cancellation = cancellation;
        }

        public async Task<OpenSeesRunResult> RunAsync(OpenSeesRunRequest request, CancellationToken cancellationToken)
        {
            _calls.Add("run");
            Assert.True(File.Exists(request.ScriptPath));
            Assert.Equal("running", ReadManifestStatus(request.WorkingDirectory));

            if (_cancellation is not null)
            {
                _cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            await File.WriteAllTextAsync(
                Path.Combine(request.WorkingDirectory, "section_history.out"),
                "# step loadFactor axialForceN openSeesMzNm openSeesMyNm rotationY rotationZ curvatureMagnitude converged residual\n" +
                $"1 0.5 1000 10 20 0.0005 0.001 0.001118033988749895 {(_converged ? 1 : 0)} 0\n" +
                $"2 1 1000 30 40 0.001 0.002 0.00223606797749979 {(_converged ? 1 : 0)} 0\n",
                cancellationToken);
            if (_writeMarker)
                await File.WriteAllTextAsync(Path.Combine(request.WorkingDirectory, "completed.marker"), "done\n", cancellationToken);

            return new OpenSeesRunResult
            {
                ExitCode = _exitCode,
                Stdout = "fake stdout",
                Stderr = _exitCode == 0 ? "" : "fake convergence error",
                Duration = TimeSpan.FromMilliseconds(1)
            };
        }
    }
}
