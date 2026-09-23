using System.Globalization;
using CScore;
using CScore.Tests.Submodel;
using OpenCS.OpenSees.Artifacts;
using OpenCS.OpenSees.CScore;
using OpenCS.OpenSees.Results;
using OpenCS.OpenSees.Runtime;
using OpenCS.OpenSees.Services;
using OpenCS.OpenSees.Structural;
using OpenCS.OpenSees.Tcl;
using OpenCS.OpenSees.Tests.Fixtures;

namespace OpenCS.OpenSees.Tests;

/// <summary>
/// Эталон рамы на живом OpenSees (opt-in). При OPENCS_WRITE_SUBMODEL_FIXTURE=1 перезаписывает фикстуру
/// результата в CScore.Tests; иначе сверяет живой результат с фикстурой и повторяет проверки сценария.
/// </summary>
public sealed class StraightBeamBoundaryScenarioIntegrationTests
{
    [Fact]
    public async Task PortalFrame_LiveOpenSees_MatchesFixtureAndScenarioChecks()
    {
        string executable = OpenSeesTestExecutable.ResolveOrSkip();
        var parent = PortalFrameReference.Model();
        var live = await RunLinear(executable, parent);

        string fixturePath = FixtureSourcePath();
        var liveFixture = ToFixture(live);
        if (Environment.GetEnvironmentVariable("OPENCS_WRITE_SUBMODEL_FIXTURE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fixturePath)!);
            File.WriteAllText(fixturePath, PortalFrameReference.Serialize(liveFixture));
        }

        var recorded = PortalFrameReference.Deserialize(File.ReadAllText(fixturePath));
        AssertSameResult(recorded, liveFixture);

        var result = FemLinearResultParentAdapter.FromLinearResult(live);
        var whole = PortalFrameReference.Build(parent, PortalFrameReference.Extract(parent, ["12", "13", "14"]), result);
        PortalFrameReference.Verify(parent, whole);
        PortalFrameReference.VerifyHoggingAtRigidJoints(whole);
        var middle = PortalFrameReference.Build(parent, PortalFrameReference.Extract(parent, ["13"]), result);
        PortalFrameReference.Verify(parent, middle);
    }

    static async Task<FemLinearResult> RunLinear(string executable, ParentModel parent)
    {
        string root = Path.Combine(Path.GetTempPath(), "opencs-submodel-portal-frame", Guid.NewGuid().ToString("N"));
        var sectionProps = new Dictionary<int, GeoProps>
        {
            [PortalFrameReference.SectionId] = new GeoProps
            {
                A = PortalFrameReference.Area, EA = PortalFrameReference.Area * PortalFrameReference.E,
                Ix = PortalFrameReference.Inertia, EIx = PortalFrameReference.Inertia * PortalFrameReference.E,
                Iy = PortalFrameReference.Inertia, EIy = PortalFrameReference.Inertia * PortalFrameReference.E
            }
        };
        var input = new FemLinearWorkflowInput(parent.MeshNodes, parent.MeshElements, parent.Nodes, parent.Members,
            [.. PortalFrameReference.NodeLoads()], sectionProps)
        {
            ResolvedMemberLoads = [.. PortalFrameReference.MemberLoads()]
        };
        try
        {
            var output = await new FemLinearAnalysisWorkflow(new FemLinearAnalysisService(
                    new FemLinearTclGenerator(), new OpenSeesProcessRunner(),
                    new OpenSeesArtifactStore(root), new FemLinearResultParser()))
                .RunAsync(input, new OpenSeesRunRequest
                {
                    ExecutablePath = executable, WorkingDirectory = Path.GetTempPath(), Timeout = TimeSpan.FromSeconds(30)
                }, CancellationToken.None);
            Assert.True(output.Status == "ok", $"status={output.Status}; errors={string.Join(" | ", output.Errors)}");
            return output.Result!;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static PortalFrameReference.ResultFixture ToFixture(FemLinearResult result)
    {
        static string T(int tag) => tag.ToString(CultureInfo.InvariantCulture);
        return new PortalFrameReference.ResultFixture(
            result.Displacements.OrderBy(d => d.NodeTag).Select(d => new PortalFrameReference.NodeRow(T(d.NodeTag), [d.Ux, d.Uy, d.Uz, d.Rx, d.Ry, d.Rz])).ToList(),
            result.Reactions.OrderBy(r => r.NodeTag).Select(r => new PortalFrameReference.NodeRow(T(r.NodeTag), [r.Rx, r.Ry, r.Rz, r.Mx, r.My, r.Mz])).ToList(),
            result.ElementForces.OrderBy(f => f.ElemTag).Select(f => new PortalFrameReference.ElementRow(T(f.ElemTag),
                [f.Ni, f.Qyi, f.Qzi, f.Mxi, f.Myi, f.Mzi], [f.Nj, f.Qyj, f.Qzj, f.Mxj, f.Myj, f.Mzj])).ToList());
    }

    static void AssertSameResult(PortalFrameReference.ResultFixture expected, PortalFrameReference.ResultFixture actual)
    {
        static void Rows(IReadOnlyList<double[]> e, IReadOnlyList<double[]> a)
        {
            Assert.Equal(e.Count, a.Count);
            double scale = Math.Max(1e-12, e.SelectMany(v => v).Select(Math.Abs).DefaultIfEmpty(0).Max());
            for (int r = 0; r < e.Count; r++)
                for (int k = 0; k < e[r].Length; k++)
                    Assert.True(Math.Abs(e[r][k] - a[r][k]) <= 1e-9 * scale, $"Строка {r}, компонента {k}: {e[r][k]} ≠ {a[r][k]}");
        }
        Assert.Equal(expected.Displacements.Select(r => r.Node), actual.Displacements.Select(r => r.Node));
        Assert.Equal(expected.EndForces.Select(r => r.Element), actual.EndForces.Select(r => r.Element));
        Rows(expected.Displacements.Select(r => r.Values).ToList(), actual.Displacements.Select(r => r.Values).ToList());
        Rows(expected.Reactions.Select(r => r.Values).ToList(), actual.Reactions.Select(r => r.Values).ToList());
        Rows(expected.EndForces.SelectMany(r => new[] { r.I, r.J }).ToList(), actual.EndForces.SelectMany(r => new[] { r.I, r.J }).ToList());
    }

    /// <summary>Путь к фикстуре в исходном дереве CScore.Tests (вверх от каталога сборки теста).</summary>
    static string FixtureSourcePath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "CScore.Tests");
            if (Directory.Exists(candidate))
                return Path.Combine(candidate, PortalFrameReference.FixtureRelativePath.Replace('/', Path.DirectorySeparatorChar));
        }
        throw new DirectoryNotFoundException("Не найден каталог CScore.Tests выше каталога сборки теста.");
    }
}
