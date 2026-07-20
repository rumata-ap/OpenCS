using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Services;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;

namespace OpenCS.OpenSees.Tests;

public sealed class FemLinearIntegrationTests
{
    [Fact]
    public async Task Cantilever_TransverseTipLoad_MatchesBeamTheory()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        string root = Path.Combine(Path.GetTempPath(), "opencs-fem-linear-integration", Guid.NewGuid().ToString("N"));

        // Консоль вдоль X, L=3 м; узел 1 — заделка, узел 2 — свободный конец.
        // Нагрузка -1000 Н вдоль Z (= локальная z) → изгиб об локальную ось y (E·Iy).
        const double L = 3.0, E = 2e11, I = 8.333e-6, P = -1000.0;
        var model = new FemLinearModel
        {
            Nodes =
            [
                new FemLinearNode(1, 0, 0, 0, [true, true, true, true, true, true]),
                new FemLinearNode(2, L, 0, 0, new bool[6]),
            ],
            Elements =
            [
                new FemLinearElement(1, 1, 2, A: 0.01, E: E, G: 8e10, J: 1e-5, Iy: I, Iz: I, Vecxz: (0, 0, 1)),
            ],
            Loads = [new FemLinearNodalLoad(2, 0, 0, P, 0, 0, 0)],
        };

        try
        {
            var result = await new FemLinearAnalysisService(
                new FemLinearTclGenerator(),
                new OpenSeesProcessRunner(),
                new OpenSeesArtifactStore(root),
                new FemLinearResultParser())
                .RunAsync(model, new OpenSeesRunRequest
                {
                    ExecutablePath = executable,
                    WorkingDirectory = Path.GetTempPath(),
                    Timeout = TimeSpan.FromSeconds(30)
                }, CancellationToken.None);

            Assert.True(result.Status == "ok", $"status={result.Status}; diagnostics={string.Join(" | ", result.Diagnostics)}");

            double expectedUz = P * L * L * L / (3.0 * E * I);   // ≈ -0.0054 м
            double uz = result.Displacements.Single(d => d.NodeTag == 2).Uz;
            Assert.InRange(uz, expectedUz * 1.02, expectedUz * 0.98);   // отрицательное: 1.02 < 0.98

            var reaction = result.Reactions.Single(r => r.NodeTag == 1);
            Assert.InRange(Math.Abs(reaction.Rz), 950, 1050);            // вертикальная реакция ≈ |P|

            double baseMoment = result.ElementForces
                .SelectMany(f => new[] { Math.Abs(f.Myi), Math.Abs(f.Myj) })
                .Max();
            Assert.InRange(baseMoment, 2900, 3100);                      // момент заделки ≈ |P|·L = 3000
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
