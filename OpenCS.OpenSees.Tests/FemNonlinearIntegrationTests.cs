using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Services;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;
using Xunit;

namespace OpenCS.OpenSees.Tests;

public sealed class FemNonlinearIntegrationTests
{
    [Fact]
    public async Task Cantilever_SmallElasticTipLoad_MatchesBeamTheory()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        string root = Path.Combine(Path.GetTempPath(), "opencs-fem-nonlinear-integration", Guid.NewGuid().ToString("N"));

        // Консоль вдоль X, L=2 м; узел 1 — заделка, узел 2 — свободный конец.
        // Сечение: SymmetricElasticSection() — 4 фибры в углах квадрата 1x1 м, E=2e8 Па, Iy=Iz=0.25 м⁴.
        // Нагрузка -1000 Н вдоль Z: изгиб об локальную ось y, макс. деформация фибры ≈ 2e-5 —
        // далеко в пределах линейного участка диаграммы материала (±0.01).
        const double L = 2.0, E = 2e8, Iy = 0.25, P = -1000.0;
        var baseSection = CrossSectionFixtures.SymmetricElasticSection();
        // SymmetricElasticSection() не задаёт GJ (по умолчанию 0 — она используется в 2D section-level
        // тестах, где кручение не участвует). Для 3D forceBeamColumn нужна собственная ручная GJ,
        // иначе агрегированная крутильная жёсткость секции равна нулю и матрица гибкости вырождена.
        var section = new OpenCS.OpenSees.Model.OpenSeesSectionModel
        {
            Materials = baseSection.Materials,
            Fibers = baseSection.Fibers,
            GJ = 1e6
        };
        var model = new FemNonlinearModel
        {
            Nodes =
            [
                new FemLinearNode(1, 0, 0, 0, [true, true, true, true, true, true]),
                new FemLinearNode(2, L, 0, 0, new bool[6]),
            ],
            Sections = new Dictionary<int, OpenCS.OpenSees.Model.OpenSeesSectionModel> { [1] = section },
            Elements = [new FemNonlinearElement(1, 1, 2, SectionTag: 1, NumIntegrationPoints: 5, Vecxz: (0, 0, 1))],
            Loads = [new FemLinearNodalLoad(2, 0, 0, P, 0, 0, 0)],
            LoadSteps = 4, Tolerance = 1e-8, MaxIterations = 30, GeomTransfKind = "Linear"
        };

        try
        {
            var result = await new FemNonlinearAnalysisService(
                new FemNonlinearTclGenerator(),
                new OpenSeesProcessRunner(),
                new OpenSeesArtifactStore(root),
                new FemNonlinearResultParser())
                .RunAsync(model, new OpenSeesRunRequest
                {
                    ExecutablePath = executable,
                    WorkingDirectory = Path.GetTempPath(),
                    Timeout = TimeSpan.FromSeconds(30)
                }, CancellationToken.None);

            Assert.True(result.Status == "ok", $"status={result.Status}; diagnostics={string.Join(" | ", result.Diagnostics)}");
            Assert.Equal(4, result.Steps.Count);
            Assert.All(result.Steps, s => Assert.True(s.Converged));

            var last = result.Steps[^1];
            Assert.InRange(last.LoadFactor, 0.99, 1.01);

            double expectedUz = P * L * L * L / (3.0 * E * Iy);   // ≈ -5.33e-5 м
            double uz = last.Displacements.Single(d => d.NodeTag == 2).Uz;
            Assert.InRange(uz, expectedUz * 1.02, expectedUz * 0.98);

            var reaction = last.Reactions.Single(r => r.NodeTag == 1);
            Assert.InRange(System.Math.Abs(reaction.Rz), 950, 1050);

            double baseMoment = last.ElementForces
                .SelectMany(f => new[] { System.Math.Abs(f.Myi), System.Math.Abs(f.Myj) })
                .Max();
            Assert.InRange(baseMoment, 1900, 2100);   // момент заделки ≈ |P|·L = 2000
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
