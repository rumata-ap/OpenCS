using CScore;
using CScore.Fem;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.CScore;
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

    [Fact]
    public async Task Cantilever_MidspanPointLoad_MatchesBeamTheory()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        string root = Path.Combine(Path.GetTempPath(), "opencs-fem-linear-point-load-integration", Guid.NewGuid().ToString("N"));

        // Консоль вдоль X, L=3 м, один элемент. Сосредоточенная сила Py=-1000 Н приложена
        // в середине пролёта (a=1.5 м, xL=0.5) через eleLoad -type -beamPoint.
        const double L = 3.0, A = 1.5, E = 2e11, I = 8.333e-6, P = -1000.0;
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
            PointLoads = [new FemLinearPointLoad(1, Py: P, Pz: 0, Px: 0, XOverL: A / L)],
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

            // Прогиб консоли от точечной силы на расстоянии a от заделки: P·a²·(3L-a)/(6EI).
            double expectedTipDeflection = P * A * A * (3 * L - A) / (6.0 * E * I);   // ≈ -0.001688 м
            var tip = result.Displacements.Single(d => d.NodeTag == 2);
            double actualDeflection = Math.Abs(tip.Uy) > Math.Abs(tip.Uz) ? tip.Uy : tip.Uz;
            Assert.InRange(actualDeflection, expectedTipDeflection * 1.02, expectedTipDeflection * 0.98);

            // Момент заделки от точечной силы на расстоянии a: |P|·a ≈ 1500 Н·м.
            double baseMoment = result.ElementForces
                .SelectMany(f => new[] { Math.Abs(f.Myi), Math.Abs(f.Mzi) })
                .Max();
            Assert.InRange(baseMoment, 1450, 1550);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Cantilever_MemberPointLoadThroughFullResolver_MatchesBeamTheory()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        string root = Path.Combine(Path.GetTempPath(), "opencs-fem-linear-point-load-resolver-integration", Guid.NewGuid().ToString("N"));

        // Тот же случай, что и Cantilever_MidspanPointLoad_MatchesBeamTheory, но нагрузка задаётся
        // как каноническая CScore.Fem.FemMemberLoad (point) и проходит весь резолвер
        // (FemPointLoadResolver → FemLinearModelResolver), а не собирается вручную.
        const double L = 3.0, A = 1.5, E = 2e11, I = 8.333e-6, P = -1000.0;
        var meshNodes = new List<FemMeshNode>
        {
            new() { Id = 10, NodeTag = "1", X = 0, Y = 0, Z = 0, SourceNodeTag = "1", SourceMemberTag = "1" },
            new() { Id = 11, NodeTag = "2", X = L, Y = 0, Z = 0, SourceNodeTag = "2", SourceMemberTag = "1" },
        };
        var meshElems = new List<FemElement>
        {
            new() { Id = 20, ElemTag = "1", NodeIdsJson = "[1,2]", SourceMemberTag = "1",
                    CrossSectionId = 5, GjStrategy = "manual", GjManualValue = 1e6 },
        };
        var srcNodes = new List<FemNode>
        {
            new() { Id = 1, NodeTag = "1", X = 0, Y = 0, Z = 0, DofMask = 63 },
            new() { Id = 2, NodeTag = "2", X = L, Y = 0, Z = 0, DofMask = 0 },
        };
        var srcMembers = new List<FemMember>
        {
            new() { Id = 1, ElemTag = "1", ElemType = "beam", NodeIdsJson = "[1,2]",
                    CrossSectionId = 5, GjStrategy = "manual", GjManualValue = 1e6 },
        };
        var sectionProps = new Dictionary<int, GeoProps>
        {
            [5] = new GeoProps { A = 0.01, EA = 0.01 * E, Ix = I, EIx = I * E, Iy = I, EIy = I * E }
        };
        var memberLoad = new FemMemberLoad
        {
            MemberId = 1, LoadCaseId = 1, CoordinateSystem = "local",
            DistributionType = "point", StartOffsetM = A, QyStart = P
        };

        var input = new FemLinearWorkflowInput(meshNodes, meshElems, srcNodes, srcMembers, [], sectionProps)
        {
            ResolvedMemberLoads = [memberLoad]
        };

        try
        {
            var output = await new FemLinearAnalysisWorkflow(
                new FemLinearAnalysisService(
                    new FemLinearTclGenerator(),
                    new OpenSeesProcessRunner(),
                    new OpenSeesArtifactStore(root),
                    new FemLinearResultParser()))
                .RunAsync(input, new OpenSeesRunRequest
                {
                    ExecutablePath = executable,
                    WorkingDirectory = Path.GetTempPath(),
                    Timeout = TimeSpan.FromSeconds(30)
                }, CancellationToken.None);

            Assert.True(output.Status == "ok", $"status={output.Status}; errors={string.Join(" | ", output.Errors)}; diagnostics={string.Join(" | ", output.Result?.Diagnostics ?? [])}");

            double expectedTipDeflection = P * A * A * (3 * L - A) / (6.0 * E * I);   // ≈ -0.001688 м
            var tip = output.Result!.Displacements.Single(d => d.NodeTag == 2);
            double actualDeflection = Math.Abs(tip.Uy) > Math.Abs(tip.Uz) ? tip.Uy : tip.Uz;
            Assert.InRange(actualDeflection, expectedTipDeflection * 1.02, expectedTipDeflection * 0.98);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Cantilever_MixedForceAndPrescribedDisplacement_UsesOneLoadPattern()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        string root = Path.Combine(Path.GetTempPath(), "opencs-fem-linear-kinematic-integration", Guid.NewGuid().ToString("N"));

        const double length = 3.0, area = 0.01, youngModulus = 2e11, axialForce = 1000.0,
            prescribedUz = -0.001;
        var model = new FemLinearModel
        {
            Nodes =
            [
                new FemLinearNode(1, 0, 0, 0, [true, true, true, true, true, true]),
                new FemLinearNode(2, length, 0, 0, new bool[6]),
            ],
            Elements =
            [
                new FemLinearElement(1, 1, 2, area, youngModulus, 8e10, 1e-5,
                    1e-5, 1e-5, (0, 0, 1)),
            ],
            Loads = [new FemLinearNodalLoad(2, axialForce, 0, 0, 0, 0, 0)],
            KinematicLoads = [new FemLinearKinematicLoad(2, 3, prescribedUz)]
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
            var tip = result.Displacements.Single(displacement => displacement.NodeTag == 2);
            Assert.InRange(tip.Ux, axialForce * length / (youngModulus * area) - 1e-10,
                axialForce * length / (youngModulus * area) + 1e-10);
            Assert.InRange(tip.Uz, prescribedUz - 1e-10, prescribedUz + 1e-10);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
